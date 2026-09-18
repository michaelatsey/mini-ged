#!/usr/bin/env bash
# Read-only review sweep. Run it on a fresh clone, before anything else.
#
#   ./scripts/audit.sh
#
# Reports rather than fixes. Nothing here writes to the repository, so it is safe to run on a
# colleague's clone or in CI.
#
# Exits 1 when section 1 finds anything, and only then: a credential is the one finding that must
# stop a push to a public repository. The other sections are readings for a reviewer — an outdated
# package is no reason to hold a push — so they report and never fail the run.

set -uo pipefail
cd "$(git rev-parse --show-toplevel)"

section() { printf '\n=== %s\n' "$1"; }
status=0

section "1. Secrets — the repository is public, so history counts, not just HEAD"
# Assignments carrying a literal value, not every line that says "token". A scan that reports twenty
# harmless lines is a scan nobody reads twice, and the one real hit is then lost in the noise.
#
# Two shapes. A quoted value, after a key that may be quoted itself: password = 'x', "Password": "x".
# Or no quotes at all, the way a connection string carries it: Password=x; — the key must then meet
# its `=` with no space, which is what keeps `password = request.Password` out.
#
# The single quote is a variable because ERE has no escape for it. Inside a bracket expression \x27
# is the four characters \ x 2 7: it left ' out of the quotes and x, 2 and 7 out of the values.
q="'"
KEY='(password|pwd|passwd|api[_-]?key|secret|access[_-]?token|connection[_-]?string)[a-z_]*'
LEAK="$KEY[\"$q]?[[:space:]]*[=:][[:space:]]*[\"$q][^\"$q{\$<]{4,}|$KEY=[^\"$q;[:space:]{\$<]{4,}"
KNOWN='POSTGRES_PASSWORD|MSSQL_SA_PASSWORD|Password=ged|Ged!Passw0rd|your-|example|changeme|placeholder|<'

# Markdown is swept too: a README showing a real connection string publishes it as surely as
# appsettings.json does. git grep exits 1 when nothing matches and above 1 when it could not search,
# and the second must not read as the first — that is how a broken pattern would print "clean".
raw=$(git grep -iEn -e "$LEAK" -- ':!*.example'); rc=$?
if [ "$rc" -gt 1 ]; then
  printf 'ERROR — the credential sweep did not run (git grep exit %s)\n' "$rc"; status=1
else
  hits=$(grep -viE "$KNOWN" <<< "$raw" || true)
  if [ -n "$hits" ]; then printf 'REVIEW — literal credentials in tracked files:\n%s\n' "$hits"; status=1
  else printf 'HEAD   clean\n'; fi
fi

# History is deliberately NOT scanned here. `git log -G` over every commit is slow enough to make
# this script something nobody runs, and a dedicated tool does it better:
#
#   docker run --rm -v "$PWD:/r" zricethezav/gitleaks:latest detect --source=/r --no-banner
#
# Run it once on a repository that has just become public, and in CI thereafter.

# Each count fails the run when it is not zero.
count() { printf '%-19s: %s\n' "$1" "$2"; if [ "$2" -gt 0 ]; then status=1; fi; }
count 'private keys' "$(git grep -lE 'BEGIN [A-Z ]*PRIVATE KEY' 2>/dev/null | wc -l)"
count 'AWS-shaped keys' "$(git grep -lE 'AKIA[0-9A-Z]{16}' 2>/dev/null | wc -l)"
count 'tracked .env/.user' "$(git ls-files | grep -cE '(^|/)\.env$|\.user$|Zone\.Identifier$' || true)"

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

# Repeated last, because section 1 has scrolled out of sight by the time the build has run.
if [ "$status" -ne 0 ]; then printf '\nFAILED — section 1 found something that must not be pushed.\n'; fi
exit "$status"
