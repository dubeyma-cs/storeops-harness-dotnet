using System.Text;
using System.Text.RegularExpressions;

namespace StoreOps.ArchCheck;

/// <summary>Which architectural layer a source file belongs to.</summary>
public enum SourceLayer
{
    /// <summary>Anything that is not a route, service or repository (models, DTOs, options).</summary>
    Other = 0,

    /// <summary>ASP.NET controller — the routes layer.</summary>
    Route = 1,

    /// <summary>Service implementation or interface — the business layer.</summary>
    Service = 2,

    /// <summary>Repository implementation or interface — the data layer.</summary>
    Repository = 3,
}

/// <summary>A scanned source file with everything the rules need to judge it.</summary>
/// <remarks>
/// Rules are evaluated against <see cref="CodeLines"/>, which is <see cref="Lines"/> with
/// comments blanked out. Without that step a doc comment such as
/// <c>"other modules reach staff through IStaffService"</c> inside a repository file would be
/// reported as a layer violation — a false positive, and false positives are fatal for a gate
/// that blocks a harness run.
/// </remarks>
public sealed class SourceFile
{
    private static readonly Regex TypeDeclaration = new(
        @"^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+|abstract\s+|static\s+|partial\s+|readonly\s+|ref\s+)*\b(?:class|record|interface|enum|struct)\b\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Module segment used for files outside <c>Modules/</c> and <c>Shared/</c>.</summary>
    public const string HostModule = "(host)";

    /// <summary>Module segment used for the shared kernel.</summary>
    public const string SharedModule = "shared";

    private SourceFile(
        string path,
        string relativePath,
        string module,
        SourceLayer layer,
        string[] lines,
        string[] codeLines)
    {
        Path = path;
        RelativePath = relativePath;
        Module = module;
        Layer = layer;
        Lines = lines;
        CodeLines = codeLines;
    }

    public string Path { get; }

    public string RelativePath { get; }

    /// <summary>Owning module in lower case: activities, programmes, staff, alerts, reports, shared, (host).</summary>
    public string Module { get; }

    public SourceLayer Layer { get; }

    /// <summary>Raw file lines, used for reporting snippets.</summary>
    public string[] Lines { get; }

    /// <summary>File lines with comment content removed, used for rule evaluation.</summary>
    public string[] CodeLines { get; }

    /// <summary>Types declared in this file.</summary>
    public IReadOnlyList<string> DeclaredTypes { get; private set; } = Array.Empty<string>();

    public bool IsModule => Module is not (HostModule or SharedModule);

    /// <summary>Reads and classifies a file. <paramref name="root"/> is the scanned source root.</summary>
    public static SourceFile Load(string path, string root)
    {
        var relative = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
        var fileName = System.IO.Path.GetFileName(path);
        var lines = File.ReadAllLines(path);
        var codeLines = StripComments(lines);

        var file = new SourceFile(
            path, relative, ResolveModule(relative), ResolveLayer(fileName), lines, codeLines);

        file.DeclaredTypes = TypeDeclaration
            .Matches(string.Join('\n', codeLines))
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return file;
    }

    /// <summary>
    /// Blanks out <c>//</c> and <c>/* … */</c> comment content while preserving line numbering.
    /// </summary>
    private static string[] StripComments(string[] lines)
    {
        var result = new string[lines.Length];
        var inBlockComment = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var builder = new StringBuilder(line.Length);

            for (var c = 0; c < line.Length; c++)
            {
                if (inBlockComment)
                {
                    if (c + 1 < line.Length && line[c] == '*' && line[c + 1] == '/')
                    {
                        inBlockComment = false;
                        c++;
                    }

                    continue;
                }

                if (c + 1 < line.Length && line[c] == '/' && line[c + 1] == '/')
                {
                    break;
                }

                if (c + 1 < line.Length && line[c] == '/' && line[c + 1] == '*')
                {
                    inBlockComment = true;
                    c++;
                    continue;
                }

                builder.Append(line[c]);
            }

            result[i] = builder.ToString();
        }

        return result;
    }

    private static string ResolveModule(string relativePath)
    {
        var segments = relativePath.Split('/');

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "Modules", StringComparison.OrdinalIgnoreCase))
            {
                return segments[i + 1].ToLowerInvariant();
            }

            if (string.Equals(segments[i], "Shared", StringComparison.OrdinalIgnoreCase))
            {
                return SharedModule;
            }
        }

        return HostModule;
    }

    private static SourceLayer ResolveLayer(string fileName)
    {
        if (fileName.EndsWith("Controller.cs", StringComparison.Ordinal))
        {
            return SourceLayer.Route;
        }

        if (fileName.Contains("Repository", StringComparison.Ordinal))
        {
            return SourceLayer.Repository;
        }

        // Module composition roots (e.g. AlertsModule.cs) wire services up; they are not services.
        if (fileName.EndsWith("Service.cs", StringComparison.Ordinal))
        {
            return SourceLayer.Service;
        }

        return SourceLayer.Other;
    }
}
