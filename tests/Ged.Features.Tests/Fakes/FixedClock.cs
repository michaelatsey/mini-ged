namespace Ged.Features.Tests.Fakes;

/// <summary>A clock that does not move, so an assertion can name the instant it expects.</summary>
/// <param name="now">The instant every handler under test will read.</param>
internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}
