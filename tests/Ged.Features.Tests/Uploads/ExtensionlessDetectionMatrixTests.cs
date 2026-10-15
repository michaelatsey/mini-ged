using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Ged.Adapters.FileTypes;
using Ged.Features.Common.FileTypes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ged.Features.Tests.Uploads;

/// <summary>
/// Every signature format of the shipped allowlist, uploaded under a name with no extension, through
/// the real built-in detector, with the declared media type enforced.
/// </summary>
/// <remarks>
/// <para>
/// The stubbed tests prove what the policy does with a detector's answer. These prove the answer is
/// the one the policy expects for actual bytes: a bare name is the one upload where nothing but the
/// content identifies the format, so a detector that misread a fixture would turn every row here
/// into a refusal — or into the wrong extension on the stored name.
/// </para>
/// <para>
/// The fixtures are the smallest files the detector accepts, built in code rather than committed: a
/// ZIP package with its manifest and main part, an OpenDocument package with its stored
/// <c>mimetype</c> entry, the leading bytes of the others. They do not cover the detector's whole
/// signature table, nor <c>FileSignaturesContentDetector</c> — that is #36.
/// </para>
/// </remarks>
public sealed class ExtensionlessDetectionMatrixTests
{
    /// <summary>The signature formats of the shipped allowlist, with the media type each records.</summary>
    public static TheoryData<string, string> Formats => new()
    {
        { "pdf", "application/pdf" },
        { "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
        { "xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
        { "pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
        { "odt", "application/vnd.oasis.opendocument.text" },
        { "ods", "application/vnd.oasis.opendocument.spreadsheet" },
        { "odp", "application/vnd.oasis.opendocument.presentation" },
        { "png", "image/png" },
        { "jpeg", "image/jpeg" },
        { "tiff", "image/tiff" },
        { "bmp", "image/bmp" },
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void A_bare_name_declared_as_its_format_is_accepted_and_named_after_it(string format, string mediaType)
    {
        var bytes = Fixture(format);

        CheckName("scan", mediaType).Reason.ShouldBe(RejectionReason.None);

        var decision = Inspect("scan", mediaType, bytes);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.Format!.Name.ShouldBe(format);
        decision.MediaType.ShouldBe(mediaType);
        decision.StoredNameFor("scan").ShouldBe("scan." + decision.Format.Extensions[0]);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void A_bare_name_that_claims_nothing_is_accepted_as_its_content(string format, string mediaType)
    {
        var bytes = Fixture(format);

        foreach (var declared in new[] { null, "", "application/octet-stream" })
        {
            CheckName("scan", declared).Reason.ShouldBe(RejectionReason.None);

            var decision = Inspect("scan", declared, bytes);

            decision.Reason.ShouldBe(RejectionReason.None);
            decision.MediaType.ShouldBe(mediaType);
        }
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void A_bare_name_declared_as_another_accepted_format_is_refused(string format, string mediaType)
    {
        _ = mediaType;

        // Another type the catalogue knows, so the name check lets it through and only the bytes can
        // contradict it.
        var other = format == "pdf" ? "image/png" : "application/pdf";

        CheckName("scan", other).Reason.ShouldBe(RejectionReason.None);
        Inspect("scan", other, Fixture(format)).Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void A_bare_name_declared_outside_the_catalogue_is_refused_before_its_content_is_read(
        string format, string mediaType)
    {
        _ = mediaType;

        CheckName("scan", "text/html").Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);
        Inspect("scan", "text/html", Fixture(format)).Reason.ShouldBe(RejectionReason.DeclaredMediaTypeMismatch);
    }

    [Fact]
    public void A_bare_image_declared_as_a_sibling_image_is_accepted_as_what_its_bytes_are()
    {
        // JPEG bytes declared PNG: a wrong label inside the images family, forgiven as a wrong extension
        // would be. The stored name and media type follow the bytes, not the claim.
        var decision = Inspect("scan", "image/png", Fixture("jpeg"));

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.MediaType.ShouldBe("image/jpeg");
        decision.StoredNameFor("scan").ShouldBe("scan.jpg");
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/csv")]
    [InlineData(null)]
    public void Bare_text_is_refused_whatever_it_declares(string? declared)
    {
        Inspect("notes", declared, "date,amount\n2026-10-15,12.50\n"u8.ToArray())
            .Reason.ShouldBe(RejectionReason.NoExtension);
    }

    [Fact]
    public void A_bare_gif_is_refused_because_the_shipped_allowlist_does_not_name_it()
    {
        Inspect("anim", "image/gif", Fixture("gif")).Reason.ShouldBe(RejectionReason.ExtensionNotAllowed);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void A_bare_name_is_refused_when_extensionless_uploads_are_off(string format, string mediaType)
    {
        var policy = Policy();
        policy.AllowMissingExtension = false;

        CheckName("scan", mediaType, policy).Reason.ShouldBe(RejectionReason.NoExtension);
        Inspect("scan", mediaType, Fixture(format), policy).Reason.ShouldBe(RejectionReason.NoExtension);
    }

    /// <summary>The shipped allowlist and ceilings, with both flags this class is about switched on.</summary>
    private static UploadPolicyOptions Policy() => new()
    {
        AllowedFormats = ["pdf", "docx", "xlsx", "pptx", "odt", "ods", "odp",
                          "txt", "csv", "jpeg", "png", "tiff", "bmp"],
        ReclassifiableGroups = ["images"],
        EnforceDeclaredMediaType = true,
        AllowMissingExtension = true,
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

    private static FileTypeInspector Inspector(UploadPolicyOptions? policy = null) => new(
        new BuiltInContentFormatDetector(),
        Options.Create(policy ?? Policy()),
        NullLogger<FileTypeInspector>.Instance);

    private static FileTypeDecision CheckName(string fileName, string? declared, UploadPolicyOptions? policy = null) =>
        Inspector(policy).CheckName(fileName, declared, docType: null);

    private static FileTypeDecision Inspect(
        string fileName, string? declared, byte[] bytes, UploadPolicyOptions? policy = null) =>
        Inspector(policy).Inspect(
            fileName,
            declared,
            bytes.Length,
            bytes.AsSpan(0, Math.Min(bytes.Length, FileTypeInspector.HeaderLength)),
            () => new MemoryStream(bytes, writable: false),
            docType: null);

    private static byte[] Fixture(string format) => format switch
    {
        "pdf" => "%PDF-1.7\n1 0 obj\n<< >>\nendobj\ntrailer\n<< >>\n%%EOF\n"u8.ToArray(),
        "png" => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52],
        "jpeg" => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9],
        "tiff" => [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00],
        "gif" => "GIF89a\u0001\0\u0001\0\0\0\0;"u8.ToArray(),
        "bmp" => Bitmap(),
        "docx" => OfficeOpenXml("word/document.xml",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"),
        "xlsx" => OfficeOpenXml("xl/workbook.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
        "pptx" => OfficeOpenXml("ppt/presentation.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"),
        "odt" => OpenDocument("application/vnd.oasis.opendocument.text"),
        "ods" => OpenDocument("application/vnd.oasis.opendocument.spreadsheet"),
        "odp" => OpenDocument("application/vnd.oasis.opendocument.presentation"),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "No fixture for this format."),
    };

    /// <summary>A one-pixel, 24-bit bitmap whose header states its real size, as the catalogue checks.</summary>
    private static byte[] Bitmap()
    {
        var bytes = new byte[58];

        "BM"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), 54);  // pixel data offset
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 40);  // BITMAPINFOHEADER
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 1);    // width
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), 1);    // height
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 1);   // planes
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 24);  // bits per pixel

        return bytes;
    }

    /// <summary>An Office Open XML package: the content-types manifest and the main part it names.</summary>
    private static byte[] OfficeOpenXml(string mainPart, string contentType)
    {
        var manifest =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + $"<Override PartName=\"/{mainPart}\" ContentType=\"{contentType}\"/>"
            + "</Types>";

        return Zip(
            ("[Content_Types].xml", manifest, CompressionLevel.Optimal),
            (mainPart, "<?xml version=\"1.0\" encoding=\"UTF-8\"?><root/>", CompressionLevel.Optimal));
    }

    /// <summary>An OpenDocument package: a stored <c>mimetype</c> entry first, as the specification requires.</summary>
    private static byte[] OpenDocument(string mediaType) =>
        Zip(
            ("mimetype", mediaType, CompressionLevel.NoCompression),
            ("content.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document-content/>", CompressionLevel.Optimal));

    private static byte[] Zip(params (string Name, string Content, CompressionLevel Level)[] entries)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content, level) in entries)
            {
                var entry = archive.CreateEntry(name, level);

                using var stream = entry.Open();
                stream.Write(Encoding.ASCII.GetBytes(content));
            }
        }

        return buffer.ToArray();
    }
}
