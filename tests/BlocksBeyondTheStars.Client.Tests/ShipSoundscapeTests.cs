// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using static BlocksBeyondTheStars.Client.ShipSoundscape;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3247: the ship as a place, by ear. One synthetic hum played for every ship, its modules were silent, a landing made
/// no sound once the engines were off and the parked hull was mute from outside. <see cref="ShipSoundscape"/> now derives
/// the hum from the design, places quiet module emitters under a budget, schedules the cooling ticks of a landing, fades
/// the outside idle hum and paces the creaks of a badly damaged hull.
/// </summary>
public sealed class ShipSoundscapeTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static readonly string[] StarterModules = { "cockpit", "reactor", "life_support", "medbay", "quarters", "cargo_hold_basic" };

    /// <summary>The starter's box hull (5 × 5 × 7) at a world origin, its two rear engines and its stations.</summary>
    private static readonly ShipBox Box = new(100f, 40f, 200f, 5, 5, 7);

    private static readonly (float X, float Z)[] RearEngines = { (101.5f, 199.5f), (103.5f, 199.5f) };

    private static readonly StationSpot[] BoxStations =
    {
        new("medbay", 101.5f, 41f, 204.5f),
        new("cockpit", 102.5f, 41f, 206.5f),
        new("cargo", 101.5f, 41f, 203.5f),
        new("workshop", 103.5f, 41f, 203.5f),
    };

    [Fact]
    public void BiggerHull_HumsDeeper_DarkerAndNoQuieter()
    {
        var scout = HumFor(5, 4, 5, StarterModules);
        var hammerhead = HumFor(14, 4, 15, StarterModules);

        Assert.True(hammerhead.BaseHz < scout.BaseHz, $"hammerhead {hammerhead.BaseHz} Hz vs scout {scout.BaseHz} Hz");
        Assert.True(hammerhead.Brightness < scout.Brightness, "a big hull sounds darker");
        Assert.True(hammerhead.Level >= scout.Level, "a big hull hums no quieter");
        Assert.Equal(SmallShipHz, HumFor(1, 1, 1, StarterModules).BaseHz);
        Assert.Equal(LargeShipHz, HumFor(60, 20, 60, StarterModules).BaseHz);
    }

    [Fact]
    public void EveryStockShip_HumsInsideTheRange_OnTheLoopGrid()
    {
        Assert.NotEmpty(Content.Ships);
        foreach (var ship in Content.Ships.Values)
        {
            var layout = Content.GetShipLayout(ship.Layout);
            int w = layout?.Width ?? ship.InteriorWidth, h = layout?.Height ?? ship.Height + 1, l = layout?.Length ?? ship.InteriorLength;
            var hum = HumFor(w, h, l, ship.StartModules);

            Assert.InRange(hum.BaseHz, LargeShipHz, SmallShipHz);
            Assert.Equal(0f, hum.BaseHz % HumHzStep, 3); // whole cycles per loop → no seam
            Assert.InRange(hum.Level, 0.9f, 1.1f);       // every hull stays at about the old hum's level
            Assert.True(hum.Reactor, $"{ship.Key}: the reactor is mandatory and pulses");
            Assert.True(hum.Air > 0f, $"{ship.Key}: the life support is mandatory and breathes");
        }
    }

    [Fact]
    public void Modules_AddTheirLayers_AndAnUnknownListAssumesTheMandatoryOnes()
    {
        var bare = HumFor(5, 5, 7, new[] { "cockpit" });
        Assert.False(bare.Reactor);
        Assert.Equal(0f, bare.Air);
        Assert.False(bare.JumpCoil);

        var full = HumFor(5, 5, 7, new[] { "cockpit", "reactor", "life_support", "oxygen_generator", "jump_generator" });
        Assert.True(full.Reactor);
        Assert.True(full.JumpCoil);
        Assert.True(full.Air > HumFor(5, 5, 7, StarterModules).Air, "an oxygen generator deepens the air cycle");

        var unknown = HumFor(5, 5, 7, null);
        Assert.True(unknown.Reactor);
        Assert.True(unknown.Air > 0f);
        Assert.False(unknown.JumpCoil);
        Assert.True(unknown.SameAs(HumFor(5, 5, 7, StarterModules)));
        Assert.False(unknown.SameAs(full));
    }

    [Fact]
    public void SynthesizedHum_LoopsWithoutASeam_AtTheOldHumsLoudness()
    {
        foreach (var p in new[] { HumFor(5, 4, 5, null), HumFor(14, 4, 15, new[] { "reactor", "life_support", "jump_generator" }), HumFor(5, 5, 7, new[] { "cockpit" }) })
        {
            var data = SynthesizeHum(p);
            Assert.Equal((int)(HumSampleRate * HumLoopSeconds), data.Length);

            float maxStep = 0f;
            for (int i = 1; i < data.Length; i++)
            {
                maxStep = Math.Max(maxStep, Math.Abs(data[i] - data[i - 1]));
            }

            float seam = Math.Abs(data[0] - data[^1]);
            Assert.True(seam <= maxStep * 1.05f, $"{p.BaseHz} Hz: the seam jumps {seam}, the largest step inside is {maxStep}");

            double rms = Math.Sqrt(data.Sum(s => (double)s * s) / data.Length);
            Assert.InRange(rms, HumRms * 0.97, HumRms * 1.03);
            Assert.True(data.Max(s => Math.Abs(s)) <= 0.95f, "no clipping");
            Assert.Equal(data, SynthesizeHum(p)); // deterministic
        }
    }

    [Fact]
    public void Emitters_PutTheReactorAtTheEngines_LifeSupportUpTop_AndTheStationsWhereTheyStand()
    {
        var emitters = EmittersFor(Box, BoxStations, StarterModules, RearEngines);

        var reactor = Assert.Single(emitters, e => e.Clip == ReactorLoop);
        Assert.True(reactor.Z < Box.CentreZ, "the reactor sits towards the engines at the rear");
        Assert.InRange(reactor.X, Box.MinX + 1f, Box.MinX + Box.Width - 1f);
        Assert.InRange(reactor.Z, Box.MinZ + 1f, Box.MinZ + Box.Length - 1f); // pulled inside the hull

        var air = Assert.Single(emitters, e => e.Clip == LifeSupportLoop);
        Assert.True(air.Y > reactor.Y, "the air cycle comes from the ceiling vents");

        var medbay = Assert.Single(emitters, e => e.Clip == MedbayLoop);
        Assert.Equal((101.5f, 204.5f), (medbay.X, medbay.Z));
        var cargo = Assert.Single(emitters, e => e.Kind == EmitterKind.Creaks);
        Assert.Equal((101.5f, 203.5f), (cargo.X, cargo.Z));
        Assert.Equal(4, emitters.Count); // the cockpit and the workshop have no emitter
        Assert.All(emitters, e => Assert.True(e.Gain < HumBedShare, $"{e.Clip} stays below the hum"));

        var stripped = EmittersFor(Box, Array.Empty<StationSpot>(), new[] { "cockpit" }, RearEngines);
        Assert.Empty(stripped); // no reactor or life support fitted, no stations → silence
        var noEngines = EmittersFor(Box, Array.Empty<StationSpot>(), null, Array.Empty<(float, float)>());
        Assert.Equal(2, noEngines.Count); // both mandatory modules assumed; the reactor goes to the rear quarter
        Assert.True(noEngines[0].Z < Box.CentreZ);
        Assert.True(SameLayout(emitters, EmittersFor(Box, BoxStations, StarterModules, RearEngines)));
        Assert.False(SameLayout(emitters, stripped));
    }

    [Fact]
    public void Picks_AreTheNearestThreeInReach_FadedByDistance()
    {
        var emitters = new List<Emitter>
        {
            new("a", EmitterKind.Loop, 1f, 0f, 0f, 0.3f),
            new("b", EmitterKind.Loop, 3f, 0f, 0f, 0.3f),
            new("c", EmitterKind.Loop, 0f, 0f, 5f, 0.3f),
            new("d", EmitterKind.Loop, 0f, 6f, 0f, 0.3f),
            new("e", EmitterKind.Loop, 9f, 0f, 0f, 0.3f), // out of reach
        };
        var picks = new List<EmitterPick>();
        PickEmitters(emitters, 0f, 0f, 0f, picks);

        Assert.Equal(new[] { 0, 1, 2 }, picks.Select(p => p.Index));
        Assert.Equal(0.3f, picks[0].Volume, 4); // within the full-volume radius
        Assert.True(picks[1].Volume < picks[0].Volume && picks[2].Volume < picks[1].Volume, "quieter with distance");

        PickEmitters(emitters, 20f, 0f, 0f, picks);
        Assert.Empty(picks);
        Assert.Equal(0f, EmitterVolume(1f, EmitterReach));
        Assert.Equal(1f, EmitterVolume(1f, EmitterFullWithin));
    }

    [Fact]
    public void Voices_NeverExceedTheBudget_AndOnlySwitchAfterFadingOut()
    {
        var voices = new EmitterVoices();
        var started = new List<int>();
        var picks = new List<EmitterPick> { new(0, 0.3f, 1f), new(1, 0.2f, 2f), new(2, 0.1f, 3f) };
        voices.Tick(picks, 0.1f, started);
        Assert.Equal(3, started.Count);
        Assert.Equal(MaxEmitters, voices.Count);

        // A fourth emitter comes closer: it must wait until a dropped voice has faded out.
        picks = new List<EmitterPick> { new(3, 0.3f, 0.5f), new(0, 0.3f, 1f), new(1, 0.2f, 2f) };
        voices.Tick(picks, 0.05f, started);
        Assert.Empty(started);
        Assert.Contains(2, Enumerable.Range(0, voices.Count).Select(voices.EmitterOf)); // still fading out
        int frames = 0;
        while (!Enumerable.Range(0, voices.Count).Select(voices.EmitterOf).Contains(3) && frames++ < 200)
        {
            voices.Tick(picks, 0.05f, started);
            Assert.True(Enumerable.Range(0, voices.Count).Count(v => voices.EmitterOf(v) >= 0) <= MaxEmitters);
        }

        Assert.DoesNotContain(2, Enumerable.Range(0, voices.Count).Select(voices.EmitterOf));
        Assert.Contains(0, Enumerable.Range(0, voices.Count).Select(voices.EmitterOf)); // the held ones never moved
        for (int i = 0; i < 40; i++)
        {
            voices.Tick(picks, 0.05f, started);
        }

        int held = Enumerable.Range(0, voices.Count).First(v => voices.EmitterOf(v) == 0);
        Assert.Equal(0.3f, voices.LevelOf(held), 4);

        voices.Reset();
        Assert.All(Enumerable.Range(0, voices.Count), v => Assert.Equal(-1, voices.EmitterOf(v)));
    }

    [Fact]
    public void Picks_KeepAPlayingEmitter_UntilAnotherIsClearlyNearer()
    {
        var emitters = new List<Emitter>
        {
            new("a", EmitterKind.Loop, 1f, 0f, 0f, 0.3f),
            new("b", EmitterKind.Loop, -1.5f, 0f, 0f, 0.3f),
            new("c", EmitterKind.Loop, 0f, 0f, 5f, 0.3f),
            new("d", EmitterKind.Loop, 0f, 0f, -5.5f, 0.3f),
        };
        var voices = new EmitterVoices();
        var picks = new List<EmitterPick>();
        var started = new List<int>();
        PickEmitters(emitters, 0f, 0f, 0f, picks, voices);
        voices.Tick(picks, 0.1f, started);
        Assert.True(voices.Holds(2) && !voices.Holds(3));

        // The player steps half a metre towards d: it is nearer now, but not by the stickiness — c keeps playing.
        PickEmitters(emitters, 0f, 0f, -0.5f, picks, voices);
        Assert.Contains(2, picks.Select(p => p.Index));
        Assert.DoesNotContain(3, picks.Select(p => p.Index));
        Assert.Equal(5.5f, picks.Single(p => p.Index == 2).Distance, 4); // the volume still fades by the true distance

        // Two metres on, d is clearly nearer and takes over.
        PickEmitters(emitters, 0f, 0f, -1.5f, picks, voices);
        Assert.Contains(3, picks.Select(p => p.Index));
        Assert.DoesNotContain(2, picks.Select(p => p.Index));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(3247)]
    [InlineData(987654)]
    public void CoolingTicks_AreSparse_SlowDown_AndStop(int seed)
    {
        var ticks = CoolingSchedule(seed);

        Assert.InRange(ticks.Count, 6, 25);
        Assert.True(ticks[0].At < 1.5f, "the first ping follows the thump");
        Assert.All(ticks, t => Assert.InRange(t.At, 0f, CoolingSeconds));
        for (int i = 1; i < ticks.Count; i++)
        {
            Assert.True(ticks[i].At - ticks[i - 1].At >= CoolingMinGap - 1e-4f, $"two pings {ticks[i].At - ticks[i - 1].At:0.00} s apart");
        }

        var early = Gaps(ticks).Take(4).Average();
        var late = Gaps(ticks).Skip(Math.Max(0, ticks.Count - 5)).Average();
        Assert.True(late > early * 1.8f, $"the pings slow down: early gaps {early:0.00} s, late {late:0.00} s");
        Assert.True(ticks[^1].Volume < ticks[0].Volume, "the last pings are softer");
        Assert.All(ticks, t => Assert.InRange(t.Variant, 0, CoolingTickClips.Length - 1));
        Assert.Equal(ticks.Select(t => t.At), CoolingSchedule(seed).Select(t => t.At));
    }

    [Fact]
    public void HullCreaks_OnlyBelowThirtyPercent_RareAndMoreOftenTheWorseItIs()
    {
        var rng = new Random(5);
        Assert.Equal(float.PositiveInfinity, NextHullCreakDelay(1f, rng));
        Assert.Equal(float.PositiveInfinity, NextHullCreakDelay(CreakBelowHull, rng));

        var nearLine = Enumerable.Range(0, 200).Select(_ => NextHullCreakDelay(0.29f, rng)).ToList();
        var wrecked = Enumerable.Range(0, 200).Select(_ => NextHullCreakDelay(0.02f, rng)).ToList();
        Assert.All(nearLine.Concat(wrecked), d => Assert.True(d >= CreakMinGap, $"{d} s"));
        Assert.True(wrecked.Average() < nearLine.Average(), "a worse hull creaks more often");
        Assert.All(Enumerable.Range(0, 50).Select(_ => NextCargoCreakDelay(rng)), d => Assert.InRange(d, 25f, 55f));
        Assert.True(IsDead(0f));
        Assert.False(IsDead(0.05f));
    }

    [Fact]
    public void ExteriorIdle_IsQuiet_AndGoneBeyondFifteenMetres()
    {
        Assert.Equal(ExteriorLevel, ExteriorIdleVolume(0f));
        Assert.Equal(ExteriorLevel, ExteriorIdleVolume(ExteriorFullWithin));
        Assert.True(ExteriorIdleVolume(9f) is > 0f and < ExteriorLevel);
        Assert.Equal(0f, ExteriorIdleVolume(ExteriorReach));
        Assert.Equal(0f, ExteriorIdleVolume(40f));
        Assert.True(ExteriorLevel < HumBedShare, "outside it is quieter than the hum aboard");
    }

    private static IEnumerable<float> Gaps(IReadOnlyList<CoolingTick> ticks)
    {
        for (int i = 1; i < ticks.Count; i++)
        {
            yield return ticks[i].At - ticks[i - 1].At;
        }
    }
}
