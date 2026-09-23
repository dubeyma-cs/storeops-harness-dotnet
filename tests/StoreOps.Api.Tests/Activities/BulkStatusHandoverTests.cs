using System.Net;
using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.TestSupport;

namespace StoreOps.Api.Tests.Activities;

/// <summary>
/// Acceptance tests for the shift-handover bulk status endpoint — the feature produced by the
/// harness demonstration run (see <c>PROMPT.md</c> and <c>.harness/reviews/</c>).
/// </summary>
/// <remarks>
/// Each test name maps to a GIVEN/WHEN/THEN acceptance criterion in
/// <c>.harness/reviews/sprint-1-contract.md</c>, which is how the Evaluator traces AC coverage.
/// </remarks>
public sealed class BulkStatusHandoverTests : IClassFixture<StoreOpsApiFactory>
{
    private const string Endpoint = "/api/activities/bulk-status";

    private readonly StoreOpsApiFactory _factory;

    public BulkStatusHandoverTests(StoreOpsApiFactory factory)
    {
        _factory = factory;
    }

    // AC-1
    [Fact]
    public async Task Given_open_activities_When_all_items_are_valid_Then_200_and_every_item_is_applied()
    {
        var client = _factory.AsDepartmentLead();

        var first = await client.CreateActivityAsync(title: "Handover: restock bay 4");
        var second = await client.CreateActivityAsync(title: "Handover: reset gondola");

        var response = await client.PatchJsonAsync(
            Endpoint,
            new BulkStatusUpdateRequest
            {
                Items =
                {
                    new BulkStatusItem { ActivityId = first.Id, Status = ActivityStatus.DONE },
                    new BulkStatusItem
                    {
                        ActivityId = second.Id,
                        Status = ActivityStatus.BLOCKED,
                        Reason = "Awaiting stock from the regional depot",
                    },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.ReadAsync<BulkStatusUpdateResponse>();
        Assert.Equal(2, body.Requested);
        Assert.Equal(2, body.Updated);
        Assert.Equal(0, body.Failed);
        Assert.All(body.Results, r => Assert.Equal(BulkStatusItemResult.OutcomeUpdated, r.Outcome));

        var firstAfter = await (await client.GetAsync($"/api/activities/{first.Id}"))
            .ReadAsync<ActivityResponse>();
        var secondAfter = await (await client.GetAsync($"/api/activities/{second.Id}"))
            .ReadAsync<ActivityResponse>();

        Assert.Equal(nameof(ActivityStatus.DONE), firstAfter.Status);
        Assert.NotNull(firstAfter.CompletedAt);
        Assert.Equal(nameof(ActivityStatus.BLOCKED), secondAfter.Status);
        Assert.Null(secondAfter.CompletedAt);
    }

    // AC-2
    [Fact]
    public async Task Given_a_mix_of_valid_and_invalid_items_When_handing_over_Then_207_and_valid_items_still_apply()
    {
        var client = _factory.AsDepartmentLead();

        var valid = await client.CreateActivityAsync(title: "Handover: partial success survivor");
        var alreadyDone = await client.CreateActivityAsync(title: "Handover: already completed");
        await client.SetStatusAsync(alreadyDone.Id, ActivityStatus.DONE);

        var response = await client.PatchJsonAsync(
            Endpoint,
            new BulkStatusUpdateRequest
            {
                Items =
                {
                    new BulkStatusItem { ActivityId = valid.Id, Status = ActivityStatus.DONE },
                    new BulkStatusItem { ActivityId = alreadyDone.Id, Status = ActivityStatus.DONE },
                    new BulkStatusItem { ActivityId = "act-not-real", Status = ActivityStatus.DONE },
                },
            });

        Assert.Equal(HttpStatusCode.MultiStatus, response.StatusCode);

        var body = await response.ReadAsync<BulkStatusUpdateResponse>();
        Assert.Equal(3, body.Requested);
        Assert.Equal(1, body.Updated);
        Assert.Equal(2, body.Failed);

        var byId = body.Results.ToDictionary(r => r.ActivityId, StringComparer.Ordinal);
        Assert.Equal(BulkStatusItemResult.OutcomeUpdated, byId[valid.Id].Outcome);
        Assert.Equal(ErrorCodes.InvalidStateTransition, byId[alreadyDone.Id].ErrorCode);
        Assert.Equal(ErrorCodes.NotFound, byId["act-not-real"].ErrorCode);

        // The one good item was committed — a partial failure is not a rollback.
        var validAfter = await (await client.GetAsync($"/api/activities/{valid.Id}"))
            .ReadAsync<ActivityResponse>();
        Assert.Equal(nameof(ActivityStatus.DONE), validAfter.Status);
    }

    // AC-3
    [Fact]
    public async Task Given_every_item_is_invalid_When_handing_over_Then_409_and_nothing_changes()
    {
        var client = _factory.AsDepartmentLead();

        var completed = await client.CreateActivityAsync(title: "Handover: all fail fixture");
        await client.SetStatusAsync(completed.Id, ActivityStatus.DONE);

        var response = await client.PatchJsonAsync(
            Endpoint,
            new BulkStatusUpdateRequest
            {
                Items =
                {
                    new BulkStatusItem { ActivityId = completed.Id, Status = ActivityStatus.DONE },
                    new BulkStatusItem { ActivityId = "act-also-not-real", Status = ActivityStatus.BLOCKED },
                },
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.ReadAsync<BulkStatusUpdateResponse>();
        Assert.Equal(0, body.Updated);
        Assert.Equal(2, body.Failed);
    }

    // AC-4
    [Fact]
    public async Task Given_an_updated_activity_When_the_handover_completes_Then_one_audit_entry_records_it()
    {
        var client = _factory.AsDepartmentLead();
        var activity = await client.CreateActivityAsync(title: "Handover: audited");

        await client.PatchJsonAsync(
            Endpoint,
            new BulkStatusUpdateRequest
            {
                Items =
                {
                    new BulkStatusItem
                    {
                        ActivityId = activity.Id,
                        Status = ActivityStatus.BLOCKED,
                        Reason = "Chiller fault raised with maintenance",
                    },
                },
            });

        var audit = await (await client.GetAsync($"/api/activities/{activity.Id}/audit"))
            .ReadAsync<List<ActivityAuditEntry>>();

        var handoverEntries = audit.Where(e => e.Source == "api.bulk-status").ToList();

        var entry = Assert.Single(handoverEntries);
        Assert.Equal("STATUS_CHANGED", entry.Action);
        Assert.Equal(nameof(ActivityStatus.TODO), entry.FromStatus);
        Assert.Equal(nameof(ActivityStatus.BLOCKED), entry.ToStatus);
        Assert.Equal("Chiller fault raised with maintenance", entry.Reason);
        Assert.Equal(InMemoryStaffRepository.SeedData.DepartmentLead.Id, entry.ActorStaffId);
        Assert.Equal(_factory.Clock.UtcNow, entry.RecordedAt);
    }

    // AC-5
    [Fact]
    public async Task Given_a_failed_item_When_the_handover_completes_Then_no_audit_entry_is_written_for_it()
    {
        var client = _factory.AsDepartmentLead();

        var activity = await client.CreateActivityAsync(title: "Handover: failed item leaves no audit");
        await client.SetStatusAsync(activity.Id, ActivityStatus.DONE);

        var auditBefore = await (await client.GetAsync($"/api/activities/{activity.Id}/audit"))
            .ReadAsync<List<ActivityAuditEntry>>();

        await client.PatchJsonAsync(
            Endpoint,
            new BulkStatusUpdateRequest
            {
                Items = { new BulkStatusItem { ActivityId = activity.Id, Status = ActivityStatus.DONE } },
            });

        var auditAfter = await (await client.GetAsync($"/api/activities/{activity.Id}/audit"))
            .ReadAsync<List<ActivityAuditEntry>>();

        Assert.Equal(auditBefore.Count, auditAfter.Count);
        Assert.DoesNotContain(auditAfter, e => e.Source == "api.bulk-status");
    }

    // AC-6
    [Fact]
    public async Task Given_a_status_outside_the_handover_set_When_handing_over_Then_that_item_fails_validation()
    {
        var client = _factory.AsDepartmentLead();
        var activity = await client.CreateActivityAsync(title: "Handover: illegal target status");

        var response = await client.PatchJsonAsync(
            Endpoint,
            new BulkStatusUpdateRequest
            {
                Items = { new BulkStatusItem { ActivityId = activity.Id, Status = ActivityStatus.IN_PROGRESS } },
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.ReadAsync<BulkStatusUpdateResponse>();
        var result = Assert.Single(body.Results);
        Assert.Equal(ErrorCodes.Validation, result.ErrorCode);
        Assert.Contains("DONE", result.Message);

        var after = await (await client.GetAsync($"/api/activities/{activity.Id}"))
            .ReadAsync<ActivityResponse>();
        Assert.Equal(nameof(ActivityStatus.TODO), after.Status);
    }

    // AC-7
    [Fact]
    public async Task Given_a_BLOCKED_item_without_a_reason_When_handing_over_Then_that_item_fails()
    {
        var client = _factory.AsDepartmentLead();
        var activity = await client.CreateActivityAsync(title: "Handover: blocked without reason");

        var response = await client.PatchJsonAsync(
            Endpoint,
            new BulkStatusUpdateRequest
            {
                Items = { new BulkStatusItem { ActivityId = activity.Id, Status = ActivityStatus.BLOCKED } },
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.ReadAsync<BulkStatusUpdateResponse>();
        var result = Assert.Single(body.Results);
        Assert.Equal(ErrorCodes.Validation, result.ErrorCode);
        Assert.Contains("reason", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // AC-8
    [Fact]
    public async Task Given_duplicate_activity_ids_When_handing_over_Then_the_whole_request_is_rejected()
    {
        var client = _factory.AsDepartmentLead();
        var activity = await client.CreateActivityAsync(title: "Handover: duplicate ids");

        var response = await client.PatchJsonAsync(
            Endpoint,
            new BulkStatusUpdateRequest
            {
                Items =
                {
                    new BulkStatusItem { ActivityId = activity.Id, Status = ActivityStatus.DONE },
                    new BulkStatusItem { ActivityId = activity.Id, Status = ActivityStatus.BLOCKED, Reason = "x" },
                },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.Validation, error.Error.Code);

        // Ambiguous input is refused outright rather than resolved by ordering luck.
        var after = await (await client.GetAsync($"/api/activities/{activity.Id}"))
            .ReadAsync<ActivityResponse>();
        Assert.Equal(nameof(ActivityStatus.TODO), after.Status);
    }

    // AC-9
    [Fact]
    public async Task Given_an_empty_item_list_When_handing_over_Then_the_request_is_rejected()
    {
        var client = _factory.AsDepartmentLead();

        var response = await client.PatchJsonAsync(Endpoint, new BulkStatusUpdateRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // AC-10
    [Fact]
    public async Task Given_more_items_than_the_cap_When_handing_over_Then_the_request_is_rejected()
    {
        var client = _factory.AsDepartmentLead();

        var request = new BulkStatusUpdateRequest();

        for (var i = 0; i < BulkStatusUpdateRequest.MaxItems + 1; i++)
        {
            request.Items.Add(new BulkStatusItem { ActivityId = $"act-cap-{i}", Status = ActivityStatus.DONE });
        }

        var response = await client.PatchJsonAsync(Endpoint, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // AC-11
    [Fact]
    public async Task Given_no_bearer_token_When_handing_over_Then_the_request_is_unauthenticated()
    {
        var client = _factory.CreateClient();

        var response = await client.PatchJsonAsync(
            Endpoint,
            new BulkStatusUpdateRequest
            {
                Items = { new BulkStatusItem { ActivityId = "act-any", Status = ActivityStatus.DONE } },
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var error = await response.ReadAsync<ErrorResponse>();
        Assert.Equal(ErrorCodes.Unauthorized, error.Error.Code);
    }

    // AC-12
    [Fact]
    public async Task Given_a_handover_request_When_items_are_applied_Then_the_route_is_not_bound_as_an_activity_id()
    {
        var client = _factory.AsDepartmentLead();

        // Proves `bulk-status` reaches the bulk action rather than PATCH /api/activities/{id}.
        var response = await client.PatchJsonAsync(
            Endpoint,
            new BulkStatusUpdateRequest
            {
                Items = { new BulkStatusItem { ActivityId = "act-unknown-but-well-formed", Status = ActivityStatus.DONE } },
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.ReadAsync<BulkStatusUpdateResponse>();
        Assert.Equal(1, body.Requested);
    }
}
