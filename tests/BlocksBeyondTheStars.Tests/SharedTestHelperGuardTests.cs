// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A test class uses the shared fakes in <c>tests/&lt;project&gt;/Helpers/</c> instead of a private copy of one. #2792
/// (PR #2795) found 79 server test files carrying their own copy of the transport fake and shared it; #2885 folded the 42
/// copies that had grown extras, and the other fakes copied from file to file (the logger, the WorldHost launcher, the
/// scripted AI backend, the HTTP stub, the world-gen probe, the client transport), into <c>Helpers/</c>. This guard scans
/// the top level of both test projects for a class declared under one of those names (old names included) and names the
/// shared file to use instead. <c>Helpers/</c> itself is not scanned — it is the one place such a class belongs.
/// </summary>
public sealed class SharedTestHelperGuardTests
{
    private static readonly string[] Projects = { "BlocksBeyondTheStars.Tests", "BlocksBeyondTheStars.Client.Tests" };

    /// <summary>Every helper name a copy has gone by, and the shared file (under <c>tests/</c>) that replaces it.</summary>
    private static readonly Dictionary<string, string> SharedFileOf = new(StringComparer.Ordinal)
    {
        ["RecordingTransport"] = "BlocksBeyondTheStars.Tests/Helpers/RecordingTransport.cs",
        ["CapturingLogger"] = "BlocksBeyondTheStars.Tests/Helpers/CapturingLogger.cs",
        ["FakeLauncher"] = "BlocksBeyondTheStars.Tests/Helpers/FakeLauncher.cs",
        ["FakeLines"] = "BlocksBeyondTheStars.Tests/Helpers/FakeNpcLines.cs",
        ["LinesProvider"] = "BlocksBeyondTheStars.Tests/Helpers/FakeNpcLines.cs",
        ["FakeNpcLines"] = "BlocksBeyondTheStars.Tests/Helpers/FakeNpcLines.cs",
        ["StubHandler"] = "BlocksBeyondTheStars.Tests/Helpers/StubHttpHandler.cs",
        ["StubHttpHandler"] = "BlocksBeyondTheStars.Tests/Helpers/StubHttpHandler.cs",
        ["Probe"] = "BlocksBeyondTheStars.Tests/Helpers/WorldProbe.cs",
        ["WorldProbe"] = "BlocksBeyondTheStars.Tests/Helpers/WorldProbe.cs",
        ["RecordingClientTransport"] = "BlocksBeyondTheStars.Client.Tests/Helpers/RecordingClientTransport.cs",
    };

    /// <summary>The shared helper files: one per fake, plus the transport's queries.</summary>
    private static readonly string[] HelperFiles = SharedFileOf.Values
        .Append("BlocksBeyondTheStars.Tests/Helpers/RecordingTransportExtensions.cs")
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>A class declaration under a helper name, nested or not, with any modifiers (<c>file</c> included).</summary>
    private static readonly Regex HelperCopy = new(
        @"^\s*(?:(?:public|private|internal|protected|file|static|sealed|partial)\s+)*class\s+(" +
        string.Join('|', SharedFileOf.Keys) + @")\b",
        RegexOptions.Multiline, TimeSpan.FromSeconds(5));

    [Fact]
    public void TestClasses_UseTheSharedHelpers_NotAPrivateCopy()
    {
        var hits = new List<string>();
        foreach (string project in Projects)
        {
            string dir = Path.Combine(TestPaths.RepoRoot(), "tests", project);
            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.Ordinal))
            {
                foreach (Match match in HelperCopy.Matches(File.ReadAllText(file)))
                {
                    string name = match.Groups[1].Value;
                    hits.Add($"{project}/{Path.GetFileName(file)}: {name} — use tests/{SharedFileOf[name]}");
                }
            }
        }

        Assert.True(hits.Count == 0,
            "Use the shared helper in tests/<project>/Helpers/ instead of a private copy; if a test needs more, add the member " +
            "to the shared class, and put a one-off query into a `file static class` of extension methods at the bottom of " +
            "the test file:\n" + string.Join('\n', hits));
    }

    [Fact]
    public void SharedHelpers_LiveInTheHelpersFolder()
    {
        foreach (string file in HelperFiles)
        {
            Assert.True(File.Exists(Path.Combine(TestPaths.RepoRoot(), "tests", file)), "missing shared helper: tests/" + file);
        }
    }
}
