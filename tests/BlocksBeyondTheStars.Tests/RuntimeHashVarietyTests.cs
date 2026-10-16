// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2687: the runtime-only per-individual picks (animal size, pack flanking, per-species gait jitter, taming rolls, enemy
/// refill cadence, drone strafe phase) come from the well-mixed <c>StableHashing</c> hash. The old h·31 string hash of
/// keys with a trailing counter (<c>e{n}</c>, <c>sp{n}</c>, <c>{location}:{ordinal}</c>) moved by a few units per step,
/// so consecutive spawns got the same size, the same flank line, a period-three taming pattern and a constant gait
/// jitter. These are spread checks over consecutive keys — the exact values are free to change.
/// </summary>
public sealed class RuntimeHashVarietyTests
{
    private static IEnumerable<string> EntityIds(int from, int count) => Enumerable.Range(from, count).Select(i => "e" + i);

    [Fact]
    public void FaunaSize_SixConsecutiveSpawns_AreDistinct_WithASensibleSpread()
    {
        // A herd is spawned in one go, so its members get consecutive entity ids.
        var herd = EntityIds(1, 6).Select(SvGameServer.FaunaSizeScaleForTest).ToArray();

        Assert.Equal(6, herd.Select(s => Math.Round(s, 4)).Distinct().Count());
        Assert.True(herd.Max() - herd.Min() > 0.08f, $"a herd should mix sizes, got {string.Join(", ", herd)}");

        // …and that is not luck of the first herd: hardly any six-id run comes out uniform.
        int uniformHerds = Enumerable.Range(0, 200)
            .Count(k =>
            {
                var sizes = EntityIds(k * 6, 6).Select(SvGameServer.FaunaSizeScaleForTest).ToArray();
                return sizes.Max() - sizes.Min() < 0.04f;
            });
        Assert.True(uniformHerds <= 4, $"{uniformHerds}/200 herds were all one size");
    }

    [Fact]
    public void FaunaSize_IsABell_WithinPlusMinus30Percent_WithRareRuntsAndGiants()
    {
        var sizes = EntityIds(0, 4000).Select(SvGameServer.FaunaSizeScaleForTest).ToArray();

        Assert.All(sizes, s => Assert.InRange(s, 0.7f, 1.3f));
        Assert.InRange(sizes.Average(), 0.98, 1.02);

        // The average of two uniforms: ~75 % within ±15 %, ~12.5 % above 1.15 (giants) and ~12.5 % below 0.85 (runts),
        // ~2 % beyond ±24 %. The old hash never produced the top bin at all.
        double near = sizes.Count(s => Math.Abs(s - 1f) <= 0.15f) / (double)sizes.Length;
        double giants = sizes.Count(s => s > 1.15f) / (double)sizes.Length;
        double runts = sizes.Count(s => s < 0.85f) / (double)sizes.Length;
        Assert.InRange(near, 0.68, 0.82);
        Assert.InRange(giants, 0.08, 0.17);
        Assert.InRange(runts, 0.08, 0.17);
        Assert.Contains(sizes, s => s > 1.24f);
        Assert.Contains(sizes, s => s < 0.76f);
    }

    [Fact]
    public void FaunaSize_IsStablePerIndividual()
    {
        Assert.Equal(SvGameServer.FaunaSizeScaleForTest("e42"), SvGameServer.FaunaSizeScaleForTest("e42"));
    }

    [Fact]
    public void FlankAngles_OfAPackSpawnedTogether_SpreadAroundTheTarget()
    {
        var pack = EntityIds(100, 6).Select(SvGameServer.FlankSpreadForTest).ToArray();
        Assert.All(pack, a => Assert.InRange(a, -0.7f, 0.7f));
        Assert.True(pack.Max() - pack.Min() > 0.5f, $"a pack should fan out, got {string.Join(", ", pack)}");

        // Over many hunters the ±0.7 rad window is used evenly (7 bins of 0.2 rad, ~143 each) — not a sliver of it.
        var many = EntityIds(0, 1000).Select(SvGameServer.FlankSpreadForTest).ToArray();
        var bins = new int[7];
        foreach (float a in many)
        {
            bins[Math.Min(6, (int)((a + 0.7f) / 0.2f))]++;
        }

        Assert.All(bins, n => Assert.InRange(n, 95, 195));

        // Neighbouring ids no longer sit a hair apart (the old hash: 0.0014 rad).
        int huddled = Enumerable.Range(0, 999).Count(i => Math.Abs(many[i] - many[i + 1]) < 0.02f);
        Assert.True(huddled < 60, $"{huddled}/999 neighbouring hunters took nearly the same line");
    }

    [Fact]
    public void GaitJitter_DiffersBetweenSpecies_OfTheSameStyle()
    {
        // Thirty roster slots of one world with the very same style, size and speed: only the per-species jitter
        // separates their profiles (HoldMin = the Grazer's 1.2 s × jitter).
        var jitters = Enumerable.Range(0, 30)
            .Select(i => LocomotionController.ForSpecies(new CreatureSpecies
            {
                Id = "sp" + i,
                LocoStyle = LocomotionStyle.Grazer,
                Size = 1f,
                Speed = 2f,
            }).HoldMin / 1.2f)
            .ToArray();

        Assert.All(jitters, j => Assert.InRange(j, 0.849f, 1.151f));
        Assert.True(jitters.Select(j => Math.Round(j, 4)).Distinct().Count() >= 28,
            $"species should move differently, got {string.Join(", ", jitters)}");
        Assert.True(jitters.Max() - jitters.Min() > 0.15f, "the ±15 % jitter should actually be used");

        // The old hash gave one constant for sp0–sp9 (and another for sp10–sp29).
        Assert.True(jitters.Take(10).Select(j => Math.Round(j, 3)).Distinct().Count() >= 8);
    }

    [Fact]
    public void GaitJitter_IsStablePerSpecies()
    {
        var sp = new CreatureSpecies { Id = "sp7", LocoStyle = LocomotionStyle.Darter, Size = 1.4f, Speed = 3f };
        Assert.Equal(LocomotionController.ForSpecies(sp).TurnRate, LocomotionController.ForSpecies(sp).TurnRate);
    }

    [Fact]
    public void TamingRolls_AreEvenAndNotPeriodic_OverConsecutiveAnimals()
    {
        var rolls = EntityIds(0, 300).Select(SvGameServer.TamingRollsForTest).ToArray();

        // Each outcome turns up about equally often…
        foreach (int shy in new[] { -1, 0, 1 })
        {
            Assert.InRange(rolls.Count(r => r.Shyness == shy), 70, 130);
        }

        foreach (string bait in new[] { "forage_bait", "meat_bait", "nectar_lure" })
        {
            Assert.InRange(rolls.Count(r => r.Bait == bait), 70, 130);
        }

        Assert.InRange(rolls.Count(r => r.ExtraPatience == 1), 115, 185);

        // …and not in a cycle: the old hash cycled shyness with period three over consecutive animals and rotated the craved
        // bait with the spawn order. Independent rolls match about one time in three.
        int shyPeriod3 = Enumerable.Range(0, 297).Count(i => rolls[i].Shyness == rolls[i + 3].Shyness);
        int baitNeighbours = Enumerable.Range(0, 299).Count(i => rolls[i].Bait == rolls[i + 1].Bait);
        Assert.InRange(shyPeriod3, 60, 140);
        Assert.InRange(baitNeighbours, 60, 140);
    }

    [Fact]
    public void TamingNeeds_AreUnpredictable_StepByStep_AndAnimalByAnimal()
    {
        var passive = new CreatureSpecies { Id = "sp0", Temperament = CreatureTemperament.Passive };

        // One animal's sequence of needs: the less usual need ("approach") on about one step in three, not on a beat.
        var steps = Enumerable.Range(0, 300).Select(s => SvGameServer.NeedForStepForTest(passive, "e17", s)).ToArray();
        Assert.All(steps, n => Assert.Contains(n, new[] { "approach", "feed" }));
        Assert.InRange(steps.Count(n => n == "approach"), 70, 130);
        int period3 = Enumerable.Range(0, 297).Count(i => steps[i] == steps[i + 3]);
        Assert.InRange(period3, 120, 220); // independent: P(alike) = 1/9 + 4/9 = 5/9 → ~165; a cycle would give 297

        // The first step across consecutive animals: no "120120…" pattern either.
        var firsts = EntityIds(0, 300).Select(id => SvGameServer.NeedForStepForTest(passive, id, 0)).ToArray();
        Assert.InRange(firsts.Count(n => n == "approach"), 70, 130);
        int firstsPeriod3 = Enumerable.Range(0, 297).Count(i => firsts[i] == firsts[i + 3]);
        Assert.InRange(firstsPeriod3, 120, 220);
    }

    [Fact]
    public void EnemyRefillIntervals_VaryAcrossTheWindow_AndBetweenSaves()
    {
        var intervals = Enumerable.Range(0, 60).Select(o => SvGameServer.EnemyRefillIntervalForTest("sys0-p1", o, 4242L)).ToArray();

        Assert.All(intervals, t => Assert.InRange(t, 20.0, 45.0));
        Assert.True(intervals.Max() - intervals.Min() > 15.0, $"refills should vary across 20–45 s, got {string.Join(", ", intervals)}");
        Assert.True(intervals.Select(t => Math.Round(t, 2)).Distinct().Count() >= 55);

        // Neighbouring ordinals are not a fraction of a second apart (the old hash stepped by ~0.03 s).
        int nearlySame = Enumerable.Range(0, 59).Count(i => Math.Abs(intervals[i] - intervals[i + 1]) < 1.0);
        Assert.True(nearlySame < 15, $"{nearlySame}/59 consecutive refills were within a second of each other");

        // The same world in another save rolls its own cadence.
        var otherSave = Enumerable.Range(0, 10).Select(o => SvGameServer.EnemyRefillIntervalForTest("sys0-p1", o, 7L)).ToArray();
        Assert.NotEqual(intervals.Take(10), otherSave);
        Assert.Equal(intervals[3], SvGameServer.EnemyRefillIntervalForTest("sys0-p1", 3, 4242L));
    }

    [Fact]
    public void DroneStrafePhases_SpreadRoundTheCircle()
    {
        var phases = EntityIds(0, 1000).Select(SvGameServer.DroneBobPhaseForTest).ToArray();

        Assert.All(phases, p => Assert.InRange(p, 0.0, 2.0 * Math.PI));
        var bins = new int[10];
        foreach (double p in phases)
        {
            bins[Math.Min(9, (int)(p / (2.0 * Math.PI) * 10.0))]++;
        }

        Assert.All(bins, n => Assert.InRange(n, 60, 140));

        // Drones spawned together no longer strafe in lock-step (the old hash: 0.01 rad apart).
        int lockStep = Enumerable.Range(0, 999).Count(i => Math.Abs(phases[i] - phases[i + 1]) < 0.05);
        Assert.True(lockStep < 40, $"{lockStep}/999 neighbouring drones strafed in lock-step");
    }
}
