
using FileSignatures;
using Ged.Core.Ports.FileTypes;
using Microsoft.Extensions.DependencyInjection;

namespace Ged.Adapters.FileTypes.FileSignatures;

/// <summary>Registers the file signatures adapter.</summary>
public static class FileSignaturesServiceCollectionExtensions
{
    /// <summary>Adds the file signatures adapter.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGedFileSignaturesDetector(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddSingleton<IFileFormatInspector>(_ => new FileFormatInspector())
            .AddSingleton<IContentFormatDetector, FileSignaturesContentDetector>();

        return services;
    }
}
