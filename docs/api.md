# The API surface

## Versioning

URL-segment versioning: `/api/v1/documents`. Built on `Asp.Versioning` 10, the first release made
for ASP.NET Core 10 and the built-in `Microsoft.AspNetCore.OpenApi`. Version 8 still runs through
roll-forward but predates this integration and needs several hand-written transformers to produce
one OpenAPI document per version.

```csharp
services.AddApiVersioning(options => options.ApiVersionReader = new UrlSegmentApiVersionReader())
        .AddApiExplorer(options => options.GroupNameFormat = "'v'VVV")
        .AddOpenApi();          // Asp.Versioning's AddOpenApi, not Microsoft's

app.MapOpenApi().WithDocumentPerVersion();
```

Three choices worth stating.

**`AssumeDefaultVersionWhenUnspecified` is off.** Serving a default to a client that asked for no
version means that the day the default moves, every such client silently changes API — the exact
breakage versioning exists to prevent.

**`ReportApiVersions` is on.** Every response carries `api-supported-versions`, and
`api-deprecated-versions` once a version is on its way out, so a client discovers a deprecation from
its own traffic instead of from a changelog it does not read.

**One document per version.** `WithDocumentPerVersion()` derives the documents from the versions
already declared on the route groups, rather than from a second list that drifts the first time
someone adds a version.

Documents are at `/openapi/v1.json`; Scalar renders them at `/scalar`, with a version selector fed
by `app.DescribeApiVersions()`.

### Where versions are declared

The host builds the versioned groups; the feature assembly fills them.

```csharp
// Ged.Api
var api = app.NewVersionedApi("Ged");
api.MapGroup("/api/v{version:apiVersion}").HasApiVersion(V1).MapGedV1();

// Ged.Features
public static RouteGroupBuilder MapGedV1(this RouteGroupBuilder version)
{
    DocumentsEndpoints.Map(version);
    FoldersEndpoints.Map(version);
    return version;
}
```

Which versions exist, and how a client selects one, is a decision about the API's contract and
belongs to the composition root. The behaviour stays in the slice.

A side effect worth knowing: the feature layer therefore carries no versioning package and depends on
ports and the domain alone — which is also why it compiles and is verified while the host is not.

Adding v2 is one group here plus a `MapGedV2` beside the existing modules. v1's slices are not
touched, which is the entire point.

### Deprecating a version

```csharp
api.MapGroup("/api/v{version:apiVersion}").HasDeprecatedApiVersion(V1).MapGedV1();
```

Pair it with a `Sunset` header (RFC 8594) giving the removal date. Deprecation that is announced only
in documentation is not announced.

## Security

| Concern | Decision | Why |
|---|---|---|
| Authorization | fallback policy requires an authenticated user | with opt-in, a new endpoint is unprotected until someone remembers; the one forgotten is never noticed in review |
| JWT clock skew | 30 seconds, not the 5-minute default | the default keeps a revoked token usable well past its stated expiry |
| JWT metadata | `RequireHttpsMetadata` | a compromised DNS answer must not hand the process a forged signing key |
| Rate limiting | partitioned per identity, falling back to IP | one global limiter lets a single noisy client exhaust everyone's budget |
| Transfers | concurrency limiter, not a rate limiter | what exhausts a server here is many large transfers at once, not many small requests |
| CORS | explicit origins only | `AllowAnyOrigin` cannot be combined with credentials, and reflecting the request origin is the same as no policy |
| Upload size | stated at 256 MB | the 30 MB default is both too small for a scanned document and unbounded in the sense that nothing declares it |
| Response headers | nosniff, `frame-ancestors 'none'`, `default-src 'none'` | an API returns data, never markup; a filename that reaches a browser as a document must still execute nothing |
| Compression | disabled over HTTPS | compressing attacker-influenced content alongside secrets is the BREACH class of attack |
| Forwarded headers | processed first | behind a proxy the remote address is the proxy's, which puts every client in one rate-limit partition |

### Policies are requested by the slice, defined by the host

```csharp
// the slice states what kind of work it does
.RequireRateLimiting(GedPolicies.Content)
.WithRequestTimeout(GedPolicies.Content);

// the host states what that costs here
options.AddPolicy(GedPolicies.Content, TimeSpan.FromMinutes(10));
```

A concurrency limit and a timeout depend on the deployment, not on the use case. The upload endpoint
knows it transfers content; it does not know how much of that this cluster can take.

Transfers are exempt from the 30-second default timeout for a concrete reason: cancelling a large
upload mid-write is how orphaned objects are created.

## Errors

One `IExceptionHandler` translates domain and persistence failures; endpoints contain no `try`.

```
BusinessRuleViolationException  -> 409, code = the rule's type name
DomainException                 -> 400
ObjectNotFoundException         -> 502   storage could not serve known content
PersistenceException            -> 409   usually a concurrency conflict
anything else                   -> not translated
```

The last line is deliberate. An unrecognised exception falls through to a bare 500 rather than being
guessed at, because a status invented for a failure nobody understands tells the client something
false.

Rules are matched on their type, never on their message — which is what makes rules-as-types pay off:
the mapping is checked by the compiler and a reworded message breaks nothing.

## Health

```
/health/live    the process answers
/health/ready   the database answers  (SELECT 1)
```

The readiness check runs the cheapest possible statement. A check that queries real data measures the
data as much as the dependency, and turns a slow table into a failed deployment.

## Pipeline order

Order is behaviour, not style.

```
UseForwardedHeaders     before anything that reads the client address
UseExceptionHandler     wraps everything after it
UseHsts / UseHttpsRedirection
UseGedSecurityHeaders
UseResponseCompression
UseRequestTimeouts
UseCors
UseAuthentication       before authorization AND before rate limiting,
UseAuthorization        since both partition on identity
UseRateLimiter
```

Putting `UseRateLimiter` before authentication is the common mistake: every authenticated client then
shares the anonymous partition.

## Versions

| Package | Version |
|---|---|
| `Asp.Versioning.Http` | 10.2.3 |
| `Asp.Versioning.Mvc.ApiExplorer` | 10.2.3 |
| `Asp.Versioning.OpenApi` | 10.2.3 |
| `Microsoft.AspNetCore.OpenApi` | 10.0.11 |
| `Scalar.AspNetCore` | 2.13.0 |
