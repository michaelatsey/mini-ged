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

# A placeholder word elsewhere on the line must not clear the credential beside it.
leak 'XML attribute'                      web.config       "<add name=\"Db\" connectionString=\"Server=x;Password=$v\" />"
leak 'hostname containing your-'          appsettings.json "{ \"Db\": \"Host=your-db.postgres.database.azure.com;Password=$v\" }"
leak 'hostname containing example'        appsettings.json "{ \"Db\": \"Host=db.example.com;Password=$v\" }"
leak 'hostname word, spaced separator'    appsettings.json "{ \"ConnectionString\": \"Host=your-db;Password = $v\" }"
leak 'compose variable, list form'        compose.yaml     "      - POSTGRES_PASSWORD=$v"
leak 'compose variable, map form'         compose.yaml     "      MSSQL_SA_PASSWORD: $v"
leak 'YAML value, unquoted'               config.yml       "password: $v"
leak 'YAML value holding a dollar'        config.yml       "password: $v\$\$1"

# git grep reports a binary file on a line of its own unless told to read it as text.
repo=$(fixture)
printf 'x\0password = "%s"\n' "$v" > "$repo/data.bin"
commit_all "$repo"
out=$(audit "$repo")
expect_exit 'binary file' 1 $? "$out"

repo=$(fixture)
put "$repo" src/Client.cs 'var session = Connect(password: dto.Password, secret: options.Secret);'
put "$repo" compose.yaml '      ConnectionStrings__Ged: Host=db;Password=${DB_PASSWORD:-ged}'
commit_all "$repo"
out=$(audit "$repo")
expect_exit 'C# named arguments and an interpolated YAML connection string do not fail the sweep' 0 $? "$out"

repo=$(fixture)
put "$repo" src/Handler.cs 'Task Handle(CancellationToken cancellationToken = default) { var password = request.Password; }'
put "$repo" appsettings.json '{ "Password": "changeme", "Db": "Host=db;Password=ged" }'
commit_all "$repo"
out=$(audit "$repo")
expect_exit 'code and known placeholders do not fail the sweep' 0 $? "$out"

finish
