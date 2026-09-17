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
internal sealed partial class GedExceptionHandler(ILogger<GedExceptionHandler> logger) : IExceptionHandler
{
    // Source-generated rather than a LogWarning call. The generator emits a cached delegate and a
    // strongly-typed signature, so nothing is boxed into an object[] and nothing is formatted when
    // the level is disabled. CA1848 asks for this; here it also removes the risk of the message
    // template and its arguments drifting apart, which a params array cannot catch.
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Warning,
        Message = "Translated {ExceptionType} on {Method} {Path}.")]
    private static partial void LogTranslatedFailure(
        ILogger logger,
        Exception exception,
        string exceptionType,
        string method,
        string path);

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

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

        LogTranslatedFailure(
            logger,
            exception,
            exception.GetType().Name,
            httpContext.Request.Method,
            httpContext.Request.Path.Value ?? "/");

        await result.ExecuteAsync(httpContext);

        return true;
    }
}
