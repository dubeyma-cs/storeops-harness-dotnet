namespace StoreOps.Api.Modules.Staff;

/// <summary>
/// In-memory staff directory seeded with a fixed roster.
/// </summary>
/// <remarks>
/// The capstone reference build uses no database. The roster and its tokens are fixed so that
/// harness demonstration runs, integration tests, and curl walkthroughs all address the same
/// identities.
/// </remarks>
public sealed class InMemoryStaffRepository : IStaffRepository
{
    private readonly Dictionary<string, StaffMember> _staff;
    private readonly Dictionary<string, AuthToken> _tokens;

    public InMemoryStaffRepository()
    {
        _staff = SeedData.Staff.ToDictionary(s => s.Id, StringComparer.Ordinal);
        _tokens = SeedData.Tokens.ToDictionary(t => t.Value, StringComparer.Ordinal);
    }

    public Task<StaffMember?> FindByIdAsync(string staffId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_staff.TryGetValue(staffId, out var staff) ? staff : null);

    public Task<StaffMember?> FindByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (!_tokens.TryGetValue(token, out var authToken))
        {
            return Task.FromResult<StaffMember?>(null);
        }

        return Task.FromResult(_staff.TryGetValue(authToken.StaffId, out var staff) ? staff : null);
    }

    public Task<IReadOnlyList<StaffMember>> ListByStoreAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StaffMember> result = _staff.Values
            .Where(s => string.Equals(s.StoreId, storeId, StringComparison.Ordinal))
            .OrderBy(s => s.DisplayName, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<StaffMember?> FindDepartmentLeadAsync(
        string storeId,
        string department,
        CancellationToken cancellationToken = default)
    {
        var lead = _staff.Values.FirstOrDefault(s =>
            s.IsActive
            && s.Role == StaffRole.DEPARTMENT_LEAD
            && string.Equals(s.StoreId, storeId, StringComparison.Ordinal)
            && string.Equals(s.Department, department, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(lead);
    }

    /// <summary>Fixed roster used by every environment of the reference build.</summary>
    public static class SeedData
    {
        public const string StoreId = "store-042";
        public const string RegionId = "region-north";

        public const string StoreManagerToken = "dev-token-store-manager";
        public const string DepartmentLeadToken = "dev-token-department-lead";
        public const string AssociateToken = "dev-token-associate";

        public static readonly StaffMember StoreManager = new(
            Id: "staff-001",
            Email: "priya.raman@storeops.example",
            DisplayName: "Priya Raman",
            StoreId: StoreId,
            RegionId: RegionId,
            Department: "OPERATIONS",
            Role: StaffRole.STORE_MANAGER,
            IsActive: true);

        public static readonly StaffMember DepartmentLead = new(
            Id: "staff-002",
            Email: "sam.okafor@storeops.example",
            DisplayName: "Sam Okafor",
            StoreId: StoreId,
            RegionId: RegionId,
            Department: "GROCERY",
            Role: StaffRole.DEPARTMENT_LEAD,
            IsActive: true);

        public static readonly StaffMember Associate = new(
            Id: "staff-003",
            Email: "lee.chen@storeops.example",
            DisplayName: "Lee Chen",
            StoreId: StoreId,
            RegionId: RegionId,
            Department: "GROCERY",
            Role: StaffRole.ASSOCIATE,
            IsActive: true);

        public static readonly IReadOnlyList<StaffMember> Staff = new[] { StoreManager, DepartmentLead, Associate };

        public static readonly IReadOnlyList<AuthToken> Tokens = new[]
        {
            new AuthToken(StoreManagerToken, StoreManager.Id, DateTimeOffset.MaxValue),
            new AuthToken(DepartmentLeadToken, DepartmentLead.Id, DateTimeOffset.MaxValue),
            new AuthToken(AssociateToken, Associate.Id, DateTimeOffset.MaxValue),
        };
    }
}
