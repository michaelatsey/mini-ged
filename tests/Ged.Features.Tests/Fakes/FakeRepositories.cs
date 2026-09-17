using Ged.Domain.Documents;
using Ged.Domain.Folders;

namespace Ged.Features.Tests.Fakes;

/// <summary>An in-memory <see cref="IBlobRepository"/> seeded with the blobs a test needs.</summary>
internal sealed class FakeBlobRepository : IBlobRepository
{
    private readonly Dictionary<string, Blob> _blobs = new(StringComparer.Ordinal);
    private readonly List<Blob> _added = [];

    public IReadOnlyList<Blob> Added => _added;

    public void Seed(Blob blob) => _blobs[blob.Id.Value] = blob;

    public ValueTask<Blob?> FindAsync(BlobId id, CancellationToken ct = default) =>
        ValueTask.FromResult(_blobs.GetValueOrDefault(id.Value));

    public ValueTask AddAsync(Blob aggregate, CancellationToken ct = default)
    {
        _added.Add(aggregate);
        _blobs[aggregate.Id.Value] = aggregate;

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(Blob aggregate, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    public ValueTask DeleteAsync(Blob aggregate, CancellationToken ct = default) =>
        throw new NotSupportedException("A blob is never removed.");

    public ValueTask CommitAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}

/// <summary>An in-memory <see cref="IDocumentRepository"/>.</summary>
internal sealed class FakeDocumentRepository : IDocumentRepository
{
    private readonly List<Document> _documents = [];
    private readonly List<Document> _added = [];

    /// <summary>What the handler staged, which is not the same as what the test seeded.</summary>
    public IReadOnlyList<Document> Added => _added;

    public void Seed(Document document) => _documents.Add(document);

    public ValueTask<Document?> FindAsync(DocumentId id, CancellationToken ct = default) =>
        ValueTask.FromResult(_documents.Find(d => d.Id == id));

    public ValueTask AddAsync(Document aggregate, CancellationToken ct = default)
    {
        _added.Add(aggregate);
        _documents.Add(aggregate);

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateAsync(Document aggregate, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    public ValueTask DeleteAsync(Document aggregate, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    public ValueTask CommitAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}

/// <summary>An <see cref="IFolderRepository"/> holding the single folder an upload targets.</summary>
internal sealed class FakeFolderRepository(Folder folder) : IFolderRepository
{
    public ValueTask<Folder?> FindAsync(FolderId id, CancellationToken ct = default) =>
        ValueTask.FromResult(folder.Id == id ? folder : null);

    public ValueTask<FolderAncestry?> GetAncestryAsync(
        FolderId id, CancellationToken ct = default) =>
        ValueTask.FromResult<FolderAncestry?>(null);

    public ValueTask AddAsync(Folder aggregate, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    public ValueTask UpdateAsync(Folder aggregate, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    public ValueTask DeleteAsync(Folder aggregate, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    public ValueTask CommitAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
}
