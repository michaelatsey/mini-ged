# syntax=docker/dockerfile:1.10

# Build context is the REPOSITORY ROOT, not this folder:
#
#   docker build -f hosts/Ged.Api/Dockerfile -t mini-ged-api .
#
# MicroKit is vendored as source under libraries/, so the build needs three top-level folders. A
# context narrower than the repository root fails on the first ProjectReference.

# The SDK tag must satisfy global.json (10.0.111, rollForward latestFeature). The floating 10.0 tag
# resolves to the newest patch and does, but a digest pinned to an older SDK would not: rollForward
# moves up, never down. Pin the digest in CI and bump it deliberately.
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
WORKDIR /src

# ─── layer 1: what governs the restore ───
# NuGet.Config matters more than it looks: it starts with <clear />, so it is what stops a
# machine-level feed from leaking into the restore. Omitting it makes the container build resolve
# from different sources than the workstation — the definition of an unreproducible build.
COPY ["global.json", "NuGet.Config", "Directory.Build.props", "Directory.Packages.props", "./"]

# ─── layer 2: the 12 projects in Ged.Api's transitive closure ───
# Derived from the actual ProjectReference graph, not guessed. Ged.Migrations, the tests, the unused
# MicroKit.Persistence.EntityFrameworkCore* and MicroKit.Result* projects, and the empty Ged.Adapters
# are all absent: this build never reads them, so copying them would only add cache-invalidation
# points for files that cannot change its output.
COPY ["hosts/Ged.Api/Ged.Api.csproj", "hosts/Ged.Api/"]

COPY ["src/Ged.Domain/Ged.Domain.csproj", "src/Ged.Domain/"]
COPY ["src/Ged.Core/Ged.Core.csproj", "src/Ged.Core/"]
COPY ["src/Ged.Features/Ged.Features.csproj", "src/Ged.Features/"]
COPY ["src/Ged.Adapters.Storage/Ged.Adapters.Storage.csproj", "src/Ged.Adapters.Storage/"]
COPY ["src/Ged.Adapters.Storage.FileSystem/Ged.Adapters.Storage.FileSystem.csproj", "src/Ged.Adapters.Storage.FileSystem/"]
COPY ["src/Ged.Adapters.Persistence/Ged.Adapters.Persistence.csproj", "src/Ged.Adapters.Persistence/"]
COPY ["src/Ged.Adapters.Persistence.PostgreSql/Ged.Adapters.Persistence.PostgreSql.csproj", "src/Ged.Adapters.Persistence.PostgreSql/"]
COPY ["src/Ged.Adapters.Persistence.SqlServer/Ged.Adapters.Persistence.SqlServer.csproj", "src/Ged.Adapters.Persistence.SqlServer/"]
COPY ["src/Ged.Adapters.FileTypes/Ged.Adapters.FileTypes.csproj", "src/Ged.Adapters.FileTypes/"]
COPY ["src/Ged.Adapters.FileTypes.FileSignatures/Ged.Adapters.FileTypes.FileSignatures.csproj", "src/Ged.Adapters.FileTypes.FileSignatures/"]

COPY ["libraries/MicroKit.Domain/MicroKit.Domain.csproj", "libraries/MicroKit.Domain/"]
COPY ["libraries/MicroKit.Core/src/MicroKit.Core/MicroKit.Core.csproj", "libraries/MicroKit.Core/src/MicroKit.Core/"]
COPY ["libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/MicroKit.Persistence.Abstractions.csproj", "libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/"]

RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "hosts/Ged.Api/Ged.Api.csproj" -a "$TARGETARCH"

# ─── layer 3: the source of those projects only ───
COPY ["libraries/MicroKit.Domain/", "libraries/MicroKit.Domain/"]
COPY ["libraries/MicroKit.Core/src/MicroKit.Core/", "libraries/MicroKit.Core/src/MicroKit.Core/"]
COPY ["libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/", "libraries/MicroKit.Persistence/src/MicroKit.Persistence.Abstractions/"]
COPY ["src/", "src/"]
COPY ["hosts/", "hosts/"]

# Release, so Directory.Build.props turns warnings into errors. The container build is therefore
# stricter than a local Debug build — which is the right way round.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "hosts/Ged.Api/Ged.Api.csproj" \
        -c "$BUILD_CONFIGURATION" \
        -a "$TARGETARCH" \
        --no-restore \
        -p:UseAppHost=false \
        -o /app

# Created in a stage that still has a shell, and copied with ownership below. A chiseled image has no
# shell for `RUN mkdir && chown`, and a named volume mounted onto a root-owned directory is
# unwritable by the non-root user the image runs as.
RUN mkdir -p /seed/storage

# ─────────────────────────── runtime ───────────────────────────
# Chiseled: no shell, no package manager, no libc utilities. The -extra variant carries ICU and
# tzdata — worth its ~10 MB, since the alternative silently changes string comparison and makes a
# driver failure read as a data bug.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final

WORKDIR /app

# Application files stay owned by root: the runtime user reads and executes them but cannot modify
# them, so code execution inside the container cannot rewrite the application.
COPY --from=build /app .

# The only writable path, owned by the runtime user.
COPY --from=build --chown=$APP_UID:$APP_UID /seed/storage /data/storage

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1 \
    Ged__Storage__RootPath=/data/storage

EXPOSE 8080
USER $APP_UID

# No HEALTHCHECK: a chiseled image has no curl, no wget and no shell to run one with. Probing belongs
# to the orchestrator, which reaches /health/ready over HTTP from outside the container.

ENTRYPOINT ["dotnet", "Ged.Api.dll"]
