#!/usr/bin/env bash
# scripts/audit.sh — the credential sweep gates the push, so its exit status is what is under test.
#
# Every credential below is assembled at run time. Written out literally, it would sit in a tracked
# file of this repository and make the real sweep report the test that is meant to exercise it.

source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

# The build, package and format sections are reports, not gates, and need an SDK and a solution
# that a fixture does not have. A no-op dotnet keeps each case to the sweep under test.
mkdir -p "$WORK/bin"
printf '#!/bin/sh\nexit 0\n' > "$WORK/bin/dotnet"
chmod +x "$WORK/bin/dotnet"

audit() { (cd "$1" && PATH="$WORK/bin:$PATH" bash "$ROOT/scripts/audit.sh" 2>&1); }

v=Sup3rS3cretValue
q="'"

# leak <case> <file> <line> — the line alone in an otherwise clean repository must fail the sweep.
leak() {
  local repo out
  repo=$(fixture)
  put "$repo" "$2" "$3"
  commit_all "$repo"
  out=$(audit "$repo")
  expect_exit "$1" 1 $? "$out"
}

leak 'JSON setting, key quoted'           appsettings.json "{ \"Password\": \"$v\" }"
leak 'connection string, value unquoted'  appsettings.json "{ \"Db\": \"Host=db;Password=$v;\" }"
leak 'single-quoted value'                settings.py      "password = $q$v$q"
leak 'value starting with x or 7'         client.py        "api_key = \"x7$v\""
leak 'Markdown is swept too'              docs/setup.md    "password = \"$v\""
leak 'private key'                        deploy/id_rsa    "$(printf -- '-----BEGIN %s PRIVATE KEY-----' RSA)"

repo=$(fixture)
put "$repo" src/Handler.cs 'Task Handle(CancellationToken cancellationToken = default) { var password = request.Password; }'
put "$repo" appsettings.json '{ "Password": "changeme", "Db": "Host=db;Password=ged" }'
commit_all "$repo"
out=$(audit "$repo")
expect_exit 'code and known placeholders do not fail the sweep' 0 $? "$out"

finish
