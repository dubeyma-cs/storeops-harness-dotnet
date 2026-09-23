using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StoreOps.Api.Modules.Staff;

namespace StoreOps.Api.Tests.TestSupport;

/// <summary>Helpers for talking to the API as a specific seeded staff member.</summary>
public static class ApiClient
{
    /// <summary>JSON options matching the API's own (camelCase, enums as strings).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static HttpClient AsStoreManager(this StoreOpsApiFactory factory) =>
        factory.WithToken(InMemoryStaffRepository.SeedData.StoreManagerToken);

    public static HttpClient AsDepartmentLead(this StoreOpsApiFactory factory) =>
        factory.WithToken(InMemoryStaffRepository.SeedData.DepartmentLeadToken);

    public static HttpClient AsAssociate(this StoreOpsApiFactory factory) =>
        factory.WithToken(InMemoryStaffRepository.SeedData.AssociateToken);

    public static HttpClient WithToken(this StoreOpsApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Reads a JSON response body, failing the test with the raw body when it will not bind.</summary>
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        try
        {
            return JsonSerializer.Deserialize<T>(body, Json)
                   ?? throw new InvalidOperationException($"Response body deserialised to null: {body}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Could not read {typeof(T).Name} from response ({(int)response.StatusCode}): {body}", ex);
        }
    }

    /// <summary>PATCH with a JSON body.</summary>
    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PatchAsync(url, JsonContent.Create(body, options: Json));
}
