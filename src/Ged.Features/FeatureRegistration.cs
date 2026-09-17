using Ged.Features.Blobs.Jobs;
using Ged.Features.Common.FileTypes;
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
using Microsoft.Extensions.Configuration;
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
    /// <param name="configuration">Supplies the upload policy.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGedFeatureHandlers(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Validated on first use and at startup: a typo in a format name stops the deployment
        // rather than silently changing what the API accepts.
        services.AddOptions<UploadPolicyOptions>()
            .Bind(configuration.GetSection(UploadPolicyOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                policy => policy.AllowedFormats.All(FileFormats.IsKnown),
                "Ged:Uploads:AllowedFormats contains an entry this application does not know. "
                    + "Known formats: " + string.Join(", ", FileFormats.Known.Select(f => f.Name))
                    + ". Known sets: " + string.Join(", ", FileFormats.GroupNames))
            .Validate(
                policy => policy.FormatsByDocType.Values
                    .SelectMany(names => names)
                    .All(FileFormats.IsKnown),
                "Ged:Uploads:FormatsByDocType references an unknown format.")
            .ValidateOnStart();

        services.AddSingleton<IFileTypeInspector, FileTypeInspector>();

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
