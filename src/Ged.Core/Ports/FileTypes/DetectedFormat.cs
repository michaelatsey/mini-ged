namespace Ged.Core.Ports.FileTypes;

/// <summary>What a detector concluded the content actually is.</summary>
/// <param name="Extension">The canonical extension of the detected format, lowercase, no dot.</param>
/// <param name="MediaType">The media type the detector reports.</param>
/// <remarks>
/// Deliberately two plain strings. A detector's own format types must not cross this boundary, or
/// replacing the detector would mean touching every caller — which is the one thing a port exists to
/// prevent.
/// </remarks>
public sealed record DetectedFormat(string Extension, string MediaType);
