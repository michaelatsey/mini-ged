#!/usr/bin/env bash
# Verifies that a Dockerfile copies every project file its publish target needs.
#
#   ./scripts/check-dockerfile-copies.sh src/Ged.Api/Dockerfile
#
# A multi-stage .NET Dockerfile lists its project files by hand so the restore layer survives a
# source-only change. That list is a second copy of the reference graph, and a second copy drifts:
# adding a ProjectReference builds locally and fails in the image with NETSDK1004, several layers
# and thirty seconds later.
#
# Read-only. Exits non-zero when the list is stale, so it belongs in CI.

set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

dockerfile="${1:?usage: check-dockerfile-copies.sh <path/to/Dockerfile>}"

# The project the image publishes, taken from the Dockerfile itself rather than guessed.
# Quoted or bare — Visual Studio writes one form, a hand-written Dockerfile often the other.
target=$(grep -oE 'dotnet publish "?[^" ]+\.csproj"?' "$dockerfile" | head -1 \
         | sed -E 's/dotnet publish "?([^" ]+)"?/\1/')
if [ -z "$target" ]; then printf 'no publish target found in %s\n' "$dockerfile"; exit 2; fi

# Transitive closure of ProjectReference, normalised to repository-relative forward-slash paths.
closure() {
  local queue=("$1") seen=() current dir ref resolved
  while [ ${#queue[@]} -gt 0 ]; do
    current="${queue[0]}"; queue=("${queue[@]:1}")
    case " ${seen[*]-} " in *" $current "*) continue;; esac
    seen+=("$current")
    dir=$(dirname "$current")
    while IFS= read -r ref; do
      [ -z "$ref" ] && continue
      resolved=$(cd "$dir" && realpath -m --relative-to="$(git rev-parse --show-toplevel)" "${ref//\\//}")
      queue+=("$resolved")
    done < <(grep -oE 'ProjectReference Include="[^"]+"' "$current" 2>/dev/null \
             | sed 's/.*Include="\(.*\)"/\1/' || true)
  done
  printf '%s\n' "${seen[@]}"
}

needed=$(closure "$target" | sort -u)
# Both COPY forms too: COPY ["a.csproj", "a/"] and COPY a.csproj a/
copied=$(grep -oE 'COPY \[?"?[^" ]*\.csproj' "$dockerfile" | sed -E 's/COPY \[?"?//' | sort -u)

missing=$(comm -23 <(printf '%s\n' "$needed") <(printf '%s\n' "$copied"))
extra=$(comm -13 <(printf '%s\n' "$needed") <(printf '%s\n' "$copied"))

printf 'target   : %s\n' "$target"
printf 'needed   : %s\n' "$(printf '%s\n' "$needed" | wc -l)"
printf 'copied   : %s\n' "$(printf '%s\n' "$copied" | wc -l)"

status=0
if [ -n "$missing" ]; then
  printf '\nMISSING — restore will skip these, publish fails with NETSDK1004:\n'
  printf '%s\n' "$missing" | while read -r m; do
    printf 'COPY ["%s", "%s/"]\n' "$m" "$(dirname "$m")"
  done
  status=1
fi
if [ -n "$extra" ]; then
  printf '\nSUPERFLUOUS — copied but not in the graph, each one a needless cache invalidation:\n%s\n' "$extra"
fi
[ "$status" -eq 0 ] && printf '\nup to date\n'
exit "$status"
