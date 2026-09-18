#!/usr/bin/env bash
# Shared by the script tests. Sourced, never run.
#
# Each case builds a throwaway repository and runs the real script from scripts/ inside it. The
# scripts resolve the repository from the working directory, not from their own location, so a case
# never touches this repository's tree.

set -uo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
failures=0

# A repository with one commit, so that HEAD exists. The -c flags keep a developer's global
# configuration — signing, hooks — out of a commit that only exists for the duration of a case.
fixture() {
  local dir
  dir=$(mktemp -d "$WORK/repo.XXXXXX")
  git -C "$dir" init -q -b main
  commit_all "$dir"
  printf '%s\n' "$dir"
}

commit_all() {
  git -C "$1" add -A
  git -C "$1" -c user.name=test -c user.email=test@test.invalid -c commit.gpgsign=false \
    -c core.hooksPath=/dev/null commit -q --allow-empty -m fixture
}

# Writes a file inside a fixture, creating its directory.
put() {
  mkdir -p "$(dirname "$1/$2")"
  printf '%s\n' "$3" > "$1/$2"
}

ok()   { printf 'ok    %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1"; failures=$((failures + 1)); }

# expect_exit <case> <expected status> <actual status> [output shown on failure]
expect_exit() {
  if [ "$3" -eq "$2" ]; then ok "$1"; else fail "$1 — exit $3, expected $2"; printf '%s\n' "${4-}" | sed 's/^/      | /'; fi
}

# expect_contains <case> <needle> <haystack>
expect_contains() {
  if grep -qF -- "$2" <<< "$3"; then ok "$1"; else fail "$1 — missing: $2"; printf '%s\n' "$3" | sed 's/^/      | /'; fi
}

# expect_absent <case> <needle> <haystack>
expect_absent() {
  if grep -qF -- "$2" <<< "$3"; then fail "$1 — unexpected: $2"; printf '%s\n' "$3" | sed 's/^/      | /'; else ok "$1"; fi
}

finish() {
  if [ "$failures" -gt 0 ]; then printf '\n%s failed\n' "$failures"; exit 1; fi
}
