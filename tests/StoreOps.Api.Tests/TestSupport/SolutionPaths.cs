namespace StoreOps.Api.Tests;

/// <summary>Locates repository paths from the test binary's location.</summary>
/// <remarks>
/// Walks up from the test assembly rather than relying on the working directory, so the
/// architecture tests behave the same under <c>dotnet test</c>, an IDE runner, and CI.
/// </remarks>
public static class SolutionPaths
{
    /// <summary>Repository root — the directory containing <c>StoreOps.sln</c>.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The application source root scanned by the architecture gate.</summary>
    public static string SourceRoot { get; } = Path.Combine(RepositoryRoot, "src");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StoreOps.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate StoreOps.sln above {AppContext.BaseDirectory}.");
    }
}
