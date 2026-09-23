using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StoreOps.ArchCheck;

/*
 * StoreOps.ArchCheck — deterministic architecture gate for the StoreOps harness.
 *
 *   dotnet run --project tools/StoreOps.ArchCheck                  human-readable report
 *   dotnet run --project tools/StoreOps.ArchCheck -- --json        machine-readable report
 *   dotnet run --project tools/StoreOps.ArchCheck -- --md out.md   markdown for evaluator-feedback
 *
 * Exit codes: 0 = no violations, 1 = violations found, 2 = bad usage.
 * The Evaluator treats a non-zero exit as a hard-gate FAIL (see .harness/agents/evaluator.agent.md).
 */

var arguments = args;

if (arguments.Contains("--help") || arguments.Contains("-h"))
{
    Console.WriteLine(
        """
        StoreOps.ArchCheck — enforces the StoreOps module boundary, event bus, error contract,
        layering, read-only reports and acyclic-module rules.

        Usage:
          dotnet run --project tools/StoreOps.ArchCheck [-- options]

        Options:
          --src <path>   Source root to scan (default: <repo>/src)
          --json         Emit the report as JSON on stdout
          --md <path>    Also write a markdown report to <path>
          -h, --help     Show this help
        """);
    return 0;
}

var sourceRoot = ReadOption(arguments, "--src") ?? ResolveDefaultSourceRoot();

if (sourceRoot is null || !Directory.Exists(sourceRoot))
{
    Console.Error.WriteLine(
        $"error: source root not found ({sourceRoot ?? "<unresolved>"}). Pass --src <path>.");
    return 2;
}

var result = ArchitectureScanner.Scan(Path.GetFullPath(sourceRoot));
var markdownPath = ReadOption(arguments, "--md");

if (markdownPath is not null)
{
    File.WriteAllText(markdownPath, BuildMarkdown(result), new UTF8Encoding(false));
}

if (arguments.Contains("--json"))
{
    Console.WriteLine(JsonSerializer.Serialize(
        result,
        new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        }));
}
else
{
    Console.Write(BuildText(result));
}

return result.Passed ? 0 : 1;

static string? ReadOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string? ResolveDefaultSourceRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "StoreOps.sln")))
        {
            return Path.Combine(directory.FullName, "src");
        }

        directory = directory.Parent;
    }

    return null;
}

static string BuildText(ScanResult result)
{
    var report = new StringBuilder();

    report.AppendLine("StoreOps architecture check");
    report.AppendLine($"  root          : {result.Root}");
    report.AppendLine($"  files scanned : {result.FilesScanned}");
    report.AppendLine($"  rules         : {ArchitectureRules.All.Count}");
    report.AppendLine();
    report.AppendLine("Module dependency graph (module -> modules it depends on):");

    foreach (var (module, dependencies) in result.ModuleGraph.OrderBy(kv => kv.Key, StringComparer.Ordinal))
    {
        var rendered = dependencies.Count == 0 ? "(none)" : string.Join(", ", dependencies);
        report.AppendLine($"  {module,-12} -> {rendered}");
    }

    report.AppendLine();

    if (result.Passed)
    {
        report.AppendLine("PASS — no architecture violations found.");
        return report.ToString();
    }

    report.AppendLine($"FAIL — {result.Violations.Count} violation(s):");
    report.AppendLine();

    foreach (var violation in result.Violations)
    {
        report.AppendLine($"  [{violation.RuleCode}] {violation.RuleTitle}");
        report.AppendLine($"    {violation.File}:{violation.Line}");
        report.AppendLine($"    {violation.Message}");

        if (!string.IsNullOrWhiteSpace(violation.Snippet))
        {
            report.AppendLine($"    > {violation.Snippet}");
        }

        report.AppendLine();
    }

    return report.ToString();
}

static string BuildMarkdown(ScanResult result)
{
    var report = new StringBuilder();

    report.AppendLine("# Architecture check");
    report.AppendLine();
    report.AppendLine($"- Files scanned: **{result.FilesScanned}**");
    report.AppendLine($"- Verdict: **{(result.Passed ? "PASS" : "FAIL")}**");
    report.AppendLine($"- Violations: **{result.Violations.Count}**");
    report.AppendLine();
    report.AppendLine("## Module dependency graph");
    report.AppendLine();
    report.AppendLine("| Module | Depends on |");
    report.AppendLine("| --- | --- |");

    foreach (var (module, dependencies) in result.ModuleGraph.OrderBy(kv => kv.Key, StringComparer.Ordinal))
    {
        report.AppendLine($"| `{module}` | {(dependencies.Count == 0 ? "—" : string.Join(", ", dependencies.Select(d => $"`{d}`")))} |");
    }

    report.AppendLine();
    report.AppendLine("## Rule results");
    report.AppendLine();
    report.AppendLine("| Rule | Title | Violations |");
    report.AppendLine("| --- | --- | --- |");

    foreach (var rule in ArchitectureRules.All)
    {
        var count = result.Violations.Count(v => v.RuleCode == rule.Code);
        report.AppendLine($"| `{rule.Code}` | {rule.Title} | {(count == 0 ? "0 ✅" : $"{count} ❌")} |");
    }

    if (result.Violations.Count == 0)
    {
        return report.ToString();
    }

    report.AppendLine();
    report.AppendLine("## Violations");
    report.AppendLine();

    foreach (var violation in result.Violations)
    {
        report.AppendLine($"### `{violation.RuleCode}` {violation.File}:{violation.Line}");
        report.AppendLine();
        report.AppendLine(violation.Message);

        if (!string.IsNullOrWhiteSpace(violation.Snippet))
        {
            report.AppendLine();
            report.AppendLine("```csharp");
            report.AppendLine(violation.Snippet);
            report.AppendLine("```");
        }

        report.AppendLine();
    }

    return report.ToString();
}
