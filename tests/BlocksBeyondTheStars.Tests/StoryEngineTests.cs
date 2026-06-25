// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// P0 — the story-agnostic engine: the weighted progress score and threshold-driven beat reveal that let a
/// linear arc work in a procedurally-ordered world. Pure logic, no server/persistence/networking.
/// </summary>
public class StoryEngineTests
{
    private static StoryDefinition Def(params int[] thresholds) => new()
    {
        Id = "test",
        FragmentWeight = 3,
        KillWeight = 1,
        MilestoneWeight = 2,
        KillContributionCap = 40,
        Beats = thresholds.Select((t, i) => new StoryBeat { Index = i, Threshold = t, TextKey = "k" + i }).ToList(),
    };

    [Fact]
    public void Progress_is_the_weighted_sum()
    {
        var def = Def(0);
        var s = new StoryState { FragmentsFound = 4, MachineKills = 5, Milestones = 2 };
        // 4*3 + 5*1 + 2*2 = 21
        Assert.Equal(21, StoryEngine.Progress(def, s));
    }

    [Fact]
    public void Machine_kills_contribution_is_capped()
    {
        var def = Def(0);
        var s = new StoryState { MachineKills = 1000 };
        Assert.Equal(def.KillContributionCap * def.KillWeight, StoryEngine.Progress(def, s));
    }

    [Fact]
    public void Negative_counters_never_reduce_progress_below_zero()
    {
        var def = Def(0);
        var s = new StoryState { FragmentsFound = -5, MachineKills = -5, Milestones = -5 };
        Assert.Equal(0, StoryEngine.Progress(def, s));
    }

    [Fact]
    public void First_beat_reveals_at_zero_progress()
    {
        var def = Def(0, 100);
        var s = new StoryState();
        var newly = StoryEngine.AdvanceBeats(def, s);
        Assert.Single(newly);
        Assert.Equal(0, newly[0].Index);
        Assert.Equal(1, s.BeatsRevealed);
    }

    [Fact]
    public void Beats_reveal_in_order_as_progress_rises_and_are_monotonic()
    {
        var def = Def(0, 6, 14);
        var s = new StoryState();
        Assert.Single(StoryEngine.AdvanceBeats(def, s));   // progress 0 -> B0
        Assert.Empty(StoryEngine.AdvanceBeats(def, s));    // no change -> nothing

        s.FragmentsFound = 2;                              // progress 6 -> B1
        var r = StoryEngine.AdvanceBeats(def, s);
        Assert.Single(r);
        Assert.Equal(1, r[0].Index);

        s.FragmentsFound = 100;                            // far past -> remaining in order
        Assert.Equal(new[] { 2 }, StoryEngine.AdvanceBeats(def, s).Select(b => b.Index).ToArray());
        Assert.True(StoryEngine.AllBeatsRevealed(def, s));
    }

    [Fact]
    public void Crossing_multiple_thresholds_at_once_reveals_them_all_in_order()
    {
        var def = Def(0, 6, 14, 24);
        var s = new StoryState { FragmentsFound = 100 };
        Assert.Equal(new[] { 0, 1, 2, 3 }, StoryEngine.AdvanceBeats(def, s).Select(b => b.Index).ToArray());
        Assert.Empty(StoryEngine.AdvanceBeats(def, s));    // idempotent afterwards
    }

    [Fact]
    public void Kills_alone_can_advance_the_story_but_stay_capped()
    {
        var def = Def(0, 30, 60);
        var s = new StoryState { MachineKills = 40 };       // capped at 40 -> progress 40
        Assert.Equal(new[] { 0, 1 }, StoryEngine.AdvanceBeats(def, s).Select(b => b.Index).ToArray());

        s.MachineKills = 1000;                              // still capped -> 60 unreachable by kills alone
        Assert.Empty(StoryEngine.AdvanceBeats(def, s));
    }

    [Fact]
    public void Vega_protocol_pack_has_ordered_monotonic_thresholds()
    {
        var def = StoryRegistry.Default;
        Assert.Equal("vega_protocol", def.Id);
        Assert.NotEmpty(def.Beats);
        Assert.Equal(0, def.Beats[0].Threshold);
        for (int i = 0; i < def.Beats.Count; i++)
        {
            Assert.Equal(i, def.Beats[i].Index);
            Assert.False(string.IsNullOrEmpty(def.Beats[i].TextKey));
            if (i > 0)
            {
                Assert.True(def.Beats[i].Threshold >= def.Beats[i - 1].Threshold);
            }
        }
    }

    [Fact]
    public void Vega_protocol_reveals_all_beats_under_enough_progress()
    {
        var def = StoryRegistry.Default;
        var s = new StoryState { StoryId = def.Id, FragmentsFound = 1000 };
        StoryEngine.AdvanceBeats(def, s);
        Assert.True(StoryEngine.AllBeatsRevealed(def, s));
        Assert.Equal(def.Beats.Count, s.BeatsRevealed);
    }

    [Fact]
    public void Registry_lookup_and_none_sentinel()
    {
        Assert.True(StoryRegistry.IsStoryActive("vega_protocol"));
        Assert.False(StoryRegistry.IsStoryActive("none"));
        Assert.False(StoryRegistry.IsStoryActive(""));
        Assert.False(StoryRegistry.IsStoryActive("does_not_exist"));

        Assert.True(StoryRegistry.TryGet("vega_protocol", out var def));
        Assert.Equal("vega_protocol", def.Id);
        Assert.False(StoryRegistry.TryGet("nope", out var fallback));
        Assert.Equal(StoryRegistry.Default.Id, fallback.Id); // unknown id falls back to the default pack
    }
}
