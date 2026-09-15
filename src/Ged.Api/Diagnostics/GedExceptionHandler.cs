using Ged.Core.Ports;
using Ged.Features.Common;
using MicroKit.Domain.Exceptions;
using MicroKit.Persistence.Abstractions;
using Microsoft.AspNetCore.Diagnostics;

namespace Ged.Api.Diagnostics;

/// <summary>Turns domain and persistence failures into problem responses.</summary>
/// <param name="logger">Records what was translated.</param>
/// <remarks>
/// <para>
/// One handler rather than a try/catch in every endpoint. A broken business rule is raised by the
/// aggregate and travels up; catching it per route would mean restating every rule at every call
/// site, and the one that is forgotten returns a 500 with a stack trace.
/// </para>
/// <para>
/// Nothing unrecognised is translated. An unexpected exception falls through to the default handler,
/// which returns a bare 500 — an endpoint that guesses at a status for a failure it does not
/// understand tells the client something false.
/// </para>
/// </remarks>
internal sealed class GedExceptionHandler(ILogger<GedExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var result = exception switch
        {
            BusinessRuleViolationException rule => rule.ToResult(),
            DomainException domain => domain.ToResult(),
            ObjectNotFoundException => Results.Problem(
                title: "The stored content could not be read.",
                statusCode: StatusCodes.Status502BadGateway),
            PersistenceException persistence => Results.Problem(
                title: persistence.Message,
                statusCode: StatusCodes.Status409Conflict),
            _ => null,
        };

        if (result is null)
            return false;

        logger.LogWarning(
            exception,
            "Translated {Exception} on {Method} {Path}.",
            exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path);

        await result.ExecuteAsync(httpContext);

        return true;
    }
}
