#!/usr/bin/env bash
# Runs the tests of the scripts under scripts/. Each case builds a throwaway repository, so they need
# bash and git and nothing else — no SDK, no Docker.
#
#   ./tests/scripts/run.sh

set -uo pipefail

status=0
for t in "$(dirname "${BASH_SOURCE[0]}")"/*.test.sh; do
  printf '\n=== %s\n' "$(basename "$t" .test.sh)"
  bash "$t" || status=1
done
exit "$status"
