using Ged.Features.Documents.AddDocumentVersion;
using Ged.Features.Documents.DeleteDocument;
using Ged.Features.Documents.DownloadDocumentContent;
using Ged.Features.Documents.GetDocumentById;
using Ged.Features.Documents.ListDocumentsByFolder;
using Ged.Features.Documents.MoveDocument;
using Ged.Features.Documents.RenameDocument;
using Ged.Features.Documents.UploadDocument;

namespace Ged.Features.Documents;

/// <summary>Registers every document route, explicitly.</summary>
/// <remarks>
/// One call per slice, written out. No assembly scanning and no <c>IEndpoint</c> convention: a
/// reader finds every route this module exposes by reading one method, the compiler catches a slice
/// that was never wired, and nothing depends on reflection that trimming and AOT have to be told
/// about.
/// </remarks>
internal static class DocumentsEndpoints
{
    /// <summary>Maps the document routes.</summary>
    /// <param name="version">The versioned route group.</param>
    public static void Map(RouteGroupBuilder version)
    {
        var documents = version.MapGroup("/documents").WithTags("Documents");

        UploadDocumentEndpoint.Map(documents);
        AddDocumentVersionEndpoint.Map(documents);
        RenameDocumentEndpoint.Map(documents);
        MoveDocumentEndpoint.Map(documents);
        DeleteDocumentEndpoint.Map(documents);
        GetDocumentByIdEndpoint.Map(documents);
        ListDocumentsByFolderEndpoint.Map(documents);
        DownloadDocumentContentEndpoint.Map(documents);
    }
}
