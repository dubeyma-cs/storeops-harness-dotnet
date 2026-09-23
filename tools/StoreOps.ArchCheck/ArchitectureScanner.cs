using System.Text.RegularExpressions;

namespace StoreOps.ArchCheck;

/// <summary>Result of one scan.</summary>
public sealed record ScanResult(
    string Root,
    int FilesScanned,
    IReadOnlyList<Violation> Violations,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ModuleGraph)
{
    public bool Passed => Violations.Count == 0;
}

/// <summary>
/// Deterministic source scanner for the StoreOps architecture rules.
/// </summary>
/// <remarks>
/// This is the .NET equivalent of the dependency analyser the capstone brief names for the
/// Node stack. It is intentionally a static analyser over text rather than an LLM judgement:
/// the Evaluator needs a gate whose verdict cannot vary between runs on identical input.
/// <para>
/// Ownership of a type is derived from where it is declared, so the rules keep working when new
/// modules, services or repositories are added — no allowlist to maintain.
/// </para>
/// </remarks>
public static class ArchitectureScanner
{
    /// <summary>Modules whose work is triggered by events rather than by direct calls.</summary>
    private static readonly string[] SideEffectModules = { "alerts", "reports" };

    /// <summary>Method-name prefixes that constitute a read on another module's service.</summary>
    private static readonly string[] ReadVerbs =
    {
        "List", "Get", "Find", "Count", "Query", "Exists", "Authenticate",
    };

    private static readonly Regex ThrowNew = new(
        @"throw\s+new\s+(?<type>[A-Za-z_][A-Za-z0-9_\.]*)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex ServiceCall = new(
        @"(?<receiver>_[A-Za-z0-9_]*[Ss]ervice)\s*\.\s*(?<method>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
        RegexOptions.Compiled);

    private static readonly string[] HttpTypes =
    {
        "HttpContext", "HttpRequest", "HttpResponse", "ControllerBase", "IActionResult",
        "ActionResult", "IHeaderDictionary",
    };

    /// <summary>Scans every <c>.cs</c> file beneath <paramref name="root"/>.</summary>
    public static ScanResult Scan(string root)
    {
        var files = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsGenerated(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => SourceFile.Load(p, root))
            .ToList();

        // typeName -> owning module, derived from declarations.
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            foreach (var type in file.DeclaredTypes)
            {
                owner.TryAdd(type, file.Module);
            }
        }

        var repositoryTypes = owner
            .Where(kv => kv.Key.EndsWith("Repository", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var sideEffectServiceTypes = owner
            .Where(kv =>
                kv.Key.EndsWith("Service", StringComparison.Ordinal)
                && SideEffectModules.Contains(kv.Value, StringComparer.Ordinal))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

        var violations = new List<Violation>();
        var graph = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var referenced = ReferencedTypes(file, owner.Keys);

            CheckModuleBoundary(file, referenced, repositoryTypes, violations);
            CheckEventBusOnly(file, referenced, sideEffectServiceTypes, owner, violations);
            CheckErrorContract(file, violations);
            CheckLayerSeparation(file, referenced, violations);
            CheckReadOnlyReports(file, owner, violations);
            CheckSharedIndependence(file, referenced, owner, violations);

            if (!file.IsModule)
            {
                continue;
            }

            if (!graph.TryGetValue(file.Module, out var edges))
            {
                edges = new SortedSet<string>(StringComparer.Ordinal);
                graph[file.Module] = edges;
            }

            foreach (var (type, _) in referenced)
            {
                var target = owner[type];

                if (target != file.Module && target != SourceFile.SharedModule && target != SourceFile.HostModule)
                {
                    edges.Add(target);
                }
            }
        }

        foreach (var module in owner.Values.Distinct(StringComparer.Ordinal))
        {
            if (module is SourceFile.HostModule or SourceFile.SharedModule)
            {
                continue;
            }

            graph.TryAdd(module, new SortedSet<string>(StringComparer.Ordinal));
        }

        violations.AddRange(FindCycles(graph));

        return new ScanResult(
            root,
            files.Count,
            violations
                .OrderBy(v => v.RuleCode, StringComparer.Ordinal)
                .ThenBy(v => v.File, StringComparer.Ordinal)
                .ThenBy(v => v.Line)
                .ToList(),
            graph.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value.ToList(),
                StringComparer.Ordinal));
    }

    private static bool IsGenerated(string path)
    {
        var normalised = path.Replace('\\', '/');

        return normalised.Contains("/obj/", StringComparison.Ordinal)
            || normalised.Contains("/bin/", StringComparison.Ordinal)
            || normalised.EndsWith(".g.cs", StringComparison.Ordinal)
            || normalised.EndsWith(".Designer.cs", StringComparison.Ordinal);
    }

    /// <summary>Finds the first line on which each known type name is referenced in code.</summary>
    private static List<(string Type, int Line)> ReferencedTypes(SourceFile file, IEnumerable<string> knownTypes)
    {
        var declared = file.DeclaredTypes.ToHashSet(StringComparer.Ordinal);
        var found = new List<(string, int)>();

        foreach (var type in knownTypes)
        {
            if (declared.Contains(type))
            {
                continue;
            }

            var pattern = new Regex($@"\b{Regex.Escape(type)}\b", RegexOptions.Compiled);

            for (var i = 0; i < file.CodeLines.Length; i++)
            {
                if (pattern.IsMatch(file.CodeLines[i]))
                {
                    found.Add((type, i + 1));
                    break;
                }
            }
        }

        return found;
    }

    private static void CheckModuleBoundary(
        SourceFile file,
        List<(string Type, int Line)> referenced,
        Dictionary<string, string> repositoryTypes,
        List<Violation> violations)
    {
        foreach (var (type, line) in referenced)
        {
            if (!repositoryTypes.TryGetValue(type, out var repositoryModule) || repositoryModule == file.Module)
            {
                continue;
            }

            violations.Add(new Violation(
                ArchitectureRules.ModuleBoundary.Code,
                ArchitectureRules.ModuleBoundary.Title,
                file.RelativePath,
                line,
                file.Lines[line - 1].Trim(),
                $"'{file.Module}' references '{type}', which belongs to the '{repositoryModule}' module. "
                + $"Use the '{repositoryModule}' module's service layer instead."));
        }
    }

    private static void CheckEventBusOnly(
        SourceFile file,
        List<(string Type, int Line)> referenced,
        HashSet<string> sideEffectServiceTypes,
        Dictionary<string, string> owner,
        List<Violation> violations)
    {
        if (!file.IsModule || SideEffectModules.Contains(file.Module, StringComparer.Ordinal))
        {
            return;
        }

        foreach (var (type, line) in referenced)
        {
            if (!sideEffectServiceTypes.Contains(type))
            {
                continue;
            }

            violations.Add(new Violation(
                ArchitectureRules.EventBusOnly.Code,
                ArchitectureRules.EventBusOnly.Title,
                file.RelativePath,
                line,
                file.Lines[line - 1].Trim(),
                $"'{file.Module}' references '{type}' from the '{owner[type]}' module directly. "
                + "Cross-module side effects must be raised through IEventBus."));
        }
    }

    private static void CheckErrorContract(SourceFile file, List<Violation> violations)
    {
        if (file.Layer is not (SourceLayer.Service or SourceLayer.Route))
        {
            return;
        }

        for (var i = 0; i < file.CodeLines.Length; i++)
        {
            foreach (var match in ThrowNew.Matches(file.CodeLines[i]).Cast<Match>())
            {
                var thrown = match.Groups["type"].Value;
                var simpleName = thrown.Split('.').Last();

                if (simpleName.EndsWith("Error", StringComparison.Ordinal))
                {
                    continue;
                }

                violations.Add(new Violation(
                    ArchitectureRules.ErrorContract.Code,
                    ArchitectureRules.ErrorContract.Title,
                    file.RelativePath,
                    i + 1,
                    file.Lines[i].Trim(),
                    $"'{simpleName}' is not part of the AppError hierarchy. Services and routes must "
                    + "throw an AppError subclass so the error carries a code and a status code."));
            }
        }
    }

    private static void CheckLayerSeparation(
        SourceFile file,
        List<(string Type, int Line)> referenced,
        List<Violation> violations)
    {
        if (file.Layer == SourceLayer.Route || file.Module == SourceFile.HostModule)
        {
            foreach (var (type, line) in referenced.Where(r =>
                r.Type.EndsWith("Repository", StringComparison.Ordinal)))
            {
                violations.Add(new Violation(
                    ArchitectureRules.LayerSeparation.Code,
                    ArchitectureRules.LayerSeparation.Title,
                    file.RelativePath,
                    line,
                    file.Lines[line - 1].Trim(),
                    $"'{type}' is a data-layer type; routes and host wiring must go through the "
                    + "service layer (Routes -> Service -> Repository, no skipping)."));
            }
        }

        if (file.Layer != SourceLayer.Repository)
        {
            return;
        }

        foreach (var (type, line) in referenced)
        {
            var isServiceOrBus =
                (type.EndsWith("Service", StringComparison.Ordinal) && type != "IHostedService")
                || type == "IEventBus"
                || type == "IStaffContextAccessor";

            if (!isServiceOrBus)
            {
                continue;
            }

            violations.Add(new Violation(
                ArchitectureRules.LayerSeparation.Code,
                ArchitectureRules.LayerSeparation.Title,
                file.RelativePath,
                line,
                file.Lines[line - 1].Trim(),
                $"Repositories must not reach outward; '{type}' belongs above the data layer."));
        }

        for (var i = 0; i < file.CodeLines.Length; i++)
        {
            foreach (var httpType in HttpTypes)
            {
                if (!Regex.IsMatch(file.CodeLines[i], $@"\b{Regex.Escape(httpType)}\b"))
                {
                    continue;
                }

                violations.Add(new Violation(
                    ArchitectureRules.LayerSeparation.Code,
                    ArchitectureRules.LayerSeparation.Title,
                    file.RelativePath,
                    i + 1,
                    file.Lines[i].Trim(),
                    $"'{httpType}' is an HTTP concern and must not appear in a repository."));
            }
        }
    }

    private static void CheckReadOnlyReports(
        SourceFile file,
        Dictionary<string, string> owner,
        List<Violation> violations)
    {
        if (file.Module != "reports")
        {
            return;
        }

        for (var i = 0; i < file.CodeLines.Length; i++)
        {
            foreach (var match in ServiceCall.Matches(file.CodeLines[i]).Cast<Match>())
            {
                var receiver = match.Groups["receiver"].Value;
                var method = match.Groups["method"].Value;

                // `_repository` calls and own-module services are out of scope for this rule.
                if (IsOwnModuleReceiver(receiver, owner))
                {
                    continue;
                }

                if (ReadVerbs.Any(v => method.StartsWith(v, StringComparison.Ordinal)))
                {
                    continue;
                }

                violations.Add(new Violation(
                    ArchitectureRules.ReadOnlyReports.Code,
                    ArchitectureRules.ReadOnlyReports.Title,
                    file.RelativePath,
                    i + 1,
                    file.Lines[i].Trim(),
                    $"'{receiver}.{method}(…)' is not a read. The reports module may only call read "
                    + $"methods ({string.Join(", ", ReadVerbs)}…) on another module's service."));
            }
        }
    }

    /// <summary>
    /// True when a field such as <c>_reportService</c> refers to a service owned by the reports
    /// module itself, which the read-only rule does not restrict.
    /// </summary>
    private static bool IsOwnModuleReceiver(string receiver, Dictionary<string, string> owner)
    {
        var name = receiver.TrimStart('_');
        var candidate = char.ToUpperInvariant(name[0]) + name[1..];

        return owner.TryGetValue(candidate, out var module) && module == "reports";
    }

    private static void CheckSharedIndependence(
        SourceFile file,
        List<(string Type, int Line)> referenced,
        Dictionary<string, string> owner,
        List<Violation> violations)
    {
        if (file.Module != SourceFile.SharedModule)
        {
            return;
        }

        foreach (var (type, line) in referenced)
        {
            if (owner[type] is SourceFile.SharedModule or SourceFile.HostModule)
            {
                continue;
            }

            violations.Add(new Violation(
                ArchitectureRules.SharedIndependence.Code,
                ArchitectureRules.SharedIndependence.Title,
                file.RelativePath,
                line,
                file.Lines[line - 1].Trim(),
                $"Shared references '{type}' from the '{owner[type]}' module. The shared kernel must "
                + "not depend on any module."));
        }
    }

    /// <summary>Depth-first cycle detection over the module dependency graph.</summary>
    private static IEnumerable<Violation> FindCycles(Dictionary<string, SortedSet<string>> graph)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var violations = new List<Violation>();

        foreach (var start in graph.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var path = new List<string>();
            var visiting = new HashSet<string>(StringComparer.Ordinal);
            Walk(start);

            void Walk(string module)
            {
                if (visiting.Contains(module))
                {
                    var cycleStart = path.IndexOf(module);
                    var cycle = path.Skip(cycleStart).Append(module).ToList();

                    // Deduplicate on the distinct set of modules in the cycle, not the raw path.
                    // The same cycle reached from different start nodes closes on different repeated
                    // nodes (activities->programmes->activities vs programmes->activities->programmes),
                    // so keying on the path would report one logical cycle multiple times.
                    var key = string.Join("->", cycle.Distinct().OrderBy(c => c, StringComparer.Ordinal));

                    if (reported.Add(key))
                    {
                        violations.Add(new Violation(
                            ArchitectureRules.NoModuleCycles.Code,
                            ArchitectureRules.NoModuleCycles.Title,
                            "(module graph)",
                            0,
                            string.Join(" -> ", cycle),
                            $"Circular module dependency: {string.Join(" -> ", cycle)}."));
                    }

                    return;
                }

                if (!graph.TryGetValue(module, out var edges))
                {
                    return;
                }

                visiting.Add(module);
                path.Add(module);

                foreach (var next in edges)
                {
                    Walk(next);
                }

                path.RemoveAt(path.Count - 1);
                visiting.Remove(module);
            }
        }

        return violations;
    }
}
