namespace Ged.Features.Tests;

/// <summary>Locates the files in the repository that tests read rather than construct.</summary>
/// <remarks>
/// Three classes assert against shipped configuration — the upload allowlist, the compose budget, the
/// reclassifiable sets — and each had walked up from the test binaries on its own. The walk lives here
/// once so that renaming the solution file, or finding the root another way, is one edit instead of
/// three that pass if one is missed.
/// </remarks>
internal static class Repository
{
    /// <summary>The absolute path of a file named relative to the repository root.</summary>
    /// <param name="relativePath">The path, relative to the root.</param>
    /// <returns>The absolute path.</returns>
    public static string FileAt(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MiniGed.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("The repository root was not found above the test binaries.");

        return Path.Combine(directory.FullName, relativePath);
    }
}
