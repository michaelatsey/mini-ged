using System.Text.Json;
using Ged.Core.Ports.FileTypes;
using Ged.Domain.Folders;
using Ged.Features.Common.FileTypes;
using Ged.Features.Documents.UploadDocument;
using Ged.Features.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ged.Features.Tests.Uploads;

/// <summary>
/// Every upload with no extension that the inspector decides on leaves exactly one log entry,
/// accepted or refused, and nothing else does.
/// </summary>
/// <remarks>
/// <para>
/// The entries are what will say, later, how many clients send bare names and what those files turn
/// out to be — the evidence for keeping <see cref="UploadPolicyOptions.AllowMissingExtension"/> on or
/// off. Exactly one matters: the name check runs twice per request, once at the endpoint and once in
/// the handler, and a count that doubled would be worse than none.
/// </para>
/// <para>
/// Assertions are on event ids and structured fields, never on the rendered message: the fields are
/// what a pipeline indexes, and a reworded template must not break a test.
/// </para>
/// </remarks>
public sealed class ExtensionlessUploadLoggingTests
{
    private const int Accepted = 2001;

    private const int Refused = 2002;

    private static readonly DetectedFormat Pdf = new("pdf", "application/pdf");

    private static readonly DetectedFormat Doc = new("doc", "application/msword");

    [Fact]
    public void A_bare_name_refused_at_the_name_check_is_logged_with_no_size()
    {
        var logger = new RecordingLogger<FileTypeInspector>();

        Inspector(Policy(allowMissingExtension: false), null, logger).CheckName("rapport", null, "CONTRACT");

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(Refused);
        entry.Level.ShouldBe(LogLevel.Information);
        entry.Fields["FileName"].ShouldBe("rapport");
        entry.Fields["Reason"].ShouldBe(nameof(RejectionReason.NoExtension));
        entry.Fields["Stage"].ShouldBe("name");
        entry.Fields["SizeBytes"].ShouldBeNull();
        entry.Fields["DetectedFormat"].ShouldBeNull();
        entry.Fields["DocType"].ShouldBe("CONTRACT");
    }

    [Fact]
    public void An_accepted_bare_upload_is_logged_with_its_format_and_stored_name()
    {
        var logger = new RecordingLogger<FileTypeInspector>();

        Inspect(Inspector(Policy(), Pdf, logger), "rapport", sizeBytes: 4096);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(Accepted);
        entry.Fields["FileName"].ShouldBe("rapport");
        entry.Fields["Format"].ShouldBe("pdf");
        entry.Fields["SizeBytes"].ShouldBe(4096L);
        entry.Fields["StoredName"].ShouldBe("rapport.pdf");
    }

    [Fact]
    public void A_bare_upload_refused_by_its_content_is_logged_with_what_the_detector_saw()
    {
        // The detected format is the useful half of a refusal: "a DOC nobody allowed" and "nothing
        // recognisable" call for different answers.
        var logger = new RecordingLogger<FileTypeInspector>();

        Inspect(Inspector(Policy(), Doc, logger), "rapport", sizeBytes: 4096);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(Refused);
        entry.Fields["Reason"].ShouldBe(nameof(RejectionReason.ExtensionNotAllowed));
        entry.Fields["Stage"].ShouldBe("content");
        entry.Fields["DetectedFormat"].ShouldBe("doc");
        entry.Fields["SizeBytes"].ShouldBe(4096L);
    }

    [Fact]
    public void An_empty_bare_upload_is_logged_as_empty()
    {
        var logger = new RecordingLogger<FileTypeInspector>();

        Inspect(Inspector(Policy(), Pdf, logger), "rapport", sizeBytes: 0);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(Refused);
        entry.Fields["Reason"].ShouldBe(nameof(RejectionReason.Empty));
    }

    [Fact]
    public void A_bare_name_declared_outside_the_catalogue_is_logged_at_the_name_check()
    {
        var logger = new RecordingLogger<FileTypeInspector>();
        var policy = Policy();
        policy.EnforceDeclaredMediaType = true;

        Inspector(policy, Pdf, logger).CheckName("rapport", "text/html", null);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(Refused);
        entry.Fields["Reason"].ShouldBe(nameof(RejectionReason.DeclaredMediaTypeMismatch));
        entry.Fields["Stage"].ShouldBe("name");
        entry.Fields["SizeBytes"].ShouldBeNull();
        entry.Fields["DetectedFormat"].ShouldBeNull();
    }

    [Fact]
    public void A_bare_upload_its_content_contradicts_is_logged_once_at_the_content_check()
    {
        var logger = new RecordingLogger<FileTypeInspector>();
        var policy = Policy();
        policy.EnforceDeclaredMediaType = true;
        var inspector = Inspector(policy, Pdf, logger);

        inspector.CheckName("rapport", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", null)
            .Reason.ShouldBe(RejectionReason.None);

        inspector.Inspect(
            "rapport",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            4096,
            [],
            () => new MemoryStream([1, 2, 3]),
            docType: null);

        var entry = logger.Entries.ShouldHaveSingleItem();
        entry.EventId.Id.ShouldBe(Refused);
        entry.Fields["Reason"].ShouldBe(nameof(RejectionReason.DeclaredMediaTypeMismatch));
        entry.Fields["Stage"].ShouldBe("content");
        entry.Fields["DetectedFormat"].ShouldBe("pdf");
        entry.Fields["SizeBytes"].ShouldBe(4096L);
    }

    [Fact]
    public void A_bare_name_passing_the_name_check_is_not_logged_there()
    {
        // Logged once, by the content check, when there is an outcome to record.
        var logger = new RecordingLogger<FileTypeInspector>();

        Inspector(Policy(), Pdf, logger).CheckName("rapport", null, null);

        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void A_named_upload_is_never_logged()
    {
        var logger = new RecordingLogger<FileTypeInspector>();
        var inspector = Inspector(Policy(), Pdf, logger);

        inspector.CheckName("rapport.pdf", null, null);
        Inspect(inspector, "rapport.pdf", sizeBytes: 4096);

        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void With_information_switched_off_nothing_is_logged_and_no_decision_changes()
    {
        // A deployment can raise this category's level. Logging must then cost the decision nothing:
        // the same outcome, and nothing reaching the logger on any of the three paths.
        var logger = new RecordingLogger<FileTypeInspector>(LogLevel.Warning);

        var accepted = Inspect(Inspector(Policy(), Pdf, logger), "rapport", sizeBytes: 4096);
        var refused = Inspect(Inspector(Policy(), Doc, logger), "rapport", sizeBytes: 4096);
        var refusedByName = Inspector(Policy(allowMissingExtension: false), null, logger)
            .CheckName("rapport", null, null);

        accepted.Reason.ShouldBe(RejectionReason.None);
        accepted.StoredNameFor("rapport").ShouldBe("rapport.pdf");
        refused.Reason.ShouldBe(RejectionReason.ExtensionNotAllowed);
        refusedByName.Reason.ShouldBe(RejectionReason.NoExtension);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_bare_upload_through_the_handler_leaves_exactly_one_entry()
    {
        // The handler runs the name check and then the content check on the same name; the endpoint
        // has run the name check once already. One upload, one entry.
        var logger = new RecordingLogger<FileTypeInspector>();
        var target = Folder.CreateRoot(new FolderName("Scans"), FolderType.Category, Fixed.Now, Fixed.Me);
        var journal = new Journal();
        var inspector = Inspector(Policy(), Pdf, logger);

        inspector.CheckName("rapport", null, null);

        var handler = new UploadDocumentHandler(
            new FakeFolderRepository(target),
            new FakeBlobRepository(),
            new FakeDocumentRepository(),
            new SingleStorageRegistry(new RecordingStorage(journal)),
            inspector,
            new RecordingUnitOfWork(journal),
            new FixedClock(Fixed.Now));

        await using var content = Content.Open();

        (await handler.HandleAsync(new UploadDocumentCommand(target.Id.Value, "rapport", null, null, content, Fixed.By)))
            .Succeeded.ShouldBeTrue();

        logger.Entries.ShouldHaveSingleItem().EventId.Id.ShouldBe(Accepted);
    }

    [Fact]
    public void The_shipped_configuration_writes_json_with_scopes()
    {
        // JSON, so every field above reaches the output as a field rather than as text a later
        // pipeline would have to parse back; scopes, so each entry carries the request's TraceId and
        // can be joined to the rest of that request.
        using var document = JsonDocument.Parse(
            File.ReadAllText(Repository.FileAt(Path.Combine("hosts", "Ged.Api", "appsettings.json"))),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var console = document.RootElement.GetProperty("Logging").GetProperty("Console");

        console.GetProperty("FormatterName").GetString().ShouldBe("json");
        console.GetProperty("FormatterOptions").GetProperty("IncludeScopes").GetBoolean().ShouldBeTrue();
        console.GetProperty("FormatterOptions").GetProperty("UseUtcTimestamp").GetBoolean().ShouldBeTrue();
    }

    private static UploadPolicyOptions Policy(bool allowMissingExtension = true) => new()
    {
        AllowedFormats = ["pdf", "docx"],
        AllowMissingExtension = allowMissingExtension,
    };

    private static FileTypeInspector Inspector(
        UploadPolicyOptions policy, DetectedFormat? detected, RecordingLogger<FileTypeInspector> logger) =>
        new(new StubDetector(detected), Options.Create(policy), logger);

    private static FileTypeDecision Inspect(FileTypeInspector inspector, string fileName, long sizeBytes) =>
        inspector.Inspect(fileName, declaredMediaType: null, sizeBytes, [], () => new MemoryStream([1, 2, 3]), docType: null);
}
