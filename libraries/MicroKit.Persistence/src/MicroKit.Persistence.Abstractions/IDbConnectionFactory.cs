using System.Data.Common;

namespace MicroKit.Persistence.Abstractions;

/// <summary>Opens connections for the read side.</summary>
/// <remarks>
/// <para>
/// Read handlers write their own SQL and project straight into the shape one screen needs. They do
/// not go through a repository: a repository exists to reconstitute an aggregate so a rule can be
/// applied to it, and the moment it gains a search method it becomes a data-access layer every
/// slice starts reaching into.
/// </para>
/// <para>
/// The port returns <see cref="DbConnection"/> rather than a provider type so a read can later be
/// pointed at a replica, a materialized view or a different database without touching the handler.
/// </para>
/// </remarks>
public interface IDbConnectionFactory
{
    /// <summary>Opens a new connection.</summary>
    /// <param name="ct">Propagates notification that the operation should be cancelled.</param>
    /// <returns>An open connection the caller is responsible for disposing.</returns>
    ValueTask<DbConnection> OpenAsync(CancellationToken ct = default);
}
