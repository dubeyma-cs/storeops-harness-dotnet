using System.Net;
using System.Net.Http.Json;
using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.TestSupport;

namespace StoreOps.Api.Tests.Activities;

/// <summary>Integration tests for the base activities REST surface.</summary>
public sealed class ActivitiesEndpointsTests : IClassFixture<StoreOpsApiFactory>
{
    private readonly StoreOpsApiFactory _factory;

    public ActivitiesEndpointsTests(StoreOpsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_a_valid_request_When_creating_an_activity_Then_it_starts_in_TODO_and_is_retrievable()
    {
        var client = _factory.AsStoreManager();

        var created = await client.CreateActivityAsync(title: "Weekly compliance walk");

        Assert.Equal(nameof(ActivityStatus.TODO), created.Status);
        Assert.Equal(InMemoryStaffRepository.SeedData.StoreId, created.StoreId);
        Assert.Equal(InMemoryStaffRepository.SeedData.StoreManager.Id, created.CreatedByStaffId);
        Assert.Null(created.CompletedAt);

        var fetched = await client.GetAsync($"/api/activities/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        var body = await fetched.ReadAsync<ActivityResponse>();
        Assert.Equal(created.Id, body.Id);
        Assert.Equal("Weekly compliance walk", body.Title);
    }

    [Fact]
    public async Task Given_a_due_date_in_the_past_When_creating_an_activity_Then_it_is_rejected_as_a_validation_error()
    {
        var client = _factory.AsStoreManager();

        var response = await client.PostAsJsonAsync(
            "/api/activities",
            new CreateActivityRequest
            {
                Title = "Backdated audit",
                Department = "GROCERY",
                DueAt = _factory.Clock.UtcNow.AddHours(-1),
            },
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.Validation, error.Error.Code);
        Assert.Contains("DueAt", error.Error.Details!.Keys);
    }

    [Fact]
    public async Task Given_an_assignee_who_is_not_store_staff_When_creating_an_activity_Then_it_is_rejected()
    {
        var client = _factory.AsStoreManager();

        var response = await client.PostAsJsonAsync(
            "/api/activities",
            new CreateActivityRequest
            {
                Title = "Assign to a stranger",
                Department = "GROCERY",
                AssigneeStaffId = "staff-does-not-exist",
            },
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.Validation, error.Error.Code);
        Assert.Contains("assigneeStaffId", error.Error.Details!.Keys);
    }

    [Fact]
    public async Task Given_a_title_below_the_minimum_length_When_creating_an_activity_Then_model_validation_fails()
    {
        var client = _factory.AsStoreManager();

        var response = await client.PostAsJsonAsync(
            "/api/activities",
            new CreateActivityRequest { Title = "no", Department = "GROCERY" },
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.Validation, error.Error.Code);
    }

    [Fact]
    public async Task Given_activities_in_several_states_When_listing_by_status_Then_only_matching_activities_return()
    {
        var client = _factory.AsStoreManager();

        var todo = await client.CreateActivityAsync(title: "Filter fixture: stays TODO");
        var blocked = await client.CreateActivityAsync(title: "Filter fixture: becomes BLOCKED");
        await client.SetStatusAsync(blocked.Id, ActivityStatus.BLOCKED, "Pallet jack unavailable");

        var response = await client.GetAsync($"/api/activities?status={nameof(ActivityStatus.BLOCKED)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await response.ReadAsync<List<ActivityResponse>>();

        Assert.Contains(results, a => a.Id == blocked.Id);
        Assert.DoesNotContain(results, a => a.Id == todo.Id);
        Assert.All(results, a => Assert.Equal(nameof(ActivityStatus.BLOCKED), a.Status));
    }

    [Fact]
    public async Task Given_a_programme_filter_When_listing_activities_Then_only_that_programmes_activities_return()
    {
        var client = _factory.AsStoreManager();

        var inProgramme = await client.CreateActivityAsync(
            title: "Programme-scoped activity", programmeId: "prg-filter-fixture");
        var unrelated = await client.CreateActivityAsync(title: "Unscoped activity");

        var results = await (await client.GetAsync("/api/activities?programmeId=prg-filter-fixture"))
            .ReadAsync<List<ActivityResponse>>();

        Assert.Contains(results, a => a.Id == inProgramme.Id);
        Assert.DoesNotContain(results, a => a.Id == unrelated.Id);
    }

    [Fact]
    public async Task Given_a_completed_activity_When_reopening_it_Then_the_lifecycle_rule_rejects_the_transition()
    {
        var client = _factory.AsStoreManager();

        var activity = await client.CreateActivityAsync(title: "Completed then reopened");
        await client.SetStatusAsync(activity.Id, ActivityStatus.DONE);

        var response = await client.PatchJsonAsync(
            $"/api/activities/{activity.Id}",
            new UpdateActivityRequest { Status = ActivityStatus.IN_PROGRESS });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.InvalidStateTransition, error.Error.Code);

        // And the stored activity is untouched, not partially updated.
        var reread = await (await client.GetAsync($"/api/activities/{activity.Id}"))
            .ReadAsync<ActivityResponse>();
        Assert.Equal(nameof(ActivityStatus.DONE), reread.Status);
    }

    [Fact]
    public async Task Given_a_block_request_without_a_reason_When_patching_Then_the_reason_rule_rejects_it()
    {
        var client = _factory.AsStoreManager();
        var activity = await client.CreateActivityAsync(title: "Blocked without explanation");

        var response = await client.PatchJsonAsync(
            $"/api/activities/{activity.Id}",
            new UpdateActivityRequest { Status = ActivityStatus.BLOCKED });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.Validation, error.Error.Code);
        Assert.Contains("reason", error.Error.Details!.Keys);
    }

    [Fact]
    public async Task Given_an_empty_patch_body_When_patching_Then_it_is_rejected_rather_than_silently_ignored()
    {
        var client = _factory.AsStoreManager();
        var activity = await client.CreateActivityAsync(title: "Empty patch");

        var response = await client.PatchJsonAsync(
            $"/api/activities/{activity.Id}",
            new UpdateActivityRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Given_an_activity_created_by_someone_else_When_an_associate_deletes_it_Then_it_is_forbidden()
    {
        var manager = _factory.AsStoreManager();
        var associate = _factory.AsAssociate();

        var activity = await manager.CreateActivityAsync(title: "Manager-owned activity");

        var response = await associate.DeleteAsync($"/api/activities/{activity.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.Forbidden, error.Error.Code);

        // The activity survives the rejected delete.
        Assert.Equal(
            HttpStatusCode.OK,
            (await manager.GetAsync($"/api/activities/{activity.Id}")).StatusCode);
    }

    [Fact]
    public async Task Given_an_activity_they_created_When_an_associate_deletes_it_Then_it_is_removed()
    {
        var associate = _factory.AsAssociate();

        var activity = await associate.CreateActivityAsync(title: "Associate-owned activity");

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await associate.DeleteAsync($"/api/activities/{activity.Id}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await associate.GetAsync($"/api/activities/{activity.Id}")).StatusCode);
    }

    [Fact]
    public async Task Given_an_activity_owned_by_an_associate_When_a_store_manager_deletes_it_Then_it_is_removed()
    {
        var associate = _factory.AsAssociate();
        var manager = _factory.AsStoreManager();

        var activity = await associate.CreateActivityAsync(title: "Removed by store management");

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await manager.DeleteAsync($"/api/activities/{activity.Id}")).StatusCode);
    }

    [Fact]
    public async Task Given_an_unknown_id_When_fetching_an_activity_Then_a_typed_not_found_error_is_returned()
    {
        var client = _factory.AsStoreManager();

        var response = await client.GetAsync("/api/activities/act-missing");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.NotFound, error.Error.Code);
        Assert.Contains("act-missing", error.Error.Message);
    }
}
