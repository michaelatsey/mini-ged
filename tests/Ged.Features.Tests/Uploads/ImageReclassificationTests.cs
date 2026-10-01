using System.Text.Json;
using Ged.Core.Ports.FileTypes;
using Ged.Features.Common.FileTypes;
using Ged.Features.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ged.Features.Tests.Uploads;

/// <summary>
/// Covers the single mismatch the policy forgives: content of one allowed image format arriving
/// under the extension of another allowed image format.
/// </summary>
/// <remarks>
/// <para>
/// A PNG a user saved as <c>photo.jpg</c> is a naming accident, and refusing it with
/// <c>ContentDoesNotMatchExtension</c> taught nobody anything. Every other mismatch stays a refusal:
/// a DOCX named <c>.pdf</c> reaches a client that opens files by extension, which is the case this
/// whole check exists for.
/// </para>
/// <para>
/// The detector is stubbed throughout, because none of this is about recognising bytes. What is under
/// test is the policy — which swaps are allowed, which allowlist still applies, and whose ceiling.
/// </para>
/// </remarks>
public sealed class ImageReclassificationTests
{
    /// <summary>The per-format ceilings `appsettings.json` states for these two formats.</summary>
    private const long PngCeiling = 25L * 1024 * 1024;

    private const long TiffCeiling = 50L * 1024 * 1024;

    private static readonly DetectedFormat Png = new("png", "image/png");

    private static readonly DetectedFormat Jpeg = new("jpg", "image/jpeg");

    private static readonly DetectedFormat Bmp = new("bmp", "image/bmp");

    private static readonly DetectedFormat Tiff = new("tif", "image/tiff");

    private static readonly DetectedFormat Docx = new(
        "docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

    [Fact]
    public void A_png_named_jpg_is_accepted_as_the_png_it_is()
    {
        var decision = Inspect(Shipped(), "photo.jpg", Png);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.Format.ShouldNotBeNull().Name.ShouldBe("png");
        decision.MediaType.ShouldBe("image/png");
        decision.Reclassified.ShouldBeTrue();
        decision.MaxSizeBytes.ShouldBe(PngCeiling);
        decision.StoredNameFor("photo.jpg").ShouldBe("photo.png");
    }

    [Fact]
    public void A_reclassified_name_takes_the_formats_first_extension()
    {
        // The `jpeg` format declares `jpg` first, and that is what a client needs to see: the stored
        // name carries an extension, not a format name.
        var decision = Inspect(Shipped(), "scan.png", Jpeg);

        decision.Format.ShouldNotBeNull().Name.ShouldBe("jpeg");
        decision.StoredNameFor("scan.png").ShouldBe("scan.jpg");
    }

    [Fact]
    public void Only_the_last_extension_of_a_reclassified_name_is_corrected()
    {
        // The segment the inspection read is the segment that gets fixed. Correcting any other one
        // would leave the name describing something that was never checked.
        Inspect(Shipped(), "holiday.2026.jpg", Png)
            .StoredNameFor("holiday.2026.jpg").ShouldBe("holiday.2026.png");
    }

    [Fact]
    public void A_png_named_png_is_accepted_without_being_reclassified()
    {
        var decision = Inspect(Shipped(), "photo.png", Png);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.Reclassified.ShouldBeFalse();
        decision.StoredNameFor("photo.png").ShouldBe("photo.png");
    }

    [Fact]
    public void A_png_named_jpg_is_refused_where_no_family_is_reclassifiable()
    {
        // The behaviour of every deployment that does not state the key: unchanged.
        var policy = Shipped();
        policy.ReclassifiableGroups = [];

        Inspect(policy, "photo.jpg", Png).Reason
            .ShouldBe(RejectionReason.ContentDoesNotMatchExtension);
    }

    [Fact]
    public void A_docx_named_pdf_is_refused_although_both_formats_are_allowed()
    {
        // Never across families, whatever is enabled. The client opens a .pdf with a PDF reader.
        Inspect(Shipped(), "report.pdf", Docx).Reason
            .ShouldBe(RejectionReason.ContentDoesNotMatchExtension);
    }

    [Fact]
    public void Reclassification_never_reaches_a_format_the_allowlist_excludes()
    {
        var policy = Shipped();
        policy.AllowedFormats = ["jpeg"];

        Inspect(policy, "photo.jpg", Png).Reason
            .ShouldBe(RejectionReason.ContentDoesNotMatchExtension);
    }

    [Fact]
    public void Reclassification_never_reaches_a_format_the_document_type_excludes()
    {
        // `FormatsByDocType` narrows and never widens; a swap resolves through the same narrowing,
        // or a classification would become the way around the policy.
        var policy = Shipped();
        policy.FormatsByDocType["PHOTO"] = ["jpeg"];

        Inspect(policy, "photo.jpg", Png, docType: "PHOTO").Reason
            .ShouldBe(RejectionReason.ContentDoesNotMatchExtension);
    }

    [Fact]
    public void A_png_named_tif_is_held_to_the_png_ceiling_not_the_tiff_one()
    {
        // 30 MiB: inside TIFF's 50 and over PNG's 25. Borrowing a name must not borrow its budget.
        var decision = Inspect(Shipped(), "scan.tif", Png, sizeBytes: 30L * 1024 * 1024);

        decision.Reason.ShouldBe(RejectionReason.TooLarge);
    }

    [Fact]
    public void A_png_named_tif_within_both_ceilings_is_still_accepted()
    {
        var decision = Inspect(Shipped(), "scan.tif", Png, sizeBytes: 20L * 1024 * 1024);

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.Format.ShouldNotBeNull().Name.ShouldBe("png");
        decision.MaxSizeBytes.ShouldBe(PngCeiling);
    }

    [Fact]
    public void A_bitmap_named_png_still_has_to_satisfy_the_bitmap_header_check()
    {
        // The fourth condition, and BMP is the only format with a check to satisfy: two ASCII letters
        // are far too common a signature to stand alone, so the size its header declares at offset 2
        // has to agree with the size received.
        const long size = 4096;

        var decision = Inspect(Shipped(), "scan.png", Bmp, size, header: BitmapHeader(size));

        decision.Reason.ShouldBe(RejectionReason.None);
        decision.Format.ShouldNotBeNull().Name.ShouldBe("bmp");
        decision.StoredNameFor("scan.png").ShouldBe("scan.bmp");
    }

    [Fact]
    public void A_bitmap_whose_header_disagrees_with_its_size_is_not_reclassified()
    {
        // The same swap with one byte of the header changed. A format borrowed through another name
        // does not get to skip a check an upload under its own name would have to pass.
        var decision = Inspect(Shipped(), "scan.png", Bmp, 4096, header: BitmapHeader(4095));

        decision.Reason.ShouldBe(RejectionReason.ContentDoesNotMatchExtension);
    }

    [Fact]
    public void A_tiff_over_the_png_ceiling_named_png_is_refused_rather_than_reclassified()
    {
        // The asymmetry, frozen on purpose. 30 MiB is inside TIFF's 50 and over PNG's 25, and the
        // claimed format's ceiling is the one that applies first — it is also what bounds the staging
        // of the request body, so these bytes are never read in full. Reclassification may tighten
        // that limit and never lift it: widening it would undo the bound #14 added, which is the whole
        // reason an oversized upload stops at the limit instead of filling a temp volume first.
        var decision = Inspect(Shipped(), "scan.png", Tiff, sizeBytes: 30L * 1024 * 1024);

        decision.Reason.ShouldBe(RejectionReason.TooLarge);
    }

    [Theory]
    [InlineData("office")]      // a real set, holding three macro-capable formats
    [InlineData("documents")]   // a real set, holding four
    [InlineData("imgaes")]      // the typo this validation exists for
    [InlineData("png")]         // a format name: a set of one states no relation
    public void Startup_refuses_a_set_the_code_does_not_declare_reclassifiable(string entry) =>
        Should.Throw<OptionsValidationException>(() => Bound(entry));

    [Fact]
    public void Startup_accepts_the_one_set_the_code_declares() =>
        Bound("images").ReclassifiableGroups.ShouldBe(["images"]);

    [Fact]
    public void Every_declared_set_is_eligible_as_the_catalogue_stands()
    {
        // The second half of the eligibility rule, and why it reads the catalogue rather than the
        // list beside it: adding a macro-capable format to `images` has to stop a deployment instead
        // of quietly turning "renamed .xls" into an accepted upload. This fails that day.
        foreach (var family in FileFormats.ReclassifiableGroupNames)
        {
            FileFormats.IsReclassifiable(family).ShouldBeTrue(
                $"'{family}' is declared reclassifiable but holds a format carrying executable "
                + "content, so every deployment naming it now fails at startup.");
        }
    }

    [Fact]
    public void The_shipped_configuration_enables_images_and_nothing_else()
    {
        // The code decides what is eligible; configuration decides what is on. This asserts the
        // second half, in the one file that ships it — a key quietly dropped from `appsettings.json`
        // would otherwise turn every reclassification back into a 415 with nothing to point at.
        using var document = JsonDocument.Parse(
            File.ReadAllText(Repository.FileAt(Path.Combine("hosts", "Ged.Api", "appsettings.json"))),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        document.RootElement
            .GetProperty("Ged").GetProperty("Uploads").GetProperty("ReclassifiableGroups")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .ShouldBe(["images"]);
    }

    /// <summary>The shipped policy, as `appsettings.json` states it where it matters here.</summary>
    private static UploadPolicyOptions Shipped() => new()
    {
        AllowedFormats = ["pdf", "docx", "jpeg", "png", "tiff", "bmp"],
        ReclassifiableGroups = ["images"],
        MaxSizeBytes = 256L * 1024 * 1024,
        MaxSizeByFormat = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["png"] = PngCeiling,
            ["tiff"] = TiffCeiling,
        },
    };

    /// <summary>A bitmap header declaring the given size, which is what BMP's extra check reads.</summary>
    private static byte[] BitmapHeader(long sizeBytes) =>
    [
        0x42, 0x4D,
        (byte)(sizeBytes & 0xFF),
        (byte)((sizeBytes >> 8) & 0xFF),
        (byte)((sizeBytes >> 16) & 0xFF),
        (byte)((sizeBytes >> 24) & 0xFF),
    ];

    /// <summary>Inspects content the detector reports as <paramref name="detected"/>.</summary>
    private static FileTypeDecision Inspect(
        UploadPolicyOptions policy,
        string fileName,
        DetectedFormat detected,
        long sizeBytes = 4096,
        string? docType = null,
        byte[]? header = null) =>
        new FileTypeInspector(new StubDetector(detected), Options.Create(policy)).Inspect(
            fileName, sizeBytes, header ?? [], () => new MemoryStream([1, 2, 3]), docType);

    /// <summary>Binds the policy the way the host does, which is where the validation lives.</summary>
    private static UploadPolicyOptions Bound(params string[] reclassifiable)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Ged:Uploads:AllowedFormats:0"] = "png",
            ["Ged:Uploads:AllowedFormats:1"] = "jpeg",
        };

        for (var index = 0; index < reclassifiable.Length; index++)
        {
            settings[$"Ged:Uploads:ReclassifiableGroups:{index}"] = reclassifiable[index];
        }

        // Disposed here: the options object outlives the provider, and six call sites leaving one to
        // the finalizer is six providers holding their singletons for no reason.
        using var services = new ServiceCollection()
            .AddGedFeatureHandlers(
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();

        return services.GetRequiredService<IOptions<UploadPolicyOptions>>().Value;
    }
}
