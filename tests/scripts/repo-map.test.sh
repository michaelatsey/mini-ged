#!/usr/bin/env bash
# scripts/repo-map.sh — the map describes the tree it is regenerated from, says whether that tree was
# committed, and never replaces REPO-MAP.md with a partial one.

source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

# One interface, because the Ports section's `xargs grep -l` fails the whole run when no file matches.
tree() {
  put "$1" src/Core/Core.csproj '<Project />'
  put "$1" src/Core/IClock.cs 'public interface IClock { }'
  put "$1" README.md '# Readme'
}

map() { (cd "$1" && bash "$ROOT/scripts/repo-map.sh" 2>&1); }

repo=$(fixture)
tree "$repo"
commit_all "$repo"
put "$repo" docs/new.md '# Not yet added'
out=$(map "$repo")
expect_exit 'regenerates REPO-MAP.md in place' 0 $? "$out"
content=$(cat "$repo/REPO-MAP.md" 2>/dev/null)
expect_contains 'lists a file not yet added to the index' 'docs/new.md' "$content"
expect_contains 'says the work was not committed' 'plus uncommitted changes' "$content"

# Committed now, the map included. A previous map left modified is not a change to what it maps.
commit_all "$repo"
map "$repo" > /dev/null
expect_absent 'a committed tree is stamped with its commit alone' 'uncommitted' "$(cat "$repo/REPO-MAP.md" 2>/dev/null)"

# No commit, so HEAD does not resolve and the run fails part-way.
repo=$(mktemp -d "$WORK/repo.XXXXXX")
git -C "$repo" init -q -b main
put "$repo" REPO-MAP.md 'previous map'
out=$(map "$repo")
if [ $? -ne 0 ]; then ok 'a failed run exits non-zero'; else fail 'a failed run exits non-zero'; fi
expect_contains 'a failed run leaves the previous map in place' 'previous map' "$(cat "$repo/REPO-MAP.md")"
expect_absent 'a failed run leaves no temporary file' 'repo-map.' "$(ls -a "$repo" "$repo/.git")"

finish
