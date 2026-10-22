// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>Locates the repository's <c>data</c> directory from the test output path (mirrors the
/// server test suite's <c>TestPaths</c>; duplicated here so this project stays self-contained).</summary>
public static class ClientTestPaths
{
    /// <summary>The repository root (the folder holding <c>data/</c>) — for tests that read source files,
    /// e.g. the Unity UI layout guards.</summary>
    public static string RepoRoot() => Directory.GetParent(DataDir())!.FullName;

    /// <summary>The text of every file of a client class split into partial files — <c>Scripts/&lt;type&gt;.cs</c> and its
    /// <c>&lt;type&gt;.*.cs</c> parts (e.g. <c>HudUi.cs</c> + <c>HudUi.Toasts.cs</c>, #2909) — in a stable order, so a source
    /// guard keeps finding a member whichever part it lives in.</summary>
    public static IReadOnlyList<string> ScriptParts(string type)
    {
        string dir = Path.Combine(RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");
        if (!File.Exists(Path.Combine(dir, type + ".cs")))
        {
            throw new FileNotFoundException($"{type}.cs not found in {dir} — did the client scripts move?");
        }

        return Directory.GetFiles(dir, type + "*.cs")
            .Where(f =>
            {
                string name = Path.GetFileName(f);
                return name == type + ".cs" || (name.StartsWith(type + ".", StringComparison.Ordinal) && name.EndsWith(".cs", StringComparison.Ordinal));
            })
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToList();
    }

    /// <summary><see cref="ScriptParts"/> as one text, the parts separated by a line break.</summary>
    public static string ScriptPartsText(string type) => string.Join("\n", ScriptParts(type));

    /// <summary>The text of the client's <c>PlayerController</c> — <c>PlayerController.cs</c> and its
    /// <c>PlayerController.*.cs</c> concern parts (#2911) — so a source guard keeps finding a member whichever part it
    /// lives in.</summary>
    public static string PlayerControllerSources() => ScriptPartsText("PlayerController");

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
