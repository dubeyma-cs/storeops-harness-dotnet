namespace StoreOps.Api.Shared.Auth;

/// <summary>
/// Non-human identities used by background work.
/// </summary>
/// <remarks>
/// Background jobs still need a <see cref="StaffContext"/>, because store scoping and audit
/// attribution are not optional. Giving them an explicit, named identity keeps
/// "who changed this?" answerable in the audit trail instead of showing a blank actor.
/// </remarks>
public static class SystemIdentity
{
    /// <summary>Identity used by the activities module's SLA sweep.</summary>
    public static readonly StaffContext SlaSweep = new(
        StaffId: "system-sla-sweep",
        StoreId: "*",
        RegionId: "*",
        Role: "SYSTEM");
}
