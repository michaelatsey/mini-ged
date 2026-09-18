# Deviations from MicroKit.Domain

**There are none.** `Ged.Domain` uses MicroKit's own base types directly —
`AuditableAggregateRoot<TId>`, `DomainEvent` and `CheckRule` — and there is no
`Ged.Domain/Abstractions` folder for a shim to live in.

This file used to describe three of them, written against `MicroKit.Domain 1.0.0-preview.5`. Every
one of those arguments was adopted upstream, and the vendored source under `libraries/` now has the
behaviour they were working around. The table is kept so the shims are not reintroduced from memory
by someone who remembers the problem but not the fix.

| Worked around then | Upstream now |
|---|---|
| `AuditableAggregateRoot<TId>` read the clock in its constructor | it takes `createdAt` and `createdBy` as parameters, so a test or an import can state the instant |
| `DomainEvent.OccurredAt` defaulted to `DateTimeOffset.UtcNow` | positional with no default, so the compiler rejects an omission; `EventId` is a UUIDv7, not a v4 |
| `AggregateRoot<TId>.CheckRule` was an instance method | `protected static`, which is why `Folder.CreateChild` can check the depth rule before the folder exists |

Two further observations this file recorded have gone stale the same way:

- `Entity<TId>` rejects the default value as well as null, so a `default(FolderId)` carrying
  `Guid.Empty` no longer gets through. The `From` factories still close it from the other side, and
  both are worth keeping.
- `MicroKit.Domain` no longer declares its own `IRepository`/`IUnitOfWork`. They live in
  `MicroKit.Persistence.Abstractions` alone, which is the pair `Ged.Domain` references.

`libraries/` is vendored source referenced by project rather than a version pin, so "the version
this targets" is whatever the last vendoring pass brought in — check `git log -- libraries/`, not a
`PackageReference`.

---

# Unverified in this environment

`Ged.Adapters.Persistence` and `Ged.Migrations` were written without a compiler: EF Core, Npgsql,
Dapper and DbUp all come from nuget.org, which was unreachable when they were authored.
`Ged.Domain`, `Ged.Core` and the domain test suite are compiler-verified; these two are not.

Three mapping decisions are bets until the first round-trip test. They are listed here so the first
person to run them knows where to look rather than reading 900 lines.

There was a fourth. `PromoteToPrimary` demoted one location and promoted another against the partial
unique index `ux_blob_location_single_primary`, and the note here offered `DEFERRABLE INITIALLY
DEFERRED` on that index as the fix. It was not a bet that could be won: the ordering it depended on
reverses between a migration and its rollback, and the escape was not valid SQL — PostgreSQL defers
constraints, not indexes, and a `UNIQUE` constraint cannot carry the filter this one needed. Issue #8
settled it by moving the value into `blob.primary_location_id` and dropping the index on both
engines. There is nothing left to order.

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

## The test that settles all three

```
1. Blob.Register(beys) → AddLocation(minio) → VerifyLocation → PromoteToPrimary
2. SaveChanges
3. new DbContext, FindAsync
4. primary_location_id names the minio row, ReadOrder correct, digest and size intact
5. PromoteToPrimary back to beys — the direction the dropped index rejected
```

Add it to a persistence test project with Testcontainers before writing any application slice.
