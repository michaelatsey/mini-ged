using System.Text.Json;
using MicroKit.Domain.Events;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ged.Adapters.Persistence.Outbox;

/// <summary>
/// Moves domain events from tracked aggregates into the outbox table, inside the same transaction.
/// </summary>
/// <remarks>
/// An interceptor rather than a call in each handler. A handler that forgets to publish produces a
/// state change with no event — a silent failure that surfaces days later as a missing thumbnail or
/// a stale index. Here the aggregate raises, and capture is structural.
/// </remarks>
public sealed class OutboxInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            Capture(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            Capture(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private static void Capture(DbContext context)
    {
        var providers = context.ChangeTracker
            .Entries<IDomainEventsProvider>()
            .Select(entry => entry.Entity)
            .ToArray();

        if (providers.Length == 0)
            return;

        var messages = new List<OutboxMessage>();

        foreach (var provider in providers)
        {
            foreach (var domainEvent in provider.DrainDomainEvents())
            {
                messages.Add(new OutboxMessage
                {
                    Id = domainEvent.EventId,
                    OccurredAt = domainEvent.OccurredAt,
                    Type = domainEvent.GetType().FullName!,
                    Payload = JsonSerializer.Serialize(
                        domainEvent, domainEvent.GetType(), SerializerOptions),
                });
            }
        }

        if (messages.Count > 0)
            context.Set<OutboxMessage>().AddRange(messages);
    }
}
