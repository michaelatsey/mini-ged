# ged-platform

Domain model for a document management system (GED), built on
[MicroKit](https://github.com/michaelatsey/microkit).

This repository currently contains **one layer**: `Ged.Domain`. No persistence, no API, no storage
adapter. The domain is being settled first, on purpose — the shape of everything above it follows
from the decisions made here.

```
Ged.Domain                  aggregates, rules, events        ← verified
Ged.Core                    technical ports                  ← verified
Ged.Adapters.Persistence    EF Core write side, Dapper reads ← not yet compiled
Ged.Migrations              DbUp: the schema's only owner    ← not yet compiled
Ged.Application             vertical slices                  ← next
Ged.Api                                                      ← later
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
dotnet run --project src/Ged.Migrations -- "Host=localhost;Database=ged;Username=ged;Password=ged"
dotnet run --project src/Ged.Migrations -- "$GED_DB" --what-if   # list pending scripts
```

`Release` builds with `TreatWarningsAsErrors`, so the build is the first reviewer.

## Who owns what

| Concern | Owner |
|---|---|
| The schema | `Ged.Migrations` (DbUp), and nothing else |
| Invariants during a mutation | EF Core, through the aggregates |
| Screens and projections | Dapper, in the slice that needs them |
| Publishing events | the outbox, written in the same transaction |

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

## Status

All three aggregates are implemented and the `Release` build is clean. Nothing above the domain is
written yet.

## License

MIT
