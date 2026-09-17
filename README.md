# mini-ged

Document management platform (GED) on .NET 10, built to replace a Beys-backed store with MinIO — or
any other backend — as a configuration change rather than a migration.

## The idea in three sentences

A document is not a file. It is a stable identity with a history of versions, each pointing at
immutable content addressed by its SHA-256. Separating the two makes versioning, restore,
deduplication and storage-provider migration consequences of the model rather than features bolted
onto it.

## Layout

```
src/Ged.Domain                      aggregates, rules, events
src/Ged.Core                        ports
src/Ged.Features                    vertical slices, endpoints
src/Ged.Adapters.Persistence        provider-agnostic write side
    .PostgreSql                     xmin, SKIP LOCKED, jsonb
    .SqlServer                      rowversion, READPAST, clustering
src/Ged.Adapters.Storage            registry, location resolver
    .FileSystem                     a real backend needing nothing installed
src/Ged.Adapters.FileTypes          content detection, no dependency
    .FileSignatures                 optional third-party detector
hosts/Ged.Api                       composition root, versioned
database/Ged.Migrations             DbUp — the schema's only owner
libraries/                          MicroKit, vendored as source
tests/                              domain and upload assertions
```

`Ged.Migrations` sits under `database/` rather than `src/` because its lifecycle is the schema's, not
the application's: it ships as its own image, runs to completion, and is versioned by SQL scripts.

## Run it

```bash
cp .env.example .env
docker compose up --build                       # PostgreSQL
docker compose --profile sqlserver up --build   # the other engine

# http://localhost:8080/scalar            API reference with a version selector
# http://localhost:8080/health/ready
```

Two chiseled, non-root, read-only images: the API, and a schema runner that exits before the API
starts. The whole chain — upload, download, replication, purge — runs with no MinIO and no Beys, on
filesystem storage.

## Build

```bash
dotnet build -c Release
dotnet test

# apply the schema (DbUp owns it; EF Core never generates it)
dotnet run --project database/Ged.Migrations -- --provider postgres  --connection "$GED_DB"
dotnet run --project database/Ged.Migrations -- --provider sqlserver --connection "$GED_DB" --what-if
```

`Release` builds with `TreatWarningsAsErrors`, so the build is the first reviewer — and the container
build is stricter than a local `Debug` run.

## Aggregates

| Aggregate | Holds | Deliberately does not hold |
| --- | --- | --- |
| `Document` | its versions | its folder, its content bytes |
| `Folder` | nothing but itself | its children, its documents |
| `Blob` | its storage locations | who references it |

Each references the others by identity only. A folder that contained its children would make the
consistency boundary the entire tree, and an unrelated document upload would contend with a folder
rename.

`Blob` keeps its locations because switching which one serves reads must be atomic — a blob with two
primaries, or none, has no defined read path. It does not keep a reference count: a stored counter
drifts on the first incident, and once nobody trusts it, nobody dares purge anything again.

## Three principles the code enforces

**Nothing ambient.** The instant and the acting identity are parameters of every operation, never
read from a clock or a context. Aggregates are deterministic and testable against fixed values.

```csharp
document.Rename(new DocumentName("rapport-v2.pdf"), now, actor);
```

**Rules are types, not strings.** Every invariant is a `BusinessRule`, so tests assert on the
violated rule's type and the API maps it exhaustively.

**The domain owns the rule, the caller supplies the fact.** Hierarchy invariants need more than one
node, so the facts arrive as parameters instead of the aggregate reaching for a repository.

```csharp
folder.MoveTo(targetAncestry, now, actor);           // cycle + depth checked inside
folder.SoftDelete(hasChildFolders, hasDocuments, now, actor);
blob.MarkPurged(hasLiveReferences, retentionCutoff, now, actor);
```

## What the model makes impossible

| Mistake | Prevented by |
| --- | --- |
| A document with no content | `Create` requires a blob; the state is unreachable |
| Rewriting a past version | no setters, constructor is `internal` |
| A gap in version numbers | only `VersionNumber.Next()` produces one |
| Re-uploading identical content as a "change" | `VersionContentMustDifferFromCurrentRule` |
| A cycle in the folder tree | `FolderMustNotMoveIntoItsOwnSubtreeRule` |
| Promoting an unverified copy to serve reads | `LocationMustBeVerifiedBeforePromotionRule` |
| A blob with two primaries, or none | one transaction, plus a unique partial index |
| Purging content still referenced | re-checked immediately before the purge |
| Purging before the retention window elapses | `BlobMustBeAnAgedOrphanToPurgeRule` |
| Passing a `FolderId` where a `DocumentId` belongs | distinct types |
| Deleting a byte from the domain | no API exists |

## Changing storage provider

The reason content is addressed by digest rather than by path: a provider change is a sequence of
state transitions on data, with no code change and a two-step rollback.

```
blob.AddLocation(minio, key, copyInFlight: true)   → MIGRATING   copy in flight
blob.VerifyLocation(id)                            → REPLICA     digest confirmed
blob.PromoteToPrimary(id)                          → PRIMARY     reads switch, old → LEGACY
blob.RemoveLocation(oldId)                                       record dropped
```

Reads follow `blob.ReadOrder` — primary, then verified replicas, then legacy — so a failed read falls
back instead of surfacing as an error, and a migration stays invisible to users.

## Features

Fourteen slices, each a folder holding its endpoint, its command or query, and its handler.
Registration is explicit all the way down: `MapGedV1()` calls one method per module, each module
calls one per slice. No assembly scanning, no `IEndpoint` convention, no reflection.

Writes go through a repository and EF Core. Reads open a connection and run the SQL their screen
needs, in the slice that needs it — a repository exists to reconstitute an aggregate for a rule, and
the moment it gains a search method every slice starts reaching into it.

```
UploadDocument     content reaches storage BEFORE the transaction opens, so a failed
                   commit leaves a detectable orphan rather than an object nothing refers to
DeleteDocument     breaks a link; no code path from here reaches a deleted byte
ReplicateBlobs     the provider migration, as state transitions
```

## Uploads

An extension is a string the client chose, and so is a `Content-Type` header. Three layers, in
increasing order of cost and authority:

```
extension          free, a claim       refuses before the body is read
declared type      free, a claim       refuses before the body is read
detected format    needs bytes         decides, and sets the stored media type
```

Nine of the accepted formats share a container — `PK\x03\x04` covers docx, xlsx, pptx, odt, ods, odp
plus zip, jar and apk; `D0 CF 11 E0` covers doc, xls, ppt plus msi — so containers are opened rather
than trusted.

Detection sits behind `IContentFormatDetector` with two implementations, selected by
`Ged:Uploads:Detector`: a built-in one with no dependency, and one delegating to FileSignatures.
Policy — allowlist, per-format ceilings, narrowing per document type — stays in the feature layer,
because no library can hold business rules. Reasoning in `docs/uploads.md`.

## Storage

`IObjectStorage` is deliberately poor — put, open, exists, copy, delete, list. Presigning is a
separate capability interface, because an abstraction that assumes its richest implementation forces
every other adapter to throw.

`Ged.Adapters.Storage.FileSystem` is a real adapter, not a mock: the whole system runs on a plain
directory. That matters beyond convenience — an abstraction with one implementation is an untested
hypothesis, and the second adapter is what turns it into an abstraction.

## API

URL-segment versioning on `Asp.Versioning` 10, one OpenAPI document per version, Scalar at `/scalar`.

`AssumeDefaultVersionWhenUnspecified` is off and `ReportApiVersions` is on, so a client must state the
version it wants and learns of a deprecation from its own traffic.

Authorization denies by default, rate limiting partitions per identity, transfers are bounded by
concurrency rather than by rate, and one `IExceptionHandler` translates domain failures so no endpoint
contains a `try`. Reasoning in `docs/api.md`.

## Who owns what

| Concern | Owner |
| --- | --- |
| The schema | `database/Ged.Migrations` (DbUp), and nothing else |
| Invariants during a mutation | EF Core, through the aggregates |
| Screens and projections | Dapper-free SQL, in the slice that needs it |
| Publishing events | the outbox, written in the same transaction |
| Engine-specific dialect | `IPersistenceProvider`, and nothing else |
| Recognising a file format | `IContentFormatDetector`, and nothing else |

Some guarantees exist in both places on purpose. `ux_blob_location_single_primary` repeats an
invariant the aggregate already enforces: the aggregate covers one transaction, the index covers two
concurrent ones, and neither covers the other's case.

## Reading order

1. `docs/domain-boundaries.md` — what this domain deliberately does not guarantee, and why
2. `src/Ged.Domain/Documents/Document.cs` — the versioning and deletion model
3. `src/Ged.Domain/Blobs/Blob.cs` — locations, migration path, retention window
4. `src/Ged.Domain/Folders/FolderAncestry.cs` — how hierarchy rules stay inside the domain
5. `docs/uploads.md`, `docs/api.md`, `docs/persistence-providers.md`, `docs/containers.md`
6. `docs/microkit-deviations.md` — why there are none left, and the EF mapping bets it records

## Status

Builds clean in `Release` with `TreatWarningsAsErrors`. Both schemas apply — PostgreSQL and SQL
Server — and the stack runs under `docker compose`. 64 domain tests, green. No upload tests: FileTypes, StagedContent and the detectors are uncovered..

Not yet covered: no malware scanning, no audit trail, no full-text search, and no integration test
against a live database. See the roadmap below.

64 domain tests, green. No upload tests: FileTypes, StagedContent and the detectors are uncovered.
`dotnet test`; folding them into `tests/` is part of item 1 below.

---

# Roadmap

Ordered by what a GED cannot credibly ship without, not by what is interesting to build. Items marked
**◆** are generic enough to be lifted into another product — SaaS BTP and anything else that accepts
files from users.

## Now — what makes the current code trustworthy

### 1. Integration tests against a live database ◆

The model has never been round-tripped through EF Core against a real engine. One mapping bet remains
open: `PromoteToPrimary` demotes one location and promotes another in a single `SaveChanges`, against
a unique partial index. It surfaces on write, never at build.

```
Testcontainers, [Theory] over both engines
  - full Blob lifecycle round-trip
  - optimistic concurrency: uint vs byte[] both reject a stale write
  - the outbox row lands in the same transaction as the aggregate
  - DbUp applies, then every DbSet is queried once — the mapping guard
```

### 2. Malware scanning ◆

The single largest gap, and the one an audit finds first. Type validation answers *"is this really a
PDF?"*; it says nothing about a genuinely valid PDF carrying an exploit — which is the realistic
attack against a corporate GED, not a renamed `.exe`.

The quarantine already exists: `StagedContent` holds the bytes on disk, hashed and inspected, before
anything reaches storage. What is missing is a port and an adapter.

```csharp
IMalwareScanner          // slice-local port on UploadDocument
  ICAP                   // if the organisation already runs an AV/DLP appliance — ask first
  ClamAV (clamd)         // otherwise; containerised, free, enough to start
  CDR                    // high-stakes: the file is rebuilt without active content
```

### 3. The outbox dispatcher ◆

Events are written in the same transaction as the state change and nothing reads them. A
`BackgroundService` claiming with `FOR UPDATE SKIP LOCKED`, with retry and a dead-letter threshold,
plus a first consumer to prove the seam.

### 4. Scheduled maintenance

`MarkOrphanBlobs`, `PurgeOrphanBlobs`, `ReconcileStorage` and `ReplicateBlobs` exist and nothing runs
them. Until they do, deleted content is never reclaimed and a silently missing copy is never noticed.

### 5. Multi-tenancy ◆

Nothing tenant-aware exists yet — no `ITenantContext`, no tenant column, no filter. Three options,
and the choice decides the persistence adapters rather than the domain:

```
database per tenant    strongest isolation, highest operational cost
schema per tenant      one connection, search_path per request
tenant_id column       cheapest, riskiest — a WHERE clause is the only barrier
```

This is the prerequisite for reusing the platform in another product.

## Next — what a GED is expected to do

### 6. Audit trail ◆

NF Z42-013 requires a system to *demonstrate* that procedures were followed — auditability is one of
its seven domains, alongside integrity, metadata and destruction procedures. Today nothing records
who read what.

```
who, when, which document, which version, which action, from where
append-only, retained as long as the documents it describes
```

Every mutation already raises a domain event; reads do not, and reads are what an access audit is
about.

### 7. Full-text search and OCR

The feature users assume exists. The outbox seam is already in place: a `DocumentVersionAdded`
consumer extracts text and feeds an index.

```
Apache Tika        extraction and OCR, and it also solves container detection
PostgreSQL FTS     enough until it is not
OpenSearch         when it is not
```

### 8. Retention, legal hold and destruction

French legal retention runs to 10 years for accounting records, 6 for tax and 5 for payroll — while
GDPR pushes the other way, toward minimisation. The two obligations conflict, and a GED has to hold
both.

```
retention policy per document type, with a computed destruction date
legal hold that suspends every policy, including deletion
destruction with a certificate — proof it happened, and when
```

The blob retention window already implements the mechanism; what is missing is policy above it.

### 9. Permissions beyond deny-by-default ◆

Today authorization is all-or-nothing. A GED needs rights per folder, inherited down the tree, per
role and per action.

```
RessourceCode × ActionCode × scope
inherited from the folder, overridable per document
```

### 10. Previews and thumbnails ◆

Another outbox consumer. Bounded by the same concurrency policy as uploads, because rendering a PDF
is as expensive as transferring one.

## Later — what makes it provable

### 11. Qualified timestamping and a proof chain

Article 1366 of the Code civil gives an electronic document the same probative force as paper under
two cumulative conditions: the author can be identified, and it is kept in conditions guaranteeing
integrity. A SHA-256 proves content has not changed; it does not prove *when* it existed.

```
eIDAS qualified timestamp at deposit
periodic re-sealing before algorithms age
a verifiable chain, produced on demand
```

### 12. Preservation formats

PDF/A (ISO 19005) conversion at deposit, keeping the original alongside. A format that cannot be
opened in fifteen years is an archive in name only, and format preservation is one of the seven NF
Z42-013 domains.

### 13. Reversibility

Being able to leave. A complete export — documents, versions, metadata, audit trail — in an open
format. It is a requirement of the standard and, more practically, the thing every serious buyer asks
about before signing.

## Deliberately out of scope

Worth stating, because a GED is where feature requests go to multiply.

| Not building | Why |
| --- | --- |
| A workflow engine | rarely stabilised early; the outbox is the seam when it is |
| Collaborative editing | a different product |
| Electronic signature | integrate a qualified provider, do not implement one |
| **NF 461 certification** | the standard's auditability requirements are met by a certified operator, not by application code. Build to the norm; certify with a partner |

The last line is the important one. This platform can be built to satisfy NF Z42-013's requirements —
integrity, metadata, retention, destruction, auditability — and that is worth doing. Certifying it is
an organisational undertaking, not a sprint.

## What another product would reuse

Everything marked ◆, plus the parts that are already generic:

```
IObjectStorage + registry + resolver     any product that stores files
StagedContent + upload validation        any product that accepts them
IPersistenceProvider                     any product on two engines
the outbox                               any product that needs events
```

Multi-tenancy is the ◆ above, not a line here: nothing tenant-aware is written yet.

The domain — documents, folders, blobs — is GED-specific and would not travel. That split is the
point of the layout, and it is the reason the ports are where they are.

## License

MIT
