using System.Data.Common;
using Ged.Core.Ports;
using Npgsql;

namespace Ged.Adapters.Persistence.PostgreSql;

/// <summary>Opens PostgreSQL connections for the read side.</summary>
/// <param name="dataSource">The pooled data source.</param>
/// <remarks>
/// Backed by a single <see cref="NpgsqlDataSource"/> so connections come from one pool rather than
/// being built per call. Read handlers are the highest-frequency path in the system; a connection
/// string parsed on every request is a cost paid on every screen.
/// </remarks>
internal sealed class NpgsqlConnectionFactory(NpgsqlDataSource dataSource) : IDbConnectionFactory
{
    /// <inheritdoc />
    public async ValueTask<DbConnection> OpenAsync(CancellationToken ct = default) =>
        await dataSource.OpenConnectionAsync(ct);
}
