using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using StoreOps.Api.Modules.Activities;
using StoreOps.Api.Modules.Alerts;
using StoreOps.Api.Modules.Programmes;
using StoreOps.Api.Modules.Reports;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Auth;
using StoreOps.Api.Shared.Errors;
using StoreOps.Api.Shared.Events;
using StoreOps.Api.Shared.Time;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------------------
// Shared kernel
// ---------------------------------------------------------------------------------------
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
builder.Services.AddScoped<IStaffContextAccessor, StaffContextAccessor>();

// ---------------------------------------------------------------------------------------
// Domain modules. Each module owns its own registrations; Program.cs never reaches inside one.
// ---------------------------------------------------------------------------------------
builder.Services.AddStaffModule();
builder.Services.AddActivitiesModule(builder.Configuration);
builder.Services.AddProgrammesModule();
builder.Services.AddAlertsModule(builder.Configuration);
builder.Services.AddReportsModule();

builder.Services
    // Keep the "Async" suffix on action names so CreatedAtAction(nameof(GetAsync)) and other
    // nameof-based route references resolve. MVC strips it by default, which breaks those links.
    .AddControllers(options => options.SuppressAsyncSuffixInActionNames = false)
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// Model-binding failures are translated into the StoreOps error envelope rather than the
// framework's ProblemDetails, so clients only ever parse one error shape.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var details = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray(),
                StringComparer.Ordinal);

        throw new ValidationError("The request body failed validation.", details);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // The API authenticates via 'Authorization: Bearer <seed-token>'. Without this definition the
    // Swagger UI has no Authorize button, so every "Try it out" against /api/* returns 401 with no
    // way to attach a token. Registering the scheme puts the Authorize button back and applies the
    // header to every operation. Dev seed tokens: dev-token-store-manager / dev-token-department-lead
    // / dev-token-associate.
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        In = ParameterLocation.Header,
        Description = "Staff seed token, e.g. dev-token-store-manager",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer",
        },
    };

    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
});

var app = builder.Build();

// ---------------------------------------------------------------------------------------
// Pipeline. Order is load-bearing:
//   1. error handling wraps everything, including authentication failures
//   2. authentication resolves the staff identity before controllers run
// ---------------------------------------------------------------------------------------
app.UseMiddleware<AppErrorHandlingMiddleware>();
app.UseMiddleware<StaffAuthenticationMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health")
    .ExcludeFromDescription();

app.MapControllers();

await app.RunAsync();

/// <summary>
/// Marker type so the integration test project can reference this assembly through
/// <c>WebApplicationFactory&lt;IApiMarker&gt;</c> without a public Program class.
/// </summary>
public interface IApiMarker
{
}
