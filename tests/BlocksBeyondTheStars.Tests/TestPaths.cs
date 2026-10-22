// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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

    /// <summary>The text of the client's <c>PlayerController</c> — <c>PlayerController.cs</c> and its
    /// <c>PlayerController.*.cs</c> concern parts (#2911), in a stable order, the parts separated by a line break — so a
    /// source guard keeps finding a member whichever part it lives in.</summary>
    public static string PlayerControllerSources()
    {
        string dir = Path.Combine(RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");
        if (!File.Exists(Path.Combine(dir, "PlayerController.cs")))
        {
            throw new FileNotFoundException($"PlayerController.cs not found in {dir} — did the client scripts move?");
        }

        return string.Join("\n", Directory.GetFiles(dir, "PlayerController*.cs")
            .Where(f =>
            {
                string name = Path.GetFileName(f);
                return name == "PlayerController.cs"
                       || (name.StartsWith("PlayerController.", StringComparison.Ordinal) && name.EndsWith(".cs", StringComparison.Ordinal));
            })
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(File.ReadAllText));
    }

    /// <summary>The client's runtime icon directory (generated content icons live here).</summary>
    public static string ClientIconsDir()
        => Path.Combine(RepoRoot(), "client", "Assets", "Resources", "icons");

    /// <summary>Every <c>*.cs</c> file under <paramref name="relativeDir"/> (relative to the repository root), build output skipped.</summary>
    public static IEnumerable<string> SourceFiles(string relativeDir)
    {
        string root = Path.Combine(RepoRoot(), relativeDir);
        char sep = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal)
                        && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal)
                        && !f.Contains($"{sep}artifacts{sep}", StringComparison.Ordinal));
    }
}
