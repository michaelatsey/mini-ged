#!/usr/bin/env bash
# Read-only review sweep. Run it on a fresh clone, before anything else.
#
#   ./scripts/audit.sh
#
# Reports rather than fixes. Nothing here writes to the repository, so it is safe to run on a
# colleague's clone or in CI.

set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

section() { printf '\n=== %s\n' "$1"; }

section "1. Secrets — the repository is public, so history counts, not just HEAD"
# Assignments carrying a literal value, not every line that says "token". A scan that reports twenty
# harmless lines is a scan nobody reads twice, and the one real hit is then lost in the noise.
# CancellationToken and ConcurrencyToken are excluded by requiring a separator and a quoted value.
LEAK='(password|pwd|passwd|api[_-]?key|secret|access[_-]?token|connection[_-]?string)[a-z_]*[[:space:]]*[=:][[:space:]]*["\x27][^"\x27{$<]{4,}'
KNOWN='POSTGRES_PASSWORD|MSSQL_SA_PASSWORD|Password=ged|Ged!Passw0rd|your-|example|changeme|placeholder|<'

hits=$(git grep -iEn "$LEAK" -- ':!*.md' ':!*.example' 2>/dev/null | grep -viE "$KNOWN" || true)
if [ -n "$hits" ]; then printf 'REVIEW — literal credentials in tracked files:\n%s\n' "$hits"; else printf 'HEAD   clean\n'; fi

# History is deliberately NOT scanned here. `git log -G` over every commit is slow enough to make
# this script something nobody runs, and a dedicated tool does it better:
#
#   docker run --rm -v "$PWD:/r" zricethezav/gitleaks:latest detect --source=/r --no-banner
#
# Run it once on a repository that has just become public, and in CI thereafter.

printf 'private keys       : '; git grep -lE 'BEGIN [A-Z ]*PRIVATE KEY' 2>/dev/null | wc -l
printf 'AWS-shaped keys    : '; git grep -lE 'AKIA[0-9A-Z]{16}' 2>/dev/null | wc -l
printf 'tracked .env/.user : '; git ls-files | grep -cE '(^|/)\.env$|\.user$|Zone\.Identifier$' || true

section "2. Files that should never have been committed"
git ls-files | grep -E '(^|/)(bin|obj)/' | head -5 || echo "none"
git ls-files | grep -E '\.(suo|user)$' | head -5 || echo "none"

section "3. Build — the real test of a fresh clone"
dotnet build -c Release --nologo 2>&1 | grep -E 'Warning\(s\)|Error\(s\)|error ' | tail -5

section "4. Vulnerable and outdated packages"
dotnet list package --vulnerable --include-transitive 2>/dev/null | grep -E '>|has no vulnerable' | head -20
dotnet list package --outdated 2>/dev/null | grep '>' | head -20

section "5. Licences — a transitive copyleft is still a copyleft"
dotnet list package --include-transitive 2>/dev/null | grep '>' | awk '{print $2}' | sort -u | head -40

section "6. Format"
dotnet format --verify-no-changes --verbosity quiet 2>&1 | tail -5 || printf 'dotnet format reported differences\n'

section "7. Repository hygiene"
printf 'tracked files      : %s\n' "$(git ls-files | wc -l)"
printf 'commits            : %s\n' "$(git rev-list --count HEAD)"
printf 'largest tracked    :\n'
git ls-files -z | xargs -0 -r du -h 2>/dev/null | sort -rh | head -5
printf 'author(s)          : %s\n' "$(git log --format='%an <%ae>' | sort -u | tr '\n' ' ')"
