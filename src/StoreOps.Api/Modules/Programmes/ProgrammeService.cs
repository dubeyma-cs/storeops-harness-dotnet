using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Auth;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Shared.Events;
using StoreOps.Api.Shared.Time;

namespace StoreOps.Api.Modules.Programmes;

/// <inheritdoc cref="IProgrammeService"/>
public sealed class ProgrammeService : IProgrammeService
{
    private readonly IProgrammeRepository _repository;
    private readonly IStaffService _staffService;
    private readonly IEventBus _eventBus;
    private readonly IStaffContextAccessor _staffContext;
    private readonly IClock _clock;

    public ProgrammeService(
        IProgrammeRepository repository,
        IStaffService staffService,
        IEventBus eventBus,
        IStaffContextAccessor staffContext,
        IClock clock)
    {
        _repository = repository;
        _staffService = staffService;
        _eventBus = eventBus;
        _staffContext = staffContext;
        _clock = clock;
    }

    private StaffContext Caller =>
        _staffContext.Current
        ?? throw new UnauthorizedError("No authenticated staff identity on this request.");

    public Task<IReadOnlyList<Programme>> ListAsync(CancellationToken cancellationToken = default) =>
        _repository.ListByStoreAsync(Caller.StoreId, cancellationToken);

    public async Task<Programme> GetAsync(string programmeId, CancellationToken cancellationToken = default)
    {
        var programme = await _repository.FindAsync(programmeId, Caller.StoreId, cancellationToken);

        if (programme is null)
        {
            throw new NotFoundError("Programme", programmeId);
        }

        return programme;
    }

    public async Task<Programme> CreateAsync(
        CreateProgrammeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = Caller;
        var now = _clock.UtcNow;

        if (request.StartsOn is { } start && request.EndsOn is { } end && end <= start)
        {
            throw ValidationError.ForField(nameof(request.EndsOn), "End date must be after the start date.");
        }

        var programme = new Programme
        {
            Id = $"prg-{Guid.NewGuid():N}"[..12],
            StoreId = caller.StoreId,
            RegionId = caller.RegionId,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Status = request.StartsOn is null || request.StartsOn <= now
                ? ProgrammeStatus.ACTIVE
                : ProgrammeStatus.PLANNED,
            OwnerStaffId = caller.StaffId,
            StartsOn = request.StartsOn,
            EndsOn = request.EndsOn,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _repository.AddAsync(programme, cancellationToken);
        return programme;
    }

    public async Task<Programme> AddMemberAsync(
        string programmeId,
        AddProgrammeMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = Caller;
        var programme = await GetAsync(programmeId, cancellationToken);

        if (programme.Status == ProgrammeStatus.CLOSED)
        {
            throw new ConflictError($"Programme '{programmeId}' is closed and cannot take new members.");
        }

        // Cross-module read-only lookup: staff is read-only for every other module.
        var staff = await _staffService.FindAsync(request.StaffId, cancellationToken);

        if (staff is null || !staff.IsActive || !string.Equals(staff.StoreId, caller.StoreId, StringComparison.Ordinal))
        {
            throw ValidationError.ForField(
                nameof(request.StaffId),
                $"Staff member '{request.StaffId}' is not active in store '{caller.StoreId}'.");
        }

        if (programme.Members.Any(m => string.Equals(m.StaffId, request.StaffId, StringComparison.Ordinal)))
        {
            throw new ConflictError($"Staff member '{request.StaffId}' is already on programme '{programmeId}'.");
        }

        var now = _clock.UtcNow;

        var updated = programme with
        {
            Members = programme.Members
                .Append(new ProgrammeMember(request.StaffId, request.Role, now, caller.StaffId))
                .ToList(),
            UpdatedAt = now,
        };

        await _repository.UpdateAsync(updated, cancellationToken);

        await _eventBus.PublishAsync(
            new ProgrammeMemberAddedEvent(
                updated.Id,
                updated.StoreId,
                request.StaffId,
                request.Role.ToString(),
                caller.StaffId),
            cancellationToken);

        return updated;
    }

    public async Task<Programme> CloseAsync(string programmeId, CancellationToken cancellationToken = default)
    {
        var caller = Caller;
        var programme = await GetAsync(programmeId, cancellationToken);

        if (programme.Status == ProgrammeStatus.CLOSED)
        {
            throw new ConflictError($"Programme '{programmeId}' is already closed.");
        }

        var isOwner = string.Equals(programme.OwnerStaffId, caller.StaffId, StringComparison.Ordinal);

        if (!isOwner && !caller.IsStoreManagement)
        {
            throw new ForbiddenError("Only the programme owner or a store manager may close a programme.");
        }

        var now = _clock.UtcNow;
        var closed = programme with { Status = ProgrammeStatus.CLOSED, ClosedAt = now, UpdatedAt = now };

        await _repository.UpdateAsync(closed, cancellationToken);

        await _eventBus.PublishAsync(
            new ProgrammeClosedEvent(closed.Id, closed.StoreId, closed.RegionId, caller.StaffId),
            cancellationToken);

        return closed;
    }

    public async Task<IReadOnlyList<ProgrammeReadModel>> ListForStoreAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        var programmes = await _repository.ListByStoreAsync(storeId, cancellationToken);

        return programmes
            .Select(p => new ProgrammeReadModel(
                p.Id,
                p.StoreId,
                p.RegionId,
                p.Name,
                p.Status.ToString(),
                p.Members.Count,
                p.ClosedAt))
            .ToList();
    }
}
