namespace StoreOps.Api.Modules.Staff;

/// <summary>
/// Data access for the staff module. Only <c>StaffService</c> may depend on this type —
/// other modules reach staff through <see cref="IStaffService"/>.
/// </summary>
public interface IStaffRepository
{
    Task<StaffMember?> FindByIdAsync(string staffId, CancellationToken cancellationToken = default);

    Task<StaffMember?> FindByTokenAsync(string token, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaffMember>> ListByStoreAsync(string storeId, CancellationToken cancellationToken = default);

    Task<StaffMember?> FindDepartmentLeadAsync(
        string storeId,
        string department,
        CancellationToken cancellationToken = default);
}
