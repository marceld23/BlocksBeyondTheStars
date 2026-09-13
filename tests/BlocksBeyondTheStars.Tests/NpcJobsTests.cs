// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Jobs with yield (#1868, Marcel 2026-09-13): the base's blocks decide the job — crops and hydro trays make a
/// gardener who harvests a standing crop into the base crate (regrowth scheduled like a player's harvest), a
/// workbench makes a craftsman who puts plant fibre in the crate or, with a forge and ore, smelts two ore into one
/// ingot (the workshop ratio — no metal from nothing), a walled yard makes a guard who walks the wall on the night
/// shift, warns the owner by radio and sends watching bandit scouts on their way.
/// </summary>
public sealed class NpcJobsTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static StoredContainer Crate(NpcLifeWorld w, int x, int z, params ItemStack[] items)
    {
        w.Set(x, w.Feet, z, "crate");
        var crate = new StoredContainer
        {
            Id = "npc_crate_" + x + "_" + z,
            Planet = w.Server.ActiveLocationId,
            Kind = "crate",
            Position = new Vector3i(x, w.Feet, z),
            Items = items.ToList(),
        };
        w.Server.AddContainerForTest(crate);
        return crate;
    }

    private static int CountIn(NpcLifeWorld w, string crateId, string item)
        => w.Server.Containers.Single(c => c.Id == crateId).Items.Where(s => s.Item == item).Sum(s => s.Count);

    [Fact]
    public void TheGardener_HarvestsTheStandingCrop_IntoTheBaseCrate_AndItRegrows()
    {
        using var w = new NpcLifeWorld(_content, "jobs_garden");
        int baseId = w.FoundBase();
        int tx = NpcLifeWorld.Cx + 4, tz = NpcLifeWorld.Cz + 3;
        w.Set(tx, w.Feet, tz, "hydro_tray");
        w.Set(tx, w.Feet + 1, tz, "flora_cropberry");
        var crate = Crate(w, NpcLifeWorld.Cx - 4, NpcLifeWorld.Cz - 3);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal("gardener", w.Server.BaseResidentsForTest(baseId).Single().Job);
        int id = w.Server.BaseResidentsForTest(baseId).Single().NpcId;
        Assert.Equal("npc_hoe", w.Server.NpcRoutineForTest(id).Held);

        var crop = new Vector3i(tx, w.Feet + 1, tz);
        w.TickAt(0.45, 90, until: () => w.Server.World.GetBlock(crop).IsAir);

        Assert.True(w.Server.World.GetBlock(crop).IsAir, "the crop was never harvested");
        Assert.True(CountIn(w, crate.Id, "berries") >= 2, "the berries did not reach the crate");
        Assert.True(CountIn(w, crate.Id, "plant_fiber") >= 1, "the fibre did not reach the crate");
        Assert.True(w.Server.FloraRegrowTimerForTest(crop.X, crop.Y, crop.Z) > 0, "the harvested crop does not regrow");
    }

    [Fact]
    public void WithoutACrate_TheGardenerOnlyTends_AndTheCropStays()
    {
        using var w = new NpcLifeWorld(_content, "jobs_garden_nocrate");
        int baseId = w.FoundBase();
        int tx = NpcLifeWorld.Cx + 4, tz = NpcLifeWorld.Cz + 3;
        w.Set(tx, w.Feet, tz, "hydro_tray");
        w.Set(tx, w.Feet + 1, tz, "flora_cropberry");
        w.Server.ScanBaseLifeForTest();
        Assert.Equal("gardener", w.Server.BaseResidentsForTest(baseId).Single().Job);

        w.TickAt(0.45, 60);
        Assert.False(w.Server.World.GetBlock(new Vector3i(tx, w.Feet + 1, tz)).IsAir, "a crop was picked with nowhere to put it");
    }

    [Fact]
    public void TheCraftsman_SmeltsOreFromTheCrate_AtTheWorkshopRatio_ThenMakesFibre()
    {
        using var w = new NpcLifeWorld(_content, "jobs_craft");
        int baseId = w.FoundBase(machine: "workbench");
        w.Set(NpcLifeWorld.Cx + 5, w.Feet, NpcLifeWorld.Cz - 6, "forge");
        var crate = Crate(w, NpcLifeWorld.Cx - 4, NpcLifeWorld.Cz - 3, new ItemStack("iron_ore", 2));
        w.Server.ScanBaseLifeForTest();
        int id = w.Server.BaseResidentsForTest(baseId).Single(r => r.Job == "craftsman").NpcId;
        Assert.Equal("npc_hammer", w.Server.NpcRoutineForTest(id).Held);

        w.TickAt(0.45, 25); // walk to the bench
        w.Server.DueCraftsmanYieldForTest();
        w.TickAt(0.45, 3);
        Assert.Equal(1, CountIn(w, crate.Id, "iron_ingot"));
        Assert.Equal(0, CountIn(w, crate.Id, "iron_ore"));

        w.Server.DueCraftsmanYieldForTest();
        w.TickAt(0.45, 3);
        Assert.Equal(2, CountIn(w, crate.Id, "plant_fiber"));
        Assert.Equal(1, CountIn(w, crate.Id, "iron_ingot")); // no ore left: nothing more appears from nothing
    }

    [Fact]
    public void TheGuard_WalksTheWallAtNight_WarnsTheOwner_AndSendsTheScoutsAway()
    {
        using var w = new NpcLifeWorld(_content, "jobs_guard", padHalf: 48, configure: c =>
        {
            c.Rules.BaseVisitors = true;
            c.Rules.Bandits = AlienActivity.Normal;
            c.Rules.PlanetEnemies = AlienActivity.Rare;
        });
        int baseId = w.FoundBase();
        w.WallRing(9);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 1.5f, w.Feet, NpcLifeWorld.Cz + 1.5f);
        w.Server.ScanBaseLifeForTest();
        var guard = w.Server.BaseResidentsForTest(baseId).Single();
        Assert.Equal("guard", guard.Job);
        Assert.True(w.Server.GuardPatrolLengthForTest(guard.NpcId) >= 8, "no round along the wall");

        // By day the guard sleeps (the night shift); at night it walks the round.
        var seen = new HashSet<(int, int)>();
        w.TickAt(0.9, 60, every: () =>
        {
            var p = w.Server.NpcSnapshots.Single(n => n.Id == guard.NpcId).Pos;
            seen.Add(((int)Math.Floor(p.X / 3f), (int)Math.Floor(p.Z / 3f)));
        });
        Assert.True(seen.Count >= 4, $"the guard barely moved on its round ({seen.Count} places)");

        Assert.True(w.Server.SpawnScoutsForTest(baseId));
        bool sentAway = false;
        w.TickAt(0.9, 55, // inside the scouts' own 60 s visit — a scout leaving on its own clock proves nothing

            every: () => sentAway |= w.Server.Bandits.Any(b => b.ScoutBaseId == baseId && b.BanditPhase == BanditPhase.Leaving),
            until: () => sentAway);

        Assert.Contains(w.Transport.Sent, s => s.Conn == w.Player.ConnectionId && s.Msg is ChatMessage { IsNpcCall: true });
        Assert.True(sentAway, "the guard never sent the scouts away");
    }
}
