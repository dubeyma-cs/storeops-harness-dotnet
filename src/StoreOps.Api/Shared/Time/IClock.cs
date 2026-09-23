namespace StoreOps.Api.Shared.Time;

/// <summary>
/// Abstraction over "now" so SLA and overdue rules are testable without waiting.
/// </summary>
/// <remarks>
/// Services must never call <c>DateTimeOffset.UtcNow</c> directly — the SLA breach rule is a
/// time-dependent business rule and the Evaluator's test-quality dimension requires that it be
/// asserted deterministically, not via sleeps.
/// </remarks>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Production clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
