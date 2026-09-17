using Ged.Features.Common.FileTypes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ged.Features;

/// <summary>Lifts the transport ceilings to the one <c>Ged:Uploads</c> already states.</summary>
/// <remarks>
/// <para>
/// Two limits sit in front of the upload policy and neither belongs to it: Kestrel's
/// <c>MaxRequestBodySize</c>, 30,000,000 bytes by default, and the form binder's
/// <c>MultipartBodyLengthLimit</c>, 128 MiB. The lowest of the three decides, and the two defaults
/// refuse while the request is being bound — before a handler runs, so the answer is a bare 413 with
/// nothing of the policy in it.
/// </para>
/// <para>
/// They are derived from <see cref="UploadPolicyOptions.MaxSizeBytes"/> rather than written beside
/// it because a second copy of a number is a number that drifts: the ceiling was raised to 256 MiB
/// in configuration while the transport stayed at 30 MB, and every upload over 28.6 MB failed with
/// a message about neither.
/// </para>
/// <para>
/// This only decides how large a body may be read. What the bytes are allowed to be is still the
/// policy's business, and the per-format ceilings are still enforced during staging — the transport
/// limit is set for the largest format the application accepts, so it cannot stand in for them.
/// </para>
/// </remarks>
public static class UploadTransportLimits
{
    /// <summary>Room for the multipart envelope wrapped around the file itself.</summary>
    /// <remarks>
    /// Boundaries, part headers and the other fields of the form all count towards the body size but
    /// not towards the ceiling the policy applies to the content. A mebibyte is far more than an
    /// envelope needs and far less than a second file would take.
    /// <para>
    /// Public because it is part of what a request costs: the transport admits the ceiling
    /// <em>plus</em> this, and whatever sizes the temporary space an upload is staged in has to
    /// budget for the sum rather than for the ceiling alone.
    /// </para>
    /// </remarks>
    public const long MultipartEnvelopeBytes = 1024 * 1024;

    /// <summary>The ceiling for a form field that is not a file.</summary>
    /// <remarks>
    /// Tightened from the 4 MiB default. An upload carries a file and a couple of identifiers; a
    /// megabyte of one of those identifiers is not a case worth buffering.
    /// </remarks>
    private const int ValueLengthLimit = 1024 * 1024;

    /// <summary>Configures Kestrel and the form binder from the upload policy.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Supplies the ceiling, from <c>Ged:Uploads:MaxSizeBytes</c>.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <remarks>
    /// The section is read here rather than resolved through <c>IOptions</c>: both limits are read
    /// once, while the server is being built, so binding them later would only hide that a change
    /// takes a restart. A section that is absent leaves the policy's own default in charge, which is
    /// still far above the 30 MB this exists to replace. The range is validated by the policy, not
    /// here, so a nonsensical value fails with the name of the setting in the message.
    /// </remarks>
    public static IServiceCollection AddGedUploadTransportLimits(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var policy = configuration.GetSection(UploadPolicyOptions.SectionName).Get<UploadPolicyOptions>()
            ?? new UploadPolicyOptions();

        var bodyLimit = policy.MaxSizeBytes + MultipartEnvelopeBytes;

        services.Configure<KestrelServerOptions>(options => options.Limits.MaxRequestBodySize = bodyLimit);

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = bodyLimit;
            options.ValueLengthLimit = ValueLengthLimit;
        });

        return services;
    }
}
