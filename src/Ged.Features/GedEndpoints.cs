using Ged.Features.Documents;
using Ged.Features.Folders;

namespace Ged.Features;

/// <summary>Maps every feature module onto a route group supplied by the host.</summary>
/// <remarks>
/// <para>
/// Registration is explicit all the way down: this method calls one per module, each module calls
/// one per slice. No assembly scanning, no <c>IEndpoint</c> convention, no reflection. A reader
/// finds every route by following three calls, the compiler catches a slice that was written but
/// never wired, and nothing here needs explaining to a trimmer.
/// </para>
/// <para>
/// The group is a parameter rather than something this assembly builds. Which versions exist and
/// how a client selects one — URL segment, header, query string — is a decision about the API's
/// shape, and it belongs to the host that composes the application. This assembly keeps the
/// behaviour; the host keeps the contract. It also means the feature layer carries no versioning
/// package, so it depends on ports and the domain and on nothing else.
/// </para>
/// </remarks>
public static class GedEndpoints
{
    /// <summary>Maps version 1 of every feature module.</summary>
    /// <param name="version">The versioned route group to hang the modules off.</param>
    /// <returns>The same group, for chaining.</returns>
    public static RouteGroupBuilder MapGedV1(this RouteGroupBuilder version)
    {
        ArgumentNullException.ThrowIfNull(version);

        DocumentsEndpoints.Map(version);
        FoldersEndpoints.Map(version);

        return version;
    }
}
