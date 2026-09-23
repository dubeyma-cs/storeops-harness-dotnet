namespace StoreOps.ArchCheck;

/// <summary>A single architecture rule, identified by a stable code.</summary>
public sealed record ArchitectureRule(string Code, string Title, string Rationale);

/// <summary>The StoreOps architecture rule catalogue.</summary>
/// <remarks>
/// Codes are referenced verbatim by the harness Evaluator's hard gates
/// (see <c>.harness/skills/evaluation-criteria/SKILL.md</c>) and by the run log, so they are
/// append-only and never renumbered.
/// </remarks>
public static class ArchitectureRules
{
    public static readonly ArchitectureRule ModuleBoundary = new(
        "SO-001",
        "Module boundary",
        "No module may reference another module's repository. Cross-module reads go through the "
        + "target module's service layer only.");

    public static readonly ArchitectureRule EventBusOnly = new(
        "SO-002",
        "Event bus only",
        "Side effects that cross a module boundary must be raised via IEventBus. A module must "
        + "not take a dependency on a downstream side-effect module's service (alerts, reports).");

    public static readonly ArchitectureRule ErrorContract = new(
        "SO-003",
        "Error contract",
        "Services and controllers must not throw raw exceptions. Every failure must use the "
        + "AppError typed hierarchy (code, message, statusCode).");

    public static readonly ArchitectureRule LayerSeparation = new(
        "SO-004",
        "Layer separation",
        "Routes -> Service -> Repository, no skipping. Controllers must not reference a "
        + "repository; repositories must not reference services, the event bus, or HTTP types.");

    public static readonly ArchitectureRule ReadOnlyReports = new(
        "SO-005",
        "Read-only reports",
        "The reports module aggregates activities, programmes and staff but never writes to "
        + "them: only read verbs may be invoked on another module's service.");

    public static readonly ArchitectureRule SharedIndependence = new(
        "SO-006",
        "Shared independence",
        "Shared must not depend on any module. Modules depend on Shared, never the reverse, so "
        + "the shared kernel stays reusable and cycle-free.");

    public static readonly ArchitectureRule NoModuleCycles = new(
        "SO-007",
        "No circular module dependencies",
        "The module dependency graph must stay acyclic. A cycle means two modules can no longer "
        + "be reasoned about, tested, or deployed independently.");

    public static readonly IReadOnlyList<ArchitectureRule> All = new[]
    {
        ModuleBoundary,
        EventBusOnly,
        ErrorContract,
        LayerSeparation,
        ReadOnlyReports,
        SharedIndependence,
        NoModuleCycles,
    };
}

/// <summary>One concrete rule breach, with the file and line that caused it.</summary>
public sealed record Violation(
    string RuleCode,
    string RuleTitle,
    string File,
    int Line,
    string Snippet,
    string Message);
