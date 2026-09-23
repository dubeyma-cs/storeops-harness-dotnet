using Microsoft.Extensions.DependencyInjection;
using StoreOps.Api.Modules.Staff;
using StoreOps.Api.Shared.Auth;

namespace StoreOps.Api.Tests.TestSupport;

/// <summary>
/// Runs code inside a DI scope with an explicit staff identity, the way a background worker or
/// an event handler does.
/// </summary>
/// <remarks>
/// Used to drive the SLA and escalation sweeps deterministically: the background workers are
/// disabled in tests, so the test itself plays their part and the assertion is about the rule,
/// not about timer luck.
/// </remarks>
public static class ScopeRunner
{
    public static readonly StaffContext SystemIdentity = new(
        StaffId: "test-system",
        StoreId: InMemoryStaffRepository.SeedData.StoreId,
        RegionId: InMemoryStaffRepository.SeedData.RegionId,
        Role: "SYSTEM");

    public static async Task<T> RunAsync<T>(
        this StoreOpsApiFactory factory,
        Func<IServiceProvider, Task<T>> work,
        StaffContext? identity = null)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IStaffContextAccessor>().Set(identity ?? SystemIdentity);
        return await work(scope.ServiceProvider);
    }

    public static async Task RunAsync(
        this StoreOpsApiFactory factory,
        Func<IServiceProvider, Task> work,
        StaffContext? identity = null)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IStaffContextAccessor>().Set(identity ?? SystemIdentity);
        await work(scope.ServiceProvider);
    }
}
