using Ged.Features.Documents;
using Ged.Features.Folders;

namespace Ged.Features;

/// <summary>The single entry point the host calls to expose every feature route.</summary>
/// <remarks>
/// <para>
/// Registration is explicit all the way down: this method calls one per module, each module calls
/// one per slice. No assembly scanning, no <c>IEndpoint</c> convention, no reflection.
/// </para>
/// <para>
/// The reason is not taste. A reader finds every route by following three method calls; the compiler
/// catches a slice that was written but never wired; and nothing here needs to be explained to a
/// trimmer or an AOT compiler. Auto-discovery would buy none of that and cost all of it.
/// </para>
/// <para>
/// Decoupling comes from the direction of dependencies, not from how registration happens. The host
/// knows this assembly; this assembly knows only ports and the domain.
/// </para>
/// </remarks>
public static class GedEndpoints
{
    /// <summary>Maps every feature route.</summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapGedEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        DocumentsEndpoints.Map(app);
        FoldersEndpoints.Map(app);

        return app;
    }
}
