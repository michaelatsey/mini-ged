namespace Ged.Features.Tests.Fakes;

/// <summary>
/// Records what the fakes were asked to do, in order. Both defects under test are ordering
/// defects, so the sequence is the assertion — not the final state, which can be identical either
/// way round.
/// </summary>
internal sealed class Journal
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public void Record(string what) => _entries.Add(what);
}
