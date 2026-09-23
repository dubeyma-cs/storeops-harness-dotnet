namespace StoreOps.Api.Shared.Auth;

/// <summary>
/// The authenticated staff identity for the current request, resolved once per request.
/// </summary>
/// <remarks>
/// Every module-owned query is scoped by <see cref="StoreId"/>: "list programmes for the
/// authenticated store" is a repository-level filter, not something a caller can opt out of.
/// </remarks>
public sealed record StaffContext(
    string StaffId,
    string StoreId,
    string RegionId,
    string Role)
{
    /// <summary>Roles that may delete activities they do not own, and close programmes.</summary>
    public static readonly string[] StoreManagementRoles = { "REGIONAL_MANAGER", "STORE_MANAGER" };

    public bool IsStoreManagement => StoreManagementRoles.Contains(Role, StringComparer.Ordinal);
}

/// <summary>Resolves the <see cref="StaffContext"/> for the request being handled.</summary>
public interface IStaffContextAccessor
{
    /// <summary>The current staff identity, or <c>null</c> when the request is unauthenticated.</summary>
    StaffContext? Current { get; }

    /// <summary>Sets the identity for the current request. Called only by the auth middleware.</summary>
    void Set(StaffContext context);
}
