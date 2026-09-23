namespace StoreOps.Api.Shared.Errors;

/// <summary>
/// Base class for every error that StoreOps services and routes are allowed to throw.
/// </summary>
/// <remarks>
/// StoreOps architecture rule "Error contract": services and controllers must never throw
/// a raw <see cref="Exception"/>. Every failure path terminates in an <see cref="AppError"/>
/// subclass carrying a stable machine-readable <see cref="Code"/> and the HTTP
/// <see cref="StatusCode"/> the API surface should return. The mapping from error to
/// response body lives in one place only: <c>AppErrorHandlingMiddleware</c>.
/// </remarks>
public abstract class AppError : Exception
{
    protected AppError(string code, string message, int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    protected AppError(string code, string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>Stable, screaming-snake-case identifier clients may branch on.</summary>
    public string Code { get; }

    /// <summary>HTTP status code the API returns for this error.</summary>
    public int StatusCode { get; }

    /// <summary>Optional structured detail (e.g. per-field validation failures).</summary>
    public IReadOnlyDictionary<string, string[]>? Details { get; protected init; }
}
