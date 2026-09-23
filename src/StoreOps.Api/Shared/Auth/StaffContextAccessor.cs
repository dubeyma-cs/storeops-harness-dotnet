namespace StoreOps.Api.Shared.Auth;

/// <summary>Scoped holder for the request's staff identity.</summary>
public sealed class StaffContextAccessor : IStaffContextAccessor
{
    public StaffContext? Current { get; private set; }

    public void Set(StaffContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Current = context;
    }
}
