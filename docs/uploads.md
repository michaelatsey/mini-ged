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
    "ReclassifiableGroups": [ "images" ],
    "MaxSizeBytes": 268435456,
    "MaxSizeByFormat": { "csv": 10485760, "png": 26214400, "tiff": 52428800 },
    "FormatsByDocType": { "CONTRACT": [ "pdf" ], "INVOICE": [ "pdf", "xlsx" ] },
    "EnforceDeclaredMediaType": false,
    "AllowMissingExtension": false
  }
}
```

Validated with `ValidateOnStart`: a typo in a format name stops the deployment instead of silently
changing what the API accepts.

### The ceiling in front of the ceiling

`MaxSizeBytes` is the third limit a request meets, not the first. Kestrel refuses a body over
30,000,000 bytes, and the form binder refuses a multipart body over 128 MiB — both while the request
is being bound, before a handler runs, so the answer is a bare 413 with nothing of the policy in it.

Both are therefore derived from `MaxSizeBytes` at startup, plus a mebibyte for the multipart
envelope, rather than written next to it. A second copy of a number is a number that drifts, and this
one did: the policy said 256 MiB while Kestrel stayed at its default, and every upload over 28.6 MB
was refused with a message about neither.

Raising `MaxSizeBytes` therefore raises what the transport reads, which is also what a container has
to hold in `/tmp` while it is staged. `docs/containers.md` carries that arithmetic.

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
controlled client — an SDK or an integration that **states** the type of what it sends, rather than a
browser guessing one.

For that client the declared type is a claim, and it is held to the bytes:

```
declared type        compared with                        refused as
─────────────        ─────────────                        ──────────
absent, blank,       nothing — it claims nothing          never
application/octet-stream
outside the          nothing it could agree with          DeclaredMediaTypeMismatch,
catalogue                                                 before the body is read
another format,      the extension, then the format       DeclaredMediaTypeMismatch
outside the family   the content is finally accepted as
a sibling in a       the same                             never — corrected, like the extension
reclassifiable family
```

- **Compared with the content, not only the name.** The name check refuses what no content could
  reconcile — `rapport.pdf` declared `image/png` — before a byte is staged. The content check then
  compares the claim with the format finally accepted: named, reclassified, or identified from a bare
  name. A bare name declared `image/png` and holding a PDF is caught there: the claim said image, the
  bytes are a document.
- **Corrected within a family, like the extension.** A claim is held to the rule the extension is held
  to, no stricter: inside a family enabled in `ReclassifiableGroups`, a wrong label is corrected, not
  refused. `photo.jpg` declared `image/jpeg` and holding a PNG is stored as `photo.png`, exactly as with
  no header. Clients commonly derive the declared type from the file name; holding that claim to strict
  equality would refuse the naming accident reclassification exists to accept, and stop no attacker,
  who sends whatever header agrees. With no family enabled, the claim is strict again, as the name is.
- **`application/octet-stream` claims nothing.** It means "arbitrary binary data" (RFC 2046 §4.5.1),
  and it is what a client relaying an upload it could not type sends. So the flag means *a claim made
  must be true*, not *a claim must be made*: a client can always avoid the comparison by declaring
  nothing.
- **Compared as a format, not as a string.** Parameters after `;` and case are ignored; the rest must
  be the media type the catalogue records for a format. No alias is accepted — `image/jpg`,
  `application/x-pdf` — because each one widens what a claim may be and still agree. Adding one is a
  reviewed edit to `FileFormats`, with its source.
- **Never recorded.** What is stored, and served back, is the detected format's media type.
- **Always logged when it disagrees.** Every claim that does not name the file's format leaves event
  2003, with the flag on or off, corrected or refused — the evidence for deciding whether to turn the
  flag on. `docs/logging.md` lists its fields.

### One image under another image's extension

A PNG a user saved as `photo.jpg` is refused by everything above, and correctly: the name contradicts
the bytes. It is also a naming accident rather than an attack, and the only thing the refusal achieved
was to make the user rename the file.

```json
"ReclassifiableGroups": [ "images" ]
```

With that key, content of an allowed format arriving under the extension of another allowed format in
the same set is accepted as what it *is*, and the stored name takes the detected format's own
extension — `photo.jpg` becomes `photo.png`. OWASP's input-validation guidance asks for that
specifically: set the extension of a stored image from the type detected in its content, and check
that type against a defined list of image types.

Five conditions, and all five:

| | |
|---|---|
| one family | both formats sit in a set that is eligible in code **and** named in `ReclassifiableGroups` |
| still allowed | the detected format resolves through `AllowedFormats` and `FormatsByDocType` like any extension |
| carries no code | the detected format is not flagged `CarriesExecutableContent` |
| internally consistent | the detected format's own header check passes — BMP has one |
| its own ceiling | the size is within the **detected** format's limit: a PNG named `.tif` is refused at 25 MiB, not 50 |

Anything else is `ContentDoesNotMatchExtension`, with the message it always had.

That last condition is one-way, and deliberately so. The **claimed** format's ceiling still applies
first, before a byte is read — it is the number that bounds the staging of the request body — so a
30 MiB TIFF named `.png` is refused at PNG's 25 MiB and never reaches the detector at all.
Reclassification can only tighten the limit, never lift it: raising the staging bound to the widest
ceiling in the family would undo what `MaxSizeBytes` is there for, which is to stop an oversized
upload at the limit instead of writing it to a temp volume in full and refusing it afterwards. The
effective ceiling is therefore the smaller of the two, and a file that needs the larger one has to
arrive under its own extension.

Two wider versions of this were rejected. **Global reclassification** — any allowed format under any
allowed extension — accepts `report.pdf` holding a DOCX, and a client opens a document by its
extension. **Families chosen in configuration** would reopen ".xlsx renamed .docx" with one line of
JSON; the eligible families therefore live in `FileFormats`, beside the sets, for the same reason the
signatures do — this is a security control, not a setting.

So the shape is fixed: code says which families *could* be reclassified within — `images`, and
nothing else today — and configuration says which of them *are*. Startup refuses any other entry, and
refuses an eligible family the moment it holds a format flagged `CarriesExecutableContent`, so
admitting a macro-capable format to `images` fails a deployment instead of quietly turning "renamed
`.xls`" into an accepted upload.

The code default is `[]`, deliberately, and not `["images"]`: the configuration binder appends to a
pre-filled list instead of replacing it, so a default written in code could be widened by a deployment
but never turned off. `appsettings.json` states it instead.

With `EnforceDeclaredMediaType` on, the two settings work together. `photo.jpg` holding a PNG is
stored as `photo.png` whether the client declares `image/png` — truthful about the bytes — or
`image/jpeg`, derived from the name: a claim is corrected within the family as the extension is. What
stays refused is a claim that crosses families, `image/png` on a PDF. Both used to run differently:
first the header was compared with the extension alone, and refused the truthful client; then it was
held to strict equality with the bytes, and refused the client that named its type after the file.

### A name with no extension

Scanners, mobile share sheets and some legacy clients send `rapport` rather than `rapport.pdf`. That
is refused as `NoExtension` unless the deployment says otherwise:

```json
"AllowMissingExtension": true
```

A name with no extension claims nothing, so unlike `photo.jpg` it contradicts nothing — the argument
that rejected global reclassification, a client opening a file by an extension that lies, has no
extension to apply to. The content decides, and the stored name takes the detected format's
extension: `rapport` becomes `rapport.pdf`, and so does `rapport.`. A file with no extension opens in
nothing, so leaving it unnamed would only move the problem to the download.

What still applies is everything that applies to a named upload:

| | |
|---|---|
| still allowed | the detected format resolves through `AllowedFormats` and `FormatsByDocType` — refused as `ExtensionNotAllowed` otherwise |
| internally consistent | the detected format's own header check passes — BMP has one |
| its own ceiling | the size is within the **detected** format's limit |

The allowlist is the only gate on a format flagged `CarriesExecutableContent`, exactly as for a named
upload: a deployment that does not list `doc` does not receive one by leaving the name bare.

Two things are refused even with the flag on, both as `NoExtension`:

- **Text.** `txt`, `csv` and `json` carry no signature, so a signature-less format is only ever
  confirmed by the name it arrives under. Without one, picking a format would decide which document
  types may receive the file and which media type is recorded for it. A declared `text/csv` does not
  change that: the declared type confirms or contradicts a format, it never identifies one.
- **Content the detector cannot place**, and content failing its own format's header check.

With `EnforceDeclaredMediaType` on, a bare name is held to its declared type like any other upload:
`rapport` declared `application/pdf` and holding a PDF is stored as `rapport.pdf`; declared
`image/png`, it is refused as `DeclaredMediaTypeMismatch`. This used to refuse every bare name, while
the comparison stood on the format an extension names; it now stands on the format the content is.

The cost is the cheap refusal. With no claimed format there is no per-format ceiling before the body
is read, so the staging is bounded by `MaxSizeBytes`, and the detected format's ceiling applies after
detection: a 200 MB CSV-like upload with no name is written to the temp volume before being refused,
where `data.csv` would have stopped at 10 MiB. The transport admits `MaxSizeBytes` already, so the
`/tmp` arithmetic in `docs/containers.md` does not move — what moves is when the refusal happens.
That is why the flag is off by default.

## What the content check actually catches

| Attack | Caught by |
|---|---|
| `payload.exe` renamed `invoice.pdf` | signature is not `%PDF` |
| `invoice.pdf.exe` | the **last** extension is read, never the first |
| Forged `Content-Type: application/pdf` | the header is never evidence |
| Executable renamed `notes.txt` | text has no signature, but a NUL byte contradicts it |
| `.jar` renamed `.docx` | archive lacks `[Content_Types].xml` |
| `.xlsx` renamed `.docx` | archive has no `word/` part — still refused, whatever is reclassifiable |
| Empty file | refused before anything else |
| PNG renamed `photo.jpg` | **nothing**, where `images` is reclassifiable: accepted as a PNG, stored as `photo.png` |
| PDF named `rapport` | **nothing**, where `AllowMissingExtension` is on: accepted as a PDF, stored as `rapport.pdf` |
| Text named `notes` | refused — with no signature and no name, nothing identifies it |

The PNG row is the one deliberate hole, and it is one family wide. Everything above it stays refused:
reclassification never crosses a family, never passes the allowlist, and never borrows a ceiling. The
`rapport` row is not a hole in the same sense — a bare name makes no claim for the content to
contradict — and it passes through the same allowlist and the same ceilings.

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
- **It does not rewrite what it accepts.** Reclassification corrects a *name*; the bytes are stored
  exactly as they arrived. Content disarm and reconstruction — decoding an image and re-encoding it
  from its pixels — is what removes a payload hidden in a JPEG's metadata, and it changes the bytes,
  so the SHA-256, so the integrity proof every version rests on. It belongs with the scanner below,
  and it becomes urgent before previews or OCR: both decode an image server-side, which turns the
  decoder into the attack surface.
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
