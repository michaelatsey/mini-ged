using System.Globalization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ged.Features.Tests.Uploads;

/// <summary>
/// Covers the two ceilings that sit in front of the upload policy and refuse a request before any
/// handler runs: Kestrel's request body limit and the form binder's multipart limit.
/// </summary>
/// <remarks>
/// Both default to a number smaller than a scanned document — 30 MB and 128 MB — and both refuse
/// during model binding, where the policy has no say and the response is a bare 413. So the fixture
/// asserts on what the container of options holds, not on a response: a limit left at its default is
/// the defect, and it is invisible until a large file arrives.
/// </remarks>
public sealed class UploadTransportLimitsTests
{
    /// <summary>Above Kestrel's 30,000,000-byte default, below the form binder's 128 MiB one.</summary>
    private const long Ceiling = 50L * 1024 * 1024;

    [Fact]
    public void Kestrel_accepts_a_body_carrying_an_upload_at_the_ceiling()
    {
        var limits = Configured(Ceiling).GetRequiredService<IOptions<KestrelServerOptions>>();

        limits.Value.Limits.MaxRequestBodySize.ShouldNotBeNull()
            .ShouldBeGreaterThan(Ceiling);
    }

    [Fact]
    public void The_form_binder_accepts_a_body_carrying_an_upload_at_the_ceiling()
    {
        var form = Configured(Ceiling).GetRequiredService<IOptions<FormOptions>>();

        form.Value.MultipartBodyLengthLimit.ShouldBeGreaterThan(Ceiling);
    }

    [Fact]
    public void Both_ceilings_follow_the_policy_down_as_well_as_up()
    {
        // A deployment that narrows the policy should stop paying for bodies it will refuse: the
        // transport is derived from the ceiling, not written beside it.
        var services = Configured(4L * 1024 * 1024);

        services.GetRequiredService<IOptions<KestrelServerOptions>>()
            .Value.Limits.MaxRequestBodySize.ShouldNotBeNull()
            .ShouldBeLessThan(Ceiling);

        services.GetRequiredService<IOptions<FormOptions>>()
            .Value.MultipartBodyLengthLimit.ShouldBeLessThan(Ceiling);
    }

    [Fact]
    public void An_unconfigured_section_still_lifts_the_transport_off_its_defaults()
    {
        // appsettings.json states a ceiling, but the options class carries one of its own. A host
        // that binds nothing must not fall back to the 30 MB default this exists to replace.
        var services = new ServiceCollection()
            .AddGedUploadTransportLimits(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        services.GetRequiredService<IOptions<KestrelServerOptions>>()
            .Value.Limits.MaxRequestBodySize.ShouldNotBeNull()
            .ShouldBeGreaterThan(30_000_000);
    }

    /// <summary>A provider configured from a policy stating the given ceiling.</summary>
    private static ServiceProvider Configured(long maxSizeBytes)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ged:Uploads:AllowedFormats:0"] = "pdf",
                ["Ged:Uploads:MaxSizeBytes"] = maxSizeBytes.ToString(CultureInfo.InvariantCulture),
            })
            .Build();

        return new ServiceCollection()
            .AddGedUploadTransportLimits(configuration)
            .BuildServiceProvider();
    }
}
