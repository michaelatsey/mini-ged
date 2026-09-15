# ged-platform

Domain model for a document management system (GED), built on
[MicroKit](https://github.com/michaelatsey/microkit).

This repository currently contains **one layer**: `Ged.Domain`. No persistence, no API, no storage
adapter. The domain is being settled first, on purpose — the shape of everything above it follows
from the decisions made here.

```
Ged.Domain                  aggregates, rules, events        ← verified
Ged.Core                    technical ports                  ← verified
Ged.Adapters.Persistence            provider-agnostic write side  ← not yet compiled
Ged.Adapters.Persistence.PostgreSql xmin, SKIP LOCKED, jsonb       ← not yet compiled
Ged.Adapters.Persistence.SqlServer  rowversion, READPAST, clustering ← not yet compiled
Ged.Migrations                      DbUp, one script set per engine ← not yet compiled
Ged.Adapters.Storage                registry, location resolver     ← verified
Ged.Adapters.Storage.FileSystem     works with no MinIO, no Beys     ← verified
Ged.Features                        vertical slices, endpoints      ← verified
Ged.Api                             composition root                ← next
```

EF Core, Npgsql, Dapper and DbUp were unreachable when the persistence layer was written, so it has
not been through a compiler. `docs/microkit-deviations.md` lists the four mapping decisions to check
first.

## The idea in three sentences

A document is not a file. It is a stable identity with a history of versions, each pointing at an
immutable content addressed by its SHA-256 digest. Separating the two makes versioning, restore,
deduplication and storage-provider migration consequences of the model rather than features bolted
onto it.

## Aggregates

| Aggregate | Holds | Deliberately does not hold |
|---|---|---|
| `Document` | its versions | its folder, its content bytes |
| `Folder` | nothing but itself | its children, its documents |
| `Blob` | its storage locations | who references it |

Each references the others by identity only. A folder that contained its children would make the
consistency boundary the entire tree, and an unrelated document upload would contend with a folder
rename.

`Blob` keeps its locations because switching which one serves reads must be atomic — a blob with
two primaries, or none, has no defined read path. It does not keep a reference count: a stored
counter drifts on the first incident, and once nobody trusts it, nobody dares purge anything again.
The caller counts and passes the answer in.

## Three principles the code enforces

**Nothing ambient.** The instant and the acting identity are parameters of every operation, never
read from a clock or a context. Aggregates are deterministic and testable against fixed values.

```csharp
document.Rename(new DocumentName("rapport-v2.pdf"), now, actor);
```

**Rules are types, not strings.** Every invariant is a `BusinessRule`, so tests assert on the
violated rule's type and the API maps it exhaustively.

```csharp
catch (BusinessRuleViolationException ex) => ex.ViolatedRule switch
{
    DocumentMustNotBeDeletedRule => Results.Conflict("DOCUMENT_DELETED"),
    VersionContentMustDifferFromCurrentRule => Results.Conflict("VERSION_IDENTICAL"),
    _ => Results.UnprocessableEntity()
};
```

**The domain owns the rule, the caller supplies the fact.** Hierarchy invariants need more than one
node, so the facts arrive as parameters instead of the aggregate reaching for a repository.

```csharp
folder.MoveTo(targetAncestry, now, actor);           // cycle + depth checked inside
folder.SoftDelete(hasChildFolders, hasDocuments, now, actor);
blob.MarkPurged(hasLiveReferences, retentionCutoff, now, actor);
```

## Changing storage provider

The reason content is addressed by digest rather than by path: a provider change is a sequence of
state transitions on data, with no code change and a two-step rollback.

```
blob.AddLocation(minio, key, copyInFlight: true)   → MIGRATING   copy in flight
blob.VerifyLocation(id)                            → REPLICA     digest confirmed
blob.PromoteToPrimary(id)                          → PRIMARY     reads switch, old → LEGACY
blob.RemoveLocation(oldId)                                       record dropped
```

Reads follow `blob.ReadOrder` — primary, then verified replicas, then legacy — so a failed read
falls back instead of surfacing as an error, and a migration stays invisible to users.

## What the model makes impossible

| Mistake | Prevented by |
|---|---|
| A document with no content | `Create` requires a blob; the state is unreachable |
| Rewriting a past version | `DocumentVersion` has no setters, constructor is `internal` |
| A gap in version numbers | only `VersionNumber.Next()` produces one |
| Re-uploading identical content as a "change" | `VersionContentMustDifferFromCurrentRule` |
| A cycle in the folder tree | `FolderMustNotMoveIntoItsOwnSubtreeRule` |
| Deleting a folder that still holds content | `FolderMustBeEmptyToDeleteRule` |
| Modifying a deleted document or folder | `*MustNotBeDeletedRule` |
| Passing a `FolderId` where a `DocumentId` belongs | distinct types |
| Promoting an unverified copy to serve reads | `LocationMustBeVerifiedBeforePromotionRule` |
| A blob with two primaries, or none | promotion and demotion happen in one transaction |
| Removing the last readable copy | `BlobMustKeepAReadableLocationRule` |
| Purging content that is still referenced | `BlobMustHaveNoLiveReferencesRule`, re-checked at purge |
| Purging before the retention window elapses | `BlobMustBeAnAgedOrphanToPurgeRule` |
| Deleting a byte from the domain | no API exists |

## Layout

```
src/Ged.Domain/
├── Abstractions/        Actor, GedDomainEvent, AuditableRoot, DomainRules
├── Blobs/               Blob, BlobLocation, IBlobRepository, VOs, rules, events
├── Documents/           Document, DocumentVersion, IDocumentRepository, VOs, rules, events
└── Folders/             Folder, FolderAncestry, IFolderRepository, VOs, rules, events

tests/Ged.Domain.Tests/  behaviour + architecture guards
docs/                    deviations from MicroKit, and what the domain cannot guarantee
```

## Build

```bash
dotnet build -c Release
dotnet test

# apply the schema (DbUp owns it; EF Core never generates it)
dotnet run --project src/Ged.Migrations -- --provider postgres  --connection "$GED_DB"
dotnet run --project src/Ged.Migrations -- --provider sqlserver --connection "$GED_DB" --what-if
```

`Release` builds with `TreatWarningsAsErrors`, so the build is the first reviewer.

## Who owns what

| Concern | Owner |
|---|---|
| The schema | `Ged.Migrations` (DbUp), and nothing else |
| Invariants during a mutation | EF Core, through the aggregates |
| Screens and projections | Dapper, in the slice that needs them |
| Publishing events | the outbox, written in the same transaction |
| Engine-specific dialect | `IPersistenceProvider`, and nothing else |

Both PostgreSQL and SQL Server are supported. The domain, the repositories and the entity
configurations are identical on both; what genuinely differs — concurrency token type, lock hints,
recursive-query syntax, and how each engine orders a GUID — is documented in
`docs/persistence-providers.md`.

Two tools able to change the same schema will eventually disagree, so EF Core has no migrations
folder and `Database.Migrate` is never called.

Some guarantees exist in both places on purpose. `ux_blob_location_single_primary` repeats an
invariant the aggregate already enforces: the aggregate covers one transaction, the index covers
two concurrent ones, and neither covers the other's case.

## Reading order

1. `docs/domain-boundaries.md` — what this domain deliberately does not guarantee, and why
2. `src/Ged.Domain/Documents/Document.cs` — the versioning and deletion model
3. `src/Ged.Domain/Blobs/Blob.cs` — locations, migration path, and the retention window
4. `src/Ged.Domain/Folders/FolderAncestry.cs` — how hierarchy rules stay inside the domain
5. `docs/microkit-deviations.md` — the three shims and the upstream changes that remove them

## Features

Fourteen slices, each a folder holding its endpoint, its command or query, and its handler.
Registration is explicit all the way down — `MapGedEndpoints()` calls one method per module, each
module calls one per slice. No assembly scanning, no `IEndpoint` convention, no reflection: a reader
finds every route by following three calls, the compiler catches a slice that was never wired, and
nothing needs explaining to a trimmer.

Writes go through a repository and EF Core, because they mutate an aggregate whose invariants must
hold. Reads open a connection and run the SQL their screen needs, in the slice that needs it — a
repository exists to reconstitute an aggregate for a rule, and the moment it gains a search method
every slice starts reaching into it.

```
UploadDocument     content is written to storage BEFORE the transaction opens
                   a failed commit then leaves a detectable orphan, not a ghost
DeleteDocument     breaks a link; no code path from here reaches a deleted byte
ReplicateBlobs     MIGRATING -> REPLICA -> PRIMARY -> LEGACY, a provider change
                   as data transitions rather than a code change
```

## Storage

`IObjectStorage` is deliberately poor — put, open, exists, copy, delete, list. Presigning is a
separate capability interface, because an abstraction that assumes its richest implementation forces
every other adapter to throw.

`Ged.Adapters.Storage.FileSystem` is a real adapter, not a mock: the whole system runs on a plain
directory, with no MinIO and no Beys. That matters beyond convenience — an abstraction with one
implementation is an untested hypothesis, and the second adapter is what turns it into an
abstraction.

## Status

Domain, ports, storage adapters and features build clean in `Release`. The persistence adapters and
the migration runner are written but not yet compiled, since their packages were unreachable when
they were authored.

## License

MIT
