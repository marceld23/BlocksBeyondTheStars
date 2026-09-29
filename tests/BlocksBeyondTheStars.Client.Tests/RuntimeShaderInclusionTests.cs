// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Every shader the client looks up by name at runtime must survive the player build. The game builds its materials
/// in code (<c>Shader.Find</c>), so nothing references those shaders as assets and Unity strips them unless they are
/// "always included" — then <c>Shader.Find</c> returns null in the .exe only, while the Editor still finds them.
/// That silently broke the starfield, the milky glass and — until #2136 — the form editor's preview, which looked
/// up URP/Lit and threw <c>new Material(null)</c> on every paint stroke. The Unity sources are read as text.
/// </summary>
public sealed class RuntimeShaderInclusionTests
{
    private static readonly Regex FindLiteral = new(@"(?<fallback>\?\?\s*)?Shader\.Find\(""(?<name>[^""]+)""\)", RegexOptions.ExplicitCapture, System.TimeSpan.FromSeconds(5));

    private static string Client(params string[] parts)
        => Path.Combine(new[] { ClientTestPaths.RepoRoot(), "client" }.Concat(parts).ToArray());

    /// <summary>Every <c>Shader.Find("…")</c> literal in the runtime scripts; <c>Fallback</c> is true when it only
    /// runs after a <c>??</c> (a second choice — the primary lookup is what must be kept).</summary>
    private static List<(string File, string Name, bool Fallback)> Lookups()
    {
        var found = new List<(string, string, bool)>();
        foreach (var file in Directory.EnumerateFiles(Client("Assets", "BlocksBeyondTheStars", "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(file))
            {
                string code = line.TrimStart();
                if (code.StartsWith("//", System.StringComparison.Ordinal))
                {
                    continue; // doc comments mention Shader.Find("BlocksBeyondTheStars/…") as prose
                }

                foreach (Match m in FindLiteral.Matches(code))
                {
                    found.Add((Path.GetFileName(file), m.Groups["name"].Value, m.Groups["fallback"].Success));
                }
            }
        }

        return found;
    }

    /// <summary>The names in BuildScript.RuntimeShaders — the build's force-include safety net.</summary>
    private static HashSet<string> BuildScriptRuntimeShaders()
    {
        string src = File.ReadAllText(Client("Assets", "BlocksBeyondTheStars", "Editor", "BuildScript.cs"));
        int start = src.IndexOf("RuntimeShaders =", System.StringComparison.Ordinal);
        Assert.True(start >= 0, "BuildScript.RuntimeShaders not found");
        int end = src.IndexOf("};", start, System.StringComparison.Ordinal);
        return Regex.Matches(src.Substring(start, end - start), @"""(?<v>[^""]+)""", RegexOptions.ExplicitCapture, System.TimeSpan.FromSeconds(5))
            .Select(m => m.Groups["v"].Value)
            .ToHashSet();
    }

    /// <summary>Our own shaders: name (from the <c>Shader "…"</c> line) → asset GUID (from the .meta).</summary>
    private static Dictionary<string, string> RepoShaderGuids()
    {
        var byName = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(Client("Assets"), "*.shader", SearchOption.AllDirectories))
        {
            var name = Regex.Match(File.ReadAllText(file), @"^\s*Shader\s+""(?<v>[^""]+)""", RegexOptions.Multiline | RegexOptions.ExplicitCapture, System.TimeSpan.FromSeconds(5));
            var guid = Regex.Match(File.ReadAllText(file + ".meta"), @"guid:\s*(?<v>[0-9a-f]{32})", RegexOptions.ExplicitCapture, System.TimeSpan.FromSeconds(5));
            if (name.Success && guid.Success)
            {
                byName[name.Groups["v"].Value] = guid.Groups["v"].Value;
            }
        }

        return byName;
    }

    [Fact]
    public void EveryPrimaryShaderLookup_IsForceIncludedByTheBuild()
    {
        var lookups = Lookups();
        Assert.NotEmpty(lookups);
        var forced = BuildScriptRuntimeShaders();

        var missing = lookups
            .Where(l => !l.Fallback && !forced.Contains(l.Name))
            .Select(l => $"{l.File}: Shader.Find(\"{l.Name}\")")
            .Distinct()
            .ToList();
        Assert.True(missing.Count == 0,
            "These runtime shader lookups are stripped from the player build (Shader.Find returns null there): "
            + string.Join("; ", missing)
            + ". Use one of our own BlocksBeyondTheStars/* shaders, or add the name to BuildScript.RuntimeShaders "
            + "and GraphicsSettings.asset m_AlwaysIncludedShaders.");
    }

    [Fact]
    public void EveryOwnShaderLookedUpAtRuntime_IsAlwaysIncluded()
    {
        var guids = RepoShaderGuids();
        string graphics = File.ReadAllText(Client("ProjectSettings", "GraphicsSettings.asset"));
        int start = graphics.IndexOf("m_AlwaysIncludedShaders:", System.StringComparison.Ordinal);
        Assert.True(start >= 0, "m_AlwaysIncludedShaders not found in GraphicsSettings.asset");
        int end = graphics.IndexOf("\n  m_", start + 1, System.StringComparison.Ordinal);
        string included = end < 0 ? graphics.Substring(start) : graphics.Substring(start, end - start);

        var names = Lookups().Select(l => l.Name).Concat(BuildScriptRuntimeShaders()).Distinct();
        var missing = names
            .Where(n => guids.ContainsKey(n) && !included.Contains(guids[n], System.StringComparison.Ordinal))
            .ToList();
        Assert.True(missing.Count == 0,
            "Own shaders used at runtime but missing from GraphicsSettings.asset m_AlwaysIncludedShaders: "
            + string.Join(", ", missing));
    }
}
