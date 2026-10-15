using System.Text;
using Ged.Core.Ports.FileTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ged.Features.Common.FileTypes;

/// <summary>Applies the upload policy to a file name and then to its content.</summary>
/// <param name="detector">Identifies what the content actually is.</param>
/// <param name="options">The configured policy.</param>
/// <param name="logger">
/// Records every upload whose name carries no extension and what became of it, and every declared media
/// type that disagrees with the file.
/// </param>
/// <remarks>
/// <para>
/// Three layers, in increasing order of cost and of authority:
/// </para>
/// <code>
/// extension          free, and a claim the client makes
/// declared type      free, and a claim the client makes
/// detected format    needs bytes, and is the only evidence
/// </code>
/// <para>
/// The first two exist to refuse cheaply, not to decide. A file named <c>.pdf</c> with an
/// <c>application/pdf</c> header proves nothing — both are strings the uploader chose.
/// </para>
/// <para>
/// The third is delegated. Recognising a format is knowledge about file layouts that a maintained
/// library holds better; deciding whether the answer is acceptable here is business policy that no
/// library can hold at all. This class keeps the second half and only the second half.
/// </para>
/// </remarks>
public sealed partial class FileTypeInspector(
    IContentFormatDetector detector,
    IOptions<UploadPolicyOptions> options,
    ILogger<FileTypeInspector> logger) : IFileTypeInspector
{
    /// <summary>How many leading bytes are captured for the weak-signature checks.</summary>
    public const int HeaderLength = 512;

    private readonly UploadPolicyOptions _policy = options.Value;

    /// <inheritdoc />
    public FileTypeDecision CheckName(string fileName, string? declaredMediaType, string? docType)
    {
        var extension = ExtensionOf(fileName);
        var claimed = ClaimOf(declaredMediaType);

        if (extension.Length == 0)
        {
            // No extension claimed, so no per-format ceiling to bound the staging with: the content
            // will decide, and the global ceiling stops the copy until then. Only refusals are logged
            // here: an accepted name is logged once, by Inspect, when the content has decided — and
            // this check runs twice per request, at the endpoint and again in the handler.
            var refused = !MayIdentifyByContent
                ? FileTypeDecision.Reject(RejectionReason.NoExtension, "The file name carries no extension.")
                : claimed is { Format: null }
                    ? DeclaredOutsideCatalogue()
                    : null;

            if (refused is null)
            {
                return FileTypeDecision.Undetermined(_policy.MaxSizeBytes);
            }

            LogExtensionlessRefused(
                logger, fileName, refused.Code, Stage.Name, detectedFormat: null, sizeBytes: null, docType);

            if (refused.Reason == RejectionReason.DeclaredMediaTypeMismatch && claimed is { } outside)
            {
                RecordRefusedClaim(fileName, outside, comparedWith: null, docType);
            }

            return refused;
        }

        if (!TryResolveByExtension(extension, docType, out var format) || format is null)
        {
            return FileTypeDecision.Reject(
                RejectionReason.ExtensionNotAllowed,
                $"Files with the '.{extension}' extension are not accepted here.");
        }

        if (claimed is { } claim && !MayStillAgree(claim, format))
        {
            RecordRefusedClaim(fileName, claim, format, docType);

            return claim.Format is null
                ? DeclaredOutsideCatalogue()
                : FileTypeDecision.Reject(
                    RejectionReason.DeclaredMediaTypeMismatch,
                    "The declared content type does not match the file extension.");
        }

        // Not accepted yet — only "nothing rules it out so far". The content still decides. The
        // ceiling travels back so the caller can stop reading the body once it is exceeded.
        return FileTypeDecision.Accept(format, CeilingFor(format));
    }

    /// <inheritdoc />
    public FileTypeDecision Inspect(
        string fileName,
        string? declaredMediaType,
        long sizeBytes,
        ReadOnlySpan<byte> header,
        Func<Stream> openContent,
        string? docType)
    {
        ArgumentNullException.ThrowIfNull(openContent);

        var extension = ExtensionOf(fileName);

        if (extension.Length == 0)
        {
            return InspectExtensionless(fileName, declaredMediaType, sizeBytes, header, openContent, docType);
        }

        return ConfirmAndRecord(
            fileName, InspectNamed(extension, sizeBytes, header, openContent, docType), declaredMediaType, docType);
    }

    /// <summary>The content check for a name carrying an extension.</summary>
    /// <param name="extension">Its last extension, already read.</param>
    /// <param name="sizeBytes">The size actually received.</param>
    /// <param name="header">The first bytes of the content.</param>
    /// <param name="openContent">Opens the staged content.</param>
    /// <param name="docType">The document classification, or null.</param>
    /// <returns>The decision, before any declared type is compared with it.</returns>
    private FileTypeDecision InspectNamed(
        string extension,
        long sizeBytes,
        ReadOnlySpan<byte> header,
        Func<Stream> openContent,
        string? docType)
    {
        if (sizeBytes <= 0)
        {
            return FileTypeDecision.Reject(RejectionReason.Empty, "The file is empty.");
        }

        if (!TryResolveByExtension(extension, docType, out var format) || format is null)
        {
            return FileTypeDecision.Reject(
                RejectionReason.ExtensionNotAllowed,
                $"Files with the '.{extension}' extension are not accepted here.");
        }

        var ceiling = CeilingFor(format);

        if (sizeBytes > ceiling)
        {
            return TooLarge(format, ceiling);
        }

        var detected = SafelyDetect(openContent);

        var matches = format.Mode switch
        {
            // The detector recognised nothing AND the bytes look like text. Both halves matter:
            // without the first, an executable renamed .txt is only caught by the NUL check; without
            // the second, an empty answer would be taken as agreement.
            DetectionMode.TextHeuristic => detected is null && LooksLikeText(header),

            // The detected format must be one this catalogue entry accepts. Matched on extension
            // rather than media type: extensions are stable identifiers, while a third-party media
            // type table can carry a typo — and one of them does.
            _ => detected is not null
                 && format.Extensions.Contains(detected.Extension, StringComparer.OrdinalIgnoreCase)
                 && (format.Additional is null || format.Additional(header, sizeBytes)),
        };

        if (matches)
        {
            return FileTypeDecision.Accept(format, ceiling);
        }

        if (detected is not null && Reclassify(format, detected, header, sizeBytes, docType) is { } swapped)
        {
            return swapped;
        }

        // The detected format is named in the message when there is one. It turns "rejected" into
        // "you sent a spreadsheet named .docx", which is the difference between a support ticket and
        // a user who fixes it themselves.
        var detail = detected is null
            ? $"The content is not a valid {format.Name.ToUpperInvariant()} file."
            : $"The content is a {detected.Extension.ToUpperInvariant()} file, not a {format.Name.ToUpperInvariant()}.";

        return FileTypeDecision.Reject(RejectionReason.ContentDoesNotMatchExtension, detail);
    }

    /// <summary>Gets a value indicating whether a name with no extension may be decided by its content.</summary>
    /// <remarks>
    /// Both gates read this one property, so a caller reaching <see cref="Inspect"/> without
    /// <see cref="CheckName"/> meets the same rule. <see cref="UploadPolicyOptions.EnforceDeclaredMediaType"/>
    /// used to turn it off, because its comparison needed the format an extension names. It no longer
    /// does: the declared type is compared with the format the content turns out to be, which a bare
    /// name has as surely as a named one.
    /// </remarks>
    private bool MayIdentifyByContent => _policy.AllowMissingExtension;

    /// <summary>Reads the client's declared media type as a claim, when the policy holds clients to one.</summary>
    /// <param name="declaredMediaType">The value the client sent. Untrusted.</param>
    /// <returns>The claim <see cref="ReadClaim"/> reads, or null when the flag is off.</returns>
    /// <remarks>
    /// What the decisions act on. The log reads the claim with <see cref="ReadClaim"/> directly, so a
    /// mislabel is recorded whether or not the policy acts on it.
    /// </remarks>
    private Claim? ClaimOf(string? declaredMediaType) =>
        _policy.EnforceDeclaredMediaType ? ReadClaim(declaredMediaType) : null;

    /// <summary>Reads a declared media type as a claim, whatever the policy does with it.</summary>
    /// <param name="declaredMediaType">The value the client sent. Untrusted.</param>
    /// <returns>
    /// Null when nothing is claimed — the value is absent, blank or <c>application/octet-stream</c>.
    /// Otherwise the claim: the media type as compared, and the catalogue format it names, or a null
    /// format when it names none.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>application/octet-stream</c> means "arbitrary binary data" (RFC 2046 §4.5.1). It names no
    /// format, and it is what a client relaying an upload it could not type sends — refusing it would
    /// refuse a caller for not knowing, which is not the same as lying. The consequence is stated in
    /// <see cref="UploadPolicyOptions.EnforceDeclaredMediaType"/>: a claim made must be true, but no
    /// claim has to be made.
    /// </para>
    /// <para>
    /// Parameters after <c>;</c> are dropped and case is ignored, because <c>charset=</c> and
    /// capitalisation say nothing about the format. Nothing else is normalised: an alias would be a
    /// second name under which a claim agrees with content, and the catalogue holds none.
    /// </para>
    /// </remarks>
    private static Claim? ReadClaim(string? declaredMediaType)
    {
        if (string.IsNullOrWhiteSpace(declaredMediaType))
        {
            return null;
        }

        var mediaType = declaredMediaType.Split(';')[0].Trim();

        if (mediaType.Length == 0
            || string.Equals(mediaType, UnknownMediaType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new Claim(mediaType, FileFormats.TryGetByMediaType(mediaType, out var format) ? format : null);
    }

    /// <summary>Determines whether a claim may still agree with the content once it is read.</summary>
    /// <param name="claim">What the client declared.</param>
    /// <param name="named">The format the extension names.</param>
    /// <returns>
    /// True when the claim names that format, or a sibling the content could be reclassified into.
    /// </returns>
    /// <remarks>
    /// The name check refuses only what no content could reconcile. <c>photo.jpg</c> declared
    /// <c>image/png</c> is let through: the bytes may be the PNG the client says, and reclassification
    /// would then make the name agree too. <c>rapport.pdf</c> declared <c>image/png</c> is refused
    /// here, before a byte is staged — no PDF and no reclassification can make those two agree.
    /// </remarks>
    private bool MayStillAgree(Claim claim, FileFormat named) =>
        claim.Format is { } declared && AgreesWithinFamily(declared, named);

    /// <summary>Determines whether a declared format agrees with another format, as an extension would.</summary>
    /// <param name="declared">The format the client's media type names.</param>
    /// <param name="format">The format it is held against — named by the extension, or accepted.</param>
    /// <returns>True when the two are the same format, or siblings in a reclassifiable family.</returns>
    /// <remarks>
    /// The one rule both checks apply to a claim, and the same one <see cref="Reclassify"/> applies to an
    /// extension: a wrong label is forgiven within a family the catalogue declares safe and the
    /// configuration enables, never across one. A client that derives its declared type from the file
    /// name sends nothing the name did not already say, so holding the claim to a stricter rule than
    /// the name would refuse the very naming accident reclassification exists to accept.
    /// </remarks>
    private bool AgreesWithinFamily(FileFormat declared, FileFormat format) =>
        string.Equals(declared.Name, format.Name, StringComparison.OrdinalIgnoreCase)
        || ShareReclassifiableGroup(format, declared);

    /// <summary>Holds an accepted decision to the claim the client made, if it made one.</summary>
    /// <param name="decision">The content check's decision.</param>
    /// <param name="declaredMediaType">What the client declared.</param>
    /// <returns>The decision unchanged, or a <see cref="RejectionReason.DeclaredMediaTypeMismatch"/> refusal.</returns>
    /// <remarks>
    /// <para>
    /// Compared with the format finally accepted — named, reclassified, or identified from a bare
    /// name's bytes — never with the extension alone. Agreement is <see cref="AgreesWithinFamily"/>:
    /// <c>photo.jpg</c> declared <c>image/jpeg</c> and holding a PNG is accepted as <c>photo.png</c>,
    /// as it would be with no header at all, while a bare name declared <c>image/png</c> and holding a
    /// PDF is refused — the claim changed what the file was said to be, not just its label.
    /// </para>
    /// <para>
    /// The declared format is not checked against the allowlist. Nothing is stored under it: what is
    /// stored is the accepted format, which already passed every gate on the bytes.
    /// </para>
    /// <para>
    /// A refusal is returned as it is. Its reason already names what the content really is, which says
    /// more to the client than a mismatch would.
    /// </para>
    /// </remarks>
    private FileTypeDecision Confirm(FileTypeDecision decision, string? declaredMediaType)
    {
        if (!decision.Accepted || decision.Format is not { } accepted || ClaimOf(declaredMediaType) is not { } claim)
        {
            return decision;
        }

        if (claim.Format is null)
        {
            return DeclaredOutsideCatalogue();
        }

        return AgreesWithinFamily(claim.Format, accepted)
            ? decision
            : FileTypeDecision.Reject(
                RejectionReason.DeclaredMediaTypeMismatch,
                $"The declared content type does not match the content, which is a "
                    + $"{accepted.Name.ToUpperInvariant()} file.");
    }

    /// <summary>Refuses a declared type that names no format this catalogue knows.</summary>
    /// <returns>The refusal.</returns>
    /// <remarks>
    /// <c>text/html</c> or <c>application/x-msdownload</c> cannot agree with anything this policy
    /// accepts, so it is refused as a mismatch whatever the bytes turn out to be.
    /// </remarks>
    private static FileTypeDecision DeclaredOutsideCatalogue() =>
        FileTypeDecision.Reject(
            RejectionReason.DeclaredMediaTypeMismatch,
            "The declared content type is not one of the accepted formats.");

    /// <summary>The media type a client sends when it names no format.</summary>
    private const string UnknownMediaType = "application/octet-stream";

    /// <summary>A media type the client declared, read against the catalogue.</summary>
    /// <param name="MediaType">The media type as compared: parameters dropped, trimmed.</param>
    /// <param name="Format">The format it names, or null when it names none the catalogue knows.</param>
    private readonly record struct Claim(string MediaType, FileFormat? Format);

    /// <summary>Applies <see cref="Confirm"/>, and records a claim that disagrees with the content.</summary>
    /// <param name="fileName">The name the client supplied, logged as it arrived.</param>
    /// <param name="inspected">The content check's decision, before any claim is compared with it.</param>
    /// <param name="declaredMediaType">What the client declared.</param>
    /// <param name="docType">The document classification, or null.</param>
    /// <returns>The decision <see cref="Confirm"/> returns.</returns>
    /// <remarks>
    /// <para>
    /// Recorded whenever the content was accepted as a format the claim does not name, whatever
    /// <see cref="Confirm"/> then makes of it: forgiven within a family, refused across one, or not
    /// looked at because the flag is off. The record exists to measure mislabelling before deciding
    /// whether to act on it, so it cannot depend on the setting that acts on it.
    /// </para>
    /// <para>
    /// Not recorded when the content was refused for its own reason. It never became a format the claim
    /// could disagree with, and that refusal is already the client's answer.
    /// </para>
    /// </remarks>
    private FileTypeDecision ConfirmAndRecord(
        string fileName, FileTypeDecision inspected, string? declaredMediaType, string? docType)
    {
        var confirmed = Confirm(inspected, declaredMediaType);

        if (inspected.Accepted
            && inspected.Format is { } accepted
            && ReadClaim(declaredMediaType) is { } claim
            && !string.Equals(claim.Format?.Name, accepted.Name, StringComparison.OrdinalIgnoreCase))
        {
            var declaredFormat = claim.Format?.Name;
            var outcome = confirmed.Accepted ? Outcome.Accepted : Outcome.Refused;

            LogDeclaredMediaTypeDisagreement(
                logger,
                fileName,
                claim.MediaType,
                declaredFormat,
                accepted.Name,
                Stage.Content,
                _policy.EnforceDeclaredMediaType,
                outcome,
                docType);
        }

        return confirmed;
    }

    /// <summary>Records a claim refused at the name check, before any content is read.</summary>
    /// <param name="fileName">The name the client supplied, logged as it arrived.</param>
    /// <param name="claim">The claim that was refused.</param>
    /// <param name="comparedWith">The format the extension names, or null for a name with none.</param>
    /// <param name="docType">The document classification, or null.</param>
    /// <remarks>
    /// Only reachable with the flag on: a refusal is the policy acting on the claim. The name check
    /// runs twice per request, but a refusal at the endpoint ends the request before the handler runs
    /// it again, so a refused claim is recorded once.
    /// </remarks>
    private void RecordRefusedClaim(string fileName, Claim claim, FileFormat? comparedWith, string? docType)
    {
        var declaredFormat = claim.Format?.Name;
        var format = comparedWith?.Name;

        LogDeclaredMediaTypeDisagreement(
            logger,
            fileName,
            claim.MediaType,
            declaredFormat,
            format,
            Stage.Name,
            _policy.EnforceDeclaredMediaType,
            Outcome.Refused,
            docType);
    }

    /// <summary>The content check for a name with no extension, and the one place it is logged.</summary>
    /// <param name="fileName">The name the client supplied, logged as it arrived.</param>
    /// <param name="declaredMediaType">What the client declared, compared once the content has decided.</param>
    /// <param name="sizeBytes">The size actually received.</param>
    /// <param name="header">The first bytes of the content.</param>
    /// <param name="openContent">Opens the staged content.</param>
    /// <param name="docType">The document classification, or null.</param>
    /// <returns>The decision.</returns>
    /// <remarks>
    /// <para>
    /// Every outcome goes through the one log call at the end, so an extensionless upload that reaches
    /// the content check produces exactly one entry, whichever branch decided it. An upload that never
    /// gets here was refused by <see cref="CheckName"/>, which logs that refusal itself.
    /// </para>
    /// <para>
    /// The empty-file check is repeated here rather than run before the extension is read, because an
    /// empty file with no name is still an extensionless arrival, and the log is what says how many
    /// of those there are.
    /// </para>
    /// </remarks>
    private FileTypeDecision InspectExtensionless(
        string fileName,
        string? declaredMediaType,
        long sizeBytes,
        ReadOnlySpan<byte> header,
        Func<Stream> openContent,
        string? docType)
    {
        DetectedFormat? detected = null;

        var decision = sizeBytes <= 0
            ? FileTypeDecision.Reject(RejectionReason.Empty, "The file is empty.")
            : MayIdentifyByContent
                ? ConfirmAndRecord(
                    fileName,
                    IdentifyByContent(sizeBytes, header, openContent, docType, out detected),
                    declaredMediaType,
                    docType)
                : FileTypeDecision.Reject(
                    RejectionReason.NoExtension, "The file name carries no extension.");

        if (decision.Accepted)
        {
            // A local, not an argument: CA1873 flags a method call passed to a logging method even
            // under an IsEnabled guard. No guard either — it would save one string per bare upload,
            // and neither a test nor the analyser could tell whether it was still there.
            var storedName = decision.StoredNameFor(fileName);

            LogExtensionlessAccepted(logger, fileName, decision.Format!.Name, sizeBytes, storedName, docType);
        }
        else
        {
            LogExtensionlessRefused(
                logger, fileName, decision.Code, Stage.Content, detected?.Extension, sizeBytes, docType);
        }

        return decision;
    }

    /// <summary>Decides on content whose name claims no format at all.</summary>
    /// <param name="sizeBytes">The size actually received.</param>
    /// <param name="header">The first bytes, for the detected format's own consistency check.</param>
    /// <param name="openContent">Opens the staged content for the detector.</param>
    /// <param name="docType">The document classification, or null.</param>
    /// <param name="detected">What the detector reported, or null — for the log, refusals included.</param>
    /// <returns>
    /// An acceptance as the detected format, flagged so the stored name takes its extension; or a
    /// refusal.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The same narrowings as <see cref="Reclassify"/>, minus the family: there is no claimed format
    /// for the detected one to belong with. The allowlist and the document type's narrowing of it
    /// resolve the detected format exactly as they would an extension, so leaving the extension out is
    /// never a way to a format this request could not have named. That is also the only gate on a
    /// format flagged <see cref="FileFormat.CarriesExecutableContent"/>, as it is for a named upload.
    /// </para>
    /// <para>
    /// Content the detector cannot place is refused as <see cref="RejectionReason.NoExtension"/>:
    /// without a name, nothing else identifies it. That covers every text format — a signature-less
    /// format is only ever confirmed by the name it arrives under — and a format whose own header
    /// check fails, since two ASCII letters are no evidence of a bitmap.
    /// </para>
    /// <para>
    /// The ceiling is the detected format's. The body was staged against the global one, because no
    /// other was known before it was read; this is where the tighter limit catches up.
    /// </para>
    /// </remarks>
    private FileTypeDecision IdentifyByContent(
        long sizeBytes,
        ReadOnlySpan<byte> header,
        Func<Stream> openContent,
        string? docType,
        out DetectedFormat? detected)
    {
        detected = SafelyDetect(openContent);

        if (detected is null)
        {
            return Unidentified();
        }

        if (!TryResolveByExtension(detected.Extension, docType, out var format) || format is null)
        {
            return FileTypeDecision.Reject(
                RejectionReason.ExtensionNotAllowed,
                $"The content is a {detected.Extension.ToUpperInvariant()} file, which is not accepted here.");
        }

        if (format.Mode == DetectionMode.TextHeuristic
            || (format.Additional is not null && !format.Additional(header, sizeBytes)))
        {
            return Unidentified();
        }

        var ceiling = CeilingFor(format);

        return sizeBytes > ceiling
            ? TooLarge(format, ceiling)
            : FileTypeDecision.Accept(format, ceiling, reclassified: true);

        static FileTypeDecision Unidentified() =>
            FileTypeDecision.Reject(
                RejectionReason.NoExtension,
                "The file name carries no extension, and its content does not identify an accepted format.");
    }

    // Source-generated, as CA1848 asks: no boxing into an object[], nothing formatted when the level
    // is off, and a template the compiler keeps in step with its arguments. The event ids are stable
    // on purpose — they are what a filter or a dashboard keys on once these logs leave the console.
    // The file name and the declared media type go out as the client sent them: they are what says
    // which client produces bare names or mislabels its files. The JSON console formatter escapes both,
    // so a value carrying a line break cannot forge a second entry.
    [LoggerMessage(
        EventId = 2001,
        EventName = "ExtensionlessUploadAccepted",
        Level = LogLevel.Information,
        Message = "Upload {FileName} has no extension; accepted as {Format} ({SizeBytes} bytes, "
            + "document type {DocType}), stored as {StoredName}.")]
    private static partial void LogExtensionlessAccepted(
        ILogger logger,
        string fileName,
        string format,
        long sizeBytes,
        string storedName,
        string? docType);

    [LoggerMessage(
        EventId = 2002,
        EventName = "ExtensionlessUploadRefused",
        Level = LogLevel.Information,
        Message = "Upload {FileName} has no extension; refused as {Reason} at the {Stage} check "
            + "(detected {DetectedFormat}, {SizeBytes} bytes, document type {DocType}).")]
    private static partial void LogExtensionlessRefused(
        ILogger logger,
        string fileName,
        string reason,
        string stage,
        string? detectedFormat,
        long? sizeBytes,
        string? docType);

    [LoggerMessage(
        EventId = 2003,
        EventName = "DeclaredMediaTypeDisagreement",
        Level = LogLevel.Information,
        Message = "Upload {FileName} was declared {DeclaredMediaType} (format {DeclaredFormat}), which does not "
            + "match {Format} at the {Stage} check; {Outcome} (declared type enforced: {Enforced}, "
            + "document type {DocType}).")]
    private static partial void LogDeclaredMediaTypeDisagreement(
        ILogger logger,
        string fileName,
        string declaredMediaType,
        string? declaredFormat,
        string? format,
        string stage,
        bool enforced,
        string outcome,
        string? docType);

    /// <summary>What happened to an upload whose claim disagreed, as the log records it.</summary>
    /// <remarks>
    /// The inspector's decision on the upload, not on the claim: <c>accepted</c> means the file passed the
    /// inspection — as what its bytes are — while its claim was wrong. Whether it was then stored is the
    /// handler's to say; storage and commit come after.
    /// </remarks>
    private static class Outcome
    {
        public const string Accepted = "accepted";

        public const string Refused = "refused";
    }

    /// <summary>Which check an entry was written at, as the log records it.</summary>
    /// <remarks>
    /// The name check runs before a byte is read, so its entries carry no size and no detected
    /// format; telling the two apart is what makes those empty fields readable.
    /// </remarks>
    private static class Stage
    {
        public const string Name = "name";

        public const string Content = "content";
    }

    /// <summary>Decides whether content may be accepted as the format it turned out to be.</summary>
    /// <param name="named">The format the extension claimed.</param>
    /// <param name="detected">What the content actually is.</param>
    /// <param name="header">The first bytes, for the detected format's own consistency check.</param>
    /// <param name="sizeBytes">The size actually received.</param>
    /// <param name="docType">The document classification, or null.</param>
    /// <returns>
    /// An acceptance as the detected format; a <see cref="RejectionReason.TooLarge"/> refusal when the
    /// content is over that format's own ceiling; or null when the policy does not allow the swap, in
    /// which case the caller refuses the mismatch exactly as it always did.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A PNG saved as <c>photo.jpg</c> is a naming accident, and the only thing a refusal achieved was
    /// to make the user rename the file. So inside a family the catalogue declares reclassifiable, the
    /// bytes win and the name is corrected — which is what OWASP's file-upload guidance asks for: set
    /// the stored extension from the detected type, checked against a defined list of image types.
    /// </para>
    /// <para>
    /// Everything else stays a refusal, and the three narrowings here are why. Across families a swap
    /// would accept a DOCX named <c>.pdf</c>, and a client opens a file by its extension. Past the
    /// allowlist it would accept a format this request was never entitled to upload. Under another
    /// format's ceiling it would let a 40 MB PNG in through the name of a TIFF.
    /// </para>
    /// <para>
    /// The executable-content test is defence in depth rather than a fourth independent gate: no input
    /// reaches it alone, because <see cref="FileFormats.IsReclassifiable"/> already refuses a family
    /// holding such a format and the detected format has to be a member of one. It is written out
    /// because it is the condition that must survive if that rule is ever relaxed to the declared list
    /// alone — the day it becomes reachable is the day it is the only thing left.
    /// </para>
    /// </remarks>
    private FileTypeDecision? Reclassify(
        FileFormat named,
        DetectedFormat detected,
        ReadOnlySpan<byte> header,
        long sizeBytes,
        string? docType)
    {
        // Resolved exactly as the claimed extension was, so a swap cannot reach a format the global
        // allowlist, or this document type's narrowing of it, would have refused.
        if (!TryResolveByExtension(detected.Extension, docType, out var actual)
            || actual is null
            || actual.CarriesExecutableContent
            || !ShareReclassifiableGroup(named, actual)
            || (actual.Additional is not null && !actual.Additional(header, sizeBytes)))
        {
            return null;
        }

        var ceiling = CeilingFor(actual);

        // The ceiling of what it is, never the one its name borrowed: appsettings allows a TIFF 50 MiB
        // and a PNG 25, so a PNG named .tif is refused at 25 like any other PNG.
        return sizeBytes > ceiling
            ? TooLarge(actual, ceiling)
            : FileTypeDecision.Accept(actual, ceiling, reclassified: true);
    }

    /// <summary>Determines whether two formats sit in one family a swap is allowed within.</summary>
    /// <param name="first">One of the two formats.</param>
    /// <param name="second">The other.</param>
    /// <returns>True when a configured, eligible family holds both.</returns>
    /// <remarks>
    /// <para>
    /// Symmetric, so callers pass the two formats in whichever order reads best: the extension's and
    /// the content's for <see cref="Reclassify"/>, a declared format and the one it is held against for
    /// <see cref="AgreesWithinFamily"/>.
    /// </para>
    /// <para>
    /// Eligibility is re-read here and not only at startup. The configured list is validated when the
    /// options are bound, but the code-side list and the executable-content rule are what make a
    /// family safe, and a family that stopped being either must stop reclassifying — not keep going
    /// because an old configuration still names it.
    /// </para>
    /// </remarks>
    private bool ShareReclassifiableGroup(FileFormat first, FileFormat second)
    {
        foreach (var group in _policy.ReclassifiableGroups)
        {
            if (!FileFormats.IsReclassifiable(group))
            {
                continue;
            }

            var members = FileFormats.Expand(group);

            if (members.Contains(first.Name, StringComparer.OrdinalIgnoreCase)
                && members.Contains(second.Name, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Refuses an upload larger than the ceiling of the format that ceiling belongs to.</summary>
    /// <param name="format">The format whose limit applied — the claimed one, or the detected one.</param>
    /// <param name="ceiling">The limit, in bytes.</param>
    /// <returns>The refusal.</returns>
    /// <remarks>
    /// The format is named because the two callers mean different ones. Before detection the ceiling is
    /// the claimed format's; after a reclassification it is the detected format's, and "the limit for
    /// this type" would then point at the extension the content has just contradicted — a 25 MB limit
    /// reported against a name ending in <c>.tif</c> reads as a bug until the message says PNG.
    /// </remarks>
    private static FileTypeDecision TooLarge(FileFormat format, long ceiling) =>
        FileTypeDecision.Reject(
            RejectionReason.TooLarge,
            $"The file exceeds the {ceiling / (1024 * 1024)} MB limit for "
                + $"{format.Name.ToUpperInvariant()} content.");

    /// <summary>Runs the detector, treating any failure as "not recognised".</summary>
    /// <param name="openContent">Opens the staged content.</param>
    /// <returns>The detected format, or null when nothing matched or the detector failed.</returns>
    /// <remarks>
    /// <para>
    /// A detector reads bytes an attacker chose. Parsing a container — a truncated ZIP, a compound
    /// file with a corrupt directory — can throw, and a malformed upload must not surface as a 500
    /// with a stack trace from a third-party library.
    /// </para>
    /// <para>
    /// Failing closed is the only safe reading: content the detector could not parse is content
    /// nobody has identified, which is exactly the case this whole class exists to refuse. The
    /// alternative — treating a parse failure as "probably fine" — would make a deliberately
    /// corrupted archive the easiest way past the check.
    /// </para>
    /// </remarks>
    private DetectedFormat? SafelyDetect(Func<Stream> openContent)
    {
        try
        {
            using var content = openContent();

            return detector.Detect(content);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Returns the last extension of a name, lowercase and without its dot.</summary>
    /// <remarks>
    /// The last one, deliberately. <c>invoice.pdf.exe</c> is an executable, and reading anything but
    /// the final segment is how that file gets treated as a PDF.
    /// </remarks>
    private static string ExtensionOf(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var extension = Path.GetExtension(fileName);

        return extension.Length > 1 ? extension[1..].ToLowerInvariant() : string.Empty;
    }

    private long CeilingFor(FileFormat format) =>
        _policy.MaxSizeByFormat.TryGetValue(format.Name, out var perFormat)
            ? Math.Min(perFormat, _policy.MaxSizeBytes)
            : _policy.MaxSizeBytes;

    private bool TryResolveByExtension(string extension, string? docType, out FileFormat? format)
    {
        format = null;

        if (extension.Length == 0)
        {
            return false;
        }

        // Entries may name a format or a set — "documents" expands to thirteen names. The document
        // type may narrow the global list, never widen it: an unknown classification falls back to
        // the global list, and a known one is intersected with it. Otherwise adding a classification
        // would become a way around the policy.
        IEnumerable<string> allowed = _policy.AllowedFormats.SelectMany(FileFormats.Expand);

        if (docType is not null
            && _policy.FormatsByDocType.TryGetValue(docType, out var narrowed))
        {
            allowed = allowed.Intersect(
                narrowed.SelectMany(FileFormats.Expand), StringComparer.OrdinalIgnoreCase);
        }

        foreach (var name in allowed)
        {
            if (FileFormats.TryGet(name, out var candidate)
                && candidate is not null
                && candidate.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                format = candidate;

                return true;
            }
        }

        return false;
    }

    /// <summary>Decides whether a header plausibly contains text.</summary>
    /// <remarks>
    /// Text formats have no signature, so they cannot be confirmed — only contradicted. A NUL byte is
    /// the contradiction that matters: legal in a binary, essentially absent from text. A UTF-8 BOM
    /// is skipped first, since it is the one leading sequence text may legitimately carry.
    /// </remarks>
    private static bool LooksLikeText(ReadOnlySpan<byte> header)
    {
        if (header.IsEmpty)
        {
            return false;
        }

        var body = header.StartsWith(Encoding.UTF8.Preamble) ? header[Encoding.UTF8.Preamble.Length..] : header;

        foreach (var b in body)
        {
            if (b == 0x00)
            {
                return false;
            }
        }

        return true;
    }
}
