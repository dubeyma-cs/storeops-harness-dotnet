using System.Net;
using System.Net.Http.Json;
using StoreOps.Api.Modules.Activities;

namespace StoreOps.Api.Tests.TestSupport;

/// <summary>Creates activities through the real API so fixtures cannot drift from the contract.</summary>
public static class ActivityFixtures
{
    public static async Task<ActivityResponse> CreateActivityAsync(
        this HttpClient client,
        string title = "Reset end-cap planogram",
        string department = "GROCERY",
        ActivityPriority priority = ActivityPriority.MEDIUM,
        ActivityCategory category = ActivityCategory.PLANOGRAM,
        string? assigneeStaffId = null,
        string? programmeId = null,
        DateTimeOffset? dueAt = null)
    {
        var response = await client.PostAsJsonAsync(
            "/api/activities",
            new CreateActivityRequest
            {
                Title = title,
                Department = department,
                Priority = priority,
                Category = category,
                AssigneeStaffId = assigneeStaffId,
                ProgrammeId = programmeId,
                DueAt = dueAt,
            },
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ActivityResponse>();
    }

    /// <summary>Moves an activity to a status through the single-activity PATCH route.</summary>
    public static async Task<ActivityResponse> SetStatusAsync(
        this HttpClient client,
        string activityId,
        ActivityStatus status,
        string? reason = null)
    {
        var response = await client.PatchJsonAsync(
            $"/api/activities/{activityId}",
            new UpdateActivityRequest { Status = status, Reason = reason });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsync<ActivityResponse>();
    }
}
