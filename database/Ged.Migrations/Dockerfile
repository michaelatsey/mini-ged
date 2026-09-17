# syntax=docker/dockerfile:1.10

# Build context is the REPOSITORY ROOT:
#
#   docker build -f database/Ged.Migrations/Dockerfile -t mini-ged-migrations .
#
# The schema runner ships as its own image and runs to completion before the API starts. Migrating at
# application startup races with itself during a rolling deploy: several replicas boot at once, each
# tries to take the schema forward, and the loser either fails or applies a half-state.

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
WORKDIR /src

COPY ["global.json", "NuGet.Config", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["database/Ged.Migrations/Ged.Migrations.csproj", "database/Ged.Migrations/"]

# Ged.Migrations references nothing: it opens a connection, runs embedded SQL and exits. If that ever
# changes, the referenced project files must be copied above or the restore fails here.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet restore "database/Ged.Migrations/Ged.Migrations.csproj" -a "$TARGETARCH"

COPY ["database/", "database/"]

# Published directly. Visual Studio emits a separate `dotnet build` stage because its fast mode reuses
# the build output while debugging; for an image that ships, that stage compiles everything twice.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "database/Ged.Migrations/Ged.Migrations.csproj" \
        -c "$BUILD_CONFIGURATION" \
        -a "$TARGETARCH" \
        --no-restore \
        -p:UseAppHost=false \
        -o /app

# No ASP.NET Core: this process needs no web server, so the runtime image carries none for an
# attacker to find.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled-extra AS final

WORKDIR /app
COPY --from=build /app .

USER $APP_UID

ENTRYPOINT ["dotnet", "Ged.Migrations.dll"]
CMD ["--provider", "postgres"]
