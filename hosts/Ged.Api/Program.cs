using Ged.Adapters.Persistence.PostgreSql;
using Ged.Adapters.Persistence.SqlServer;
using Ged.Adapters.Storage;
using Ged.Adapters.Storage.FileSystem;
using Ged.Api.Composition;
using Ged.Api.Diagnostics;
using Ged.Core.Ports;
using Ged.Domain.Blobs;
using Ged.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Ged.Adapters.FileTypes.FileSignatures;
using Ged.Adapters.FileTypes;

var builder = WebApplication.CreateBuilder(args);

// *************************** services ***************************

var connectionString = builder.Configuration.GetConnectionString("Ged")
    ?? throw new InvalidOperationException("ConnectionStrings:Ged is not configured.");

var provider = builder.Configuration["Ged:Provider"] ?? "postgres";

// One composition root, two engines. Neither the domain, the features nor the storage adapters know
// which line below ran; the difference is confined to IPersistenceProvider and the DbUp scripts.
_ = provider.ToLowerInvariant() switch
{
    "postgres" or "postgresql" => builder.Services.AddGedPostgreSql(
        connectionString,
        builder.Configuration.GetValue("Ged:PostgresMajorVersion", 16)),
    "sqlserver" or "mssql" => builder.Services.AddGedSqlServer(connectionString),
    _ => throw new InvalidOperationException($"Unknown Ged:Provider '{provider}'."),
};

// One composition root, two detectors. Neither the feature layer nor the domain knows which line
// below ran: they see IContentFormatDetector and nothing else — the same shape as the two
// persistence providers and the storage adapters.
var detector = builder.Configuration["Ged:Uploads:Detector"] ?? "builtin";

_ = detector.ToLowerInvariant() switch
{
    "builtin" =>
        builder.Services.AddGedFileTypesBuiltInDetector(),

    "filesignatures" => builder.Services.AddGedFileSignaturesDetector(),

    _ => throw new InvalidOperationException($"Unknown Ged:Uploads:Detector '{detector}'."),
};

// A real backend that needs nothing installed, so the whole chain — upload, download, replication,
// purge — runs against a directory. Registering a second IObjectStorage next to it is what a
// migration to MinIO or Beys would look like; nothing else changes.
var storageOptions = builder.Configuration.GetSection("Ged:Storage").Get<FileSystemStorageOptions>()
    ?? new FileSystemStorageOptions();

builder.Services.AddSingleton(storageOptions);
builder.Services.AddSingleton<IObjectStorage, FileSystemObjectStorage>();
builder.Services.AddSingleton<IObjectStorageRegistry>(sp =>
    new ObjectStorageRegistry(
        sp.GetServices<IObjectStorage>(), new StorageProvider(storageOptions.ProviderName)));
builder.Services.AddSingleton<IBlobLocationResolver, BlobLocationResolver>();

// Feature handlers. Written out rather than scanned, for the same reason the endpoints are: a
// handler that was added but never registered is then a startup failure with a name in it.
builder.Services.AddGedFeatureHandlers(builder.Configuration);

builder.Services.AddGedApiVersioning();
builder.Services.AddGedSecurity(builder.Configuration, requireAuthentication: !builder.Environment.IsDevelopment());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GedExceptionHandler>();
builder.Services.AddResponseCompression(options => options.EnableForHttps = false);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// Behind a reverse proxy the remote address is the proxy's, which would put every client in one
// rate-limit partition and log one IP for the whole fleet.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

// Uploads are bounded here rather than left to the 30 MB default, which is both too small for a
// scanned file and unbounded in the sense that nothing states it.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 256L * 1024 * 1024;
    options.ValueLengthLimit = 1024 * 1024;
});

var app = builder.Build();

// *************************** pipeline ***************************
// Order is behaviour, not style. Forwarded headers must precede anything that reads the client
// address; the exception handler must wrap everything after it; authentication must precede
// authorization and rate limiting, since both partition on identity.

app.UseForwardedHeaders();
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseGedSecurityHeaders();
app.UseResponseCompression();
app.UseRequestTimeouts();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGedApi();

app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapGet("/health/live", () => Results.Ok()).AllowAnonymous().ExcludeFromDescription();

await app.RunAsync();
