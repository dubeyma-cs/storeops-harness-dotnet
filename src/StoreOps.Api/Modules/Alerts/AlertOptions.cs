namespace StoreOps.Api.Modules.Alerts;

/// <summary>Tunable behaviour of the alerts module, bound from the <c>Alerts</c> config section.</summary>
public sealed class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>
    /// How long a breached activity may stay unresolved before the breach is escalated from the
    /// department lead to store management. "Configurable grace period" from the SLA feature
    /// requirement — it belongs to the alerts module because alerts owns escalation.
    /// </summary>
    public TimeSpan EscalationGracePeriod { get; set; } = TimeSpan.FromHours(4);

    /// <summary>Interval at which due escalations are swept.</summary>
    public TimeSpan EscalationSweepInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Disabled in tests and CI so runs stay deterministic.</summary>
    public bool EnableBackgroundEscalationSweep { get; set; } = true;
}
