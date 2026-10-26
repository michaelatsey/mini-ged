using System.IO.Compression;
using System.Text;
using Ged.Adapters.FileTypes;
using Ged.Features.Common.FileTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ged.Features.Tests.Uploads;

/// <summary>
/// Names holding a dot that introduces no extension, through the real built-in detector (#51).
/// </summary>
/// <remarks>
/// <para>
/// What follows the last dot is an extension only when it is 1 to 10 ASCII letters or digits, at least
/// one of them a letter. Anything else leaves the name extensionless, and the content decides as it
/// does for any bare name.
/// </para>
/// <para>
/// The rule is read twice — by the inspector, to know what the name claims, and by
/// <see cref="FileTypeDecision.StoredNameFor"/>, to know what to replace. Fixing only the first turns a
/// refusal into a stored name missing its end, which is why both readings are pinned here.
/// </para>
/// </remarks>
public sealed class ExtensionPlausibilityTests
{
    /// <summary>
    /// A name of the shape a calling application produces when it shortens a long title with
    /// <c>(...)</c>: the last dot sits inside the ellipsis, far from the end of the name.
    /// </summary>
    private const string Shortened = "Bilan … servic(...) - du 01_01_2026 au 19_10_2026";

    /// <summary>The table of #51: name, content, expected outcome, expected stored name.</summary>
    public static TheoryData<string, string, RejectionReason, string?> Table => new()
    {
        { Shortened, "xlsx", RejectionReason.None, Shortened + ".xlsx" },
        { "rapport v1.2", "pdf", RejectionReason.None, "rapport v1.2.pdf" },
        { "facture.pdf.exe", "pdf", RejectionReason.ExtensionNotAllowed, null },
        { "pintade.jpg", "png", RejectionReason.None, "pintade.png" },
        { "rapport.", "pdf", RejectionReason.None, "rapport.pdf" },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void Each_name_of_the_issue_table_is_decided_by_the_plausible_extension_rule(
        string fileName, string content, RejectionReason expected, string? storedName)
    {
        var policy = Policy(allowMissingExtension: true);

        CheckName(policy, fileName).Reason.ShouldBe(expected);

        var decision = Inspect(policy, fileName, Fixture(content));

        decision.Reason.ShouldBe(expected);

        if (storedName is not null)
        {
            decision.StoredNameFor(fileName).ShouldBe(storedName);
        }
    }

    [Theory]
    [InlineData(Shortened, "xlsx")]
    [InlineData("rapport v1.2", "pdf")]
    public void A_name_with_no_plausible_extension_is_refused_as_extensionless_when_the_flag_is_off(
        string fileName, string content)
    {
        var policy = Policy(allowMissingExtension: false);

        CheckName(policy, fileName).Reason.ShouldBe(RejectionReason.NoExtension);
        Inspect(policy, fileName, Fixture(content)).Reason.ShouldBe(RejectionReason.NoExtension);
    }

    [Theory]
    [InlineData("Mr. Smith")]             // a space
    [InlineData("bilan.2026")]            // digits only: a version or a year, not an extension
    [InlineData("bilan.abcdefghijk")]     // eleven characters
    [InlineData("bilan.été")]             // not ASCII
    public void A_tail_outside_the_rule_is_not_read_as_an_extension(string fileName)
    {
        // With the flag off, an extensionless name is NoExtension; a name read as carrying an
        // unknown extension would be ExtensionNotAllowed instead.
        var policy = Policy(allowMissingExtension: false);

        CheckName(policy, fileName).Reason.ShouldBe(RejectionReason.NoExtension);
    }

    [Theory]
    [InlineData("bilan.abcdefghij")]      // ten characters, the longest still read as one
    [InlineData("bilan.v2")]              // a letter is enough
    public void A_tail_inside_the_rule_is_read_as_an_extension(string fileName)
    {
        var policy = Policy(allowMissingExtension: false);

        CheckName(policy, fileName).Reason.ShouldBe(RejectionReason.ExtensionNotAllowed);
    }

    [Theory]
    [InlineData(Shortened, Shortened + ".xlsx")]
    [InlineData("rapport v1.2", "rapport v1.2.xlsx")]
    [InlineData("Mr. Smith", "Mr. Smith.xlsx")]
    [InlineData("bilan.2026", "bilan.2026.xlsx")]
    [InlineData("bilan.abcdefghijk", "bilan.abcdefghijk.xlsx")]
    [InlineData("bilan", "bilan.xlsx")]
    [InlineData("bilan.", "bilan.xlsx")]
    [InlineData("bilan.pdf", "bilan.xlsx")]
    [InlineData("bilan.PDF", "bilan.xlsx")]
    [InlineData("bilan.abcdefghij", "bilan.xlsx")]
    [InlineData("bilan.2026.pdf", "bilan.2026.xlsx")]
    public void The_stored_name_replaces_a_plausible_extension_and_appends_otherwise(
        string fileName, string storedName)
    {
        FileFormats.TryGet("xlsx", out var xlsx).ShouldBeTrue();

        var decision = FileTypeDecision.Accept(xlsx!, reclassified: true);

        decision.StoredNameFor(fileName).ShouldBe(storedName);
    }

    /// <summary>The shipped allowlist and ceilings, with the extensionless flag as the test needs it.</summary>
    private static UploadPolicyOptions Policy(bool allowMissingExtension) => new()
    {
        AllowedFormats = ["pdf", "docx", "xlsx", "pptx", "odt", "ods", "odp",
                          "txt", "csv", "jpeg", "png", "tiff", "bmp"],
        ReclassifiableGroups = ["images"],
        AllowMissingExtension = allowMissingExtension,
        MaxSizeBytes = 256L * 1024 * 1024,
        MaxSizeByFormat = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["txt"] = 10L * 1024 * 1024,
            ["csv"] = 10L * 1024 * 1024,
            ["png"] = 25L * 1024 * 1024,
            ["jpeg"] = 25L * 1024 * 1024,
            ["bmp"] = 25L * 1024 * 1024,
            ["tiff"] = 50L * 1024 * 1024,
        },
    };

    private static FileTypeInspector Inspector(UploadPolicyOptions policy) => new(
        new BuiltInContentFormatDetector(),
        Options.Create(policy),
        NullLogger<FileTypeInspector>.Instance);

    private static FileTypeDecision CheckName(UploadPolicyOptions policy, string fileName) =>
        Inspector(policy).CheckName(fileName, declaredMediaType: null, docType: null);

    private static FileTypeDecision Inspect(UploadPolicyOptions policy, string fileName, byte[] bytes) =>
        Inspector(policy).Inspect(
            fileName,
            declaredMediaType: null,
            bytes.Length,
            bytes.AsSpan(0, Math.Min(bytes.Length, FileTypeInspector.HeaderLength)),
            () => new MemoryStream(bytes, writable: false),
            docType: null);

    private static byte[] Fixture(string format) => format switch
    {
        "pdf" => "%PDF-1.7\n1 0 obj\n<< >>\nendobj\ntrailer\n<< >>\n%%EOF\n"u8.ToArray(),
        "png" => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52],
        "xlsx" => Workbook(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "No fixture for this format."),
    };

    /// <summary>The smallest Office Open XML workbook the detector accepts: a manifest and its main part.</summary>
    private static byte[] Workbook()
    {
        const string mainPart = "xl/workbook.xml";

        var manifest =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + $"<Override PartName=\"/{mainPart}\" "
            + "ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
            + "</Types>";

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in new[]
            {
                ("[Content_Types].xml", manifest),
                (mainPart, "<?xml version=\"1.0\" encoding=\"UTF-8\"?><root/>"),
            })
            {
                using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                stream.Write(Encoding.ASCII.GetBytes(content));
            }
        }

        return buffer.ToArray();
    }
}
