using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Ged.Features.Common;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authorization;

namespace Ged.Api.Composition;

/// <summary>Authentication, authorization, throttling and transport hardening.</summary>
internal static class SecurityExtensions
{
    /// <summary>Registers the security services.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="requireAuthentication">The requirement for authentication.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGedSecurity(
        this IServiceCollection services, IConfiguration configuration, bool requireAuthentication = true)
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
        {
            // Deny by default in every environment that has an identity provider. Left off locally
            // because there is none — and a developer who cannot call the API will disable
            // authorization somewhere far less visible than this line.
            if (requireAuthentication)
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            }
        });

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

    private static string PartitionKeyFor(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true
            ? http.User.Identity.Name ?? "authenticated"
            : http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
