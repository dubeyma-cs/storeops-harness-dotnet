namespace StoreOps.Api.Modules.Staff;

/// <summary>
/// The staff module's public face. This is the <em>only</em> type other modules may use to
/// reach staff data, and every member on it is a read-only lookup by design.
/// </summary>
/// <remarks>
/// StoreOps architecture rule "staff is read-only for other modules": there is no
/// <c>UpdateAsync</c> or <c>DeleteAsync</c> here, so no amount of Generator creativity can
/// produce a programmes-writes-to-staff violation. The constraint is expressed in the type
/// system rather than only in a skill file.
/// </remarks>
public interface IStaffService
{
    /// <summary>Resolves a bearer token to a staff identity, or <c>null</c> if unknown/inactive.</summary>
    Task<StaffMember?> AuthenticateAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Gets a staff member by id. Throws <c>NotFoundError</c> when absent.</summary>
    Task<StaffMember> GetAsync(string staffId, CancellationToken cancellationToken = default);

    /// <summary>Gets a staff member by id, or <c>null</c> when absent.</summary>
    Task<StaffMember?> FindAsync(string staffId, CancellationToken cancellationToken = default);

    /// <summary>Lists the active roster for a store.</summary>
    Task<IReadOnlyList<StaffMember>> ListForStoreAsync(
        string storeId,
        CancellationToken cancellationToken = default);

    /// <summary>Finds the department lead who owns escalations for a department.</summary>
    Task<StaffMember?> FindDepartmentLeadAsync(
        string storeId,
        string department,
        CancellationToken cancellationToken = default);
}
