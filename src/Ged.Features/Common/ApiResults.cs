using MicroKit.Domain.Rules;

namespace Ged.Features.Common;

/// <summary>Turns use-case outcomes and domain failures into HTTP responses.</summary>
/// <remarks>
/// <para>
/// One place decides the mapping, so a rule added to the domain surfaces with a consistent status
/// and shape rather than whatever the nearest endpoint happened to return.
/// </para>
/// <para>
/// Business rules are matched on their <em>type</em>, never on their message. That is what makes
/// rules-as-types pay off: the mapping is checked by the compiler and a reworded message breaks
/// nothing.
/// </para>
/// </remarks>
public static class ApiResults
{
    /// <summary>Maps an outcome to a response.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="outcome">The outcome.</param>
    /// <param name="onSuccess">Builds the response for a successful outcome.</param>
    /// <returns>The response.</returns>
    public static IResult ToResult<T>(this Outcome<T> outcome, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (outcome.Succeeded)
            return onSuccess(outcome.Value!);

        return outcome.ErrorCode switch
        {
            "NOT_FOUND" => Results.Problem(
                title: outcome.Message, statusCode: StatusCodes.Status404NotFound,
                extensions: Extensions(outcome.ErrorCode)),
            // 415 rather than 400: the request is well formed, the payload's media type is what
            // this endpoint will not accept — which is exactly what the status means.
            "UNSUPPORTED_MEDIA_TYPE" => Results.Problem(
                title: outcome.Message, statusCode: StatusCodes.Status415UnsupportedMediaType,
                extensions: Extensions(outcome.ErrorCode)),
            "CONFLICT" => Results.Problem(
                title: outcome.Message, statusCode: StatusCodes.Status409Conflict,
                extensions: Extensions(outcome.ErrorCode)),
            _ => Results.Problem(
                title: outcome.Message, statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: Extensions(outcome.ErrorCode)),
        };
    }

    /// <summary>Maps a broken business rule to a response.</summary>
    /// <param name="exception">The violation raised by the domain.</param>
    /// <returns>The response.</returns>
    public static IResult ToResult(this BusinessRuleViolationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var rule = exception.ViolatedRule;
        var code = rule.GetType().Name.Replace("Rule", string.Empty, StringComparison.Ordinal);

        return Results.Problem(
            title: rule.Message,
            statusCode: StatusCodes.Status409Conflict,
            extensions: Extensions(code));
    }

    /// <summary>Maps a rejected value object to a response.</summary>
    /// <param name="exception">The validation failure raised by the domain.</param>
    /// <returns>The response.</returns>
    public static IResult ToResult(this DomainException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return Results.Problem(
            title: exception.Message,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: Extensions("INVALID_INPUT"));
    }

    private static Dictionary<string, object?> Extensions(string? code) =>
        new(StringComparer.Ordinal) { ["code"] = code };
}
