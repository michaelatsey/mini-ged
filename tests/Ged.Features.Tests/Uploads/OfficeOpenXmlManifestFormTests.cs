using System.IO.Compression;
using System.Text;
using Ged.Adapters.FileTypes;
using Ged.Core.Ports.FileTypes;
using Ged.Features.Common.FileTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ged.Features.Tests.Uploads;

/// <summary>How a package's manifest gives its main part a content type.</summary>
public enum ManifestForm
{
    /// <summary>An <c>Override</c> keyed by the main part's name — what Office writes.</summary>
    Override,

    /// <summary>
    /// A <c>Default</c> keyed by the <c>xml</c> extension, and no override for the main part — what a
    /// package written through <c>System.IO.Packaging</c> may carry.
    /// </summary>
    Default,

    /// <summary>Both, agreeing.</summary>
    Both,
}

/// <summary>
/// Office Open XML packages in every form the content-types manifest allows, through the real
/// built-in detector.
/// </summary>
/// <remarks>
/// <para>
/// OPC gives a part its content type from an <c>Override</c> naming it, or failing that from the
/// <c>Default</c> for its extension. The detector used to read overrides only, and every fixture in
/// this suite was built with one — so a valid workbook typed by a <c>Default</c> was reported as a
/// plain ZIP and refused, and nothing could have noticed (#48).
/// </para>
/// <para>
/// The fixtures here take the form as a parameter so that a form the detector does not handle fails a
/// row, rather than never being built.
/// </para>
/// </remarks>
public sealed class OfficeOpenXmlManifestFormTests
{
    private const string WordMain = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private const string ExcelMain = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string PowerPointMain = "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml";
    private const string WordMacroMain = "application/vnd.ms-word.document.macroEnabled.main+xml";
    private const string ExcelMacroMain = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";
    private const string PowerPointMacroMain = "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml";

    private const string ExtendedProperties = "application/vnd.openxmlformats-officedocument.extended-properties+xml";

    private const string Xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><root/>";

    [Theory]
    [InlineData("docx", ManifestForm.Override)]
    [InlineData("docx", ManifestForm.Default)]
    [InlineData("docx", ManifestForm.Both)]
    [InlineData("xlsx", ManifestForm.Override)]
    [InlineData("xlsx", ManifestForm.Default)]
    [InlineData("xlsx", ManifestForm.Both)]
    [InlineData("pptx", ManifestForm.Override)]
    [InlineData("pptx", ManifestForm.Default)]
    [InlineData("pptx", ManifestForm.Both)]
    public void Each_family_is_detected_whichever_form_types_its_main_part(string format, ManifestForm form)
    {
        var (mainPart, contentType) = MainOf(format);

        Detect(Package(mainPart, contentType, form)).Extension.ShouldBe(format);
    }

    [Theory]
    [InlineData(ManifestForm.Override)]
    [InlineData(ManifestForm.Default)]
    public void The_manifest_is_read_without_regard_to_case(ManifestForm form)
    {
        // OPC compares extensions and part names without case, and a media type's type and subtype are
        // case-insensitive. A producer writing either in another case writes the same manifest.
        var bytes = Package(
            "xl/Workbook.xml",
            ExcelMain.ToUpperInvariant(),
            form,
            defaultExtension: "XML",
            overridePartName: "/XL/WORKBOOK.XML");

        Detect(bytes).Extension.ShouldBe("xlsx");
    }

    [Theory]
    [InlineData("word/document.xml", WordMacroMain, "docm")]
    [InlineData("xl/workbook.xml", ExcelMacroMain, "xlsm")]
    [InlineData("ppt/presentation.xml", PowerPointMacroMain, "pptm")]
    public void A_macro_enabled_main_type_given_by_a_default_is_reported_as_macro_enabled(
        string mainPart, string contentType, string expected)
    {
        Detect(Package(mainPart, contentType, ManifestForm.Default)).Extension.ShouldBe(expected);
    }

    [Fact]
    public void A_vba_project_beside_a_default_typed_main_part_is_reported_as_macro_enabled()
    {
        var bytes = Package(
            "xl/workbook.xml",
            ExcelMain,
            ManifestForm.Default,
            extraEntries: [("xl/vbaProject.bin", "vba")]);

        Detect(bytes).Extension.ShouldBe("xlsm");
    }

    [Fact]
    public void Main_parts_of_two_families_name_no_document()
    {
        // The Default makes word/document.xml a Word main part; the Override makes xl/workbook.xml an
        // Excel one. A file claiming two formats at once is neither.
        var manifest = Manifest(
            "<Default Extension=\"xml\" ContentType=\"" + WordMain + "\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"" + ExcelMain + "\"/>");

        var bytes = Zip(
            ("[Content_Types].xml", manifest),
            ("word/document.xml", Xml),
            ("xl/workbook.xml", Xml));

        Detect(bytes).Extension.ShouldBe("zip");
    }

    [Fact]
    public void A_part_with_an_override_is_not_typed_by_the_default()
    {
        // The Default would make every .xml part a Word main part. docProps/app.xml has its own,
        // non-main override, so the only main part is the workbook the other override names. A rule
        // that let the default reach an overridden part would find two families and answer zip.
        var manifest = Manifest(
            "<Default Extension=\"xml\" ContentType=\"" + WordMain + "\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"" + ExcelMain + "\"/>"
            + "<Override PartName=\"/docProps/app.xml\" ContentType=\"" + ExtendedProperties + "\"/>");

        var bytes = Zip(
            ("[Content_Types].xml", manifest),
            ("docProps/app.xml", Xml),
            ("xl/workbook.xml", Xml));

        Detect(bytes).Extension.ShouldBe("xlsx");
    }

    [Fact]
    public void A_conflicting_declaration_that_types_no_main_part_does_not_refuse_the_document()
    {
        // Invalid OPC, but it cannot change what the package is: the key simply types nothing.
        var bytes = Package(
            "word/document.xml",
            WordMain,
            ManifestForm.Override,
            extraDeclarations:
                "<Default Extension=\"png\" ContentType=\"image/png\"/>"
                + "<Default Extension=\"png\" ContentType=\"image/jpeg\"/>",
            extraEntries: [("word/media/image1.png", "png")]);

        Detect(bytes).Extension.ShouldBe("docx");
    }

    [Fact]
    public void A_conflicting_declaration_of_a_main_type_names_no_document()
    {
        // The workbook is a main part under one reading and not under the other.
        var bytes = Package(
            "xl/workbook.xml",
            ExcelMain,
            ManifestForm.Override,
            extraDeclarations: "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/xml\"/>");

        Detect(bytes).Extension.ShouldBe("zip");
    }

    [Fact]
    public void An_override_naming_an_absent_part_proves_nothing()
    {
        var manifest = Manifest(
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"" + ExcelMain + "\"/>");

        Detect(Zip(("[Content_Types].xml", manifest), ("xl/other.xml", Xml))).Extension.ShouldBe("zip");
    }

    [Fact]
    public void The_manifest_is_not_a_part_its_own_default_can_type()
    {
        // "[Content_Types].xml" ends in .xml, but OPC does not count it as a part. A Default for xml
        // must not turn the manifest alone into an Excel workbook.
        var manifest = Manifest("<Default Extension=\"xml\" ContentType=\"" + ExcelMain + "\"/>");

        Detect(Zip(("[Content_Types].xml", manifest))).Extension.ShouldBe("zip");
    }

    [Fact]
    public void A_bare_workbook_typed_by_a_default_is_accepted_and_named_xlsx()
    {
        var bytes = Package("xl/workbook.xml", ExcelMain, ManifestForm.Default);

        var decision = Inspect("bilan", declared: null, bytes);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.Format!.Name.ShouldBe("xlsx");
        decision.StoredNameFor("bilan").ShouldBe("bilan.xlsx");
    }

    private static (string MainPart, string ContentType) MainOf(string format) => format switch
    {
        "docx" => ("word/document.xml", WordMain),
        "xlsx" => ("xl/workbook.xml", ExcelMain),
        "pptx" => ("ppt/presentation.xml", PowerPointMain),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not an Office Open XML format."),
    };

    /// <summary>The detector's answer, which must exist: a null fails the test rather than skipping it.</summary>
    private static DetectedFormat Detect(byte[] bytes) =>
        new BuiltInContentFormatDetector().Detect(new MemoryStream(bytes, writable: false)).ShouldNotBeNull();

    /// <summary>
    /// A package with its main part, plus the relationships part and a second XML part typed by its
    /// own override, as a real package carries them.
    /// </summary>
    private static byte[] Package(
        string mainPart,
        string contentType,
        ManifestForm form,
        string defaultExtension = "xml",
        string? overridePartName = null,
        string extraDeclarations = "",
        (string Name, string Content)[]? extraEntries = null)
    {
        var declarations = new StringBuilder();

        declarations.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        declarations.Append(form is ManifestForm.Override
            ? "<Default Extension=\"" + defaultExtension + "\" ContentType=\"application/xml\"/>"
            : "<Default Extension=\"" + defaultExtension + "\" ContentType=\"" + contentType + "\"/>");

        if (form is ManifestForm.Override or ManifestForm.Both)
        {
            declarations.Append("<Override PartName=\"" + (overridePartName ?? "/" + mainPart)
                + "\" ContentType=\"" + contentType + "\"/>");
        }

        declarations.Append("<Override PartName=\"/docProps/app.xml\" ContentType=\"" + ExtendedProperties + "\"/>");
        declarations.Append(extraDeclarations);

        var entries = new List<(string Name, string Content)>
        {
            ("[Content_Types].xml", Manifest(declarations.ToString())),
            ("_rels/.rels", Xml),
            ("docProps/app.xml", Xml),
            (mainPart, Xml),
        };

        entries.AddRange(extraEntries ?? []);

        return Zip([.. entries]);
    }

    private static string Manifest(string declarations) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + declarations
        + "</Types>";

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Optimal);

                using var stream = entry.Open();
                stream.Write(Encoding.ASCII.GetBytes(content));
            }
        }

        return buffer.ToArray();
    }

    /// <summary>The shipped allowlist, with extensionless uploads on.</summary>
    private static FileTypeDecision Inspect(string fileName, string? declared, byte[] bytes)
    {
        var policy = new UploadPolicyOptions
        {
            AllowedFormats = ["pdf", "docx", "xlsx", "pptx", "odt", "ods", "odp",
                              "txt", "csv", "jpeg", "png", "tiff", "bmp"],
            ReclassifiableGroups = ["images"],
            EnforceDeclaredMediaType = true,
            AllowMissingExtension = true,
            MaxSizeBytes = 256L * 1024 * 1024,
        };

        var inspector = new FileTypeInspector(
            new BuiltInContentFormatDetector(),
            Options.Create(policy),
            NullLogger<FileTypeInspector>.Instance);

        return inspector.Inspect(
            fileName,
            declared,
            bytes.Length,
            bytes.AsSpan(0, Math.Min(bytes.Length, FileTypeInspector.HeaderLength)),
            () => new MemoryStream(bytes, writable: false),
            docType: null);
    }
}
