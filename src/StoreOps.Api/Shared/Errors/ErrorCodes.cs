namespace StoreOps.Api.Shared.Errors;

/// <summary>
/// Every error code StoreOps is allowed to emit. Codes are part of the public API contract:
/// clients branch on them, so they are additive-only and never renamed.
/// </summary>
public static class ErrorCodes
{
    public const string Validation = "VALIDATION_FAILED";
    public const string NotFound = "RESOURCE_NOT_FOUND";
    public const string Conflict = "RESOURCE_CONFLICT";
    public const string Forbidden = "OPERATION_FORBIDDEN";
    public const string Unauthorized = "UNAUTHENTICATED";
    public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
    public const string Unexpected = "INTERNAL_ERROR";
}
