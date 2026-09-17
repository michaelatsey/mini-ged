using MicroKit.Core;

namespace Ged.Adapters.Persistence;

/// <summary>The production clock.</summary>
/// <param name="timeProvider">The underlying time source.</param>
/// <remarks>
/// Wraps <see cref="TimeProvider"/> rather than calling <c>DateTimeOffset.UtcNow</c>, so a test can
/// substitute a fake and drive a whole use case to a known instant.
/// </remarks>
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
