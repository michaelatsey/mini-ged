
namespace Ged.Domain.Tests;

/// <summary>
/// Fixed values shared by the tests. The instants are constants rather than
/// <c>DateTimeOffset.UtcNow</c> so assertions can compare exactly — which is the whole point of
/// the aggregates taking the instant as a parameter.
/// </summary>
internal static class Fixed
{
    public static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Later = Now.AddHours(1);
    public static readonly DateTimeOffset Latest = Now.AddHours(2);
    public static readonly Actor Me = new("u-42");
}
