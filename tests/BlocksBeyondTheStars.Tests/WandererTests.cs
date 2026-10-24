// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The wanderers (#2646): a world's small cast of named travellers (stable across reloads), the encounter rules (new
/// worlds only, by day, out in the open, after a quiet spell, at most two per world, nobody twice), the spawn out of sight
/// on the 40–55 m ring, the walk to a task near the player and the work through the real cores, the leave out of sight,
/// their own talk and trade, and the admin playtest helpers (<c>/wanderer</c>, <c>/tp lodge</c>). The arena is the
/// <see cref="NpcLifeWorld"/> pad, made wide enough for the ring (<see cref="Pad"/>).
/// </summary>
public sealed class WandererTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private const int Cx = NpcLifeWorld.Cx, Cz = NpcLifeWorld.Cz;

    /// <summary>Half the pad: the spawn ring (55 m) round a player near the middle lies on it.</summary>
    private const int Pad = 60;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_wander_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string En(string key) => Content.CreateLocalizer(GameLocale.English).Get(key);

    private static NpcLifeWorld Arena(string name, int pad = Pad, Action<ServerConfig>? configure = null)
    {
        // No scan-drones: since #2950 a drone spawns over the live top of its column — on this floating pad when its
        // spawn spot lies under it — and a hostile near the player is a refusal ("hostile") these tests do not ask about.
        var w = new NpcLifeWorld(Content, name, padHalf: pad, configure: c =>
        {
            c.Rules.PlanetDrones = false;
            configure?.Invoke(c);
        });
        w.TickAt(0.45, 0.5); // the first sight arms the newcomer's quiet spell …
        w.Server.SetWandererCooldownForTest(Pid(w), 0); // … which the tests skip
        return w;
    }

    private static string Pid(NpcLifeWorld w) => w.Player.State.PlayerId;

    private static void PlayerAt(NpcLifeWorld w, float x, float z) => w.Player.State.Position = new Vector3f(x, w.Feet, z);

    private static (int NpcId, int Slot, string Name, string PersonKey, string Job, string Phase, Vector3f Spawn, Vector3f Center, string HostId, int YieldTarget) Out(NpcLifeWorld w, int npcId)
        => w.Server.WanderersForTest.Single(x => x.NpcId == npcId);

    private static Vector3f NpcPos(NpcLifeWorld w, int npcId) => w.Server.NpcRoutineForTest(npcId).Pos;

    private static int Yields(NpcLifeWorld w, int npcId) => w.Server.NpcWorkForTest(npcId)?.Yields ?? 0;

    private static float Flat(Vector3f a, Vector3f b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>A crop on its own dirt tile of the pad (written without an owner, as worldgen does).</summary>
    private static Vector3i Crop(NpcLifeWorld w, int x, int z)
    {
        w.Server.World.SetBlock(new Vector3i(x, w.PadY, z), w.Block("dirt"));
        var cell = new Vector3i(x, w.Feet, z);
        w.Server.World.SetBlock(cell, w.Block("flora_cropberry"));
        return cell;
    }

    // ---- the cast ------------------------------------------------------------------------------------------------

    [Fact]
    public void TheCast_IsFourToSixNamedPeople_WithPersonKeys_SpreadOverTheProfessions_AndTheSameAfterAReload()
    {
        IReadOnlyList<(int Slot, string Name, string PersonKey, string Job)> Roster(out string world)
        {
            using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "cast"));
            var server = new SvGameServer(new ServerConfig
            {
                WorldName = "cast",
                Seed = 3,
                StartPlanet = "meadowlands",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = false,
                PlaceWrecks = false,
                PlaceBanditCamps = false,
            }, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
            server.Start();
            world = server.World.LocationId;
            var roster = server.WandererRosterForTest;
            server.Stop();
            return roster;
        }

        var first = Roster(out string world);
        var again = Roster(out _);

        Assert.InRange(first.Count, SvGameServer.WandererRosterMin, SvGameServer.WandererRosterMax);
        Assert.Equal(first, again); // the same people with the same names after a reload
        for (int i = 0; i < first.Count; i++)
        {
            Assert.Equal(i, first[i].Slot);
            Assert.Equal($"wanderer:{world}:{i}", first[i].PersonKey);
            Assert.False(string.IsNullOrWhiteSpace(first[i].Name));
            Assert.NotNull(Content.LodgeProfession(first[i].Job)); // the lodges' professions: trade, tools and AI unchanged
        }

        // Spread before one repeats: every profession of the cast shows up before the first repeat.
        int distinct = first.Select(m => m.Job).Distinct().Count();
        Assert.Equal(distinct, first.Take(distinct).Select(m => m.Job).Distinct().Count());
        Assert.True(distinct >= 3, "a lush world calls for most professions");
    }

    // ---- when nobody comes -------------------------------------------------------------------------------------

    [Fact]
    public void Nobody_OnAnOlderWorld_OrWithWanderersSwitchedOff()
    {
        using (var old = Arena("wander_gen24", pad: 8, configure: c => c.World.TerrainGeneration = 24))
        {
            Assert.False(old.Server.WanderersBelongHereForTest);
            Assert.Null(old.Server.WandererEncounterReasonForTest(Pid(old), out string r24, anyWork: true));
            Assert.Equal("none_here", r24);
        }

        using (var off = Arena("wander_off", pad: 8, configure: c => c.PlaceWanderers = false))
        {
            Assert.False(off.Server.WanderersBelongHereForTest);
        }
    }

    [Fact]
    public void Nobody_DuringTheQuietSpell_AtNight_NearSomethingDangerous_OrUnderground()
    {
        using var w = Arena("wander_rules", pad: 8);
        string pid = Pid(w);
        Assert.True(w.Server.WanderersBelongHereForTest);

        // A newcomer plays a while before the first wanderer comes by; then each encounter ends a quiet spell of its own.
        using (var fresh = new NpcLifeWorld(Content, "wander_fresh", padHalf: 8))
        {
            fresh.TickAt(0.45, 0.5);
            Assert.InRange(fresh.Server.WandererCooldownForTest(Pid(fresh)),
                SvGameServer.WandererCooldownSeconds * 0.8 - 2, SvGameServer.WandererCooldownSeconds * 1.2);
        }

        w.Server.SetWandererCooldownForTest(pid, 120);
        Assert.Null(w.Server.WandererEncounterReasonForTest(pid, out string r, anyWork: true));
        Assert.Equal("cooldown", r);
        w.Server.SetWandererCooldownForTest(pid, 0);

        w.TickAt(0.9, 0.5);
        Assert.Null(w.Server.WandererEncounterReasonForTest(pid, out r, anyWork: true));
        Assert.Equal("night", r);
        w.TickAt(0.45, 0.5);

        // Something dangerous close by: nobody wanders over.
        var sp = w.Server.SpeciesRoster.First(s => s.Habitat == CreatureHabitat.Land);
        string beast = w.Server.SpawnCreatureAtForTest(new Vector3f(Cx + 3.5f, w.Feet, Cz + 3.5f), sp.Id);
        var creature = w.Server.Creatures.Single(c => c.Id == beast);
        bool wasHostile = creature.Hostile;
        creature.Hostile = true;
        Assert.Null(w.Server.WandererEncounterReasonForTest(pid, out r, anyWork: true));
        Assert.Equal("hostile", r);
        creature.Hostile = wasHostile;

        // Underground (a cave, a mine): nobody comes.
        var standing = w.Player.State.Position;
        int top = w.PadY - 1;
        while (top > -200 && w.Server.World.GetBlock(new Vector3i(Cx - 2, top, Cz - 2)).IsAir)
        {
            top--;
        }

        w.Player.State.Position = new Vector3f(standing.X, top - 12, standing.Z);
        Assert.Null(w.Server.WandererEncounterReasonForTest(pid, out r, anyWork: true));
        Assert.Equal("no_spot", r);
        w.Player.State.Position = standing;
    }

    [Fact]
    public void Nobody_NearABaseAVillageOrAHut()
    {
        // Too close to the places people live: a base, a village, a hut — 60 m clear of each.
        using var w = Arena("wander_base", pad: 8);
        w.FoundBase();
        Assert.Null(w.Server.WandererEncounterReasonForTest(Pid(w), out string r, anyWork: true));
        Assert.Equal("too_close", r);

        using var village = Arena("wander_village", pad: 8);
        village.Server.RegisterTestSettlementForTest("Testdorf",
            new Vector3i(Cx + 50, village.PadY, Cz - 5), new Vector3i(Cx + 60, village.PadY + 8, Cz + 5));
        Assert.Null(village.Server.WandererEncounterReasonForTest(Pid(village), out r, anyWork: true));
        Assert.Equal("too_close", r);

        using var hut = Arena("wander_hut", pad: 8);
        hut.Server.SpawnLodgeForTest(Cx - 70, hut.PadY, Cz + 10, "gatherer", beds: 1);
        Assert.Null(hut.Server.WandererEncounterReasonForTest(Pid(hut), out r, anyWork: true));
        Assert.Equal("too_close", r);
    }

    // ---- the encounter -------------------------------------------------------------------------------------------

    [Fact]
    public void AWanderer_AppearsOnTheRing_OutOfSight_AndItsTaskLiesBetweenItAndThePlayer()
    {
        using var w = Arena("wander_sight");
        PlayerAt(w, Cx + 0.5f, Cz + 0.5f);
        var stone = w.Block("stone");
        // A wall 35 m to the east: only the spots on that side of the ring are out of the player's sight.
        for (int z = Cz - 14; z <= Cz + 14; z++)
            for (int y = w.Feet; y <= w.Feet + 3; y++)
            {
                w.Server.World.SetBlock(new Vector3i(Cx + 35, y, z), stone);
            }

        // Asked to come from the west (in plain sight), it comes from behind the wall.
        int id = Assert.IsType<int>(w.Server.WandererEncounterForTest(Pid(w), job: "miner", bearing: Math.PI));
        var o = Out(w, id);
        var player = w.Player.State.Position;
        Assert.InRange(Flat(o.Spawn, player), 39f, 56.5f);
        Assert.True(o.Spawn.X > Cx + 35, $"spawned at {o.Spawn}, not behind the wall");
        Assert.False(w.Server.HasLineOfSightForTest(player, o.Spawn), "the player saw it appear");

        // The task centre: 15–25 m from the player, on the spawn's side.
        Assert.InRange(Flat(o.Center, player), 14f, 26f);
        Assert.True(o.Center.X > player.X + 10, $"the task centre {o.Center} is not towards the spawn");
        Assert.Equal("Arriving", o.Phase);

        // A person of the cast: a vendor with its profession's trade, tool and look, and a person key of its own.
        var jobs = w.Server.NpcJobsForTest;
        Assert.Contains(jobs, n => n.Job == "miner" && n.Role == "vendor" && n.Theme == "miners" && n.Held == "npc_pickaxe"
                                   && n.NameKey == SvGameServer.ProspectorNameKey);
        Assert.Equal(o.PersonKey, w.Server.NpcPersonKeyForTest(id));
        Assert.Equal(SvGameServer.WandererActivityKey, w.Server.NpcRoutineForTest(id).Activity);
    }

    [Fact]
    public void TheGatherer_WalksToItsTask_AndPicksAPlantThroughTheCore_IntoItsPack()
    {
        using var w = Arena("wander_gather");
        // Nothing grows on the bare pad: no gatherer comes by (nobody turns up with nothing to do).
        Assert.Null(w.Server.WandererEncounterReasonForTest(Pid(w), out string none, job: "gatherer", bearing: 0.0));
        Assert.Equal("no_work", none);

        var crops = new[] { Crop(w, Cx + 20, Cz - 2), Crop(w, Cx + 22, Cz), Crop(w, Cx + 19, Cz + 2) };
        int id = Assert.IsType<int>(w.Server.WandererEncounterForTest(Pid(w), job: "gatherer", bearing: 0.0));
        int slot = Out(w, id).Slot;

        w.TickAt(0.45, 120, until: () => Yields(w, id) > 0);
        Assert.Equal(1, Yields(w, id));
        Assert.Equal("Working", Out(w, id).Phase);
        var site = w.Server.NpcWorkForTest(id)!.Value.Site;
        Assert.Contains(site, crops);
        Assert.True(w.Server.World.GetBlock(site).IsAir, "the picked plant is gone until it regrows");
        Assert.True(w.Server.FloraRegrowTimerForTest(site.X, site.Y, site.Z) > 0, "its regrowth is scheduled");
        Assert.True(Flat(NpcPos(w, id), new Vector3f(site.X + 0.5f, site.Y, site.Z + 0.5f)) < 2.5f, "it picked it from beside it");
        Assert.NotEmpty(w.Server.WandererPackForTest(slot)); // the yield is in its pack — no crate anywhere
        Assert.DoesNotContain(w.Server.Containers, c => c.Id.Contains("wanderer", StringComparison.Ordinal));
    }

    [Fact]
    public void TheProspector_TapsAnOutcropNearThePlayer_AndTheCellFillsUpAgainLater()
    {
        using var w = Arena("wander_mine");
        int id = Assert.IsType<int>(w.Server.WandererEncounterForTest(Pid(w), job: "miner", bearing: 0.0));
        var o = Out(w, id);

        w.TickAt(0.45, 120, until: () => Yields(w, id) > 0);
        Assert.Equal(1, Yields(w, id));
        var site = w.Server.NpcWorkForTest(id)!.Value.Site;
        Assert.True(w.Server.World.GetBlock(site).IsAir);
        Assert.True(Flat(new Vector3f(site.X + 0.5f, site.Y, site.Z + 0.5f), o.Center) <= 12.5f, "an outcrop round its task centre");
        Assert.Contains(w.Server.WandererPackForTest(o.Slot), s => s.Count > 0);

        // The refill registry has it: with nobody near, the rock closes again after about ten minutes of play.
        PlayerAt(w, Cx - 40.5f, Cz - 40.5f);
        w.TickAt(0.45, 700, dt: 1.0, until: () => !w.Server.World.GetBlock(site).IsAir);
        Assert.Equal(w.Block("stone"), w.Server.World.GetBlock(site));
    }

    [Fact]
    public void TheWoodcutter_FellsANaturalTree_AndPlantsASapling()
    {
        using var w = Arena("wander_chop");
        // The tree checks read loaded chunks only — load the air above the work area the way a player standing there would.
        for (int x = Cx; x <= Cx + 40; x += 8)
            for (int z = Cz - 16; z <= Cz + 16; z += 8)
                for (int y = w.Feet - 8; y <= w.Feet + 24; y += 8)
                {
                    w.Server.World.GetBlock(new Vector3i(x, y, z));
                }

        var trunk = new Vector3i(Cx + 21, w.Feet, Cz - 1);
        w.Server.World.SetBlock(new Vector3i(trunk.X, w.PadY, trunk.Z), w.Block("grass"));
        for (int dy = 0; dy < 5; dy++)
        {
            w.Server.World.SetBlock(new Vector3i(trunk.X, w.Feet + dy, trunk.Z), w.Block("wood_log"));
        }

        int top = w.Feet + 4;
        for (int dy = -1; dy <= 2; dy++)
        {
            int r = dy <= 0 ? 2 : dy == 1 ? 1 : 0;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    var c = new Vector3i(trunk.X + dx, top + dy, trunk.Z + dz);
                    if ((r == 2 && Math.Abs(dx) == 2 && Math.Abs(dz) == 2) || !w.Server.World.GetBlock(c).IsAir)
                    {
                        continue;
                    }

                    w.Server.World.SetBlock(c, w.Block("tree_leaves"));
                }
        }

        int id = Assert.IsType<int>(w.Server.WandererEncounterForTest(Pid(w), job: "woodcutter", bearing: 0.0));
        int slot = Out(w, id).Slot;
        w.TickAt(0.45, 150, until: () => Yields(w, id) > 0);
        Assert.Equal(1, Yields(w, id));
        Assert.Equal("flora_sapling", Content.BlockById(w.Server.World.GetBlock(trunk))!.Key);
        Assert.Contains(w.Server.WandererPackForTest(slot), s => s.Item == "wood_log" && s.Count > 0);

        // One tree per encounter: then it is on its way.
        w.TickAt(0.45, 5, until: () => Out(w, id).Phase == "Leaving");
        Assert.Equal("Leaving", Out(w, id).Phase);
    }

    [Fact]
    public void TheHunter_TakesOneAnimalNearThePlayer_ThenMovesOn()
    {
        using var w = Arena("wander_hunt", configure: c => c.StartPlanet = "meadowlands");
        var sp = w.Server.SpeciesRoster.First(s => s.BodyPlan == CreatureBodyPlan.Standard && s.Legs is > 0 and < 6
                                                 && !s.HasGasSac && !s.HasWings && !s.Id.StartsWith("au_", StringComparison.Ordinal));
        sp.Habitat = CreatureHabitat.Land;
        sp.Temperament = CreatureTemperament.Passive;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.BegsForFood = false;
        w.Server.ResetWandererRosterForTest(); // the cast is drawn again with this calm walker in the roster
        Assume(w.Server.WandererRosterForTest.Any(m => m.Job == "hunter"), "the cast has a hunter");

        // Two animals graze beside the task centre, off the line between the player and the hunter's spawn side.
        string a = w.Server.SpawnCreatureAtForTest(new Vector3f(Cx + 19.5f, w.Feet, Cz + 6.5f), sp.Id);
        string b = w.Server.SpawnCreatureAtForTest(new Vector3f(Cx + 21.5f, w.Feet, Cz + 8.5f), sp.Id);
        int id = Assert.IsType<int>(w.Server.WandererEncounterForTest(Pid(w), job: "hunter", bearing: 0.0));
        int slot = Out(w, id).Slot;

        w.TickAt(0.45, 150, until: () => Yields(w, id) > 0);
        Assert.Equal(1, Yields(w, id));
        Assert.True(w.Server.Creatures.Any(c => c.Id == a || c.Id == b), "one animal, never the whole herd");
        Assert.NotEmpty(w.Server.WandererPackForTest(slot));
        w.TickAt(0.45, 5, until: () => Out(w, id).Phase == "Leaving");
        Assert.Equal("Leaving", Out(w, id).Phase);
    }

    [Fact]
    public void ItLeavesAwayFromThePlayer_IsGoneWhereNobodySeesIt_AndTheSamePersonComesBackAnotherTime()
    {
        using var w = Arena("wander_leave");
        string pid = Pid(w);
        int id = Assert.IsType<int>(w.Server.WandererEncounterForTest(pid, job: "miner", bearing: 0.0));
        var o = Out(w, id);
        var look = w.Server.NpcLooksForTest.Single(n => n.Id == id);

        w.TickAt(0.45, 60, until: () => w.Server.NpcWorkForTest(id) is not null);
        w.Server.EndWandererWorkForTest();
        w.TickAt(0.45, 30, until: () => Out(w, id).Phase == "Leaving");
        Assert.Equal("Leaving", Out(w, id).Phase);

        var player = w.Player.State.Position;
        float startDist = Flat(NpcPos(w, id), player), farthest = startDist;
        w.TickAt(0.45, 150, until: () => w.Server.WanderersForTest.Count == 0,
            every: () =>
            {
                if (w.Server.WanderersForTest.Count > 0)
                {
                    farthest = Math.Max(farthest, Flat(NpcPos(w, id), player));
                }
            });
        Assert.Empty(w.Server.WanderersForTest);
        Assert.True(farthest > startDist + 15f, $"it walked from {startDist:0} m to {farthest:0} m — not away from the player");
        Assert.True(farthest > 32f, "it was gone before it was out of sight");
        Assert.DoesNotContain(w.Server.NpcSnapshots, n => n.Id == id);
        Assert.InRange(w.Server.WandererCooldownForTest(pid), SvGameServer.WandererCooldownSeconds * 0.8 - 2,
            SvGameServer.WandererCooldownSeconds * 1.2); // the next one comes after a quiet spell
        Assert.True(startDist < 40f, "it worked near the player");

        // Another day, the same person: same name, same look, same person key.
        w.Server.SetWandererCooldownForTest(pid, 0);
        int again = Assert.IsType<int>(w.Server.WandererEncounterForTest(pid, slot: o.Slot, anyWork: true, bearing: Math.PI));
        Assert.NotEqual(id, again);
        var back = Out(w, again);
        Assert.Equal((o.Name, o.PersonKey), (back.Name, back.PersonKey));
        var lookAgain = w.Server.NpcLooksForTest.Single(n => n.Id == again);
        Assert.Equal((look.SkinRgb, look.OutfitRgb, look.LegsRgb), (lookAgain.SkinRgb, lookAgain.OutfitRgb, lookAgain.LegsRgb));
    }

    [Fact]
    public void ThePlayerWalkingOff_OrNightFalling_SendsItOnItsWay()
    {
        using var w = Arena("wander_go");
        string pid = Pid(w);
        var home = w.Player.State.Position;
        int id = Assert.IsType<int>(w.Server.WandererEncounterForTest(pid, job: "miner", bearing: 0.0));
        w.TickAt(0.45, 3);
        PlayerAt(w, Cx - 57.5f, Cz + 0.5f); // more than 80 m from it
        w.TickAt(0.45, 3, until: () => w.Server.WanderersForTest.All(x => x.Phase == "Leaving"));
        Assert.True(w.Server.WanderersForTest.All(x => x.Phase == "Leaving"), "it stayed though the player walked off");
        w.TickAt(0.45, 150, until: () => w.Server.WanderersForTest.Count == 0); // nobody near: gone
        Assert.Empty(w.Server.WanderersForTest);

        w.Player.State.Position = home;
        w.Server.SetWandererCooldownForTest(pid, 0);
        id = Assert.IsType<int>(w.Server.WandererEncounterForTest(pid, job: "miner", bearing: 0.0));
        w.TickAt(0.45, 3);
        w.TickAt(0.8, 3, until: () => Out(w, id).Phase == "Leaving");
        Assert.Equal("Leaving", Out(w, id).Phase); // night falls: off it goes
    }

    [Fact]
    public void AtMostTwoWanderers_PerWorld_OnePerPlayer_AndNobodyOfTheCastTwice()
    {
        using var w = Arena("wander_cap");
        var second = w.Server.AddLocalPlayer("Second");
        var third = w.Server.AddLocalPlayer("Third");
        second.State.AboardShip = false;
        third.State.AboardShip = false;
        second.State.Position = new Vector3f(Cx + 2.5f, w.Feet, Cz + 2.5f);
        third.State.Position = new Vector3f(Cx - 3.5f, w.Feet, Cz + 3.5f);
        w.Server.SetWandererCooldownForTest(second.State.PlayerId, 0);
        w.Server.SetWandererCooldownForTest(third.State.PlayerId, 0);

        int a = Assert.IsType<int>(w.Server.WandererEncounterForTest(Pid(w), anyWork: true, bearing: 0.0));
        Assert.Null(w.Server.WandererEncounterReasonForTest(Pid(w), out string own, anyWork: true));
        Assert.Equal("busy", own); // one at a time per player
        int b = Assert.IsType<int>(w.Server.WandererEncounterForTest(second.State.PlayerId, anyWork: true, bearing: Math.PI));
        Assert.Null(w.Server.WandererEncounterReasonForTest(third.State.PlayerId, out string full, anyWork: true));
        Assert.Equal("busy", full); // two on the world
        Assert.Equal(2, w.Server.WanderersForTest.Count);
        Assert.NotEqual(Out(w, a).Slot, Out(w, b).Slot);
        Assert.NotEqual(Out(w, a).PersonKey, Out(w, b).PersonKey);
    }

    [Fact]
    public void AWalkingWanderer_StopsForAPlayerWhoComesOver_AndWalksOnWhenTheyGo()
    {
        using var w = Arena("wander_stop");
        int id = Assert.IsType<int>(w.Server.WandererEncounterForTest(Pid(w), job: "miner", bearing: 0.0));
        w.TickAt(0.45, 4);
        var o = Out(w, id);
        Assert.Equal("Arriving", o.Phase);

        var at = NpcPos(w, id);
        var home = w.Player.State.Position;
        w.Player.State.Position = new Vector3f(at.X - 2f, at.Y, at.Z);
        w.TickAt(0.45, 1.5, every: () => w.Player.State.Position = new Vector3f(at.X - 2f, at.Y, at.Z));
        var held = NpcPos(w, id);
        w.TickAt(0.45, 4, every: () => w.Player.State.Position = new Vector3f(at.X - 2f, at.Y, at.Z));
        Assert.True(Flat(NpcPos(w, id), held) < 0.8f, "it walked on past the player");
        Assert.Equal("Arriving", Out(w, id).Phase);

        w.Player.State.Position = home;
        w.TickAt(0.45, 60, until: () => Out(w, id).Phase == "Working");
        Assert.Equal("Working", Out(w, id).Phase);
    }

    // ---- talk and trade ------------------------------------------------------------------------------------------

    [Fact]
    public void TheirTalk_IsTheirOwn_NeverALodgeResidents_AndTheTipShowsTheWayToAHut()
    {
        var wanderDialogs = Content.Dialogs.Where(d => d.Key.StartsWith("wanderer_", StringComparison.Ordinal)).ToList();
        foreach (var l in Content.Lodges)
        {
            var mine = wanderDialogs.Where(d => d.Job == l.Key).ToList();
            var d = Assert.Single(mine);
            Assert.Equal("wanderer_" + l.Key, d.Key);
            Assert.Equal(DialogDefinition.HomeWander, d.Home); // a wanderer's talk, nobody else's of that job
            Assert.True(d.AiPrompt, d.Key + " may be re-worded on hosted worlds");
            Assert.All(d.Nodes, n => Assert.InRange(n.Choices.Count, 1, 3));
            Assert.Contains(d.Nodes.SelectMany(n => n.Choices), c => c.Consequence == SvGameServer.WandererTipConsequence);
        }

        // Every dialogue of a profession is somebody's own: a lodge's, a wanderer's — never one for both.
        Assert.All(Content.Dialogs.Where(d => Content.LodgeProfession(d.Job) is not null),
            d => Assert.Contains(d.Home, new[] { DialogDefinition.HomeLodge, DialogDefinition.HomeWander }));

        using var w = Arena("wander_talk");
        string lodge = w.Server.SpawnLodgeForTest(Cx + 45, w.PadY, Cz + 45, "miner", beds: 1);
        var resident = w.Server.LodgeResidentsForTest(lodge)[0];
        int id = Assert.IsType<int>(w.Server.WandererEncounterForTest(Pid(w), job: "miner", bearing: Math.PI));

        // Walk over: it stops for the player and talks its own talk — the lodge's miner talks the lodge's.
        w.TickAt(0.45, 3);
        var at = NpcPos(w, id);
        w.Player.State.Position = new Vector3f(at.X + 1.5f, at.Y, at.Z);
        w.TickAt(0.45, 1);
        w.Player.State.Position = new Vector3f(NpcPos(w, id).X + 1.5f, at.Y, NpcPos(w, id).Z);
        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, id);
        Assert.Equal("wanderer_miner", w.Server.ActiveDialogForTest(NpcLifeWorld.Owner)?.Key);
        w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 1); // "Seen anything interesting on your way?"
        var said = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcDialogState>().Last();
        Assert.StartsWith(resident.Name + " the " + En("npc.role.miner") + " lives in a hut", said.Text, StringComparison.Ordinal);
        Assert.Contains(w.Server.PlanetPoisForTest(Pid(w)), p => p.Type == "lodge");
        // A person to meet, like a villager: the first meeting's lift on the wanderer's own key.
        Assert.True(w.Server.RelationshipValueForTest(NpcLifeWorld.Owner, Out(w, id).PersonKey) > 0);

        var home = w.Server.NpcRoutineForTest(resident.Id).Pos;
        w.Player.State.Position = new Vector3f(home.X + 1.2f, home.Y, home.Z);
        w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, resident.Id);
        Assert.Equal("lodge_miner", w.Server.ActiveDialogForTest(NpcLifeWorld.Owner)?.Key);
    }

    [Fact]
    public void AWanderer_TradesItsProfessionsGoods()
    {
        using var w = Arena("wander_trade");
        int id = Assert.IsType<int>(w.Server.WandererEncounterForTest(Pid(w), job: "miner", bearing: 0.0));
        foreach (var (item, n) in new[] { ("iron_ore", 200), ("copper_ore", 200), ("silicate", 200), ("carbon", 200) })
        {
            w.Player.State.Inventory.Add(item, n, 999);
        }

        w.TickAt(0.45, 3);
        var at = NpcPos(w, id);
        w.Player.State.Position = new Vector3f(at.X + 1.2f, at.Y, at.Z);
        w.TickAt(0.45, 1);
        at = NpcPos(w, id);
        w.Player.State.Position = new Vector3f(at.X + 1.2f, at.Y, at.Z);

        long day = w.Server.MarketDayForTest;
        var recipes = Content.Recipes.Values.ToList();
        var own = MarketStock.ListFor(recipes, "miners", day)
            .Where(x => x.MarketTheme == "miners" && x.Inputs.All(i => w.Player.State.Inventory.CountOf(i.Item) >= i.Count))
            .ToList();
        Assert.NotEmpty(own);
        int mark = w.Transport.Sent.Count;
        w.Server.Craft(Pid(w), own[0].Key);
        var res = w.Transport.Sent.Skip(mark).Select(x => x.Msg).OfType<CraftResult>().Last();
        Assert.True(res.Success, res.Reason);
    }

    // ---- the playtest helpers ------------------------------------------------------------------------------------

    private static IEnumerable<string> Rejections(NpcLifeWorld w, PlayerSession who)
        => w.Transport.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Select(r => r.Reason);

    [Fact]
    public void Slash_Wanderer_CallsOneAtAnyHour_ForTheAdminOnly_AndTpLodge_JumpsToTheNearestHut()
    {
        using var w = Arena("wander_admin", configure: c => c.Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true, AllowCheatsInCreative = true });
        var guest = w.Server.AddLocalPlayer("Guest");
        guest.State.AboardShip = false;
        guest.State.Position = new Vector3f(Cx + 2.5f, w.Feet, Cz + 2.5f);

        w.Server.HandleForTest(guest, new AdminCommandIntent { Command = "summon_wanderer" });
        Assert.Contains("@srv.admin.not_admin", Rejections(w, guest));
        Assert.Empty(w.Server.WanderersForTest);

        // At night, during a quiet spell, with no work anywhere on the bare pad: the admin's wanderer comes anyway.
        w.TickAt(0.9, 1);
        w.Server.SetWandererCooldownForTest(Pid(w), 600);
        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "summon_wanderer" });
        var called = Assert.Single(w.Server.WanderersForTest);
        Assert.Contains(w.MessagesToPlayer(), m => m.StartsWith(called.Name + " the ", StringComparison.Ordinal));
        w.TickAt(0.9, 5);
        Assert.NotEqual("Leaving", Out(w, called.NpcId).Phase); // the evening does not send a called one away

        // A second call while one is out for this player: the answer says why.
        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "summon_wanderer" });
        Assert.Contains("@srv.admin.wanderer_busy", Rejections(w, w.Player));

        // /tp lodge: the nearest hut first, then the next one; the German word works too.
        string near = w.Server.SpawnLodgeForTest(Cx + 20, w.PadY, Cz + 30, "gatherer", beds: 1);
        string far = w.Server.SpawnLodgeForTest(Cx - 55, w.PadY, Cz - 55, "miner", beds: 1);
        PlayerAt(w, Cx + 0.5f, Cz + 0.5f);
        var targets = w.Server.TeleportTargetsForTest(Pid(w)).Where(t => t.Kind == "lodge").ToList();
        Assert.Equal(2, targets.Count);
        var markers = w.Server.LodgeMarkersForTest(near);
        var yard = markers.Single(m => m.Type == BlocksBeyondTheStars.WorldGeneration.LodgeGenerator.MarkerYard).Pos;
        Assert.True(Math.Abs(targets[0].Position.X - yard.X) < 0.01f && Math.Abs(targets[0].Position.Z - yard.Z) < 0.01f);

        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "teleport_to_named", StringArg = "lodge" });
        Assert.Equal(targets[0].Position, w.Player.State.Position);
        PlayerAt(w, Cx + 0.5f, Cz + 0.5f);
        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "teleport_to_named", StringArg = "hütte 2" });
        Assert.Equal(targets[1].Position, w.Player.State.Position);
        _ = far;
    }

    [Fact]
    public void Slash_Wanderer_IsACheat_OffWhereCheatsAreOff()
    {
        using var w = Arena("wander_nocheat", pad: 8);
        w.Server.HandleForTest(w.Player, new AdminCommandIntent { Command = "summon_wanderer" });
        Assert.Contains("@srv.admin.cheats_disabled", Rejections(w, w.Player));
        Assert.Empty(w.Server.WanderersForTest);
    }

    private static void Assume(bool condition, string what)
    {
        if (!condition)
        {
            throw new Xunit.Sdk.XunitException("test world precondition failed: " + what);
        }
    }

    [Fact]
    public void APlayerWhoLeft_IsForgotten_ByTheWanderersTables()
    {
        // A long-running server must not keep every player id it ever saw (a rejoin arms a fresh quiet spell anyway).
        using var w = Arena("wander_prune", pad: 8);
        w.TickAt(0.45, 2.0);
        Assert.True(w.Server.WandererPlayerTableSizeForTest > 0);
        w.Server.DisconnectLocalPlayerForTest(Pid(w));
        w.Server.PruneWandererPlayerTablesForTest();
        Assert.Equal(0, w.Server.WandererPlayerTableSizeForTest);
    }
}
