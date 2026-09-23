using StoreOps.Api.Shared.Errors;

namespace StoreOps.Api.Modules.Staff;

/// <inheritdoc cref="IStaffService"/>
public sealed class StaffService : IStaffService
{
    private readonly IStaffRepository _repository;

    public StaffService(IStaffRepository repository)
    {
        _repository = repository;
    }

    public async Task<StaffMember?> AuthenticateAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var staff = await _repository.FindByTokenAsync(token, cancellationToken);
        return staff is { IsActive: true } ? staff : null;
    }

    public async Task<StaffMember> GetAsync(string staffId, CancellationToken cancellationToken = default)
    {
        var staff = await _repository.FindByIdAsync(staffId, cancellationToken);

        if (staff is null)
        {
            throw new NotFoundError("Staff member", staffId);
        }

        return staff;
    }

    public Task<StaffMember?> FindAsync(string staffId, CancellationToken cancellationToken = default) =>
        _repository.FindByIdAsync(staffId, cancellationToken);

    public async Task<IReadOnlyList<StaffMember>> ListForStoreAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        var roster = await _repository.ListByStoreAsync(storeId, cancellationToken);
        return roster.Where(s => s.IsActive).ToList();
    }

    public Task<StaffMember?> FindDepartmentLeadAsync(
        string storeId,
        string department,
        CancellationToken cancellationToken = default) =>
        _repository.FindDepartmentLeadAsync(storeId, department, cancellationToken);
}
