using StoreOps.ArchCheck;

namespace StoreOps.Api.Tests.Architecture;

/// <summary>A throwaway source tree on disk, used to prove each architecture rule fires.</summary>
public sealed class SyntheticSourceTree : IDisposable
{
    private SyntheticSourceTree(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public static SyntheticSourceTree Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "storeops-archcheck", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new SyntheticSourceTree(root);
    }

    /// <summary>Writes a file at a path relative to the tree root, creating directories as needed.</summary>
    public void Write(string relativePath, string content)
    {
        var fullPath = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    public ScanResult Scan() => ArchitectureScanner.Scan(Root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
