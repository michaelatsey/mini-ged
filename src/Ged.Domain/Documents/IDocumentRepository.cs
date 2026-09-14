using Ged.Domain.Documents.Identifiers;
using MicroKit.Persistence.Abstractions;

namespace Ged.Domain.Documents;

/// <summary>Loads and stores <see cref="Document"/> aggregates.</summary>
/// <remarks>
/// Deliberately limited to loading one aggregate and staging a new one. A repository exists to
/// reconstitute an aggregate so a business rule can be applied to it; the moment it gains a
/// search or paging method it stops being a repository and becomes a data-access layer that
/// every slice starts reaching into. Read models are built with SQL, in the slice that needs
/// them.
/// </remarks>
public interface IDocumentRepository : IRepository<Document>
{
    /// <summary>Loads a document aggregate by identity, including all of its versions.</summary>
    /// <param name="id">The document identifier.</param>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>The document, or null when no such document exists.</returns>
    ValueTask<Document?> FindAsync(DocumentId id, CancellationToken ct = default);
}
