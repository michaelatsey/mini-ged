namespace Ged.Domain.Tests.Architecture;

/// <summary>
/// Placeholder for the mapping guard. Moves to a persistence test project once a database is
/// available in CI.
/// </summary>
/// <remarks>
/// The check that matters: DbUp applies every script to an empty database, then each DbSet is
/// queried once. Any divergence between the SQL scripts and the EF configurations fails the build
/// instead of failing at startup in an environment nobody was watching.
/// </remarks>
public sealed class SchemaMappingTests
{
    [Fact(Skip = "Requires a PostgreSQL container; enable once Testcontainers is wired into CI.")]
    public void Ef_model_matches_the_dbup_schema() { }
}
