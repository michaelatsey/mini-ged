# Logging

## The shape

One JSON object per line, on stdout. Configured in `appsettings.json` and nowhere else — no provider
is added in `Program.cs`, so the format is a configuration change, not a code change.

```json
"Logging": {
  "Console": {
    "FormatterName": "json",
    "FormatterOptions": {
      "IncludeScopes": true,
      "UseUtcTimestamp": true,
      "TimestampFormat": "yyyy-MM-dd'T'HH:mm:ss.fff'Z'"
    }
  }
}
```

An entry, as the API writes it:

```json
{"Timestamp":"2026-10-14T12:31:53.282Z","EventId":2001,"LogLevel":"Information",
 "Category":"Ged.Features.Common.FileTypes.FileTypeInspector",
 "Message":"Upload rapport has no extension; accepted as pdf (4096 bytes, document type CONTRACT), stored as rapport.pdf.",
 "State":{"FileName":"rapport","Format":"pdf","SizeBytes":4096,"StoredName":"rapport.pdf","DocType":"CONTRACT", …},
 "Scopes":[{"TraceId":"b9d7…","SpanId":"0373…", …},{"ConnectionId":"0HNP…"},{"RequestId":"0HNP…:00000001","RequestPath":"/api/v1/documents"}]}
```

(Wrapped here for reading; the output is a single line.)

| Choice | Why |
|---|---|
| JSON rather than text | every field reaches the output as a field; a later pipeline indexes it instead of parsing a sentence back |
| stdout rather than a file | the image is read-only and has one writable path, `/tmp`; the orchestrator already collects stdout |
| `IncludeScopes` | ASP.NET opens a scope per request, so each entry carries its `TraceId` and `RequestId` and can be joined to the rest of that request |
| UTC timestamps | one clock across replicas and time zones |

Development inherits the same format. A readable console for local work is one override in
`appsettings.Development.json` (`"FormatterName": "simple"`) — left out on purpose, so the format a
developer sees is the format production writes.

## Writing an entry

Every entry is a `[LoggerMessage]` method — CA1848 refuses the `LogInformation` family in Release,
and the generator keeps a template and its arguments in step where a `params` array cannot.

- **Every argument appears in the template.** One that does not is a generator warning, which is an
  error in Release.
- **A method call is never passed as an argument; compute it into a local first.** CA1873 flags it,
  and in this repository it does so even under a `logger.IsEnabled` guard enclosing the call — the
  guard does not satisfy it. Add a guard only where the cost is real: a guard that no test and no
  analyser can see is a guard nobody will notice going missing.
- **Event ids are stable and allocated by range.** They are what a filter or a dashboard keys on; the
  JSON formatter writes the number, not the `EventName`.

| Range | Owner |
|---|---|
| 1000–1999 | the host — `GedExceptionHandler` is 1000 |
| 2000–2999 | uploads — `FileTypeInspector` |

Tests assert on the event id and the structured fields through `RecordingLogger<T>`, never on the
rendered message — the same rule as asserting on a rule's type rather than its text.

## What is logged today

| Id | Event | Level | Fields |
|---|---|---|---|
| 2001 | `ExtensionlessUploadAccepted` | Information | `FileName`, `Format`, `SizeBytes`, `StoredName`, `DocType` |
| 2002 | `ExtensionlessUploadRefused` | Information | `FileName`, `Reason`, `Stage`, `DetectedFormat`, `SizeBytes`, `DocType` |
| 2003 | `DeclaredMediaTypeDisagreement` | Information | `FileName`, `DeclaredMediaType`, `DeclaredFormat`, `Format`, `Stage`, `Enforced`, `Outcome`, `DocType` |

### 2001 and 2002 — names with no extension

One entry for each upload with no extension that the inspector decides on, whatever it decided, and
none for any other upload. They are the evidence for keeping `AllowMissingExtension` on or off: how
often bare names arrive, and what they turn out to be. `Stage` is `name` when the refusal came before
a byte was read — which is why such entries carry no size and no detected format — and `content`
otherwise.

Two bare uploads are refused before the inspector decides, and leave no entry. A count built on these
logs is a count of decisions, not of arrivals:

| Refused by | Response | When |
|---|---|---|
| `DocumentName`, in the handler | 400 | the name holds a path separator or a control character |
| the staging, in the handler | 415 | the body exceeds `MaxSizeBytes` — reachable only within the mebibyte the transport admits above it |

With `AllowMissingExtension` off neither gap applies: the endpoint's name check refuses, and logs,
before the handler runs.

Both are `Information`. A refused file is a client's outcome, not an operational fault, and a
`Warning` that fires on every misnamed upload trains everyone to ignore warnings.

### 2003 — a declared media type that disagrees with the file

One entry whenever a client **claimed** a media type — not absent, blank or
`application/octet-stream` — and the claim does not name the format it is compared with:

| `Stage` | Written when | `Format` is | `Outcome` |
|---|---|---|---|
| `name` | the name check refuses on the claim: a type outside the catalogue, named or bare, or a named file declared across families | the extension's format, or null for a bare name | `refused` |
| `content` | the content was accepted as a format the claim does not name | the accepted format | `accepted` when the flag is off or the claim was corrected within its family; `refused` across families |

It is written **whether or not `EnforceDeclaredMediaType` is on** — `Enforced` says which. It is the
evidence for that setting: which clients mislabel, how often, and whether turning the flag on would
refuse any of them. Measuring cannot depend on the switch it is meant to inform.

- **Not written** when the claim names the compared format, when no claim was made, or when the
  content was refused for its own reason — it never became a format the claim could disagree with.
- **On any upload, bare or named.** A bare upload can leave two entries: its 2001 or 2002, and a 2003.
  A count of bare uploads keys on 2001 and 2002 only.
- **`DeclaredMediaType` is the value as compared** — parameters after `;` dropped, trimmed — and
  `DeclaredFormat` the catalogue format it names, null when it names none (`text/html`).
- **`Outcome` is the inspector's decision on the upload**, not on the claim: `accepted` means the file
  passed the inspection, as what its bytes are, while its claim was wrong. Storage and commit come
  after, in the handler, and can still fail.
- **Expect volume with the flag off.** A browser sending `application/vnd.ms-excel` for a CSV is a
  disagreement and is recorded. That is the measurement; filter on the id. `Information`, not
  `Warning`, for the reason given above.
- **Not truncated**, like `FileName`: a truncated value is a different value. It is bounded by the
  multipart header limit and the rate limiter per identity. Who the client is comes from the request
  scope (`TraceId`, `RequestId`), not from the entry.

## The file name is logged as the client sent it

It is the one field that says which client produces bare names, so it is logged unaltered. Two
consequences, both deliberate:

- **Injection.** A name carrying a line break cannot forge a second entry: the JSON formatter escapes
  it, and the entry stays one line. That protection belongs to the JSON formatter — switching to
  `simple` would give it up. The same holds for `DeclaredMediaType` in 2003, the other value logged as
  the client sent it.
- **Personal data.** A name like `bulletin-salaire-dupont` is personal data, so the log inherits the
  retention duty the document has. Whatever collects these logs later needs a retention period set
  for that, not the default "keep forever".
- **Length.** A refusal at the name check is logged before `DocumentName` validates the name, so its
  255-character limit has not applied yet: the entry is bounded by the multipart header limit
  instead, and by the rate limiter per identity. Not truncated, on purpose — a truncated name is a
  different name.

## Not covered yet

Shipping the logs anywhere. Stdout is the seam: a collector (Fluent Bit, Filebeat, the orchestrator's
own) reads it without a change to the application. Choosing and running one is the ELK work, and it
is deliberately not started here.
