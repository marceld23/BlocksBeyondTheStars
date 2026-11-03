// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3148 — the energy fence guards the air above it. Fliers hover 3–12 blocks over the ground and used to glide over any
/// pen; a tester asked for "a raised energy fence" to keep the big fliers off his spaceport. An airborne step is blocked by
/// a closed fence in the column below it (down to the ground, at most 13 deep), a walking step keeps the old body-height
/// rule, and over the closed yard of a base that uses energy fences no wild flier spawns or stays. A closed energy gate is
/// a wall for the yard fill now (its membrane is not solid, but no animal passes it). Bases without a fence keep the old
/// rule: fliers are free over their yards.
/// </summary>
public sealed class FenceAirspaceTests
{
    private const int Cx = NpcLifeWorld.Cx, Cz = NpcLifeWorld.Cz;

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static BlockId Id(string key) => Content.Value.GetBlock(key)!.NumericId;

    /// <summary>A two-high ring of <paramref name="block"/> at Chebyshev radius 8 around the base core on the arena floor;
    /// <paramref name="gate"/> puts an energy gate in one column, <paramref name="gap"/> leaves one column open.</summary>
    private static void Ring(NpcLifeWorld w, string block, bool gate = false, bool gap = false)
    {
        const int r = 8;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r)
                {
                    continue;
                }

                bool door = dx == r && dz == 0;
                if (door && gap)
                {
                    continue;
                }

                var id = door && gate ? Id("energy_gate") : Id(block);
                for (int y = w.Feet; y <= w.Feet + 1; y++)
                {
                    w.Server.World.SetBlock(new Vector3i(Cx + dx, y, Cz + dz), id, owner: w.Player.State.PlayerId);
                }
            }
        }
    }

    private static NpcLifeWorld World(string name)
    {
        var w = new NpcLifeWorld(Content.Value, name);
        w.FoundBase();
        return w;
    }

    [Fact]
    public void AnAirborneStep_IsHeldByAFenceBelowIt_AWalkingStepOnlyAtBodyHeight()
    {
        using var w = new NpcLifeWorld(Content.Value, "fence_air_column");
        var fence = Id("energy_fence");
        w.Server.World.SetBlock(new Vector3i(3, 300, 0), fence); // high above the terrain, nothing else around
        var from = new Vector3f(0.5f, 306, 0.5f);
        var to = new Vector3f(6.5f, 306, 0.5f);

        Assert.True(w.Server.BlockedByEnergyFenceAirborneForTest(from, to), "a flier six above the fence cannot cross it");
        Assert.False(w.Server.BlockedByEnergyFenceForTest(from, to), "a walker's sweep stays at its body height");
        Assert.False(w.Server.BlockedByEnergyFenceAirborneForTest(new Vector3f(0.5f, 320, 0.5f), new Vector3f(6.5f, 320, 0.5f)),
            "far above the highest hover the fence guards nothing");

        // A flier already over the fence column (it took off from the pylon, or the fence went in under it) may leave it —
        // only ENTERING a fence column is crossing the line.
        Assert.False(w.Server.BlockedByEnergyFenceAirborneForTest(new Vector3f(3.5f, 306, 0.5f), new Vector3f(3.5f, 306, 3.5f)));
        Assert.False(w.Server.BlockedByEnergyFenceAirborneForTest(new Vector3f(3.5f, 302, 0.5f), new Vector3f(0.5f, 302, 0.5f)));

        // A roof between the fence and the flier: the fence guards the room under it, not the air over the roof.
        w.Server.World.SetBlock(new Vector3i(3, 303, 0), Id("stone"));
        Assert.False(w.Server.BlockedByEnergyFenceAirborneForTest(from, to));
    }

    [Fact]
    public void AWildFlier_OverTheFencedYard_FliesOff_WhileNobodyLooks_ButNotOverAStoneYard()
    {
        foreach (var (block, swept) in new[] { ("energy_fence", true), ("stone", false) })
        {
            using var w = World("fence_air_sweep_" + block);
            Ring(w, block);
            var sp = w.Server.SpeciesRoster.First(s => !s.Id.StartsWith("au_", StringComparison.Ordinal));
            sp.Habitat = BlocksBeyondTheStars.Shared.Definitions.CreatureHabitat.Air;
            sp.Temperament = BlocksBeyondTheStars.Shared.Definitions.CreatureTemperament.Passive;
            string id = w.Server.SpawnCreatureAtForTest(new Vector3f(Cx + 2.5f, w.Feet + 6, Cz + 2.5f), sp.Id);

            // The player stands far off, looking the other way (yaw 270 = towards −X).
            w.Player.State.Position = new Vector3f(Cx - 30.5f, w.Feet, Cz + 0.5f);
            w.Player.State.Yaw = 270f;
            w.Server.SweepBaseIntrudersForTest();

            Assert.Equal(!swept, w.Server.Creatures.Any(c => c.Id == id));
        }
    }

    [Fact]
    public void OverTheFencedYardOfABase_NoWildFlierBelongs_OutsideItDoes()
    {
        using var w = World("fence_air_yard");
        Ring(w, "energy_fence");
        Assert.True(w.Server.InFencedBaseYardForTest(new Vector3f(Cx + 2.5f, w.Feet + 6, Cz + 2.5f)));
        Assert.False(w.Server.InFencedBaseYardForTest(new Vector3f(Cx + 14.5f, w.Feet + 6, Cz + 0.5f)), "outside the ring");
    }

    [Fact]
    public void AStoneWalledYard_StaysFreeForFliers()
    {
        using var w = World("fence_air_stone");
        Ring(w, "stone");
        Assert.True(w.Server.InWalledBaseAreaForTest(Cx + 2, w.Feet, Cz + 2), "the fixture: the stone ring closes the yard");
        Assert.False(w.Server.InFencedBaseYardForTest(new Vector3f(Cx + 2.5f, w.Feet + 6, Cz + 2.5f)),
            "a base without an energy fence keeps the old rule");
    }

    [Fact]
    public void AFenceRingWithItsGate_IsAClosedYard_AGapIsNot()
    {
        using (var w = World("fence_air_gate"))
        {
            Ring(w, "energy_fence", gate: true);
            Assert.True(w.Server.InWalledBaseAreaForTest(Cx + 2, w.Feet, Cz + 2), "a closed energy gate is a wall for the yard");
            Assert.True(w.Server.InFencedBaseYardForTest(new Vector3f(Cx + 2.5f, w.Feet + 6, Cz + 2.5f)));
        }

        using (var w = World("fence_air_gap"))
        {
            Ring(w, "energy_fence", gap: true);
            Assert.False(w.Server.InFencedBaseYardForTest(new Vector3f(Cx + 2.5f, w.Feet + 6, Cz + 2.5f)), "a gap opens the yard");
        }
    }
}
