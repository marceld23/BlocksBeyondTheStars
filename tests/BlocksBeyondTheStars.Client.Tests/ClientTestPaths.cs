namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>Locates the repository's <c>data</c> directory from the test output path (mirrors the
/// server test suite's <c>TestPaths</c>; duplicated here so this project stays self-contained).</summary>
public static class ClientTestPaths
{
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
}
