#!/usr/bin/env bash
# Verifies that a Dockerfile copies every project its publish target needs — the project file for
# the restore, and the directory holding its sources for the publish.
#
#   ./scripts/check-dockerfile-copies.sh hosts/Ged.Api/Dockerfile
#
# A multi-stage .NET Dockerfile lists its project files by hand so the restore layer survives a
# source-only change. That list is a second copy of the reference graph, and a second copy drifts:
# adding a ProjectReference builds locally and fails in the image with NETSDK1004, several layers
# and thirty seconds later.
#
# The source layer is a third copy wherever it names directories one by one, as it does for
# libraries/. A project whose file is copied but whose sources are not restores cleanly, compiles to
# an empty assembly, and fails its first consumer with CS0246.
#
# Read-only. Exits non-zero when either list is stale, so it belongs in CI.

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

# Every source of every COPY from the build context, one per line, in both forms:
# COPY ["a", "b", "dest/"] and COPY a b dest/. The last argument is the destination, and a
# COPY --from reads another stage, not the context.
sources() {
  awk '
    /^[[:space:]]*COPY[[:space:]]/ {
      line = $0
      sub(/^[[:space:]]*COPY[[:space:]]+/, "", line); sub(/[[:space:]]+$/, "", line)
      while (line ~ /^--/) {
        if (line ~ /^--from=/) next
        sub(/^--[^[:space:]]+[[:space:]]+/, "", line)
      }
      if (line ~ /^\[/) {
        gsub(/^\[[[:space:]]*"|"[[:space:]]*\]$/, "", line)
        n = split(line, arg, /"[[:space:]]*,[[:space:]]*"/)
      } else {
        n = split(line, arg, /[[:space:]]+/)
      }
      for (i = 1; i < n; i++) print arg[i]
    }' "$1"
}

needed=$(closure "$target" | sort -u)
copied=$(sources "$dockerfile" | grep -E '\.csproj$' | sort -u || true)

# The directories the source layer copies, normalised like the closure: no ./ prefix, no trailing
# slash, and the context root as `.`. A path that is not a directory here is a file, not a source tree.
source_dirs=$(sources "$dockerfile" | sed -E 's|^\./||; s|/+$||; s|^$|.|' | sort -u \
              | while IFS= read -r s; do if [ -d "$s" ]; then printf '%s\n' "$s"; fi; done)

# A project's sources are copied when its directory is, or sits under, a copied directory.
covered() {
  local s
  while IFS= read -r s; do
    [ -z "$s" ] && continue
    if [ "$s" = . ]; then return 0; fi
    case "$1/" in "$s"/*) return 0;; esac
  done <<< "$source_dirs"
  return 1
}

missing=$(comm -23 <(printf '%s\n' "$needed") <(printf '%s\n' "$copied"))
extra=$(comm -13 <(printf '%s\n' "$needed") <(printf '%s\n' "$copied"))
missing_sources=$(printf '%s\n' "$needed" | while IFS= read -r p; do
  if ! covered "$(dirname "$p")"; then printf '%s\n' "$(dirname "$p")"; fi
done)

printf 'target   : %s\n' "$target"
printf 'needed   : %s\n' "$(printf '%s\n' "$needed" | wc -l)"
printf 'copied   : %s\n' "$(printf '%s\n' "$copied" | wc -l)"
printf 'sources  : %s\n' "$(printf '%s\n' "$source_dirs" | grep -c . || true)"

status=0
if [ -n "$missing" ]; then
  printf '\nMISSING — restore will skip these, publish fails with NETSDK1004:\n'
  printf '%s\n' "$missing" | while read -r m; do
    printf 'COPY ["%s", "%s/"]\n' "$m" "$(dirname "$m")"
  done
  status=1
fi
if [ -n "$missing_sources" ]; then
  printf '\nMISSING SOURCES — restore succeeds, publish fails with CS0246 inside the image:\n'
  printf '%s\n' "$missing_sources" | while read -r d; do
    printf 'COPY ["%s/", "%s/"]\n' "$d" "$d"
  done
  status=1
fi
if [ -n "$extra" ]; then
  printf '\nSUPERFLUOUS — copied but not in the graph, each one a needless cache invalidation:\n%s\n' "$extra"
fi
[ "$status" -eq 0 ] && printf '\nup to date\n'
exit "$status"
