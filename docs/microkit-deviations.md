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

---

# Unverified in this environment

`Ged.Adapters.Persistence` and `Ged.Migrations` were written without a compiler: EF Core, Npgsql,
Dapper and DbUp all come from nuget.org, which was unreachable when they were authored.
`Ged.Domain`, `Ged.Core` and the domain test suite are compiler-verified; these two are not.

Four mapping decisions are bets until the first `dotnet build` and round-trip test. They are listed
here so the first person to run them knows where to look rather than reading 900 lines.

## Bet 1 — constructor binding

Aggregates have no parameterless constructor, because an entity without an identity is not a valid
domain object. EF is expected to bind the private constructor by matching parameter names to
property names:

```
private Document(DocumentId id, FolderId folderId, DocumentName name,
                 DocType type, DateTimeOffset createdAt, Actor createdBy)
                 │          │            │
                 Id         FolderId     Name  …
```

If binding fails, the fix is a private parameterless constructor on each aggregate — not a change
to the public factory methods.

## Bet 2 — owned collections over a backing field

`Document.Versions` and `Blob.Locations` expose `IReadOnlyList<T>` over a private `List<T>` and are
mapped with `OwnsMany` plus `UsePropertyAccessMode(Field)` and `AutoInclude`.

Owned rather than related, deliberately: an owned type cannot be queried independently, so the
aggregate boundary becomes a property of the model rather than a convention. The risk is that EF
rejects the read-only projection, since `AsReadOnly()` allocates a new wrapper on each access.

## Bet 3 — `ObjectKey` as a nested owned type

`ObjectKey` carries two values, so it cannot go through a single value converter. It is mapped as
an owned type nested inside the owned `BlobLocation` collection. Nesting owned types is supported;
this particular combination is the least-travelled path in the whole mapping.

## Bet 4 — two state mutations in one `SaveChanges`

`PromoteToPrimary` demotes one location and promotes another. Both must land in the same
transaction, or the unique index `ux_blob_location_single_primary` rejects the write.

If EF orders the two updates badly, the fix is `DEFERRABLE INITIALLY DEFERRED` on the index — the
same technique already used for `fk_document_current_version`, where the document and its first
version are inserted together and neither can be written first.

## The test that settles all four

```
1. Blob.Register(beys) → AddLocation(minio) → VerifyLocation → PromoteToPrimary
2. SaveChanges
3. new DbContext, FindAsync
4. exactly one PRIMARY, ReadOrder correct, digest and size intact
```

Add it to a persistence test project with Testcontainers before writing any application slice.
