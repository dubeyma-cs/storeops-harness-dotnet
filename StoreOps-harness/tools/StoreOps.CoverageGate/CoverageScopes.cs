namespace StoreOps.CoverageGate;

/// <summary>
/// One coverage scope and the floor it must clear.
/// </summary>
/// <remarks>
/// Thresholds come straight from the StoreOps testing standard
/// (<c>.harness/skills/how-to-test/SKILL.md</c>). They are compiled in rather than configurable,
/// because a threshold a run can lower is not a gate — see <c>how-to-review</c> recipe R-8.
/// </remarks>
public sealed record CoverageScope(string Name, double ThresholdPercent, Func<string, bool> Matches);

/// <summary>The four scopes the gate enforces.</summary>
public static class CoverageScopes
{
    /// <summary>Service layer — the business rules. Highest floor.</summary>
    public static readonly CoverageScope Service = new(
        "Service layer",
        80.0,
        className => LastSegment(className).EndsWith("Service", StringComparison.Ordinal));

    /// <summary>Route layer — controllers.</summary>
    public static readonly CoverageScope Route = new(
        "Route layer",
        70.0,
        className => LastSegment(className).EndsWith("Controller", StringComparison.Ordinal));

    /// <summary>Shared utilities — the kernel every module depends on.</summary>
    public static readonly CoverageScope Shared = new(
        "Shared utilities",
        60.0,
        className => className.Contains(".Shared.", StringComparison.Ordinal));

    /// <summary>Everything in the application assembly.</summary>
    public static readonly CoverageScope Overall = new(
        "Overall project",
        70.0,
        _ => true);

    /// <summary>Evaluation order: specific scopes first, overall last.</summary>
    public static readonly IReadOnlyList<CoverageScope> All = new[] { Service, Route, Shared, Overall };

    private static string LastSegment(string className)
    {
        var index = className.LastIndexOf('.');
        return index < 0 ? className : className[(index + 1)..];
    }
}
