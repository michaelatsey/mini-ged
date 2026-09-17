# CLAUDE.md — mini-ged

Document management platform (GED) on .NET 10. A document is a stable identity with a history of
versions, each pointing at immutable content addressed by its SHA-256.

`REPO-MAP.md` maps the solution. `docs/` explains why each decision was made. Read either when the
task needs it.

---

## Commands

```bash
# YOU MUST run this before any docker build. Debug hides analyser errors that Release
# treats as fatal, so the container build is stricter than F5 and fails 30s in.
dotnet build -c Release

# After adding or removing ANY ProjectReference, before building an image.
# It prints the missing COPY lines ready to paste.
./scripts/check-dockerfile-copies.sh hosts/Ged.Api/Dockerfile

docker compose up --build                       # PostgreSQL, the schema runner, the API
./scripts/audit.sh                              # before every push; the repository is public
./scripts/repo-map.sh > REPO-MAP.md             # after finishing a piece of work

# `--profile` is a top-level flag: after `up` it is `unknown flag: --profile`. And it only adds the
# SQL Server container and its migrations — the `api` service keeps `Ged__Provider: postgres`, so
# this does NOT exercise the API against SQL Server. Override the provider and the connection string
# to do that.
docker compose --profile sqlserver up --build
```

---

## Gotchas

Each one has already cost a debugging session, and none is visible from the code.

**IMPORTANT: `libraries/` is vendored MicroKit source.** Never edit it here — a change not made
upstream is an undeclared fork that the next vendoring pass reverts silently. It also means the
Docker build context is the repository root, never narrower.

**A Dockerfile's `COPY` list is a second copy of the reference graph.** It has drifted three times,
surfacing as `NETSDK1004` inside a layer — with a message about restore when the restore had
succeeded over the wrong set of projects.

**`/tmp` as tmpfs is mandatory in the container.** Uploads are staged there while being hashed;
`read_only` without it breaks every upload.

**Every SQL parameter declares its type.** An untyped null breaks PostgreSQL with `42P08` when its
first occurrence is an `IS NULL`. Use the generic `AddParameter<T>`. Never a `::uuid` cast — it would
break SQL Server in a slice that runs on both. A type it does not map throws rather than going out
undeclared, so widening the map is a deliberate edit, not a silent fallback. `DateTime` and
`TimeSpan` are refused on purpose: neither reads the same way on both engines. An instant is a
`DateTimeOffset`.

**A value object spanning more than one column is a navigation to EF, not a value**, so it cannot be
a constructor parameter. `BlobLocation` carries a second private constructor for exactly this.

**Content reaches storage before the transaction opens.** Never move the write inside it: a failed
commit must leave a detectable orphan, not an object nothing refers to.

**`PromoteToPrimary` demotes one location and promotes another in one `SaveChanges`,** against
`ux_blob_location_single_primary`. Untested against a real engine. If it fails, the fallback is
`DEFERRABLE INITIALLY DEFERRED` on the PostgreSQL index.

---

## Conventions

- French in conversation. **English in code, XML comments, commit messages and repository files.**
- XML documentation on every public member, saying *why* rather than *what*.
- `internal` for every port implementation — an abstraction that can be bypassed protects nothing.
- Tests assert on the **type** of the violated rule, never on its message, and use fixed instants.
- Conventional commits; the body explains the decision, not the diff.

---

## Already rejected

Do not reintroduce. If one deserves revisiting, say so with the argument — do not work around it
silently. Reasoning in `docs/`.

MediatR · AutoMapper · generic `IRepository<T>` · search methods on a repository · endpoint discovery
by reflection · EF Core migrations · a stored reference count.

Settled: nothing ambient (instant and actor are parameters), rules are `BusinessRule` types, the
domain owns the rule and the caller supplies the fact, and no business operation reaches a storage
`Delete`.

---

## Open — do not decide these

Four questions are deliberately unanswered. Nothing in the repository says so, which is why they are
here: without this section a session picks one and implements it.

- **Multi-tenancy** — database, schema, or `tenant_id` column. `ITenantContext` exists; the strategy
  decides the persistence adapters, not the domain.
- **Resource authorization** — how `RessourceCode` and `ActionCode` are resolved.
- **Legacy Office formats** — `.doc`, `.xls`, `.ppt`, `.rtf` are in the catalogue but not the
  allowlist. A security policy decision, not a technical one.
- **Malware scanning** — whether an ICAP service already exists. The answer changes the adapter, not
  the architecture.

<!-- Current state is deliberately absent: `dotnet build`, `dotnet test` and `git log` are
     authoritative and always current, while a paragraph here goes stale on the next commit.
     Roadmap and status live in README.md. -->
