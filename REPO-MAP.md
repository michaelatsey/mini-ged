# REPO MAP — mini-ged

Generated 2026-09-17 from commit `680fa01`.
Regenerate with `./scripts/repo-map.sh > REPO-MAP.md` — never edit by hand.

---

## Projects

```
Ged.Migrations                                 1 files     133 lines
Ged.Api                                        6 files     550 lines
MicroKit.Core                                  1 files      14 lines
MicroKit.Domain                               36 files    1782 lines
MicroKit.Persistence.Abstractions             10 files     343 lines
MicroKit.Persistence.EntityFrameworkCore.PostgreSql   2 files      36 lines
MicroKit.Persistence.EntityFrameworkCore.SqlServer   2 files      36 lines
MicroKit.Persistence.EntityFrameworkCore      11 files     819 lines
MicroKit.Persistence.Specifications            3 files     171 lines
MicroKit.Persistence                           7 files     478 lines
MicroKit.Result.AspNetCore                     2 files     135 lines
MicroKit.Result                               29 files    2726 lines
Ged.Adapters.FileTypes.FileSignatures          2 files      77 lines
Ged.Adapters.FileTypes                         2 files     231 lines
Ged.Adapters.Persistence.PostgreSql            3 files     152 lines
Ged.Adapters.Persistence.SqlServer             3 files     161 lines
Ged.Adapters.Persistence                      21 files    1131 lines
Ged.Adapters.Storage.FileSystem                2 files     204 lines
Ged.Adapters.Storage                           2 files      99 lines
Ged.Core                                       9 files     275 lines
Ged.Domain                                    51 files    2416 lines
Ged.Features                                  47 files    3193 lines
Ged.Domain.Tests                              10 files     718 lines
```

## Reference graph

```
Ged.Migrations
    packages: 3
Ged.Api
    -> Ged.Adapters.FileTypes Ged.Adapters.FileTypes.FileSignatures Ged.Adapters.Persistence.PostgreSql Ged.Adapters.Persistence.SqlServer Ged.Adapters.Storage.FileSystem Ged.Features 
    packages: 7
MicroKit.Core
MicroKit.Domain
MicroKit.Persistence.Abstractions
    -> MicroKit.Domain 
MicroKit.Persistence.EntityFrameworkCore.PostgreSql
    -> MicroKit.Persistence.EntityFrameworkCore 
    packages: 1
MicroKit.Persistence.EntityFrameworkCore.SqlServer
    -> MicroKit.Persistence.EntityFrameworkCore 
    packages: 1
MicroKit.Persistence.EntityFrameworkCore
    -> MicroKit.Persistence 
    packages: 3
MicroKit.Persistence.Specifications
    -> MicroKit.Persistence 
MicroKit.Persistence
    -> MicroKit.Persistence.Abstractions 
MicroKit.Result.AspNetCore
    -> MicroKit.Result 
MicroKit.Result
Ged.Adapters.FileTypes.FileSignatures
    -> Ged.Core 
    packages: 2
Ged.Adapters.FileTypes
    -> Ged.Core 
    packages: 1
Ged.Adapters.Persistence.PostgreSql
    -> Ged.Adapters.Persistence 
    packages: 3
Ged.Adapters.Persistence.SqlServer
    -> Ged.Adapters.Persistence 
    packages: 3
Ged.Adapters.Persistence
    -> Ged.Domain MicroKit.Core 
    packages: 4
Ged.Adapters.Storage.FileSystem
    -> Ged.Adapters.Storage 
Ged.Adapters.Storage
    -> Ged.Core 
Ged.Core
    -> Ged.Domain 
Ged.Domain
    -> MicroKit.Domain MicroKit.Persistence.Abstractions 
Ged.Features
    -> Ged.Core Ged.Domain MicroKit.Core 
Ged.Domain.Tests
    -> Ged.Domain 
    packages: 4
```

## Layout

```
.devcontainer/devcontainer.json
.devcontainer/docker-compose.yml
.github/workflows
database/Ged.Migrations
docs/api.md
docs/containers.md
docs/domain-boundaries.md
docs/microkit-deviations.md
docs/persistence-providers.md
docs/registration.md
docs/uploads.md
hosts/Ged.Api
libraries/MicroKit.Core
libraries/MicroKit.Domain
libraries/MicroKit.Persistence
libraries/MicroKit.Result
scripts/smoke.sh
src/Ged.Adapters.FileTypes
src/Ged.Adapters.FileTypes.FileSignatures
src/Ged.Adapters.Persistence
src/Ged.Adapters.Persistence.PostgreSql
src/Ged.Adapters.Persistence.SqlServer
src/Ged.Adapters.Storage
src/Ged.Adapters.Storage.FileSystem
src/Ged.Core
src/Ged.Domain
src/Ged.Features
tests/Ged.Domain.Tests
```

## Domain surface

Aggregates, entities and the rules they enforce.

```
Ged.Domain/Blobs/Blob.cs
Ged.Domain/Blobs/BlobLocation.cs
Ged.Domain/Blobs/Rules/BlobRules.cs
Ged.Domain/Documents/Document.cs
Ged.Domain/Documents/DocumentVersion.cs
Ged.Domain/Documents/Rules/DocumentMustBeDeletedToRestoreRule.cs
Ged.Domain/Documents/Rules/DocumentMustNotBeDeletedRule.cs
Ged.Domain/Documents/Rules/VersionContentMustDifferFromCurrentRule.cs
Ged.Domain/Documents/Rules/VersionMustBelongToDocumentRule.cs
Ged.Domain/Documents/Rules/VersionMustNotAlreadyBeCurrentRule.cs
Ged.Domain/Folders/Folder.cs
Ged.Domain/Folders/Rules/FolderDepthMustNotExceedLimitRule.cs
Ged.Domain/Folders/Rules/FolderMustBeDeletedToRestoreRule.cs
Ged.Domain/Folders/Rules/FolderMustBeEmptyToDeleteRule.cs
Ged.Domain/Folders/Rules/FolderMustNotBeDeletedRule.cs
Ged.Domain/Folders/Rules/FolderMustNotBeItsOwnParentRule.cs
Ged.Domain/Folders/Rules/FolderMustNotMoveIntoItsOwnSubtreeRule.cs
```

## Ports

```
IClock                           libraries/MicroKit.Core/src/MicroKit.Core/IClock.cs
IAggregateRoot                   libraries/MicroKit.Domain/Aggregates/IAggregateRoot.cs
IAuditableEntity                 libraries/MicroKit.Domain/Aggregates/IAuditableEntity.cs
IDomainEvent                     libraries/MicroKit.Domain/Events/IDomainEvent.cs
IDomainEventsProvider            libraries/MicroKit.Domain/Events/IDomainEventsProvider.cs
IEvent                           libraries/MicroKit.Domain/Events/IEvent.cs
IHasDomainEvents                 libraries/MicroKit.Domain/Events/IHasDomainEvents.cs
IEntityId                        libraries/MicroKit.Domain/Identifiers/IEntityId.cs
IBusinessRule                    libraries/MicroKit.Domain/Rules/IBusinessRule.cs
IDomainService                   libraries/MicroKit.Domain/Services/IDomainService.cs
ISpecification                   libraries/MicroKit.Domain/Specifications/ISpecification.cs
IValueObject                     libraries/MicroKit.Domain/ValueObjects/IValueObject.cs
IDbConnectionFactory             libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/IDbConnectionFactory.cs
IPagedResult                     libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/Pagination/IPagedResult.cs
IReadRepository                  libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/Repositories/IReadRepository.cs
IRepository                      libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/Repositories/IRepository.cs
ITransaction                     libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/Transactions/ITransaction.cs
ITransactionManager              libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/Transactions/ITransactionManager.cs
ITransactionalContext            libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/Transactions/ITransactionalContext.cs
IUnitOfWork                      libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/Transactions/IUnitOfWork.cs
ITransactionalUnitOfWork         libraries/MicroKit.Persistence/src/MicroKit.Persistence.EntityFrameworkCore/Transactions/ITransactionalUnitOfWork.cs
IReadRepository                  libraries/MicroKit.Persistence/src/MicroKit.Persistence/Repositories/IReadRepository.cs
ISpecificationEvaluator          libraries/MicroKit.Persistence/src/MicroKit.Persistence/Specifications/ISpecificationEvaluator.cs
IError                           libraries/MicroKit.Result/src/MicroKit.Result/Errors/IError.cs
IValidationError                 libraries/MicroKit.Result/src/MicroKit.Result/Validation/IValidationError.cs
IPersistenceProvider             Ged.Adapters.Persistence/Providers/IPersistenceProvider.cs
IContentFormatDetector           Ged.Core/Ports/FileTypes/IContentFormatDetector.cs
IBlobLocationResolver            Ged.Core/Ports/IBlobLocationResolver.cs
IObjectStorage                   Ged.Core/Ports/IObjectStorage.cs
IObjectStorageRegistry           Ged.Core/Ports/IObjectStorageRegistry.cs
IPresignCapable                  Ged.Core/Ports/IPresignCapable.cs
IBlobRepository                  Ged.Domain/Blobs/IBlobRepository.cs
IDocumentRepository              Ged.Domain/Documents/IDocumentRepository.cs
IFolderRepository                Ged.Domain/Folders/IFolderRepository.cs
IFileTypeInspector               Ged.Features/Common/FileTypes/IFileTypeInspector.cs
```

## HTTP endpoints

```
Post    /{id:guid}/versions                          AddDocumentVersionEndpoint
Delete  /{id:guid}                                   DeleteDocumentEndpoint
Get     /{id:guid}/content                           DownloadDocumentContentEndpoint
Get     /{id:guid}                                   GetDocumentByIdEndpoint
Get     /                                            ListDocumentsByFolderEndpoint
Patch   /{id:guid}/folder                            MoveDocumentEndpoint
Patch   /{id:guid}/name                              RenameDocumentEndpoint
Post    /                                            UploadDocumentEndpoint
Post    /                                            CreateFolderEndpoint
Delete  /{id:guid}                                   DeleteFolderEndpoint
Get     /{id:guid}                                   GetFolderByIdEndpoint
Get     /                                            ListFolderChildrenEndpoint
Patch   /{id:guid}/parent                            MoveFolderEndpoint
Patch   /{id:guid}/name                              RenameFolderEndpoint
```

## Database scripts

```
PostgreSql/0001_create_folder.sql
PostgreSql/0002_create_blob.sql
PostgreSql/0003_create_blob_location.sql
PostgreSql/0004_create_document.sql
PostgreSql/0005_create_document_version.sql
PostgreSql/0006_create_outbox.sql
PostgreSql/0007_seed_root_folder.sql
SqlServer/0001_create_folder.sql
SqlServer/0002_create_blob.sql
SqlServer/0003_create_blob_location.sql
SqlServer/0004_create_document.sql
SqlServer/0005_create_document_version.sql
SqlServer/0006_create_outbox.sql
SqlServer/0007_seed_root_folder.sql
```

## Documentation

```
README.md                                mini-ged
docs/api.md                              The API surface
docs/containers.md                       Containers
docs/domain-boundaries.md                What this domain does not guarantee
docs/microkit-deviations.md              Deviations from MicroKit.Domain
docs/persistence-providers.md            Persistence: what differs between PostgreSQL and SQL Server
docs/registration.md                     Choosing a detector
docs/uploads.md                          Restricting what can be uploaded
src/Ged.Adapters.FileTypes/registration.md ```csharp
```

## Recent history

```
680fa01 feat: mini-ged — document management platform on .NET 10
747b270 feat(api): versioned composition root with OpenAPI, security and throttling
74d0335 feat(features): vertical slices, storage ports and a filesystem backend
3566a4b refactor(persistence): multi-provider PostgreSQL and SQL Server, drop obsolete xmin API
2329518 feat(persistence): EF Core write side, Dapper reads, DbUp schema, outbox
e78d58e feat(domain): blob aggregate with locations and retention lifecycle
7467c83 feat(domain): document and folder aggregates
```
