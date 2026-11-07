// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Sounders;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The hum of Soaniri's singing plants (#3358, #3367) and the Quali shimmer's gate: a strict budget of three voices, the
/// nearest plants of different kinds first, a smooth fade by distance, silence near a Soundrak — and the shimmer at space
/// anomalies only once the player's Sounders pack reached chapter 5.
/// </summary>
public sealed class SingingPlantChorusTests
{
    private static SingingPlantChorus.Plant P(float x, string loop) => new(x, 0f, 0f, loop);

    private static readonly (float X, float Y, float Z)[] NoSoundrak = System.Array.Empty<(float, float, float)>();

    [Fact]
    public void EachSingingPlant_HumsItsOwnLoop_AndTheEchoFlowerOnlyAnswers()
    {
        Assert.Equal(SingingPlantChorus.ChimeReedLoop, SingingPlantChorus.LoopFor(FloraCatalog.ChimeReedKey));
        Assert.Equal(SingingPlantChorus.BellBloomLoop, SingingPlantChorus.LoopFor(FloraCatalog.BellBloomKey));
        Assert.Equal(SingingPlantChorus.BellBloomLoop, SingingPlantChorus.LoopFor(FloraCatalog.BellBloomOpenKey));
        Assert.Equal(SingingPlantChorus.HarpVineLoop, SingingPlantChorus.LoopFor(FloraCatalog.HarpVineKey));
        Assert.Null(SingingPlantChorus.LoopFor(FloraCatalog.EchoFlowerKey));
        Assert.Null(SingingPlantChorus.LoopFor("flora_fern"));
        Assert.Null(SingingPlantChorus.LoopFor(null));
        Assert.True(SingingPlantChorus.Sings(new PlanetType { SingingMeadows = true }));
        Assert.False(SingingPlantChorus.Sings(new PlanetType()));
        Assert.False(SingingPlantChorus.Sings(null));
    }

    [Fact]
    public void AtMostThreeSing_DifferentKindsFirst_NearestFirst()
    {
        var plants = new List<SingingPlantChorus.Plant>
        {
            P(1f, SingingPlantChorus.ChimeReedLoop), P(1.5f, SingingPlantChorus.ChimeReedLoop), P(2f, SingingPlantChorus.ChimeReedLoop),
            P(6f, SingingPlantChorus.BellBloomLoop), P(8f, SingingPlantChorus.HarpVineLoop), P(30f, SingingPlantChorus.BellBloomLoop),
        };
        var voices = new List<SingingPlantChorus.Voice>();
        SingingPlantChorus.Pick(plants, 0f, 0f, 0f, NoSoundrak, voices);
        Assert.Equal(new[] { 0, 3, 4 }, voices.Select(v => v.Plant));
        Assert.Equal(1f, voices[0].Level);
        Assert.InRange(voices[2].Level, 0.01f, 0.5f);

        plants.RemoveAt(4); // no vine: a second reed fills the third voice
        SingingPlantChorus.Pick(plants, 0f, 0f, 0f, NoSoundrak, voices);
        Assert.Equal(new[] { 0, 3, 1 }, voices.Select(v => v.Plant));
        Assert.True(voices.Count <= SingingPlantChorus.MaxVoices);
    }

    [Fact]
    public void TheLevel_FadesSmoothlyToTheReach()
    {
        Assert.Equal(1f, SingingPlantChorus.LevelAt(0f));
        Assert.Equal(1f, SingingPlantChorus.LevelAt(SingingPlantChorus.FullWithin));
        Assert.Equal(0f, SingingPlantChorus.LevelAt(SingingPlantChorus.Reach));
        float last = 1f;
        for (float d = SingingPlantChorus.FullWithin; d <= SingingPlantChorus.Reach; d += 0.5f)
        {
            float level = SingingPlantChorus.LevelAt(d);
            Assert.True(level <= last);
            last = level;
        }
    }

    [Fact]
    public void ASoundrakNearby_SilencesThePlantsAroundIt()
    {
        var plants = new List<SingingPlantChorus.Plant> { P(1f, SingingPlantChorus.ChimeReedLoop), P(-2f, SingingPlantChorus.BellBloomLoop) };
        var voices = new List<SingingPlantChorus.Voice>();
        SingingPlantChorus.Pick(plants, 0f, 0f, 0f, new[] { (12f, 0f, 0f) }, voices);
        Assert.Empty(voices); // both within the Soundrak's silence — the meadow's warning

        SingingPlantChorus.Pick(plants, 0f, 0f, 0f, new[] { (60f, 0f, 0f) }, voices);
        Assert.Equal(2, voices.Count);

        Assert.True(SingingPlantChorus.IsSoundrak(new NetCreature { SpeciesId = SoundersFauna.SpeciesIdOf(SoundersIds.Soundrak) }));
        Assert.False(SingingPlantChorus.IsSoundrak(new NetCreature { SpeciesId = SoundersFauna.SpeciesIdOf(SoundersIds.Soundi) }));
        Assert.False(SingingPlantChorus.IsSoundrak(null));
    }

    [Fact]
    public void TheQualiShimmer_WaitsForChapterFive_OfAnActiveSoundersPack()
    {
        static StoryPackStates States(bool enabled, bool started, bool optedOut, params string[] states)
            => new()
            {
                Packs = new[]
                {
                    new NetStoryPack
                    {
                        Id = SoundersIds.PackId,
                        Enabled = enabled,
                        Started = started,
                        OptedOut = optedOut,
                        Chapters = states.Select((s, i) => new NetStoryChapter { Id = "s" + (i + 1), State = s }).ToArray(),
                    },
                },
            };

        const string A = StoryChapterStates.Active, D = StoryChapterStates.Done, L = StoryChapterStates.Locked;
        Assert.False(SoundersPackProgress.QualiShimmer(null));
        Assert.False(SoundersPackProgress.QualiShimmer(new StoryPackStates()));
        Assert.False(SoundersPackProgress.QualiShimmer(States(true, true, false, D, D, D, A, L, L, L, L)));
        Assert.True(SoundersPackProgress.QualiShimmer(States(true, true, false, D, D, D, D, A, L, L, L)));
        Assert.True(SoundersPackProgress.QualiShimmer(States(true, true, false, D, D, D, D, D, D, A, L)));
        Assert.False(SoundersPackProgress.QualiShimmer(States(true, true, true, D, D, D, D, A, L, L, L))); // switched off
        Assert.False(SoundersPackProgress.QualiShimmer(States(false, true, false, D, D, D, D, A, L, L, L))); // the world keeps it off
        Assert.False(SoundersPackProgress.QualiShimmer(States(true, false, false)));
    }
}
