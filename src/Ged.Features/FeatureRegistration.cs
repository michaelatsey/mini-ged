using Ged.Features.Blobs.Jobs;
using Ged.Features.Documents.AddDocumentVersion;
using Ged.Features.Documents.DeleteDocument;
using Ged.Features.Documents.DownloadDocumentContent;
using Ged.Features.Documents.GetDocumentById;
using Ged.Features.Documents.ListDocumentsByFolder;
using Ged.Features.Documents.MoveDocument;
using Ged.Features.Documents.RenameDocument;
using Ged.Features.Documents.UploadDocument;
using Ged.Features.Folders.CreateFolder;
using Ged.Features.Folders.DeleteFolder;
using Ged.Features.Folders.GetFolderById;
using Ged.Features.Folders.ListFolderChildren;
using Ged.Features.Folders.MoveFolder;
using Ged.Features.Folders.RenameFolder;
using Microsoft.Extensions.DependencyInjection;

namespace Ged.Features;

/// <summary>Registers every handler this assembly exposes.</summary>
/// <remarks>
/// Written out, like the routes. A handler added but never registered is then a startup failure that
/// names the missing type, instead of a route that resolves fine until the first request reaches it.
/// </remarks>
public static class FeatureRegistration
{
    /// <summary>Adds the handlers and the maintenance jobs.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGedFeatureHandlers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<UploadDocumentHandler>();
        services.AddScoped<AddDocumentVersionHandler>();
        services.AddScoped<RenameDocumentHandler>();
        services.AddScoped<MoveDocumentHandler>();
        services.AddScoped<DeleteDocumentHandler>();
        services.AddScoped<GetDocumentByIdHandler>();
        services.AddScoped<ListDocumentsByFolderHandler>();
        services.AddScoped<DownloadDocumentContentHandler>();

        services.AddScoped<CreateFolderHandler>();
        services.AddScoped<RenameFolderHandler>();
        services.AddScoped<MoveFolderHandler>();
        services.AddScoped<DeleteFolderHandler>();
        services.AddScoped<GetFolderByIdHandler>();
        services.AddScoped<ListFolderChildrenHandler>();

        services.AddScoped<MarkOrphanBlobsJob>();
        services.AddScoped<PurgeOrphanBlobsJob>();
        services.AddScoped<ReconcileStorageJob>();
        services.AddScoped<ReplicateBlobsJob>();

        return services;
    }
}
