using Ged.Domain.Blobs;
using Ged.Domain.Documents;
using Ged.Domain.Folders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ged.Adapters.Persistence.Converters;

/// <summary>
/// Converters between the domain's strongly-typed values and their column representations.
/// </summary>
/// <remarks>
/// <para>
/// Every converter goes through the domain's own factory rather than a raw constructor, so data
/// read back from the database passes the same validation as data entering through the API. A
/// converter that bypasses validation turns the database into a way to create domain objects the
/// domain would have refused.
/// </para>
/// <para>
/// Codes are stored as text rather than as ordinals. A numeric status is unreadable in a query, and
/// renumbering an enum silently reinterprets every existing row.
/// </para>
/// </remarks>
internal static class GedValueConverters
{
    public static readonly ValueConverter<FolderId, Guid> FolderId =
        new(id => id.Value, value => Ged.Domain.Folders.FolderId.From(value));

    public static readonly ValueConverter<FolderId?, Guid?> NullableFolderId =
        new(id => id!.Value.Value,
            value => value == null ? null : Ged.Domain.Folders.FolderId.From(value.Value));

    public static readonly ValueConverter<DocumentId, Guid> DocumentId =
        new(id => id.Value, value => Ged.Domain.Documents.DocumentId.From(value));

    public static readonly ValueConverter<DocumentVersionId, Guid> DocumentVersionId =
        new(id => id.Value, value => Ged.Domain.Documents.DocumentVersionId.From(value));

    public static readonly ValueConverter<BlobId, string> BlobId =
        new(id => id.Value, value => Domain.Blobs.BlobId.FromSha256(value));

    public static readonly ValueConverter<BlobLocationId, Guid> BlobLocationId =
        new(id => id.Value, value => Domain.Blobs.BlobLocationId.From(value));

    public static readonly ValueConverter<BlobLocationId?, Guid?> NullableBlobLocationId =
        new(id => id!.Value.Value,
            value => value == null ? null : Domain.Blobs.BlobLocationId.From(value.Value));

    public static readonly ValueConverter<Actor, string> Actor =
        new(actor => actor.Value, value => new Actor(value));

    public static readonly ValueConverter<Actor?, string?> NullableActor =
        new(actor => actor == null ? null : actor.Value,
            value => value == null ? null : new Actor(value));

    public static readonly ValueConverter<FolderName, string> FolderName =
        new(name => name.Value, value => new FolderName(value));

    public static readonly ValueConverter<FolderType, string> FolderType =
        new(type => type.Code, value => new FolderType(value));

    public static readonly ValueConverter<DocumentName, string> DocumentName =
        new(name => name.Value, value => new DocumentName(value));

    public static readonly ValueConverter<DocType, string> DocType =
        new(type => type.Code, value => new DocType(value));

    public static readonly ValueConverter<MimeType, string> MimeType =
        new(mime => mime.Value, value => new MimeType(value));

    public static readonly ValueConverter<VersionNumber, int> VersionNumber =
        new(number => number.Value, value => Domain.Documents.VersionNumber.From(value));

    public static readonly ValueConverter<BlobStatus, string> BlobStatus =
        new(status => status.Code, value => Ged.Domain.Blobs.BlobStatus.From(value));

    public static readonly ValueConverter<LocationState, string> LocationState =
        new(state => state.Code, value => Domain.Blobs.LocationState.From(value));

    public static readonly ValueConverter<StorageProvider, string> StorageProvider =
        new(provider => provider.Name, value => new StorageProvider(value));
}
