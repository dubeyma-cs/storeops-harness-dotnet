using System.Xml.Linq;

namespace StoreOps.CoverageGate;

/// <summary>Per-class line coverage, as read from a cobertura report.</summary>
public sealed record ClassCoverage(string ClassName, string FileName, int CoveredLines, int TotalLines)
{
    public double Percent => TotalLines == 0 ? 100.0 : CoveredLines * 100.0 / TotalLines;
}

/// <summary>
/// Reads line coverage out of a cobertura XML report produced by coverlet.
/// </summary>
/// <remarks>
/// Coverage is computed from <c>&lt;line hits="n"/&gt;</c> elements rather than from the
/// <c>line-rate</c> attributes. The attributes are pre-rounded per class, so summing them across a
/// scope gives a slightly different answer than counting lines — and a gate has to produce the
/// same number every time, including at the boundary where 79.6% and 80.0% decide a verdict.
/// <para>
/// A class split across partial files appears once per file in cobertura. Entries are merged by
/// class name so a partial class is not counted twice with different denominators.
/// </para>
/// </remarks>
public static class CoberturaReader
{
    public static IReadOnlyList<ClassCoverage> Read(string reportPath)
    {
        var document = XDocument.Load(reportPath);
        var merged = new Dictionary<string, (string FileName, HashSet<int> Covered, HashSet<int> All)>(
            StringComparer.Ordinal);

        foreach (var classElement in document.Descendants("class"))
        {
            var className = classElement.Attribute("name")?.Value;

            if (string.IsNullOrWhiteSpace(className))
            {
                continue;
            }

            // Compiler-generated closure and async state-machine types are part of their owner.
            var ownerName = StripCompilerGeneratedSuffix(className);
            var fileName = classElement.Attribute("filename")?.Value ?? string.Empty;

            if (!merged.TryGetValue(ownerName, out var entry))
            {
                entry = (fileName, new HashSet<int>(), new HashSet<int>());
                merged[ownerName] = entry;
            }

            foreach (var lineElement in classElement.Descendants("line"))
            {
                if (!int.TryParse(lineElement.Attribute("number")?.Value, out var number))
                {
                    continue;
                }

                if (!int.TryParse(lineElement.Attribute("hits")?.Value, out var hits))
                {
                    continue;
                }

                entry.All.Add(number);

                if (hits > 0)
                {
                    entry.Covered.Add(number);
                }
            }
        }

        return merged
            .Select(kv => new ClassCoverage(kv.Key, kv.Value.FileName, kv.Value.Covered.Count, kv.Value.All.Count))
            .Where(c => c.TotalLines > 0)
            .OrderBy(c => c.ClassName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Maps <c>Foo/&lt;BarAsync&gt;d__12</c> and <c>Foo+&lt;&gt;c</c> back to <c>Foo</c>.
    /// </summary>
    private static string StripCompilerGeneratedSuffix(string className)
    {
        var cut = className.IndexOfAny(new[] { '/', '+' });
        var name = cut < 0 ? className : className[..cut];

        var angle = name.IndexOf('<', StringComparison.Ordinal);
        return angle < 0 ? name : name[..angle].TrimEnd('.');
    }
}
