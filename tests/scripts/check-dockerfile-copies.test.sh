#!/usr/bin/env bash
# scripts/check-dockerfile-copies.sh — both halves of the Dockerfile: the project files the restore
# layer copies, and the source directories the publish layer copies.

source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

# hosts/App -> src/Core -> libraries/Kit: Ged.Api's graph in three projects, one reference written
# with Windows separators as Visual Studio writes them.
graph() {
  put "$1" hosts/App/App.csproj '<Project><ItemGroup><ProjectReference Include="..\..\src\Core\Core.csproj" /></ItemGroup></Project>'
  put "$1" src/Core/Core.csproj '<Project><ItemGroup><ProjectReference Include="../../libraries/Kit/src/Kit/Kit.csproj" /></ItemGroup></Project>'
  put "$1" libraries/Kit/src/Kit/Kit.csproj '<Project />'
}

KIT_SOURCES='COPY ["libraries/Kit/src/Kit/", "libraries/Kit/src/Kit/"]'
DOCKERFILE="FROM sdk AS build
COPY [\"hosts/App/App.csproj\", \"hosts/App/\"]
COPY [\"src/Core/Core.csproj\", \"src/Core/\"]
COPY [\"libraries/Kit/src/Kit/Kit.csproj\", \"libraries/Kit/src/Kit/\"]
RUN dotnet restore \"hosts/App/App.csproj\"
$KIT_SOURCES
COPY [\"src/\", \"src/\"]
COPY [\"hosts/\", \"hosts/\"]
RUN dotnet publish \"hosts/App/App.csproj\" -c Release --no-restore -o /app
FROM runtime AS final
COPY --from=build /app ."

# check <case> <expected status> <Dockerfile> — leaves the script's output in $out.
check() {
  local repo
  repo=$(fixture)
  graph "$repo"
  put "$repo" hosts/App/Dockerfile "$3"
  out=$(cd "$repo" && bash "$ROOT/scripts/check-dockerfile-copies.sh" hosts/App/Dockerfile 2>&1)
  expect_exit "$1" "$2" $? "$out"
}

check 'complete Dockerfile' 0 "$DOCKERFILE"

check 'library project file not copied' 1 "$(grep -vF 'COPY ["libraries/Kit/src/Kit/Kit.csproj"' <<< "$DOCKERFILE")"

check 'library sources not copied' 1 "$(grep -vxF "$KIT_SOURCES" <<< "$DOCKERFILE")"
expect_contains 'prints the missing source COPY' "$KIT_SOURCES" "$out"

check 'shell-form source COPY' 0 "${DOCKERFILE/"$KIT_SOURCES"/COPY libraries/Kit/src/Kit/ libraries/Kit/src/Kit/}"

finish
