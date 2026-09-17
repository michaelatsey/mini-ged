#!/usr/bin/env bash
# Produces REPO-MAP.md — a single file describing the whole solution.
#
#   ./scripts/repo-map.sh > REPO-MAP.md
#
# Written for two readers. A new contributor gets the shape of the solution in one page instead of
# opening thirty files. An AI session gets the same, at a fraction of the context that uploading
# source would cost — and unlike source, it stays true for longer than one commit.
#
# Read-only. Derived from git and from the project files, so it cannot drift from the repository
# the way a hand-written map does.

set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

printf '# REPO MAP — %s\n\n' "$(basename "$(pwd)")"
printf 'Generated %s from commit `%s`.\n' "$(date -u +%Y-%m-%d)" "$(git rev-parse --short HEAD)"
printf 'Regenerate with `./scripts/repo-map.sh > REPO-MAP.md` — never edit by hand.\n\n'

printf -- '---\n\n## Projects\n\n```\n'
git ls-files '*.csproj' | sort | while read -r p; do
  name="$(basename "$p" .csproj)"
  dir="$(dirname "$p")"
  files=$(git ls-files "$dir/*.cs" | wc -l)
  lines=$(git ls-files "$dir/*.cs" | xargs -r wc -l 2>/dev/null | tail -1 | awk '{print $1}')
  printf '%-44s %3s files  %6s lines\n' "$name" "$files" "${lines:-0}"
done
printf '```\n\n'

printf '## Reference graph\n\n```\n'
git ls-files '*.csproj' | sort | while read -r p; do
  name="$(basename "$p" .csproj)"
  # `|| true` on every extraction: a project with no ProjectReference makes grep exit 1, and
  # under `set -o pipefail` that failure ends the script rather than yielding an empty string.
  refs=$(grep -o 'ProjectReference Include="[^"]*"' "$p" 2>/dev/null \
         | sed 's/.*[\/\\]\([^\/\\]*\)\.csproj"/\1/' | sort | tr '\n' ' ' || true)
  pkgs=$(grep -c 'PackageReference' "$p" 2>/dev/null || true)
  printf '%s\n' "$name"

  # if/fi rather than `[ ... ] && ...`: a test that evaluates false is the loop body's last
  # command, and under `set -e` its non-zero status ends the whole script silently.
  if [ -n "${refs// }" ]; then printf '    -> %s\n' "$refs"; fi
  if [ "${pkgs:-0}" -gt 0 ]; then printf '    packages: %s\n' "$pkgs"; fi
done
printf '```\n\n'

printf '## Layout\n\n```\n'
git ls-files | awk -F/ 'NF>1 {print $1"/"$2}' | sort -u | head -40
printf '```\n\n'

printf '## Domain surface\n\n'
printf 'Aggregates, entities and the rules they enforce.\n\n```\n'
git ls-files 'src/*Domain*/*.cs' 'src/Ged.Domain/**/*.cs' 2>/dev/null | sort -u | while read -r f; do
  if ! grep -q 'AggregateRoot<\|: Entity<\|: BusinessRule' "$f" 2>/dev/null; then continue; fi
  printf '%s\n' "${f#src/}"
  grep -oE 'public (static )?[A-Za-z<>?,\[\] ]+ [A-Z][A-Za-z]*\(' "$f" 2>/dev/null \
    | sed 's/(//' | awk '{print "    " $NF}' | sort -u | head -12 || true
done
printf '```\n\n'

printf '## Ports\n\n```\n'
git ls-files '*.cs' | xargs -r grep -l '^public interface I' 2>/dev/null | sort | while read -r f; do
  iface=$(grep -oE '^public interface I[A-Za-z]+' "$f" | head -1 | awk '{print $3}' || true)
  if [ -n "$iface" ]; then printf '%-32s %s\n' "$iface" "${f#src/}"; fi
done
printf '```\n\n'

printf '## HTTP endpoints\n\n```\n'
git ls-files '*Endpoint*.cs' | sort | while read -r f; do
  # Braces around the pipeline: `a | b || true | c` binds `|| true` to `b` alone and breaks the
  # pipe into `c`, which is how the route and the file name went missing from the output.
  { grep -oE 'Map(Get|Post|Put|Patch|Delete)\("[^"]*"' "$f" 2>/dev/null \
      | sed -E 's/Map([A-Za-z]+)\("(.*)"/\1 \2/' || true; } | while read -r m r; do
      printf '%-7s %-44s %s\n' "$m" "$r" "$(basename "$f" .cs)"
    done
done
printf '```\n\n'

printf '## Database scripts\n\n```\n'
git ls-files 'database/**/*.sql' | sort | sed 's|database/[^/]*/Scripts/||'
printf '```\n\n'

printf '## Documentation\n\n```\n'
git ls-files '*.md' | sort | while read -r f; do
  printf '%-40s %s\n' "$f" "$(head -1 "$f" | sed 's/^# //' | cut -c1-60)"
done
printf '```\n\n'

printf '## Recent history\n\n```\n'
git log --oneline -12
printf '```\n'
