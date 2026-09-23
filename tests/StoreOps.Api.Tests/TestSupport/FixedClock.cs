using StoreOps.Api.Shared.Time;

namespace StoreOps.Api.Tests.TestSupport;

/// <summary>
/// Controllable clock. Every time-dependent rule (due dates, SLA breach, escalation grace
/// period) is asserted by moving this clock, never by sleeping.
/// </summary>
public sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset? start = null)
    {
        UtcNow = start ?? new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);
    }

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);

    public void Set(DateTimeOffset instant) => UtcNow = instant;
}
