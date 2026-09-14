using System.Reflection;
using DbUp;

// DbUp is the single source of truth for the schema. EF Core maps onto it and never generates it:
// two tools able to change the same schema will eventually disagree, and the disagreement surfaces
// as a startup failure in an environment nobody was watching.
//
//   dotnet run --project src/Ged.Migrations -- "<connection string>"

var connectionString = args.FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("GED_DB");

if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Usage: Ged.Migrations <connection-string>  (or set GED_DB)");
    return 2;
}

EnsureDatabase.For.PostgresqlDatabase(connectionString);

var upgrader = DeployChanges.To
    .PostgresqlDatabase(connectionString)
    .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
    .WithTransactionPerScript()
    .LogToConsole()
    .Build();

if (args.Contains("--what-if"))
{
    foreach (var script in upgrader.GetScriptsToExecute())
        Console.WriteLine($"pending: {script.Name}");

    return 0;
}

var result = upgrader.PerformUpgrade();

if (result.Successful)
{
    Console.WriteLine("Upgrade successful.");
    return 0;
}

Console.Error.WriteLine(result.Error);
return 1;
