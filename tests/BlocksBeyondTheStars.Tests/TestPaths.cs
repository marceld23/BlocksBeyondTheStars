namespace BlocksBeyondTheStars.Tests;

/// <summary>Helpers for locating repository assets (the data directory) during tests.</summary>
public static class TestPaths
{
    /// <summary>Walks up from the test output directory until it finds the repo's data folder.</summary>
    public static string DataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "data");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "blocks.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository 'data' directory from the test output path.");
    }

    /// <summary>The repository root (the parent of the <c>data</c> directory).</summary>
    public static string RepoRoot() => Directory.GetParent(DataDir())!.FullName;

    /// <summary>The client's runtime icon directory (generated content icons live here).</summary>
    public static string ClientIconsDir()
        => Path.Combine(RepoRoot(), "client", "Assets", "Resources", "icons");
}
