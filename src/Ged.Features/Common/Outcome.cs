namespace Ged.Features.Common;

/// <summary>How a use case ended, when failing is an expected outcome rather than a fault.</summary>
/// <typeparam name="T">The value produced on success.</typeparam>
/// <remarks>
/// <para>
/// Used only for outcomes the caller is meant to handle — a missing document, a folder that is not
/// empty. Broken invariants still travel as <c>BusinessRuleViolationException</c>: they are raised
/// by the domain, and converting every one of them into a return value at the handler boundary
/// would mean restating each rule twice.
/// </para>
/// <para>
/// Deliberately minimal and local. A result library would be a dependency in the layer that most
/// needs to stay free of them.
/// </para>
/// </remarks>
public readonly record struct Outcome<T>
{
    internal Outcome(T? value, string? errorCode, string? message)
    {
        Value = value;
        ErrorCode = errorCode;
        Message = message;
    }

    /// <summary>Gets the value produced, when the use case succeeded.</summary>
    public T? Value { get; }

    /// <summary>Gets the stable error code, when it did not.</summary>
    public string? ErrorCode { get; }

    /// <summary>Gets the human-readable reason, when it did not.</summary>
    public string? Message { get; }

    /// <summary>Gets a value indicating whether the use case succeeded.</summary>
    public bool Succeeded => ErrorCode is null;
}

/// <summary>Builds outcomes.</summary>
/// <remarks>
/// A separate, non-generic type so the factories can be called without restating the type argument
/// at every call site — and so the generic type itself carries no static members.
/// </remarks>
public static class Outcome
{
    /// <summary>Creates a successful outcome.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value produced.</param>
    /// <returns>The outcome.</returns>
    public static Outcome<T> Ok<T>(T value) => new(value, null, null);

    /// <summary>Creates a failed outcome.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="code">A stable code the API maps to a status.</param>
    /// <param name="message">The reason, for the caller to display.</param>
    /// <returns>The outcome.</returns>
    public static Outcome<T> Fail<T>(string code, string message) => new(default, code, message);

    /// <summary>The outcome for a resource that does not exist.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="what">What was looked for.</param>
    /// <returns>The outcome.</returns>
    public static Outcome<T> NotFound<T>(string what) =>
        Fail<T>("NOT_FOUND", $"{what} was not found.");
}
