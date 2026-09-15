namespace Ged.Features.Common;

/// <summary>Names of the policies a slice asks for and the host defines.</summary>
/// <remarks>
/// The slice declares what kind of work it does; the host decides what that costs. An upload endpoint
/// states that it transfers content — it does not state a concurrency limit or a timeout, because
/// those depend on the deployment, not on the use case.
/// </remarks>
public static class GedPolicies
{
    /// <summary>Endpoints that stream file content in or out.</summary>
    public const string Content = "content";
}
