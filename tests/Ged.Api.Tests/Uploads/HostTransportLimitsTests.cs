using Ged.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace Ged.Api.Tests.Uploads;

/// <summary>
/// Guards that the host applies the upload transport limits, not only that they can be derived.
/// </summary>
/// <remarks>
/// <para>
/// <c>Ged.Features.Tests</c> covers the derivation against a service collection of its own, so it
/// still passes with the call removed from <c>Program.cs</c> — while Kestrel falls back to its
/// 30,000,000-byte default and refuses, with a bare 413, uploads the policy accepts. These resolve the
/// options from the container the host built, so the wiring is what they see.
/// </para>
/// <para>
/// Equality rather than a lower bound. A literal written into <c>Program.cs</c> large enough would
/// pass a lower bound, and a second copy of the number is precisely what the derivation replaced.
/// </para>
/// </remarks>
public sealed class HostTransportLimitsTests(GedApiFactory host) : IClassFixture<GedApiFactory>
{
    /// <summary>The body the transport has to admit: the configured ceiling and its envelope.</summary>
    private const long BodyLimit = GedApiFactory.MaxSizeBytes + UploadTransportLimits.MultipartEnvelopeBytes;

    [Fact]
    public void Kestrel_admits_the_ceiling_the_host_was_configured_with()
    {
        var kestrel = host.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        kestrel.Limits.MaxRequestBodySize.ShouldBe(BodyLimit);
    }

    [Fact]
    public void The_form_binder_admits_the_ceiling_the_host_was_configured_with()
    {
        var form = host.Services.GetRequiredService<IOptions<FormOptions>>().Value;

        form.MultipartBodyLengthLimit.ShouldBe(BodyLimit);
    }
}
