using System.Collections.Concurrent;

namespace StoreOps.Api.Modules.Programmes;

/// <summary>In-memory programme store, registered as a singleton.</summary>
public sealed class InMemoryProgrammeRepository : IProgrammeRepository
{
    private readonly ConcurrentDictionary<string, Programme> _programmes = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<Programme>> ListByStoreAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Programme> result = _programmes.Values
            .Where(p => string.Equals(p.StoreId, storeId, StringComparison.Ordinal))
            .OrderBy(p => p.CreatedAt)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<Programme?> FindAsync(
        string programmeId,
        string storeId,
        CancellationToken cancellationToken = default)
    {
        if (!_programmes.TryGetValue(programmeId, out var programme))
        {
            return Task.FromResult<Programme?>(null);
        }

        return Task.FromResult(
            string.Equals(programme.StoreId, storeId, StringComparison.Ordinal) ? programme : null);
    }

    public Task AddAsync(Programme programme, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(programme);
        _programmes[programme.Id] = programme;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Programme programme, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(programme);
        _programmes[programme.Id] = programme;
        return Task.CompletedTask;
    }
}
