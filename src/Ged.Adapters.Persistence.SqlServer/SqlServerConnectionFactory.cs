using System.Data.Common;
using Ged.Core.Ports;
using Microsoft.Data.SqlClient;

namespace Ged.Adapters.Persistence.SqlServer;

/// <summary>Opens SQL Server connections for the read side.</summary>
/// <param name="connectionString">The connection string.</param>
/// <remarks>
/// SqlClient pools connections internally by connection string, so a new
/// <see cref="SqlConnection"/> per call takes one from the pool rather than opening a socket.
/// </remarks>
internal sealed class SqlServerConnectionFactory(string connectionString) : IDbConnectionFactory
{
    /// <inheritdoc />
    public async ValueTask<DbConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
