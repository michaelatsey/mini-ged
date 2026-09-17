using System.ComponentModel.DataAnnotations;

namespace Ged.Features.Common.FileTypes;

/// <summary>Which uploads an environment accepts.</summary>
/// <remarks>
/// <para>
/// An allowlist, never a denylist. A denylist has to enumerate everything dangerous, which is an
/// open set that grows with every new parser and every new container format; an allowlist has to
/// enumerate what the business actually needs, which is short and stable.
/// </para>
/// <para>
/// Bound from <c>Ged:Uploads</c> and validated at startup, so a typo in a format name stops the
/// deployment instead of silently narrowing — or widening — what the API accepts.
/// </para>
/// </remarks>
public sealed class UploadPolicyOptions
{
    /// <summary>The configuration section this binds to.</summary>
    public const string SectionName = "Ged:Uploads";

    /// <summary>Gets or sets the formats accepted. Empty accepts nothing.</summary>
    /// <remarks>
    /// Each entry is a format name — <c>pdf</c>, <c>docx</c> — or a set name: <c>documents</c>,
    /// <c>images</c>, <c>office</c>, <c>text</c>. A set is expanded at resolution time, so adding a
    /// format to a set in code widens every environment that names that set. Where that is not
    /// wanted, list the formats explicitly.
    /// </remarks>
    [MinLength(1)]
    public IList<string> AllowedFormats { get; set; } = [];

    /// <summary>Gets or sets the ceiling applied to every upload, in bytes.</summary>
    [Range(1, 4L * 1024 * 1024 * 1024)]
    public long MaxSizeBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>Gets or sets per-format ceilings, in bytes. Overrides <see cref="MaxSizeBytes"/>.</summary>
    /// <remarks>
    /// A scanned contract is legitimately large; a CSV of 200 MB is a mistake or an attack. One
    /// global ceiling has to be set for the largest case, which leaves every other format unbounded
    /// in practice.
    /// </remarks>
    public IDictionary<string, long> MaxSizeByFormat { get; set; } =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the formats accepted for a given document classification.</summary>
    /// <remarks>
    /// Narrows <see cref="AllowedFormats"/>, never widens it: a document type absent from this map
    /// falls back to the global list, and one present may only restrict it. Otherwise a new
    /// classification would become a way around the policy.
    /// </remarks>
    public IDictionary<string, IList<string>> FormatsByDocType { get; set; } =
        new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets which detector identifies content.</summary>
    /// <remarks>
    /// <para>
    /// <c>builtin</c> covers the formats in this catalogue and carries no dependency.
    /// <c>filesignatures</c> delegates to a maintained library with wider coverage, at the cost of a
    /// package and an MPL-2.0 transitive dependency.
    /// </para>
    /// <para>
    /// The switch exists so the choice can be revisited without a code change — and so both can be
    /// run against the same test suite before one is trusted in production.
    /// </para>
    /// </remarks>
    public string Detector { get; set; } = "builtin";

    /// <summary>
    /// Gets or sets a value indicating whether the declared media type must match the detected one.
    /// </summary>
    /// <remarks>
    /// Off by default. Browsers and HTTP clients disagree about the media type of the same file —
    /// <c>text/csv</c> versus <c>application/vnd.ms-excel</c> is the classic case — so enforcing the
    /// client's header rejects legitimate uploads while stopping no attacker, who controls that
    /// header entirely. Enable it only for a known, controlled client.
    /// </remarks>
    public bool EnforceDeclaredMediaType { get; set; }
}
