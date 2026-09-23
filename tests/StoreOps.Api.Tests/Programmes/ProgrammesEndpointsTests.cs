using System.Net;
using System.Net.Http.Json;
using StoreOps.Api.Modules.Programmes;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.TestSupport;

namespace StoreOps.Api.Tests.Programmes;

/// <summary>Integration tests for the programmes REST surface and its membership rules.</summary>
public sealed class ProgrammesEndpointsTests : IClassFixture<StoreOpsApiFactory>
{
    private readonly StoreOpsApiFactory _factory;

    public ProgrammesEndpointsTests(StoreOpsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_a_valid_request_When_creating_a_programme_Then_the_caller_owns_it_and_it_is_listed()
    {
        var client = _factory.AsStoreManager();

        var created = await CreateProgrammeAsync(client, "Spring seasonal rollout");

        Assert.Equal(InMemoryStaffRepository.SeedData.StoreManager.Id, created.OwnerStaffId);
        Assert.Equal(InMemoryStaffRepository.SeedData.StoreId, created.StoreId);
        Assert.Equal(InMemoryStaffRepository.SeedData.RegionId, created.RegionId);
        Assert.Equal(nameof(ProgrammeStatus.ACTIVE), created.Status);
        Assert.Empty(created.Members);

        var listed = await (await client.GetAsync("/api/programmes")).ReadAsync<List<ProgrammeResponse>>();
        Assert.Contains(listed, p => p.Id == created.Id);
    }

    [Fact]
    public async Task Given_an_end_date_before_the_start_date_When_creating_a_programme_Then_it_is_rejected()
    {
        var client = _factory.AsStoreManager();
        var now = _factory.Clock.UtcNow;

        var response = await client.PostAsJsonAsync(
            "/api/programmes",
            new CreateProgrammeRequest
            {
                Name = "Impossible window",
                StartsOn = now.AddDays(5),
                EndsOn = now.AddDays(1),
            },
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.Validation, error.Error.Code);
        Assert.Contains("EndsOn", error.Error.Details!.Keys);
    }

    [Fact]
    public async Task Given_a_future_start_date_When_creating_a_programme_Then_it_is_PLANNED_rather_than_ACTIVE()
    {
        var client = _factory.AsStoreManager();

        var response = await client.PostAsJsonAsync(
            "/api/programmes",
            new CreateProgrammeRequest { Name = "Autumn refit", StartsOn = _factory.Clock.UtcNow.AddDays(30) },
            ApiClient.Json);

        var created = await response.ReadAsync<ProgrammeResponse>();

        Assert.Equal(nameof(ProgrammeStatus.PLANNED), created.Status);
    }

    [Fact]
    public async Task Given_active_store_staff_When_adding_them_to_a_programme_Then_the_membership_is_recorded()
    {
        var client = _factory.AsStoreManager();
        var programme = await CreateProgrammeAsync(client, "Compliance drive");

        var response = await client.PostAsJsonAsync(
            $"/api/programmes/{programme.Id}/members",
            new AddProgrammeMemberRequest
            {
                StaffId = InMemoryStaffRepository.SeedData.Associate.Id,
                Role = ProgrammeRole.ASSOCIATE,
            },
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var updated = await response.ReadAsync<ProgrammeResponse>();
        var member = Assert.Single(updated.Members);

        Assert.Equal(InMemoryStaffRepository.SeedData.Associate.Id, member.StaffId);
        Assert.Equal(nameof(ProgrammeRole.ASSOCIATE), member.Role);
        Assert.Equal(_factory.Clock.UtcNow, member.AddedAt);
    }

    [Fact]
    public async Task Given_a_staff_member_already_on_the_programme_When_adding_them_again_Then_it_conflicts()
    {
        var client = _factory.AsStoreManager();
        var programme = await CreateProgrammeAsync(client, "Duplicate membership");

        var request = new AddProgrammeMemberRequest
        {
            StaffId = InMemoryStaffRepository.SeedData.DepartmentLead.Id,
            Role = ProgrammeRole.DEPARTMENT_LEAD,
        };

        await client.PostAsJsonAsync($"/api/programmes/{programme.Id}/members", request, ApiClient.Json);
        var second = await client.PostAsJsonAsync(
            $"/api/programmes/{programme.Id}/members", request, ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var error = await second.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.Conflict, error.Error.Code);
    }

    [Fact]
    public async Task Given_someone_who_is_not_store_staff_When_adding_them_to_a_programme_Then_it_is_rejected()
    {
        var client = _factory.AsStoreManager();
        var programme = await CreateProgrammeAsync(client, "Unknown member");

        var response = await client.PostAsJsonAsync(
            $"/api/programmes/{programme.Id}/members",
            new AddProgrammeMemberRequest { StaffId = "staff-not-on-roster" },
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.Validation, error.Error.Code);
        Assert.Contains("StaffId", error.Error.Details!.Keys);
    }

    [Fact]
    public async Task Given_a_closed_programme_When_adding_a_member_Then_it_conflicts()
    {
        var client = _factory.AsStoreManager();
        var programme = await CreateProgrammeAsync(client, "Closed then amended");

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync($"/api/programmes/{programme.Id}/close", content: null)).StatusCode);

        var response = await client.PostAsJsonAsync(
            $"/api/programmes/{programme.Id}/members",
            new AddProgrammeMemberRequest { StaffId = InMemoryStaffRepository.SeedData.Associate.Id },
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Given_a_programme_owned_by_someone_else_When_an_associate_closes_it_Then_it_is_forbidden()
    {
        var manager = _factory.AsStoreManager();
        var associate = _factory.AsAssociate();

        var programme = await CreateProgrammeAsync(manager, "Manager-owned programme");

        var response = await associate.PostAsync($"/api/programmes/{programme.Id}/close", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.Forbidden, error.Error.Code);
    }

    [Fact]
    public async Task Given_an_already_closed_programme_When_closing_it_again_Then_it_conflicts()
    {
        var client = _factory.AsStoreManager();
        var programme = await CreateProgrammeAsync(client, "Closed twice");

        await client.PostAsync($"/api/programmes/{programme.Id}/close", content: null);
        var second = await client.PostAsync($"/api/programmes/{programme.Id}/close", content: null);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Given_an_unknown_id_When_fetching_a_programme_Then_a_typed_not_found_error_is_returned()
    {
        var client = _factory.AsStoreManager();

        var response = await client.GetAsync("/api/programmes/prg-missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.NotFound, error.Error.Code);
    }

    private static async Task<ProgrammeResponse> CreateProgrammeAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync(
            "/api/programmes",
            new CreateProgrammeRequest { Name = name },
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ProgrammeResponse>();
    }
}
