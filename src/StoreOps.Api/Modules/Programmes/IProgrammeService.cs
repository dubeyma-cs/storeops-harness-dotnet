namespace StoreOps.Api.Modules.Programmes;

/// <summary>Business layer and public face of the programmes module.</summary>
public interface IProgrammeService
{
    Task<IReadOnlyList<Programme>> ListAsync(CancellationToken cancellationToken = default);

    Task<Programme> GetAsync(string programmeId, CancellationToken cancellationToken = default);

    Task<Programme> CreateAsync(CreateProgrammeRequest request, CancellationToken cancellationToken = default);

    Task<Programme> AddMemberAsync(
        string programmeId,
        AddProgrammeMemberRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes a programme. Raises <c>ProgrammeClosedEvent</c>; the reports module reacts by
    /// queueing a STORE_SUMMARY report, which this module knows nothing about.
    /// </summary>
    Task<Programme> CloseAsync(string programmeId, CancellationToken cancellationToken = default);

    /// <summary>Cross-module read-only lookup, used by the reports module.</summary>
    Task<IReadOnlyList<ProgrammeReadModel>> ListForStoreAsync(
        string storeId,
        CancellationToken cancellationToken = default);
}
