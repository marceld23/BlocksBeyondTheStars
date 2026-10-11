// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The Unity-free client rules behind the story UI fixes (#2462): how the objective chip is read
/// (<see cref="ObjectiveSourceRules"/> — B03, the hints mute), the rejoin-proof beat list (<see cref="StoryBeatLog"/> —
/// B08), the mission toast (<see cref="MissionToast"/> — B24) and the WebGL fallback file list, checked against
/// <c>data/</c> because nothing else would notice it drifting (B71).
/// </summary>
public sealed class StoryUiFixesClientTests
{
    // ---- ObjectiveSourceRules (B03) --------------------------------------------------------------------------------

    [Theory]
    [InlineData("", "", "")]
    [InlineData("", "vega.obj.mine", "tutorial")]
    [InlineData("", "story.obj.search", "story")]
    [InlineData("", "story.obj.finale", "finale")] // an older server's finale chip: never treated as optional coaching
    [InlineData("finale", "story.obj.finale", "finale")]
    [InlineData("tutorial", "vega.obj.mine", "tutorial")]
    [InlineData("mission", "mission.x.name", "mission")]
    public void TheEffectiveSource_IsTheServers_OrTheLegacyGuess(string source, string key, string expected)
        => Assert.Equal(expected, ObjectiveSourceRules.Effective(source, key));

    [Fact]
    public void OnlyStoryCoaching_FollowsTheHintsMute()
    {
        Assert.True(ObjectiveSourceRules.HiddenByHintsMute(ObjectiveSources.Story, hintsOn: false));
        Assert.False(ObjectiveSourceRules.HiddenByHintsMute(ObjectiveSources.Story, hintsOn: true));
        foreach (var never in new[] { ObjectiveSources.Tutorial, ObjectiveSources.Finale, ObjectiveSources.Mission, "a_future_pack" })
        {
            Assert.False(ObjectiveSourceRules.HiddenByHintsMute(never, hintsOn: false), never);
        }
    }

    [Fact]
    public void TheFinaleChip_DuringTheTutorial_StillOffersSkip()
    {
        // B03: in the Guardian system the finale is on the chip while the lessons are unfinished — the server says so.
        Assert.True(ObjectiveSourceRules.TutorialRunning(ObjectiveSources.Finale, "story.obj.finale", tutorialActive: true));
        Assert.False(ObjectiveSourceRules.TutorialRunning(ObjectiveSources.Finale, "story.obj.finale", tutorialActive: false));
        Assert.False(ObjectiveSourceRules.TutorialRunning(ObjectiveSources.Story, "story.obj.search", tutorialActive: false));

        // An older server sends neither field: the old prefix guess stands.
        Assert.True(ObjectiveSourceRules.TutorialRunning("", "vega.obj.dock", tutorialActive: false));
        Assert.False(ObjectiveSourceRules.TutorialRunning("", "story.obj.finale", tutorialActive: false));
        Assert.False(ObjectiveSourceRules.TutorialRunning("", "", tutorialActive: false));
    }

    // ---- StoryBeatLog (B08) ----------------------------------------------------------------------------------------

    private static StoryDefinition Pack(string id = "vega_protocol") => new()
    {
        Id = id,
        Beats =
        {
            new StoryBeat { Index = 0, TextKey = "story.b0" },
            new StoryBeat { Index = 1, TextKey = "story.b1" },
            new StoryBeat { Index = 2, TextKey = "story.b2" },
            new StoryBeat { Index = 3, TextKey = "story.b3" },
        },
    };

    [Fact]
    public void TheBeatList_IsRebuiltFromTheSnapshot_InArcOrder()
    {
        var log = new StoryBeatLog();
        log.OnStoryState(Pack(), "vega_protocol", new[] { 2, 0, 1, 9 }); // 9: a beat this pack does not have
        Assert.Equal(new[] { "story.b0", "story.b1", "story.b2" }, log.Keys);
    }

    [Fact]
    public void OnlyBeats_EnterTheBeatList()
    {
        var log = new StoryBeatLog();
        var pack = Pack();
        log.OnStoryState(pack, "vega_protocol", new[] { 0 });
        Assert.True(log.OnStoryLine(pack, "story.b1"));
        Assert.False(log.OnStoryLine(pack, "story.vega.guardian_revealed"));
        Assert.False(log.OnStoryLine(pack, "vega.mem.3"));
        Assert.Equal(new[] { "story.b0", "story.b1" }, log.Keys);
    }

    [Fact]
    public void TheJoinCatchUp_AddsToTheSnapshot_ThatWasSentBeforeIt()
    {
        // The join sends the snapshot first, then the caught-up beats as lines; a later snapshot carries them too.
        var log = new StoryBeatLog();
        var pack = Pack();
        log.OnStoryState(pack, "vega_protocol", Array.Empty<int>());
        log.OnStoryLine(pack, "story.b0");
        log.OnStoryLine(pack, "story.b1");
        Assert.Equal(new[] { "story.b0", "story.b1" }, log.Keys);

        log.OnStoryState(pack, "vega_protocol", new[] { 0, 1, 2 });
        Assert.Equal(new[] { "story.b0", "story.b1", "story.b2" }, log.Keys);
    }

    [Fact]
    public void AnOlderServer_StillGetsThisSessionsBeats()
    {
        // No HeardBeats from an older server: the beats heard live stay, as before #2462.
        var log = new StoryBeatLog();
        var pack = Pack();
        log.OnStoryState(pack, "vega_protocol", null);
        log.OnStoryLine(pack, "story.b0");
        log.OnStoryState(pack, "vega_protocol", null);
        Assert.Equal(new[] { "story.b0" }, log.Keys);
    }

    [Fact]
    public void AStorySwitch_StartsTheListAfresh()
    {
        var log = new StoryBeatLog();
        log.OnStoryState(Pack(), "vega_protocol", new[] { 0, 1 });
        log.OnStoryLine(Pack(), "story.b2");
        log.OnStoryState(null, "none", Array.Empty<int>());
        Assert.Empty(log.Keys);
    }

    [Fact]
    public void WithoutThePack_TheLinesAreCollected_AndBecomeBeatsOnceItIsKnown()
    {
        var log = new StoryBeatLog();
        Assert.True(log.OnStoryLine(null, "story.b1")); // a line before the first snapshot
        Assert.Equal(new[] { "story.b1" }, log.Keys);

        log.OnStoryState(Pack(), "vega_protocol", new[] { 0 });
        Assert.Equal(new[] { "story.b0", "story.b1" }, log.Keys);
    }

    [Fact]
    public void BeatIndexOf_FindsOnlyBeatLines()
    {
        Assert.Equal(3, StoryLog.BeatIndexOf(Pack(), "story.b3"));
        Assert.Equal(-1, StoryLog.BeatIndexOf(Pack(), "story.vega.insight.companion_ward"));
        Assert.Equal(-1, StoryLog.BeatIndexOf(null, "story.b3"));
    }

    // ---- MissionToast (B24) ----------------------------------------------------------------------------------------

    private static readonly Dictionary<string, string> Texts = new()
    {
        [MissionToast.AcceptedKey] = "Mission accepted: {0}",
        [MissionToast.CompletedKey] = "Mission complete: {0}!",
        [MissionToast.PostedKey] = "Mission posted: {0}",
        [MissionToast.UpdatedKey] = "Mission updated: {0}",
        [MissionToast.UnnamedKey] = "(no name)",
        ["mission.board.haul.name"] = "Haul ore",
    };

    private static string Toast(MissionResult result, NetMission? known = null)
        => MissionToast.Text(result, known, k => Texts.TryGetValue(k, out var t) ? t : "[" + k + "]", Texts.ContainsKey);

    [Fact]
    public void TheToast_SaysWhatHappened_AndNamesTheMission()
    {
        Assert.Equal("Mission accepted: Haul ore",
            Toast(new MissionResult { Success = true, MissionId = "b1", Outcome = MissionOutcomes.Accepted, Title = "mission.board.haul.name" }));
        Assert.Equal("Mission complete: Haul ore!",
            Toast(new MissionResult { Success = true, MissionId = "b1", Outcome = MissionOutcomes.Completed, Title = "mission.board.haul.name" }));
        Assert.Equal("Mission posted: Bring me stones",
            Toast(new MissionResult { Success = true, MissionId = "pm_1", Outcome = MissionOutcomes.Posted, Title = "Bring me stones", FreeText = true }));
    }

    [Fact]
    public void AnOlderServer_GetsANeutralToast_NamedFromTheMissionList()
    {
        var known = new NetMission { Id = "b1", Title = "mission.board.haul.name" };
        Assert.Equal("Mission updated: Haul ore", Toast(new MissionResult { Success = true, MissionId = "b1" }, known));
    }

    [Fact]
    public void TheToast_NeverShowsARawKey_OrARawId()
    {
        string text = Toast(new MissionResult { Success = true, MissionId = "raw_id_42", Outcome = MissionOutcomes.Accepted, Title = "mission.unknown.name" });
        Assert.Equal("Mission accepted: (no name)", text);
        Assert.DoesNotContain("raw_id_42", text);
        Assert.DoesNotContain("mission.unknown.name", text);
    }

    // ---- The WebGL fallback list (B71) -----------------------------------------------------------------------------

    /// <summary>The built-in last-resort list in <c>StreamingAssetsCache.cs</c> must name exactly the files of
    /// <c>data/</c> (what the sync copies into <c>StreamingAssets/data</c>). It drifted once unnoticed — a browser build
    /// without <c>manifest.json</c> never fetched <c>lore_sites.json</c> and six more. Read as text: the Unity source
    /// cannot be loaded headless (the UI layout guards' pattern).</summary>
    [Fact]
    public void TheBuiltInFallbackList_MatchesTheDataFolder()
    {
        string src = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "StreamingAssetsCache.cs"));
        int start = src.IndexOf("FallbackManifest =", StringComparison.Ordinal);
        Assert.True(start >= 0, "FallbackManifest not found");
        int end = src.IndexOf("};", start, StringComparison.Ordinal);
        var listed = Regex.Matches(src.Substring(start, end - start), "\"(?<file>[^\"]+)\"", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5))
            .Select(m => m.Groups["file"].Value)
            .ToList();

        string data = ClientTestPaths.DataDir();
        var actual = Directory.GetFiles(data, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(data, f).Replace('\\', '/'))
            .Where(f => !f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && f != "manifest.json")
            .ToList();

        Assert.Equal(listed.Count, listed.Distinct(StringComparer.Ordinal).Count());
        var missing = actual.Except(listed, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var stale = listed.Except(actual, StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0 && stale.Count == 0,
            "StreamingAssetsCache.FallbackManifest is out of sync with data/. Missing: [" + string.Join(", ", missing)
            + "]; no longer present: [" + string.Join(", ", stale) + "]");
    }
}
