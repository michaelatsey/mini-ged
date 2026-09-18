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
# It prints the missing COPY lines — project files and source directories — ready to paste.
./scripts/check-dockerfile-copies.sh hosts/Ged.Api/Dockerfile

docker compose up --build                       # PostgreSQL, the schema runner, the API
./scripts/audit.sh                              # before every push; the repository is public
./scripts/repo-map.sh                           # after finishing a piece of work; writes REPO-MAP.md
./tests/scripts/run.sh                          # after changing anything under scripts/

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

**Which location serves reads is `blob.primary_location_id`, never a state on `blob_location`.**
It was a `PRIMARY` state under a partial unique index until `0008`, and that form required the
demotion and the promotion to reach the engine in a fixed order — which a rollback reverses. Do not
reintroduce a state, and do not add an index to enforce a cardinality a column already holds.

---

## Conventions

- French in conversation. **English in code, XML comments, commit messages and repository files.**
- XML documentation on every public member, saying *why* rather than *what*.
- `internal` for a port implementation **the adapter registers itself** — an abstraction that can be
  bypassed protects nothing. Eight implementations are public today, in two groups, listed so that
  neither group is rediscovered every session:
  - **Cannot be `internal`.** `Ged.Adapters.Storage` and `Ged.Adapters.Storage.FileSystem` ship no
    `AddGed…` extension, so `Program.cs` names `FileSystemObjectStorage`, `ObjectStorageRegistry`
    and `BlobLocationResolver` directly. Making those three `internal` is CS0122 at
    `hosts/Ged.Api/Program.cs`; giving the two projects a registration extension is what would earn
    it.
  - **Public without needing to be.** `SystemClock`, `EfUnitOfWork`, `BuiltInContentFormatDetector`,
    `FileSignaturesContentDetector` and `FileTypeInspector` are each registered by their own
    assembly and referenced from nowhere else — `EfDocumentRepository` beside them is already
    `internal`. Tightening one is the rule applied correctly, not a finding to report.
- Tests assert on the **type** of the violated rule, never on its message, and use fixed instants.
- Conventional commits; the body explains the decision, not the diff.

---

## Already rejected

Do not reintroduce. If one deserves revisiting, say so with the argument — do not work around it
silently. Only the reference count has a doc that argues it — `docs/domain-boundaries.md`. Endpoint
discovery is argued in `GedEndpoints.cs` and `Program.cs`, the repository shape in XML comments on
the three ports. For EF Core migrations, `README.md` states the outcome ("DbUp owns it; EF Core
never generates it") and `docs/containers.md` argues the adjacent point — never migrate at startup,
it races a rolling deploy — but the tooling choice itself is argued nowhere. Nor are MediatR and
AutoMapper: those three rest on this line alone.

MediatR · AutoMapper · a generic repository standing in for a named port · search or paging methods
on a repository · endpoint discovery by reflection · EF Core migrations · a stored reference count.

`IRepository<TAggregate>` itself is **not** rejected and not optional. `IBlobRepository`,
`IDocumentRepository` and `IFolderRepository` all derive from it for staging and commit, and each
adds a typed `FindAsync`; removing the base interface breaks every `AddAsync` call in the handlers.
`IFolderRepository` also has `GetAncestryAsync` — it returns identities that feed a domain rule, not
a projection for a screen, which is why it is not the search method above. What is rejected is
injecting `IRepository<T>` where a named port belongs — no handler takes it.

Settled: nothing ambient (instant and actor are parameters), rules are `BusinessRule` types, the
domain owns the rule and the caller supplies the fact, and no business operation reaches a storage
`Delete`.

---

## Open — do not decide these

Four questions are deliberately unanswered. Nothing in the repository says so, which is why they are
here: without this section a session picks one and implements it.

- **Multi-tenancy** — database, schema, or `tenant_id` column. Nothing tenant-aware exists yet:
  `git grep ITenantContext` matches this file and `README.md` and no type anywhere. Do not add one —
  writing the interface is already choosing. The strategy decides the persistence adapters, not the
  domain.
- **Resource authorization** — how `RessourceCode` and `ActionCode` are resolved.
- **Legacy Office formats** — `.doc`, `.xls`, `.ppt`, `.rtf` are flagged `CarriesExecutableContent`
  and left out of the **explicit** `AllowedFormats` in `appsettings.json` — but not out of the
  catalogue's sets: `documents` expands to all four and `office` to the first three.
  `docs/uploads.md` offers sets as the readable way to write a policy and then says to list formats
  explicitly in production — only the second half protects, which is why the shipped list is
  thirteen names. So `"AllowedFormats": ["documents"]` would accept all four silently, and
  `MaxSizeByFormat` already carries an `rtf` entry. A security policy decision, not a technical one.
- **Malware scanning** — whether an ICAP service already exists. The answer changes the adapter, not
  the architecture.

<!-- Current state is deliberately absent: `dotnet build`, `dotnet test` and `git log` are
     authoritative and always current, while a paragraph here goes stale on the next commit.
     Roadmap and status live in README.md. -->
