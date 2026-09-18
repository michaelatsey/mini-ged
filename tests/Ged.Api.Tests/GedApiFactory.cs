using System.Globalization;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ged.Api.Tests;

/// <summary>Boots the real composition root in memory, with no database behind it.</summary>
/// <remarks>
/// <para>
/// <c>Program.cs</c> is what runs — every registration, in its order — so a line removed from it is
/// a line removed here. The factory only supplies configuration, and supplies it as host
/// configuration: the host reads its provider, its connection string and its upload ceiling before
/// <c>Build()</c>, and host configuration reaches <c>WebApplication.CreateBuilder</c> as arguments.
/// <c>ConfigureAppConfiguration</c> is applied after <c>Build()</c>, too late for any of those reads.
/// </para>
/// <para>
/// The connection string has to be present, not reachable: nothing opens a connection while the host
/// starts — the data source is created on first resolution, and the database health check runs only
/// when <c>/health/ready</c> is requested. It names a host under <c>.invalid</c>, which never
/// resolves, so a registration that starts connecting eagerly fails here on every machine instead of
/// passing wherever a developer happens to have PostgreSQL running. The suite runs without a
/// database, and that is worth keeping.
/// </para>
/// <para>
/// Production, because that is the environment the limits have to hold in, and because it keeps user
/// secrets and <c>appsettings.Development.json</c> out. Environment variables still reach the host,
/// as they reach any deployment; the three settings stated here win over them, since host
/// configuration arrives as command-line arguments.
/// </para>
/// </remarks>
public sealed class GedApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The upload ceiling the host is started with.</summary>
    /// <remarks>
    /// Distinct from every number the host could reach without reading its configuration: Kestrel's
    /// 30,000,000-byte default, the form binder's 128 MiB, and the 256 MiB that both
    /// <c>appsettings.json</c> and <c>UploadPolicyOptions</c> state. A limit derived from any of those
    /// fails an assertion against this one instead of matching it by accident.
    /// </remarks>
    public const long MaxSizeBytes = 50L * 1024 * 1024;

    /// <inheritdoc />
    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Production);

        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Ged:Provider"] = "postgres",
                ["ConnectionStrings:Ged"] = "Host=database.invalid;Database=ged;Username=ged",
                ["Ged:Uploads:MaxSizeBytes"] = MaxSizeBytes.ToString(CultureInfo.InvariantCulture),
            }));

        return base.CreateHost(builder);
    }
}
