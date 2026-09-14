# ged-platform

Domain model for a document management system (GED), built on
[MicroKit](https://github.com/michaelatsey/microkit).

This repository currently contains **one layer**: `Ged.Domain`. No persistence, no API, no storage
adapter. The domain is being settled first, on purpose — the shape of everything above it follows
from the decisions made here.

```
Ged.Domain          ← this repository, today
Ged.Application     ← vertical slices, later
Ged.Adapters.*      ← EF Core, Dapper, MinIO, later
Ged.Api             ← later
```

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

Both reference other aggregates by identity only. A folder that contained its children would make
the consistency boundary the entire tree, and an unrelated document upload would contend with a
folder rename.

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
```

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
| Deleting a byte from the domain | no API exists |

## Layout

```
src/Ged.Domain/
├── Abstractions/        Actor, GedDomainEvent, AuditableRoot, DomainRules
├── Blobs/Identifiers/   BlobId (content-addressed, SHA-256)
├── Documents/           Document, DocumentVersion, IDocumentRepository, VOs, rules, events
└── Folders/             Folder, FolderAncestry, IFolderRepository, VOs, rules, events

tests/Ged.Domain.Tests/  behaviour + architecture guards
docs/                    deviations from MicroKit, and what the domain cannot guarantee
```

## Build

```bash
dotnet build -c Release
dotnet test
```

`Release` builds with `TreatWarningsAsErrors`, so the build is the first reviewer.

## Reading order

1. `docs/domain-boundaries.md` — what this domain deliberately does not guarantee, and why
2. `src/Ged.Domain/Documents/Document.cs` — the versioning and deletion model
3. `src/Ged.Domain/Folders/FolderAncestry.cs` — how hierarchy rules stay inside the domain
4. `docs/microkit-deviations.md` — the three shims and the upstream changes that remove them

## Status

`Ged.Domain` is complete and builds clean. Everything above it is not written yet.

## License

MIT
