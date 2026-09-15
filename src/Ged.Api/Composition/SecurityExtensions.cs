using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Ged.Features.Common;
using Microsoft.IdentityModel.Tokens;

namespace Ged.Api.Composition;

/// <summary>Authentication, authorization, throttling and transport hardening.</summary>
internal static class SecurityExtensions
{
    /// <summary>Registers the security services.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGedSecurity(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = configuration["Auth:Authority"];
                options.Audience = configuration["Auth:Audience"];

                // Metadata is fetched over the network at startup; requiring HTTPS means a
                // compromised DNS answer cannot hand the process a forged signing key.
                options.RequireHttpsMetadata = true;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    // The default is five minutes, which keeps a revoked token usable well past its
                    // stated expiry. Thirty seconds absorbs ordinary clock drift and nothing more.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization(options =>
            // Deny by default. With an opt-in model, a new endpoint is unprotected until someone
            // remembers to protect it — and the one that is forgotten is never noticed in review.
            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build());

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Partitioned per authenticated identity, falling back to the remote address. A single
            // global limiter would let one noisy client exhaust the budget for everyone.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: PartitionKeyFor(http),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 300,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            // Uploads and downloads are bounded by concurrency rather than by rate: what exhausts a
            // server here is many large transfers in flight at once, not many small requests.
            options.AddPolicy(GedPolicies.Content, http =>
                RateLimitPartition.GetConcurrencyLimiter(
                    partitionKey: PartitionKeyFor(http),
                    factory: _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = 4,
                        QueueLimit = 8,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    }));

            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsync(
                    "Too many requests. Retry later.", ct);
            };
        });

        services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            var origins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];

            // Explicit origins only. AllowAnyOrigin cannot be combined with credentials, and the
            // combination people reach for instead — reflecting the request origin — is the same as
            // having no policy at all.
            policy.WithOrigins(origins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials()
                  .WithExposedHeaders("api-supported-versions", "api-deprecated-versions", "Sunset");
        }));

        services.AddRequestTimeouts(options =>
        {
            options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
            {
                Timeout = TimeSpan.FromSeconds(30),
                TimeoutStatusCode = StatusCodes.Status504GatewayTimeout,
            };

            // Transfers are exempt: a large upload legitimately outlives a request that only touches
            // the database, and cancelling it mid-write is how orphaned objects are created.
            options.AddPolicy(GedPolicies.Content, TimeSpan.FromMinutes(10));
        });

        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });

        return services;
    }

    /// <summary>Adds the response headers that cost nothing and close whole classes of attack.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UseGedSecurityHeaders(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            // Stops a browser from re-interpreting a JSON response as HTML or script — the vector
            // that turns a stored filename into a cross-site scripting payload.
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";

            // An API returns data, never markup. Forbidding every source means a response that
            // somehow reaches a browser as a document can still execute nothing.
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

            await next();
        });

        return app;
    }

    private static string PartitionKeyFor(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true
            ? http.User.Identity.Name ?? "authenticated"
            : http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
