using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Tests.TestSupport;

namespace StoreOps.Api.Tests.Shared;

/// <summary>
/// Tests the one error envelope every StoreOps response uses.
/// </summary>
/// <remarks>
/// The error contract is a client-facing promise, not an implementation detail: a client that
/// branches on <c>error.code</c> must never have to also handle ASP.NET's ProblemDetails shape.
/// </remarks>
public sealed class ErrorContractTests : IClassFixture<StoreOpsApiFactory>
{
    private readonly StoreOpsApiFactory _factory;

    public ErrorContractTests(StoreOpsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Given_no_credentials_When_calling_an_api_route_Then_a_typed_401_envelope_is_returned()
    {
        var response = await _factory.CreateClient().GetAsync("/api/activities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, ErrorCodes.Unauthorized, StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Given_an_unrecognised_token_When_calling_an_api_route_Then_a_typed_401_envelope_is_returned()
    {
        var client = _factory.WithToken("not-a-real-token");

        var response = await client.GetAsync("/api/activities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertEnvelopeAsync(response, ErrorCodes.Unauthorized, StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Given_a_non_bearer_authorization_header_When_calling_an_api_route_Then_it_is_rejected()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "abc");

        var response = await client.GetAsync("/api/activities");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_a_missing_resource_When_fetching_it_Then_the_envelope_carries_the_not_found_code()
    {
        var response = await _factory.AsStoreManager().GetAsync("/api/activities/act-nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEnvelopeAsync(response, ErrorCodes.NotFound, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Given_an_invalid_body_When_posting_Then_validation_details_are_included_per_field()
    {
        var response = await _factory.AsStoreManager().PostAsJsonAsync(
            "/api/activities",
            new CreateActivityRequest { Title = string.Empty, Department = string.Empty },
            ApiClient.Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await AssertEnvelopeAsync(response, ErrorCodes.Validation, StatusCodes.Status400BadRequest);

        Assert.NotNull(error.Error.Details);
        Assert.NotEmpty(error.Error.Details!);
    }

    [Fact]
    public async Task Given_a_health_probe_When_it_is_called_Then_no_authentication_is_required()
    {
        var response = await _factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Given_any_error_When_it_is_returned_Then_the_response_is_json_and_not_problem_details()
    {
        var response = await _factory.AsStoreManager().GetAsync("/api/activities/act-nope");

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"error\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"title\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"traceId\":null", body, StringComparison.Ordinal);
    }

    private static async Task<ErrorResponse> AssertEnvelopeAsync(
        HttpResponseMessage response,
        string expectedCode,
        int expectedStatus)
    {
        var error = await response.ReadAsync<ErrorResponse>();

        Assert.Equal(expectedCode, error.Error.Code);
        Assert.Equal(expectedStatus, error.Error.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(error.Error.Message));
        Assert.False(string.IsNullOrWhiteSpace(error.Error.TraceId));

        return error;
    }
}
