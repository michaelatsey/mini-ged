# Deviations from MicroKit.Domain

`Ged.Domain` targets `MicroKit.Domain 1.0.0-preview.5`. Three small types in
`Ged.Domain/Abstractions` exist only to work around behaviour in that version. Each one is listed
here with the upstream change that would let it be deleted.

None of these is a criticism of the library's design — all three are defaults that favour the
ergonomics of the first call and cost something later.

## 1. `AuditableRoot<TId>` instead of `AuditableAggregateRoot<TId>`

`AuditableAggregateRoot<TId>` sets `CreatedAt = DateTimeOffset.UtcNow` inside its constructor, and
exposes `UpdatedAt` and `UpdatedBy` as independent `protected set` properties.

Two consequences:

- The aggregate reads the clock, so no test can assert an exact creation instant, and no import or
  replay can reconstruct one.
- Nothing forces the two update fields to move together, so an audit trail can record *when*
  something changed without recording *who* changed it.

`AuditableRoot<TId>` takes `createdAt` and `createdBy` as constructor arguments and exposes a
single `Touch(updatedAt, updatedBy)`.

**Delete this shim when** the constructor takes the instant as a parameter and the update fields
are behind one member.

## 2. `GedDomainEvent` instead of `DomainEvent`

`DomainEvent.OccurredAt` defaults to `DateTimeOffset.UtcNow`. The value is therefore optional at
every construction site, and events belonging to one business operation drift apart by
milliseconds with nothing to signal it.

`GedDomainEvent` takes the instant as a constructor argument, so concrete events stay one line and
the compiler rejects an omission:

```csharp
public sealed record DocumentRenamed(
    Guid DocumentId, string PreviousName, string NewName, DateTimeOffset OccurredAt)
    : GedDomainEvent(OccurredAt);
```

**Delete this shim when** `DomainEvent` itself makes the instant mandatory — either as a positional
parameter or as a `required` member.

Unrelated but worth pairing with that change: `EventId` uses `Guid.NewGuid()`. Domain events are
routinely persisted in an outbox keyed by that identifier, and a random v4 key fragments the index.
`Guid.CreateVersion7()` is a drop-in, non-breaking improvement.

## 3. `DomainRules.Check` instead of `CheckRule`

`AggregateRoot<TId>.CheckRule` is an instance method, so a static factory cannot call it before the
aggregate exists — yet that is exactly where creation-time rules such as a depth limit must run.
`Folder.CreateChild` uses `DomainRules.Check` for that reason.

`CheckRule` touches no instance state, so making it `static` is both correct and what CA1822 asks
for.

**Delete this shim when** `CheckRule` is static.

## Not worked around

Two further observations that do not affect this project but are worth recording:

- `Entity<TId>` guards its identifier with `if (id is null)`. For a `readonly record struct`
  identifier — the form the library's own samples recommend — that check never fires, so
  `default(FolderId)` carrying `Guid.Empty` would be accepted. Here the `From` factories reject an
  empty GUID, which closes the gap from the other side.
- `MicroKit.Domain.Repositories` and `MicroKit.Persistence.Abstractions` both declare `IRepository`
  and `IUnitOfWork`. ADR-001 states the contracts moved to the persistence package; the copies left
  behind in `MicroKit.Domain` are the ones this project does not use.
