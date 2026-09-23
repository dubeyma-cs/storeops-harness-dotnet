namespace StoreOps.Api.Modules.Programmes;

/// <summary>
/// Data access for the programmes module. Only <c>ProgrammeService</c> may depend on it.
/// </summary>
public interface IProgrammeRepository
{
    Task<IReadOnlyList<Programme>> ListByStoreAsync(string storeId, CancellationToken cancellationToken = default);

    Task<Programme?> FindAsync(string programmeId, string storeId, CancellationToken cancellationToken = default);

    Task AddAsync(Programme programme, CancellationToken cancellationToken = default);

    Task UpdateAsync(Programme programme, CancellationToken cancellationToken = default);
}
