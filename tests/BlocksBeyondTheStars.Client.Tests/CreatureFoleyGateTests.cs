// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3242: creature foley never turns a herd into a drum roll; #3243: the calling rhythm.</summary>
public sealed class CreatureFoleyGateTests
{
    private static FoleyCandidate Grazer(int i, float distance, bool moving = true, string species = "herd", float size = 1.2f)
        => new FoleyCandidate("c" + i, species, size, distance, moving, flier: false);

    [Fact]
    public void AHerdOfTwelve_StaysWithinTheBudget_AndOnlyTheNearestTwoStep()
    {
        var gate = new CreatureFoleyGate();
        var herd = Enumerable.Range(0, 12).Select(i => Grazer(i, 3f + i)).ToList();
        gate.Rank(herd);

        int events = 0;
        var stepped = new HashSet<string>();
        // Ten seconds at 60 fps, every animal planting a foot every frame it can — the worst case.
        for (int frame = 0; frame < 600; frame++)
        {
            float now = frame / 60f;
            foreach (var c in herd)
            {
                if (gate.TryFootfall(c.Id, c.Size, now))
                {
                    events++;
                    stepped.Add(c.Id);
                }
            }
        }

        Assert.Equal(new HashSet<string> { "c0", "c1" }, stepped);
        Assert.True(events <= CreatureFoleyGate.MaxPerSecond * 10, $"{events} footfalls in 10 s");
        Assert.True(events >= 10, "the two nearest must still be heard");
    }

    [Fact]
    public void GlobalBudget_HoldsAcrossSpecies()
    {
        var gate = new CreatureFoleyGate();
        var many = Enumerable.Range(0, 20).Select(i => Grazer(i, 4f, species: "s" + i)).ToList(); // 20 species × 1
        gate.Rank(many);
        int events = 0;
        for (int frame = 0; frame < 60; frame++)
        {
            foreach (var c in many)
            {
                if (gate.TryFootfall(c.Id, c.Size, frame / 60f))
                {
                    events++;
                }
            }
        }

        Assert.True(events <= CreatureFoleyGate.MaxPerSecond, $"{events} events in one second");
    }

    [Fact]
    public void TinyStandingOrFarAnimals_MakeNoFootfalls()
    {
        var gate = new CreatureFoleyGate();
        gate.Rank(new[]
        {
            Grazer(0, 3f, size: 0.4f),          // too small: a mouse does not thud
            Grazer(1, 3f, moving: false),        // standing still
            Grazer(2, 40f, species: "far"),      // out of range
        });
        Assert.False(gate.TryFootfall("c0", 0.4f, 1f));
        Assert.False(gate.TryFootfall("c1", 1.2f, 1f));
        Assert.False(gate.TryFootfall("c2", 1.2f, 1f));
    }

    [Fact]
    public void AnAnimal_StepsOncePerGaitCycle_BigBodiesSlower()
    {
        var gate = new CreatureFoleyGate();
        gate.Rank(new[] { Grazer(0, 3f, size: 1f) });
        Assert.True(gate.TryFootfall("c0", 1f, 0f));
        Assert.False(gate.TryFootfall("c0", 1f, CreatureFoleyGate.FootfallInterval(1f) * 0.5f));
        Assert.True(gate.TryFootfall("c0", 1f, CreatureFoleyGate.FootfallInterval(1f) + 0.01f));
        Assert.True(CreatureFoleyGate.FootfallInterval(3f) > CreatureFoleyGate.FootfallInterval(1f));
        Assert.True(CreatureFoleyGate.FootfallPitch(3f) < CreatureFoleyGate.FootfallPitch(0.8f));
    }

    [Fact]
    public void OnlyTheTwoNearestFliers_BeatTheirWings()
    {
        var gate = new CreatureFoleyGate();
        gate.Rank(Enumerable.Range(0, 5).Select(i => new FoleyCandidate("f" + i, "bird", 1f, 5f + i, true, flier: true)));
        Assert.True(gate.TryWing("f0", 0f));
        Assert.True(gate.TryWing("f1", 0.2f));
        Assert.False(gate.TryWing("f2", 0.4f));
        Assert.False(gate.TryWing("f0", 0.3f)); // its own spacing
    }

    [Theory]
    [InlineData("Diurnal", 0.05f, 0.55f)]   // dawn chorus
    [InlineData("Diurnal", 0.8f, 1f)]       // midday
    [InlineData("Diurnal", -0.8f, 2.2f)]    // night: rare
    [InlineData("Nocturnal", -0.8f, 0.85f)]
    [InlineData("Crepuscular", -0.1f, 0.5f)]
    [InlineData("Cathemeral", -0.8f, 1f)]
    public void TheHour_ShapesTheCallingRate(string activity, float sun, float expected)
        => Assert.Equal(expected, CreatureCallRhythm.CadenceFactor(activity, sun, rain: false, storm: false), 3);

    [Fact]
    public void RainHushes_AStormNearlySilences_AndItsEndBringsABurst()
    {
        Assert.Equal(2.5f, CreatureCallRhythm.CadenceFactor("Diurnal", 0.8f, rain: true, storm: false), 3);
        Assert.Equal(5f, CreatureCallRhythm.CadenceFactor("Diurnal", 0.8f, rain: true, storm: true), 3);
        var r = new CreatureCallRhythm();
        r.Observe(storm: true, 10f);
        Assert.Equal(1f, r.BurstFactor(11f));
        r.Observe(storm: false, 20f);
        Assert.Equal(0.5f, r.BurstFactor(21f));
        Assert.Equal(1f, r.BurstFactor(20f + CreatureCallRhythm.BurstSeconds + 1f));
    }

    [Fact]
    public void BiggerGroups_AnswerMoreOften_ButNeverAlways()
    {
        Assert.True(CreatureCallRhythm.AnswerChance(6) > CreatureCallRhythm.AnswerChance(0));
        Assert.True(CreatureCallRhythm.AnswerChance(50) <= 0.4f);
    }
}
