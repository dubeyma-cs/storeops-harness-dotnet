namespace StoreOps.Api.Modules.Staff;

/*
 * Naming note (see .harness/skills/app-context/SKILL.md):
 * the specification's `User` entity is modelled here as `StaffMember` and `UserProfile` as
 * `StaffProfile`, because "user" carries no meaning on a shop floor and because the module is
 * named `staff`. Roles keep the specification's exact values.
 */

/// <summary>Store staff roles, ordered from widest to narrowest scope.</summary>
public enum StaffRole
{
    ASSOCIATE = 0,
    DEPARTMENT_LEAD = 1,
    STORE_MANAGER = 2,
    REGIONAL_MANAGER = 3,
}

/// <summary>A registered member of store staff.</summary>
public sealed record StaffMember(
    string Id,
    string Email,
    string DisplayName,
    string StoreId,
    string RegionId,
    string Department,
    StaffRole Role,
    bool IsActive)
{
    public StaffProfile Profile { get; init; } = StaffProfile.Empty;
}

/// <summary>Non-identity attributes of a staff member.</summary>
public sealed record StaffProfile(string? ShiftPattern, string? PhoneExtension)
{
    public static readonly StaffProfile Empty = new(null, null);
}

/// <summary>An opaque bearer token bound to a staff member.</summary>
public sealed record AuthToken(string Value, string StaffId, DateTimeOffset ExpiresAt);
