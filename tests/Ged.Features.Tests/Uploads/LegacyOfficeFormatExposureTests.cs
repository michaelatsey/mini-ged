using System.Text.Json;
using Ged.Features.Common.FileTypes;

namespace Ged.Features.Tests.Uploads;

/// <summary>
/// Holds the shipped upload policy to the claim `CLAUDE.md` makes about it: the four macro-capable
/// legacy formats are excluded by the <em>explicit</em> allowlist, and by nothing else.
/// </summary>
/// <remarks>
/// <para>
/// `.doc`, `.xls`, `.ppt` and `.rtf` are flagged <see cref="FileFormat.CarriesExecutableContent"/>
/// and left out of <c>Ged:Uploads:AllowedFormats</c>. They are not left out of the catalogue's
/// sets: <c>documents</c> expands to all four and <c>office</c> to the first three, while
/// <c>docs/uploads.md</c> recommends writing sets because a policy then reads like its requirement.
/// </para>
/// <para>
/// So the exclusion rests on one editorial choice in one file, and the edit that undoes it —
/// replacing thirteen names with <c>"documents"</c> — looks like a tidy-up rather than a widening.
/// Nothing at startup objects, because admitting these formats is a policy the platform is supposed
/// to allow: the open question in `CLAUDE.md` is whether this deployment wants them, and a test is
/// the wrong place to answer it. What a test can do is refuse to let the answer change silently.
/// </para>
/// </remarks>
public sealed class LegacyOfficeFormatExposureTests
{
    /// <summary>The formats whose exclusion is a security decision rather than a preference.</summary>
    private static readonly string[] MacroCapable = ["doc", "xls", "ppt", "rtf"];

    private static readonly string[] ShippedAllowlist = ReadShippedAllowlist();

    [Fact]
    public void The_catalogue_flags_exactly_the_four_legacy_formats_as_executable()
    {
        // The flag is what makes the other assertions here about risk rather than about taste.
        FileFormats.Known
            .Where(f => f.CarriesExecutableContent)
            .Select(f => f.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ShouldBe(MacroCapable.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void The_shipped_allowlist_admits_no_macro_capable_format()
    {
        var admitted = ShippedAllowlist.SelectMany(FileFormats.Expand).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var format in MacroCapable)
        {
            admitted.ShouldNotContain(
                format,
                $"'{format}' carries executable content. The shipped policy excludes it today, and "
                + "whether this deployment wants it is the open question in CLAUDE.md — if that has "
                + "been answered, change this test deliberately rather than to make a build green.");
        }
    }

    [Fact]
    public void The_shipped_allowlist_names_formats_rather_than_sets()
    {
        // The regression this file exists for. A set is expanded at resolution time, so naming one
        // here hands the decision to whoever next edits the catalogue in code — and `documents`
        // already contains all four. Listing the formats is what keeps the exclusion visible in
        // the diff that would remove it.
        foreach (var entry in ShippedAllowlist)
        {
            FileFormats.Expand(entry).ShouldBe(
                [entry],
                $"'{entry}' must be a format name; a set here widens production without saying so.");
        }
    }

    [Fact]
    public void The_documents_set_would_admit_every_macro_capable_format()
    {
        var documents = FileFormats.Expand("documents").ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var format in MacroCapable)
        {
            documents.ShouldContain(format);
        }
    }

    [Fact]
    public void The_office_set_would_admit_all_of_them_but_rtf()
    {
        var office = FileFormats.Expand("office").ToHashSet(StringComparer.OrdinalIgnoreCase);

        office.ShouldContain("doc");
        office.ShouldContain("xls");
        office.ShouldContain("ppt");

        // `.rtf` is macro-capable for a different reason — embedded OLE objects — and is not an
        // Office format, so the two sets do not cover the same risk.
        office.ShouldNotContain("rtf");
    }

    /// <summary>Reads <c>Ged:Uploads:AllowedFormats</c> from the API's shipped configuration.</summary>
    private static string[] ReadShippedAllowlist()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(RepositoryFile(Path.Combine("hosts", "Ged.Api", "appsettings.json"))),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        return [.. document.RootElement
            .GetProperty("Ged").GetProperty("Uploads").GetProperty("AllowedFormats")
            .EnumerateArray()
            .Select(e => e.GetString()!)];
    }

    /// <summary>Locates a file at the repository root, walking up from the test binaries.</summary>
    private static string RepositoryFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MiniGed.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("The repository root was not found above the test binaries.");

        return Path.Combine(directory.FullName, name);
    }
}
