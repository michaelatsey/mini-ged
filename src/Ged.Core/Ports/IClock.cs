namespace Ged.Core.Ports;

/// <summary>Supplies the current instant.</summary>
/// <remarks>
/// Aggregates take the instant as a parameter rather than reading it, so this port exists for the
/// callers that have to obtain it — handlers and background jobs. Keeping it behind an interface is
/// what lets a test drive a whole use case against a fixed instant, which is the only way an
/// assertion on <c>CreatedAt</c> or on a retention cutoff can be exact.
/// </remarks>
public interface IClock
{
    /// <summary>Gets the current instant, in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
