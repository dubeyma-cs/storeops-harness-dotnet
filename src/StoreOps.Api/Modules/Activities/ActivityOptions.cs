namespace StoreOps.Api.Modules.Activities;

/// <summary>Tunable behaviour of the activities module, bound from the <c>Activities</c> config section.</summary>
public sealed class ActivityOptions
{
    public const string SectionName = "Activities";

    /// <summary>Interval at which the background SLA sweep looks for newly breached activities.</summary>
    public TimeSpan SlaSweepInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Set to false in tests and in CI so runs are deterministic — the sweep is then driven
    /// explicitly by calling <c>IActivityService.SweepSlaBreachesAsync</c>.
    /// </summary>
    public bool EnableBackgroundSlaSweep { get; set; } = true;
}
