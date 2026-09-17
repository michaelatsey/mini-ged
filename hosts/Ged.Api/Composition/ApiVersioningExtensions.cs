using Asp.Versioning;
using Ged.Features;
using Scalar.AspNetCore;

namespace Ged.Api.Composition;

/// <summary>Sets up API versioning and the versioned OpenAPI documents.</summary>
/// <remarks>
/// <para>
/// Built on Asp.Versioning 10, the first release made for ASP.NET Core 10 and the built-in
/// <c>Microsoft.AspNetCore.OpenApi</c>. Version 8 still runs through roll-forward, but predates the
/// integration used here and needs several hand-written transformers to produce one document per
/// version.
/// </para>
/// <para>
/// URL-segment versioning is the strategy chosen: <c>/api/v1/documents</c>. It is not the most
/// RESTful option — the same resource arguably should not change address between versions — but it
/// is the one a client can read, log, cache and route on without inspecting headers, and that is
/// worth more here than purity.
/// </para>
/// </remarks>
internal static class ApiVersioningExtensions
{
    /// <summary>The first published version of the API.</summary>
    public static ApiVersion V1 { get; } = new(1, 0);

    /// <summary>Registers versioning, the API explorer, and versioned OpenAPI generation.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGedApiVersioning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = V1;

                // Left off deliberately. Assuming a version for a client that did not ask for one
                // means that the day the default moves, every such client silently changes API —
                // the exact breakage versioning exists to prevent.
                options.AssumeDefaultVersionWhenUnspecified = false;

                // Advertises api-supported-versions and api-deprecated-versions on every response,
                // so a client discovers a deprecation from traffic rather than from a changelog.
                options.ReportApiVersions = true;

                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddApiExplorer(options =>
            {
                // 'v'VVV yields v1, v1.1 — matching the /openapi/v1.json convention the built-in
                // OpenAPI library already uses. Without it the group name is the literal "1.0".
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            })
            // Must come after AddApiVersioning: this is Asp.Versioning's AddOpenApi, not the one
            // from Microsoft.AspNetCore.OpenApi, and only this variant produces versioned documents.
            .AddOpenApi();

        return services;
    }

    /// <summary>Maps every versioned route group and the documentation endpoints.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <remarks>
    /// The host builds the versioned groups and the feature assembly fills them. Adding v2 means one
    /// group here and a <c>MapGedV2</c> beside the existing modules — while v1's slices keep working
    /// untouched, which is the entire point of versioning.
    /// </remarks>
    public static WebApplication MapGedApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var api = app.NewVersionedApi("Ged");

        api.MapGroup("/api/v{version:apiVersion}")
            .HasApiVersion(V1)
            .MapGedV1();

        // One document per version, generated from the versions declared above rather than from a
        // second, hand-maintained list that drifts the first time someone adds a version.
        app.MapOpenApi().WithDocumentPerVersion().AllowAnonymous();

        app.MapScalarApiReference(options =>
        {
            // Fonts otherwise come from Scalar's CDN. A documentation page should not make the
            // browser of anyone who opens it talk to a third party; the system font stack reads
            // perfectly well.
            options.DisableDefaultFonts();

            // The reference also queries Scalar's public API registry for its search box. Nothing
            // here uses it — the documents come from /openapi — and the Content-Security-Policy
            // blocks the call. The console message is the policy working, not a defect.

            var descriptions = app.DescribeApiVersions();

            for (var i = 0; i < descriptions.Count; i++)
            {
                var description = descriptions[i];

                options.AddDocument(
                    description.GroupName,
                    description.GroupName,
                    isDefault: i == descriptions.Count - 1);
            }
        }).AllowAnonymous();

        return app;
    }
}
