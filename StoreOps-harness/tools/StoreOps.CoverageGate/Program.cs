using System.Text;
using StoreOps.CoverageGate;

/*
 * StoreOps.CoverageGate — deterministic per-layer coverage gate for the StoreOps harness.
 *
 *   dotnet test StoreOps.sln /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
 *   dotnet run --project tools/StoreOps.CoverageGate -- \
 *     --report tests/StoreOps.Api.Tests/TestResults/coverage.cobertura.xml [--md out.md]
 *
 * Exit codes: 0 = every scope meets its threshold, 1 = at least one below, 2 = bad usage.
 * The Evaluator treats a non-zero exit as hard gate H3.1 failing.
 *
 * Why this exists as a separate tool: `dotnet test /p:Threshold=70` enforces one global number,
 * and the StoreOps testing standard sets four different floors by layer. A single overall
 * threshold passes happily while the service layer — where the business rules live — drifts to
 * 50%, which is precisely the drift the standards team asked the harness to prevent.
 */

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(
        """
        StoreOps.CoverageGate — enforces the StoreOps per-layer line-coverage thresholds.

        Usage:
          dotnet run --project tools/StoreOps.CoverageGate -- --report <cobertura.xml> [options]

        Options:
          --report <path>  Cobertura XML produced by coverlet (required)
          --md <path>      Also write a markdown report to <path>
          --list           List every class with its coverage, not just failures
          -h, --help       Show this help

        Thresholds (from .harness/skills/how-to-test/SKILL.md):
          Service layer     80%    classes ending in "Service"
          Route layer       70%    classes ending in "Controller"
          Shared utilities  60%    namespaces containing ".Shared."
          Overall project   70%    every class in the report
        """);
    return 0;
}

var reportPath = ReadOption(args, "--report");

if (reportPath is null)
{
    Console.Error.WriteLine("error: --report <cobertura.xml> is required. See --help.");
    return 2;
}

if (!File.Exists(reportPath))
{
    Console.Error.WriteLine(
        $"error: coverage report not found at '{reportPath}'. Run dotnet test with "
        + "/p:CollectCoverage=true /p:CoverletOutputFormat=cobertura first.");
    return 2;
}

List<ClassCoverage> classes;

try
{
    classes = CoberturaReader.Read(reportPath).ToList();
}
catch (Exception ex)
{
    // A malformed report is an indeterminate gate, not a coverage failure — exit 2 so the
    // Evaluator records INDETERMINATE and escalates rather than blaming the Generator.
    Console.Error.WriteLine($"error: could not read '{reportPath}': {ex.Message}");
    return 2;
}

if (classes.Count == 0)
{
    Console.Error.WriteLine($"error: '{reportPath}' contains no class coverage data.");
    return 2;
}

var results = CoverageScopes.All
    .Select(scope =>
    {
        var inScope = classes.Where(c => scope.Matches(c.ClassName)).ToList();
        var covered = inScope.Sum(c => c.CoveredLines);
        var total = inScope.Sum(c => c.TotalLines);
        var percent = total == 0 ? 100.0 : covered * 100.0 / total;

        return new ScopeResult(scope, inScope, covered, total, percent, percent >= scope.ThresholdPercent);
    })
    .ToList();

var markdownPath = ReadOption(args, "--md");

if (markdownPath is not null)
{
    File.WriteAllText(markdownPath, BuildMarkdown(reportPath, results), new UTF8Encoding(false));
}

Console.Write(BuildText(reportPath, results, args.Contains("--list")));

return results.All(r => r.Passed) ? 0 : 1;

static string? ReadOption(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

static string BuildText(string reportPath, List<ScopeResult> results, bool listAll)
{
    var report = new StringBuilder();

    report.AppendLine("StoreOps coverage gate");
    report.AppendLine($"  report : {reportPath}");
    report.AppendLine();
    report.AppendLine("  Scope              Covered/Total     Coverage   Threshold   Result");

    foreach (var result in results)
    {
        report.AppendLine(
            $"  {result.Scope.Name,-18} {result.CoveredLines,6}/{result.TotalLines,-8} "
            + $"{result.Percent,8:F1}%  {result.Scope.ThresholdPercent,9:F1}%   "
            + $"{(result.Passed ? "PASS" : "FAIL")}");
    }

    report.AppendLine();

    var failing = results.Where(r => !r.Passed).ToList();

    if (failing.Count == 0)
    {
        report.AppendLine("PASS — every scope meets its threshold.");
    }
    else
    {
        report.AppendLine($"FAIL — {failing.Count} scope(s) below threshold:");
        report.AppendLine();

        foreach (var result in failing)
        {
            report.AppendLine(
                $"  {result.Scope.Name}: {result.Percent:F1}% < {result.Scope.ThresholdPercent:F1}%");

            foreach (var below in result.Classes
                .Where(cls => cls.Percent < result.Scope.ThresholdPercent)
                .OrderBy(cls => cls.Percent))
            {
                report.AppendLine($"    {FormatClass(below)}");
            }

            report.AppendLine();
        }
    }

    if (listAll)
    {
        report.AppendLine("All classes:");

        foreach (var cls in results.Single(r => r.Scope == CoverageScopes.Overall).Classes)
        {
            report.AppendLine($"  {FormatClass(cls)}");
        }
    }

    return report.ToString();
}

static string FormatClass(ClassCoverage cls) =>
    $"{cls.Percent,6:F1}%  {cls.CoveredLines,4}/{cls.TotalLines,-5} {cls.ClassName}";

static string BuildMarkdown(string reportPath, List<ScopeResult> results)
{
    var report = new StringBuilder();
    var passed = results.All(r => r.Passed);

    report.AppendLine("# Coverage gate");
    report.AppendLine();
    report.AppendLine($"- Report: `{reportPath}`");
    report.AppendLine($"- Verdict: **{(passed ? "PASS" : "FAIL")}**");
    report.AppendLine();
    report.AppendLine("## Scope results");
    report.AppendLine();
    report.AppendLine("| Scope | Covered | Total | Line coverage | Threshold | Result |");
    report.AppendLine("| --- | --- | --- | --- | --- | --- |");

    foreach (var result in results)
    {
        report.AppendLine(
            $"| {result.Scope.Name} | {result.CoveredLines} | {result.TotalLines} | "
            + $"{result.Percent:F1}% | {result.Scope.ThresholdPercent:F1}% | "
            + $"{(result.Passed ? "✅" : "❌")} |");
    }

    report.AppendLine();
    report.AppendLine("## Classes below their scope threshold");
    report.AppendLine();
    report.AppendLine("| Class | Scope | Line coverage |");
    report.AppendLine("| --- | --- | --- |");

    foreach (var result in results.Where(r => r.Scope != CoverageScopes.Overall))
    {
        foreach (var below in result.Classes
            .Where(cls => cls.Percent < result.Scope.ThresholdPercent)
            .OrderBy(cls => cls.Percent))
        {
            report.AppendLine($"| `{below.ClassName}` | {result.Scope.Name} | {below.Percent:F1}% |");
        }
    }

    report.AppendLine();
    report.AppendLine(
        "*Generated by `dotnet run --project tools/StoreOps.CoverageGate -- --md`. "
        + "Exit code 0 when every scope meets its threshold, 1 otherwise.*");

    return report.ToString();
}

/// <summary>Coverage outcome for one scope.</summary>
internal sealed record ScopeResult(
    CoverageScope Scope,
    IReadOnlyList<ClassCoverage> Classes,
    int CoveredLines,
    int TotalLines,
    double Percent,
    bool Passed);
