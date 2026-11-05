// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3237: every SFX clip in <c>client/Assets/Resources/audio</c> is levelled to its class target by
/// <c>tools/audio/level_clips.py</c>, which records class, measured level, peak and SHA-256 in
/// <c>tools/audio/audio_levels.json</c>. Before that the raw ElevenLabs levels went straight into the game: groups the code
/// plays at the same volume sat 20–48 dB apart (mining stone ~30 dB under mining ore, the scrap pistol nearly silent). This
/// guard keeps it that way — a new or replaced clip fails here until the tool has levelled it.
/// </summary>
public sealed class AudioLevelManifestTests
{
    private static string AudioDir() => Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "audio");

    private static string ManifestPath() => Path.Combine(TestPaths.RepoRoot(), "tools", "audio", "audio_levels.json");

    private const string Fix = "Run: python tools/audio/level_clips.py --apply (or --import <dir> for new clips).";

    [Fact]
    public void EveryClip_IsInTheManifest_AndEveryEntryHasAClip()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var listed = doc.RootElement.GetProperty("clips").EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var shipped = Directory.GetFiles(AudioDir(), "*.mp3").Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);

        var missing = shipped.Except(listed).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var stale = listed.Except(shipped).OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"Clips not levelled yet: {string.Join(", ", missing)}. {Fix}");
        Assert.True(stale.Count == 0, $"Manifest lists clips that no longer exist: {string.Join(", ", stale)}. {Fix}");
    }

    [Fact]
    public void EveryClip_IsTheFileThatWasLevelled()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var changed = new List<string>();
        foreach (var clip in doc.RootElement.GetProperty("clips").EnumerateObject())
        {
            string path = Path.Combine(AudioDir(), clip.Name + ".mp3");
            if (!File.Exists(path))
            {
                continue; // reported by the coverage test
            }

            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            if (!string.Equals(actual, clip.Value.GetProperty("sha256").GetString(), StringComparison.Ordinal))
            {
                changed.Add(clip.Name);
            }
        }

        Assert.True(changed.Count == 0, $"Clips changed since they were levelled: {string.Join(", ", changed)}. {Fix}");
    }

    [Fact]
    public void EveryClip_SitsOnItsClassTarget_OrIsPeakLimited()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var root = doc.RootElement;
        double tolerance = root.GetProperty("tolerance").GetDouble();
        double ceiling = root.GetProperty("peakCeiling").GetDouble();
        var targets = root.GetProperty("classes").EnumerateObject()
            .ToDictionary(c => c.Name, c => c.Value.GetProperty("target").GetDouble(), StringComparer.Ordinal);

        var off = new List<string>();
        foreach (var clip in root.GetProperty("clips").EnumerateObject())
        {
            string cls = clip.Value.GetProperty("class").GetString()!;
            Assert.True(targets.ContainsKey(cls), $"{clip.Name}: unknown class '{cls}'");
            double level = clip.Value.GetProperty("level").GetDouble();
            double peak = clip.Value.GetProperty("peak").GetDouble();
            double target = targets[cls];

            // A click or a gunshot is all peak: once its peak reaches the ceiling more gain would only flatten it.
            bool limited = level < target && peak >= ceiling;
            if (Math.Abs(level - target) > tolerance && !limited)
            {
                off.Add(string.Create(CultureInfo.InvariantCulture, $"{clip.Name} ({cls}: {level:0.0} vs {target:0.0})"));
            }
        }

        Assert.True(off.Count == 0, $"Clips off their class level: {string.Join(", ", off)}. {Fix}");
    }
}
