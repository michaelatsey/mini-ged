#!/usr/bin/env bash
# End-to-end smoke test. Everything the .http file does, runnable in one go and assertable.
#
#   ./smoke.sh [host]
#
# Exits non-zero on the first unexpected status, so it can be dropped into CI unchanged.

set -euo pipefail

HOST="${1:-http://localhost:8080}"
V="v1"
ROOT="00000000-0000-7000-8000-000000000001"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

pass=0; fail=0

expect() {                      # expect <label> <expected-status> <curl args...>
  local label="$1" want="$2"; shift 2
  local got; got="$(curl -s -o "$TMP/body" -w '%{http_code}' "$@")"
  if [ "$got" = "$want" ]; then
    printf '  PASS  %-52s %s\n' "$label" "$got"; pass=$((pass + 1))
  else
    printf '  FAIL  %-52s want %s got %s\n' "$label" "$want" "$got"
    head -c 300 "$TMP/body"; echo; fail=$((fail + 1))
  fi
}

printf '%%PDF-1.7\ncontent v1\n' > "$TMP/sample.pdf"
printf '%%PDF-1.7\ncontent v2\n' > "$TMP/sample-v2.pdf"
printf 'MZ\x00\x00'              > "$TMP/forged.pdf"
printf 'name;value\nAtse;1\n'    > "$TMP/sample.csv"
printf 'nothing'                 > "$TMP/sample.exe"
: > "$TMP/empty.pdf"

echo "Health and versioning"
expect "health/live"                        200 "$HOST/health/live"
expect "health/ready"                       200 "$HOST/health/ready"
expect "openapi v1"                         200 "$HOST/openapi/$V.json"
expect "no version in the URL is refused"   404 "$HOST/api/folders"
expect "unknown version is refused"         400 "$HOST/api/v2/folders"

echo "Headers"
supported="$(curl -s -D - -o /dev/null "$HOST/api/$V/folders" | grep -i 'api-supported-versions' || true)"
[ -n "$supported" ] \
  && { printf '  PASS  %-52s %s\n' "api-supported-versions advertised" "$(echo "$supported" | tr -d '\r')"; pass=$((pass + 1)); } \
  || { printf '  FAIL  %-52s header absent\n' "api-supported-versions advertised"; fail=$((fail + 1)); }

csp="$(curl -s -D - -o /dev/null "$HOST/api/$V/folders" | grep -i 'content-security-policy' || true)"
case "$csp" in
  *"default-src 'none'"*) printf '  PASS  %-52s\n' "API gets the strict CSP"; pass=$((pass + 1)) ;;
  *) printf '  FAIL  %-52s %s\n' "API gets the strict CSP" "$csp"; fail=$((fail + 1)) ;;
esac

echo "Folders"
folder="$(curl -s -X POST "$HOST/api/$V/folders" -H 'Content-Type: application/json' \
  -d "{\"parentId\":\"$ROOT\",\"name\":\"smoke-$(date +%s)\",\"folderType\":\"CASE\"}" | tr -d '"')"
[ -n "$folder" ] \
  && { printf '  PASS  %-52s %s\n' "folder created" "$folder"; pass=$((pass + 1)); } \
  || { printf '  FAIL  %-52s\n' "folder created"; fail=$((fail + 1)); exit 1; }

expect "folder read back"                   200 "$HOST/api/$V/folders/$folder"
expect "a folder cannot be its own parent"  409 -X PATCH "$HOST/api/$V/folders/$folder/parent" \
  -H 'Content-Type: application/json' -d "{\"parentId\":\"$folder\"}"
expect "a name with a separator is refused" 400 -X POST "$HOST/api/$V/folders" \
  -H 'Content-Type: application/json' -d "{\"parentId\":\"$ROOT\",\"name\":\"a/b\",\"folderType\":\"CASE\"}"

echo "Upload policy"
expect "extension outside the allowlist"    415 -F "file=@$TMP/sample.exe" "$HOST/api/$V/documents?folderId=$folder"
expect "double extension"                   415 -F "file=@$TMP/sample.exe;filename=invoice.pdf.exe" "$HOST/api/$V/documents?folderId=$folder"
expect "content contradicts the extension"  415 -F "file=@$TMP/forged.pdf;type=application/pdf" "$HOST/api/$V/documents?folderId=$folder"
expect "empty file"                         415 -F "file=@$TMP/empty.pdf" "$HOST/api/$V/documents?folderId=$folder"
expect "docType narrows the allowlist"      415 -F "file=@$TMP/sample.csv" "$HOST/api/$V/documents?folderId=$folder&docType=CONTRACT"

echo "Documents"
upload="$(curl -s -F "file=@$TMP/sample.pdf" "$HOST/api/$V/documents?folderId=$folder&docType=CONTRACT")"
doc="$(echo "$upload" | sed -n 's/.*"documentId":"\([^"]*\)".*/\1/p')"
blob="$(echo "$upload" | sed -n 's/.*"blobId":"\([^"]*\)".*/\1/p')"
[ -n "$doc" ] \
  && { printf '  PASS  %-52s %s\n' "document uploaded" "$doc"; pass=$((pass + 1)); } \
  || { printf '  FAIL  %-52s %s\n' "document uploaded" "$upload"; fail=$((fail + 1)); exit 1; }

expect "document read back"                 200 "$HOST/api/$V/documents/$doc"
expect "listed in its folder"               200 "$HOST/api/$V/documents?folderId=$folder"

# The one test that validates hashing, storage, mapping, transaction and resolution at once.
curl -s -o "$TMP/downloaded.pdf" "$HOST/api/$V/documents/$doc/content"
cmp -s "$TMP/sample.pdf" "$TMP/downloaded.pdf" \
  && { printf '  PASS  %-52s\n' "downloaded bytes identical to uploaded"; pass=$((pass + 1)); } \
  || { printf '  FAIL  %-52s\n' "downloaded bytes identical to uploaded"; fail=$((fail + 1)); }

dedup="$(curl -s -F "file=@$TMP/sample.pdf" "$HOST/api/$V/documents?folderId=$ROOT")"
case "$dedup" in
  *'"deduplicated":true'*) [ -n "$blob" ] && case "$dedup" in *"$blob"*)
      printf '  PASS  %-52s\n' "same content deduplicates to the same blob"; pass=$((pass + 1)) ;;
    *) printf '  FAIL  %-52s different blobId\n' "same content deduplicates to the same blob"; fail=$((fail + 1)) ;; esac ;;
  *) printf '  FAIL  %-52s %s\n' "same content deduplicates to the same blob" "$dedup"; fail=$((fail + 1)) ;;
esac

expect "a new version is appended"          200 -F "file=@$TMP/sample-v2.pdf" "$HOST/api/$V/documents/$doc/versions"
expect "identical content is refused"       409 -F "file=@$TMP/sample-v2.pdf" "$HOST/api/$V/documents/$doc/versions"
expect "a non-empty folder cannot be deleted" 409 -X DELETE "$HOST/api/$V/folders/$folder"
expect "document renamed"                   204 -X PATCH "$HOST/api/$V/documents/$doc/name" \
  -H 'Content-Type: application/json' -d '{"name":"contrat-signe.pdf"}'
expect "document deleted"                   204 -X DELETE "$HOST/api/$V/documents/$doc"
expect "a deleted document refuses mutation" 409 -X PATCH "$HOST/api/$V/documents/$doc/name" \
  -H 'Content-Type: application/json' -d '{"name":"autre.pdf"}'
expect "unknown document"                   404 "$HOST/api/$V/documents/00000000-0000-7000-8000-ffffffffffff"

echo
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
