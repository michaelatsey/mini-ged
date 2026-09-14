using Ged.Domain.Documents;

namespace Ged.Domain.Tests.Architecture;

/// <summary>
/// Guards the properties that make this assembly a domain rather than a data layer. A decoupling
/// that is not verified automatically is an intention, not a decoupling.
/// </summary>
public sealed class DomainPurityTests
{
    private static readonly string[] Infrastructure =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions.DependencyInjection",
        "Dapper",
        "Npgsql",
        "Minio",
        "Azure.Storage.Blobs"
    ];

    [Fact]
    public void The_domain_references_no_infrastructure()
    {
        var referenced = typeof(Document).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToArray();

        referenced.ShouldNotContain(name => Infrastructure.Contains(name));
    }

    [Fact]
    public void Repositories_expose_no_read_model_methods()
    {
        string[] forbidden = ["Search", "GetPaged", "List", "Count", "Query"];

        var offenders = typeof(Document).Assembly.GetTypes()
            .Where(t => t.IsInterface && t.Name.EndsWith("Repository", StringComparison.Ordinal))
            .SelectMany(t => t.GetMethods())
            .Where(m => forbidden.Any(f => m.Name.StartsWith(f, StringComparison.Ordinal)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToArray();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Every_domain_event_derives_from_the_stamped_base()
    {
        var offenders = typeof(Document).Assembly.GetTypes()
            .Where(t => typeof(MicroKit.Domain.Events.IDomainEvent).IsAssignableFrom(t))
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => !typeof(GedDomainEvent).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToArray();

        offenders.ShouldBeEmpty();
    }
}
