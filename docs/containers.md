# Containers

## Why `compose.yaml` and not `docker-compose.yml`

`compose.yaml` is the canonical name in the Compose Specification. `docker-compose.yaml` and
`docker-compose.yml` are supported for backward compatibility with Compose V1, and when several are
present Compose picks the canonical one.

Auto-discovery order:

```
compose.yaml          canonical, preferred
compose.yml
docker-compose.yml    legacy
docker-compose.yaml   legacy
```

Visual Studio still emits `docker-compose.yml` when you add container orchestration support, which is
where the discrepancy usually comes from. Both work; only one is the name the specification asks for,
and mixing the two in one repository means a reader has to check which file Compose actually loaded.

## Layout

```
Directory.Build.props
Directory.Packages.props
Ged.sln
compose.yaml
src/                        application projects
  Ged.Api/Dockerfile
database/                   schema lifecycle, deliberately outside src/
  Ged.Migrations/Dockerfile
tests/
```

`Ged.Migrations` sits under `database/` rather than `src/` because its lifecycle is the schema's, not
the application's: it ships as its own image, runs to completion, and is versioned by SQL scripts
rather than by code. Keeping it beside the application projects invites the assumption that it is one
of them — and the day someone adds a `ProjectReference` from the API to it, the schema runner starts
shipping inside the API image.

Both Dockerfiles live in their project folder and take the **repository root** as build context,
because every project needs `Directory.Build.props` and `Directory.Packages.props` from the root:

```bash
docker build -f src/Ged.Api/Dockerfile           -t ged-api .
docker build -f database/Ged.Migrations/Dockerfile -t ged-migrations .
```

Two images: the API, and a schema runner that exits. Both chiseled, both non-root, both read-only.

```
src/Ged.Api/Dockerfile          aspnet:10.0-noble-chiseled-extra
database/Ged.Migrations/Dockerfile   runtime:10.0-noble-chiseled-extra
compose.yaml                    postgres | sqlserver profile, migrations, api
compose.probe.yaml              optional in-container health probe
```

## Base images

.NET 10 no longer ships Debian images; the default distro is Ubuntu 24.04 (Noble). Chiseled images
are distroless: only the packages .NET needs, no shell, no package manager, no libc utilities.

| Choice | Reason |
|---|---|
| `chiseled` over `noble` | nothing for an attacker who reaches code execution to pivot with |
| `-extra` over plain chiseled | carries ICU and tzdata |
| `runtime` for migrations | the process opens a connection, runs SQL and exits; it needs no web server |
| tag now, digest in CI | a moving tag is a build that is not reproducible |

The `-extra` variant is worth its ~10 MB. The alternative is `InvariantGlobalization=true`, which
silently changes how string comparison and casing behave — and a database driver that expected ICU
then fails in ways that read as data bugs rather than configuration ones.

## Layer caching

Project files and `Directory.*.props` are copied before the source, so the restore layer survives any
build that changes only code. Copying everything up front invalidates the restore on every edit,
which is the single biggest waste in a .NET Dockerfile.

```dockerfile
COPY Directory.Build.props Directory.Packages.props Ged.sln ./
COPY src/Ged.Domain/Ged.Domain.csproj src/Ged.Domain/
...
RUN --mount=type=cache,target=/root/.nuget/packages dotnet restore ... -a "$TARGETARCH"
COPY src/ src/
RUN dotnet publish ... --no-restore
```

`-a "$TARGETARCH"` makes a multi-arch build produce the right runtime identifier without a second
Dockerfile.

## Writable paths, and the trap that comes with them

The application writes to exactly two places. Everything else is read-only.

```
/data/storage    the filesystem storage adapter   → named volume
/tmp             StagedContent, while hashing     → tmpfs
```

`/tmp` is not optional. `StagedContent` buffers an upload to a temporary file while computing its
SHA-256, because content is addressed by its digest and an upload stream can only be read once. With
`read_only: true` and no tmpfs, every upload fails.

The trap is ownership. A chiseled image has no shell, so there is no `RUN mkdir && chown` available
in the final stage — and a named volume mounted onto a root-owned directory is unwritable by the
non-root user the image runs as. The directory is therefore created in the build stage and copied
with ownership:

```dockerfile
# build stage, which still has a shell
RUN mkdir -p /seed/storage

# final stage
COPY --from=build --chown=$APP_UID:$APP_UID /seed/storage /data/storage
```

Application files are deliberately *not* copied with that ownership. They are owned by root and read
by the runtime user, so code execution inside the container cannot rewrite the application.

## Health probes

There is no `HEALTHCHECK` instruction in either Dockerfile, and that is intentional rather than an
omission: a chiseled image has no curl, no wget and no shell to run one with.

| Environment | How |
|---|---|
| Kubernetes | `httpGet` probe on `/health/ready` — reaches the port, needs nothing in the image |
| Load balancer | target-group health check on the same path |
| compose, optional | `compose.probe.yaml` adds a curl sidecar |

Adding curl to the image to satisfy a `HEALTHCHECK` would put a network client and a shell back into
a distroless image — spending the entire benefit on a check the orchestrator already performs from
outside.

## Migrations as a separate container

```yaml
migrations:
  restart: "no"

api:
  depends_on:
    migrations:
      condition: service_completed_successfully
```

The schema runner exits; the API starts only if it exited cleanly. Migrating on application startup
races with itself during a rolling deploy: several replicas boot at once, each tries to take the
schema forward, and the loser either fails or applies a half-state.

In Kubernetes this is an init container or a Job with the same property.

## Hardening

```yaml
security_opt: [no-new-privileges:true]
cap_drop:     [ALL]
read_only:    true
tmpfs:        [/tmp:rw,noexec,nosuid,size=512m]
```

| Setting | What it closes |
|---|---|
| `no-new-privileges` | escapes that rely on a setuid binary |
| `cap_drop: ALL` | every capability; the app binds 8080, which needs none |
| `read_only` | persistence after code execution |
| `noexec` on `/tmp` | running an uploaded file from the one writable path |
| `size=512m` on `/tmp` | an upload filling the host disk |

The `noexec` flag deserves the emphasis: `/tmp` is where arbitrary user-supplied bytes land, and it
is the one place in the container that is writable.

## Resource limits

```yaml
deploy:
  resources:
    limits:
      memory: 512M
      cpus: "1.0"
```

.NET reads cgroup limits and sizes its heap from them, so declaring a limit is also how the garbage
collector learns how much memory it may use. A container with no limit gets a GC that assumes it owns
the host.

`DOTNET_gcServer=1` is set in the image. Revisit it below roughly one CPU: server GC allocates a heap
per core and costs more than it returns on a small container.

## Secrets

Connection strings come from the environment, and in production from the orchestrator's secret store.
Never from a build argument: a `--build-arg` lands in `docker history` and in an image layer, where it
survives every later rotation.

```bash
cp .env.example .env     # local only; .env is gitignored
```

## Running it

```bash
docker compose up --build                                  # PostgreSQL
docker compose --profile sqlserver up --build              # SQL Server
docker compose -f compose.yaml -f compose.probe.yaml up    # with the probe sidecar

# http://localhost:8080/scalar      API reference, version selector
# http://localhost:8080/openapi/v1.json
# http://localhost:8080/health/ready
```

`postgres` and `sqlserver` both declare a healthcheck, so `migrations` waits for a database that
accepts connections rather than for a container that is merely running.

## Production notes

- Pin base images by digest, and rebuild on base-image updates rather than on a schedule.
- Scan the images: Chiseled and Azure Linux both carry package metadata, so Trivy, Syft and Docker
  Scout all work on them.
- Run migrations as a Job or init container, never at application startup.
- `/data/storage` is a placeholder for local work. In production this is object storage behind a
  second `IObjectStorage`; the filesystem adapter stays useful as the fallback that needs nothing.
- The API image contains no shell. `docker exec` for debugging will not work — which is the point,
  and the reason logs and traces have to be good enough on their own.
