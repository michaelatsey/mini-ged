using System.Reflection;
using DbUp;
using DbUp.Builder;

// DbUp is the single source of truth for the schema. EF Core maps onto it and never generates it:
// two tools able to change the same schema will eventually disagree, and the disagreement surfaces
// as a startup failure in an environment nobody was watching.
//
// Scripts are per-engine because the differences are real — filtered indexes, clustered key choice,
// JSON column type, concurrency column — and a lowest-common-denominator schema would give up the
// optimisations that matter on each side.
//
//   dotnet run --project src/Ged.Migrations -- --provider postgres --connection "<cs>"
//   dotnet run --project src/Ged.Migrations -- --provider sqlserver --connection "<cs>" --what-if

var provider = ArgValue("--provider")
    ?? Environment.GetEnvironmentVariable("GED_PROVIDER")
    ?? "postgres";

var connectionString = ArgValue("--connection")
    ?? Environment.GetEnvironmentVariable("GED_DB");

var ensureDatabase =
    args.Contains("--ensure-database", StringComparer.OrdinalIgnoreCase)
    || string.Equals(
        Environment.GetEnvironmentVariable("GED_ENSURE_DATABASE"),
        "true",
        StringComparison.OrdinalIgnoreCase);


var whatIf = args.Contains("--what-if", StringComparer.OrdinalIgnoreCase);

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Usage: Ged.Migrations --provider <postgres|sqlserver> --connection <cs> [--what-if]");
    return 2;
}

var (folder, builder) = provider.ToLowerInvariant() switch
{
    "postgres" or "postgresql" or "npgsql"
        => ("PostgreSql", Postgres(connectionString, ensureDatabase)),

    "sqlserver" or "mssql"
        => ("SqlServer", SqlServer(connectionString, ensureDatabase)),

    _ => (string.Empty, null!),
};

if (folder.Length == 0)
{
    Console.Error.WriteLine(
        $"Unknown provider '{provider}'. Expected 'postgres' or 'sqlserver'.");
    return 2;
}

var upgrader = builder
    .WithScriptsEmbeddedInAssembly(
        Assembly.GetExecutingAssembly(),
        name => name.Contains(
            $".Scripts.{folder}.",
            StringComparison.Ordinal))
    .WithTransactionPerScript()
    .LogToConsole()
    .Build();

if (whatIf)
{
    var pending = upgrader.GetScriptsToExecute();

    if (pending.Count == 0)
    {
        Console.WriteLine($"[{folder}] schema is up to date.");
    }
    else
    {
        foreach (var script in pending)
        {
            Console.WriteLine($"[{folder}] pending: {script.Name}");
        }
    }

    return 0;
}

var result = upgrader.PerformUpgrade();

if (result.Successful)
{
    Console.WriteLine($"[{folder}] upgrade successful.");
    return 0;
}

Console.Error.WriteLine(result.Error);
return 1;

static UpgradeEngineBuilder Postgres(
    string connectionString,
    bool ensureDatabase)
{
    if (ensureDatabase)
    {
        EnsureDatabase.For.PostgresqlDatabase(connectionString);
    }

    return DeployChanges.To.PostgresqlDatabase(connectionString);
}

static UpgradeEngineBuilder SqlServer(
    string connectionString,
    bool ensureDatabase)
{
    if (ensureDatabase)
    {
        EnsureDatabase.For.SqlDatabase(connectionString);
    }

    return DeployChanges.To.SqlDatabase(connectionString);
}

string? ArgValue(string name)
{
    var index = Array.FindIndex(
        args,
        argument => string.Equals(
            argument,
            name,
            StringComparison.OrdinalIgnoreCase));

    return index >= 0 && index + 1 < args.Length
        ? args[index + 1]
        : null;
}
