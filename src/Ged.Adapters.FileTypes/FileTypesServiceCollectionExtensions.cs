
using Ged.Core.Ports.FileTypes;
using Microsoft.Extensions.DependencyInjection;

namespace Ged.Adapters.FileTypes;

/// <summary>Registers the file signatures adapter.</summary>
public static class FileTypesServiceCollectionExtensions
{
    /// <summary>Adds the file signatures adapter.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGedFileTypesBuiltInDetector(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.
            AddSingleton<IContentFormatDetector, BuiltInContentFormatDetector>();
        return services;
    }
}
