using Microsoft.Extensions.Logging;

namespace Ged.Features.Tests.Fakes;

/// <summary>A logger that keeps what it was given, so a test can assert on structured fields.</summary>
/// <param name="minimumLevel">
/// The lowest level it accepts, as a configured category would. Everything by default; raised, it
/// lets a test run the code path a production level that is switched off takes.
/// </param>
/// <remarks>
/// Entries are kept as their event id and their named properties, never as rendered text: a test on
/// the message would break on a reworded template, while the fields are what a log pipeline indexes —
/// the same reason the tests assert on a rule's type rather than its message.
/// </remarks>
internal sealed class RecordingLogger<T>(LogLevel minimumLevel = LogLevel.Trace) : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= minimumLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);

        Entries.Add(new LogEntry(logLevel, eventId, fields));
    }
}

/// <summary>One recorded entry.</summary>
/// <param name="Level">The level it was written at.</param>
/// <param name="EventId">Its event id, which is what a filter keys on.</param>
/// <param name="Fields">Its structured properties, by template name.</param>
internal sealed record LogEntry(LogLevel Level, EventId EventId, IReadOnlyDictionary<string, object?> Fields);
