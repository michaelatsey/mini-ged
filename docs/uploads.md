# Restricting what can be uploaded

## The premise

An extension is a string the client chose. A `Content-Type` header is a string the client chose.
Neither is evidence of anything, and Microsoft's own guidance is blunt about it: *don't rely on or
trust the `FileName` property of `IFormFile` without validation*.

So the policy is built on three layers with different costs and different authority:

```
extension          free      a claim
declared type      free      a claim
content signature  bytes     the only evidence
```

The first two exist to refuse cheaply — a 200 MB executable should not be buffered to disk before
anyone notices its extension was never acceptable. Only the third decides, and it is the third that
determines the media type stored on the version.

## Allowlist, not denylist

```
Denylist                       Allowlist
────────                       ─────────
enumerate everything           enumerate what the business
dangerous                      actually needs
        │                              │
an open set that grows with    short, stable, and reviewed
every parser and container     when it changes
format
```

## Configuration

```json
"Ged": {
  "Uploads": {
    "AllowedFormats": [ "pdf", "docx", "xlsx", "pptx", "odt", "ods", "odp",
                        "txt", "csv", "jpeg", "png", "tiff", "bmp" ],
    "MaxSizeBytes": 268435456,
    "MaxSizeByFormat": { "csv": 10485760, "png": 26214400 },
    "FormatsByDocType": { "CONTRACT": [ "pdf" ], "INVOICE": [ "pdf", "xlsx" ] },
    "EnforceDeclaredMediaType": false
  }
}
```

Validated with `ValidateOnStart`: a typo in a format name stops the deployment instead of silently
changing what the API accepts.

### Sets

An entry may name a format or a set, so a policy can be written the way the requirement was:

```json
"AllowedFormats": [ "documents", "images" ]
```

| Set | Expands to |
|---|---|
| `documents` | pdf, doc, docx, xls, xlsx, ppt, pptx, odt, ods, odp, txt, csv, rtf |
| `images` | jpeg, png, tiff, bmp, gif |
| `office` | doc, docx, xls, xlsx, ppt, pptx, odt, ods, odp |
| `text` | txt, csv, json |

Convenient, and worth one caution: a set is expanded at resolution time, so adding a format to a set
in code widens every environment that names that set. Where an environment must not move without a
decision — production, typically — list the formats explicitly.

### Why formats are named, not described

An operator picks formats by name; the magic bytes live in `FileFormats`, in code. Signatures in a
JSON file mean a wrong digit silently widens what gets through, and nothing reviews it. The
catalogue is code because it is a security control, not a setting.

### Per-format ceilings

One global ceiling has to be set for the largest legitimate case — a scanned contract. That leaves
every other format effectively unbounded: a 200 MB CSV is a mistake or an attack, never a document.

### `FormatsByDocType` narrows, never widens

A classification absent from the map falls back to the global list; one present is intersected with
it. Otherwise adding a document type would become a way around the policy.

### `EnforceDeclaredMediaType` is off by default

Browsers and HTTP clients disagree about the media type of the same file — `text/csv` versus
`application/vnd.ms-excel` is the classic case. Enforcing the client's header rejects legitimate
uploads while stopping no attacker, who controls that header entirely. Turn it on only for a known,
controlled client.

## What the content check actually catches

| Attack | Caught by |
|---|---|
| `payload.exe` renamed `invoice.pdf` | signature is not `%PDF` |
| `invoice.pdf.exe` | the **last** extension is read, never the first |
| Forged `Content-Type: application/pdf` | the header is never evidence |
| Executable renamed `notes.txt` | text has no signature, but a NUL byte contradicts it |
| `.jar` renamed `.docx` | archive lacks `[Content_Types].xml` |
| `.xlsx` renamed `.docx` | archive has no `word/` part |
| Empty file | refused before anything else |

## Three containers, one signature each

Three of the requested formats cannot be identified by their leading bytes at all, because the bytes
identify a *container* and not a document.

| Container | Signature | Also used by | Decided by |
|---|---|---|---|
| ZIP (OOXML) | `PK\x03\x04` | .zip, .jar, .apk, ODF | part prefix: `word/`, `xl/`, `ppt/` |
| ZIP (OpenDocument) | `PK\x03\x04` | as above | the exact `mimetype` entry |
| OLE2 (legacy Office) | `D0 CF 11 E0` | **.msi**, .msg, older formats | directory stream name |

OpenDocument is the strongest of the three: the specification requires a first entry named
`mimetype`, stored uncompressed, containing the exact media type. There is no near-miss to allow.

OLE2 is the weakest. `.doc`, `.xls`, `.ppt` and `.msi` are the same container, so the flavour has to
come from a directory stream name — `WordDocument`, `Workbook`, `PowerPoint Document` — stored as
UTF-16LE. The header gives the sector size and first directory sector, and a bounded prefix of the
file is scanned for the marker. The allocation table is deliberately *not* walked: chasing sector
chains through hostile input is more attack surface than the check is worth, and the scan is capped
so a large upload cannot turn the check into work.

## A warning about .doc, .xls, .ppt and .rtf

These four are flagged `CarriesExecutableContent` in the catalogue, and the flag is not decorative.

```
.docx / .xlsx / .pptx          .doc / .xls / .ppt
──────────────────────         ──────────────────
macros need the m-suffix       macros are native to the format
.docm, .xlsm, .pptm            and there is no suffix to warn anyone
        │                              │
refusing the m-suffix          accepting .doc accepts macro documents,
excludes macro documents       and nothing in the extension says so
```

`.rtf` is in the same category for a different reason: it embeds OLE objects, which is what made it
a long-running exploit vector in Office.

None of this is an argument for refusing them — a GED that cannot take a .doc is a GED nobody uses.
It is an argument for knowing what was accepted:

- These formats belong behind a malware scanner more than any other. The staging area already is the
  quarantine Microsoft's guidance describes.
- If the business does not actually need them, leave them out of `AllowedFormats`. The catalogue
  knowing a format is not the same as an environment accepting it.
- `FormatsByDocType` is the tool for a middle position: `"CONTRACT": ["pdf"]` means a contract can
  never arrive as a macro-capable document, whatever the global list says.

## The OOXML problem, stated honestly

`docx`, `xlsx`, `pptx`, `zip`, `jar` and `apk` all begin with `PK\x03\x04`. The signature says "this
is a ZIP" and nothing more.

So for those formats the archive is opened read-only and two things are required: the
`[Content_Types].xml` manifest, and a part under the prefix that identifies the flavour — `word/`,
`xl/`, `ppt/`.

Only entry names are read; nothing is decompressed, so a zip bomb costs nothing here. Any failure to
parse is a refusal rather than an exception reaching the caller.

## What this does not do

Worth stating, because a control that is trusted beyond its reach is worse than no control.

- **It is not a malware scanner.** A genuinely valid PDF carrying an exploit passes every check
  here. Microsoft's guidance is a quarantine area and a third-party scanner, run in the background
  rather than in the request.
- **It does not validate structure.** A file beginning with `%PDF` and continuing with garbage is
  accepted. Full parsing is a parser's job, and parsers are themselves an attack surface.
- **It does not protect a browser that renders the file.** Downloads already go out as attachments
  with `X-Content-Type-Options: nosniff`, which is what makes an SVG or an HTML file inert. SVG is
  deliberately absent from the catalogue for that reason.

The natural next step is `IVirusScanner` as a slice-local port on `UploadDocument`, called on the
staged content before it reaches storage — the staging area is already the quarantine.

## Responses

```
415 Unsupported Media Type    the payload's type is not accepted
                              (not 400: the request itself is well formed)
```

The body is a problem document carrying a `code` extension and a message safe to display —
`ExtensionNotAllowed`, `ContentDoesNotMatchExtension`, `TooLarge`, `Empty`.

## Cost

The header is captured during the single pass that already computes the SHA-256, so recognising a
format costs no additional read. Only the OOXML case opens the staged file a second time, and only
to list entry names.
