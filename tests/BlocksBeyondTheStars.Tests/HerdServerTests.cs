// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The begging herds on a live server (#2018): a beggar comes to a player holding food and orbits, leaves when the food is
/// stowed, ignores poison and baits, never begs while it sleeps; a skittish species never begs; the Feed action throws one piece
/// the herd rushes, squabbles over and eats while the thrower cannot pick it up; and a herd of twelve is cheap for the budget.
/// </summary>
public sealed class HerdServerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public HerdServerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_herd_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "herd"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "herd",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Forces roster slot 0 into an always-awake walking grazer of the wanted temper that begs (per its flag) and lives
    /// in a group of <paramref name="group"/>.</summary>
    private static CreatureSpecies ForceBeggar(SvGameServer server, CreatureTemperament temperament = CreatureTemperament.Passive,
        int group = 3, CreatureActivity activity = CreatureActivity.Cathemeral)
    {
        var sp = server.SpeciesRoster.First();
        sp.Habitat = CreatureHabitat.Land;
        sp.BodyPlan = CreatureBodyPlan.Standard;
        sp.HeadShape = CreatureHeadShape.Box;
        sp.Legs = 4;
        sp.BodySegments = 1;
        sp.Size = 1f;
        sp.Speed = 3f;
        sp.Temperament = temperament;
        sp.LocoStyle = LocomotionStyle.Strider;
        sp.Activity = activity;
        sp.SocialGroupSize = group;
        sp.BegsForFood = true;
        sp.HasWings = false;
        sp.HasGasSac = false;
        sp.AttackDamage = 0f;
        return sp;
    }

    private static int SurfaceTopY(SvGameServer server, int x, int z)
    {
        for (int y = 200; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    private static int MaxTopY(SvGameServer server, int cx, int cz, int r)
    {
        int max = int.MinValue;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                max = Math.Max(max, SurfaceTopY(server, cx + dx, cz + dz));
            }
        }

        return max;
    }

    /// <summary>A flat stone pad well above the terrain, so the body checks see open ground.</summary>
    private int BuildPad(SvGameServer server, int cx, int cz, int r)
    {
        int padY = MaxTopY(server, cx, cz, r + 4) + 8;
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                server.World.SetBlock(new Vector3i(cx + dx, padY, cz + dz), stone);
            }
        }

        return padY;
    }

    private static float Dist(Vector3f a, Vector3f b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static void Tick(SvGameServer server, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            server.TickForTest(0.1);
        }
    }

    private static Vector3f Pos(SvGameServer server, string id) => server.Creatures.First(c => c.Id == id).Position;

    [Fact]
    public void ABeggar_ComesToTheFoodInYourHand_OrbitsYou_AndLeavesWhenYouStowIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceBeggar(server);
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 24); // room to trot away in every direction
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);
            p.State.Inventory.SetSlot(0, new ItemStack("berries", 5));
            p.State.SelectedHotbarSlot = 0;

            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 8.5f, padY + 1, cz + 0.5f), sp.Id);
            Tick(server, 60);

            Assert.Equal("Beg", server.BegPhaseForTest(id));
            Assert.True(server.NetCreatureForTest(id).Begging, "the wire carries the begging for the pose and the calls");
            float near = Dist(Pos(server, id), p.State.Position);
            Assert.InRange(near, 1.0f, 4.5f); // on its ring around the player, not on top of them
            Assert.True(p.State.Milestones.Contains("vega:hint:feed_herd"), "VEGA explained the Feed action once");

            // Food away: the herd trots off and stays away for the cooldown.
            p.State.SelectedHotbarSlot = 1;
            Tick(server, 50);
            Assert.False(server.NetCreatureForTest(id).Begging);
            Assert.True(Dist(Pos(server, id), p.State.Position) > near + 1.0f, $"it left the player: {near:0.0} → {Dist(Pos(server, id), p.State.Position):0.0}");
            Assert.NotEqual("Beg", server.BegPhaseForTest(id));

            // Food back in hand right away: still in the cooldown, so no second bout.
            p.State.SelectedHotbarSlot = 0;
            Tick(server, 20);
            Assert.NotEqual("Beg", server.BegPhaseForTest(id));
        }
    }

    [Fact]
    public void PoisonAndBait_LureNobody_AndASkittishSpeciesNeverBegs()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceBeggar(server);
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 16);
            p.State.Position = new Vector3f(cx + 6.5f, padY + 1, cz + 0.5f);
            p.State.Inventory.SetSlot(0, new ItemStack("toxic_berries", 5));
            p.State.Inventory.SetSlot(1, new ItemStack("forage_bait", 5));

            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f), sp.Id);
            p.State.SelectedHotbarSlot = 0;
            Tick(server, 30);
            Assert.Equal("None", server.BegPhaseForTest(id));
            p.State.SelectedHotbarSlot = 1;
            Tick(server, 30);
            Assert.Equal("None", server.BegPhaseForTest(id));

            // A skittish species with the flag: the rule says no — it flees like it always did.
            sp.Temperament = CreatureTemperament.Skittish;
            p.State.Inventory.SetSlot(2, new ItemStack("berries", 5));
            p.State.SelectedHotbarSlot = 2;
            var before = Pos(server, id);
            p.State.Position = new Vector3f(before.X + 4.5f, padY + 1, before.Z);
            Tick(server, 30);
            Assert.Equal("None", server.BegPhaseForTest(id));
            Assert.False(server.NetCreatureForTest(id).Begging);
            Assert.True(Dist(Pos(server, id), p.State.Position) > 4.5f, "a skittish animal keeps its distance");
        }
    }

    [Fact]
    public void ASleepingHerd_IgnoresFood()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceBeggar(server, activity: CreatureActivity.Nocturnal);
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 16);
            p.State.Position = new Vector3f(cx + 12.5f, padY + 1, cz + 0.5f); // outside the wake distance, inside the lure range
            p.State.Inventory.SetSlot(0, new ItemStack("berries", 5));
            p.State.SelectedHotbarSlot = 0;
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 3.5f, padY + 1, cz + 0.5f), sp.Id);

            // Find a daytime for this longitude: a nocturnal animal sleeps through it.
            bool asleep = false;
            foreach (double fraction in new[] { 0.5, 0.0, 0.25, 0.75 })
            {
                server.SetLocalDayFractionForTest(fraction, cx);
                server.TickForTest(0.1);
                if (server.NetCreatureForTest(id).Asleep)
                {
                    asleep = true;
                    break;
                }
            }

            Assert.True(asleep, "the nocturnal test animal must be asleep at one of the four times of day");
            var start = Pos(server, id);
            Tick(server, 40);
            Assert.Equal("None", server.BegPhaseForTest(id));
            Assert.True(Dist(Pos(server, id), start) < 0.5f, "a sleeper does not come for food");
        }
    }

    [Fact]
    public void Feed_ThrowsOnePiece_TheHerdRushesAndEatsIt_AndTheThrowerCannotTakeItBack()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceBeggar(server);
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 16);
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);
            p.State.Yaw = 0f; // facing +Z
            p.State.Inventory.SetSlot(0, new ItemStack("berries", 5));
            p.State.Inventory.SetSlot(1, new ItemStack("toxic_berries", 2));
            p.State.SelectedHotbarSlot = 0;
            string id = server.SpawnCreatureAtForTest(new Vector3f(cx + 6.5f, padY + 1, cz + 0.5f), sp.Id);
            Tick(server, 30);
            Assert.Equal("Beg", server.BegPhaseForTest(id));
            int berries = p.State.Inventory.CountOf("berries"); // the starter pack carries some of its own
            int toxic = p.State.Inventory.CountOf("toxic_berries");

            // Poison is refused, and so is an empty slot — nothing leaves the pack.
            server.ThrowFoodForTest(p.State.PlayerId, 1);
            server.ThrowFoodForTest(p.State.PlayerId, 5);
            Assert.Equal(berries, p.State.Inventory.CountOf("berries"));
            Assert.Equal(toxic, p.State.Inventory.CountOf("toxic_berries"));
            Assert.Null(server.ThrownFoodCellForTest());

            server.ThrowFoodForTest(p.State.PlayerId, 0);
            Assert.Equal(berries - 1, p.State.Inventory.CountOf("berries"));
            var cell = server.ThrownFoodCellForTest();
            Assert.NotNull(cell);
            Assert.Equal(cx, cell!.Value.X);
            Assert.InRange(cell.Value.Z, cz + 2, cz + 3); // ThrowDistance 2.5 along +Z
            Assert.Contains(server.DropPackets, d => d.Position == cell.Value && d.Items.Any(s => s.Item == "berries"));

            Tick(server, 25);
            Assert.Contains(server.BegPhaseForTest(id), new[] { "Rush", "Squabble" });
            Assert.True(Dist(Pos(server, id), new Vector3f(cell.Value.X + 0.5f, cell.Value.Y, cell.Value.Z + 0.5f)) < 3f, "it ran to the piece");
            Assert.Equal(berries - 1, p.State.Inventory.CountOf("berries")); // the grace: the thrower standing 3 blocks off does not get it back

            Tick(server, 80);
            Assert.Null(server.ThrownFoodCellForTest()); // eaten
            Assert.Equal(berries - 1, p.State.Inventory.CountOf("berries"));
            Assert.DoesNotContain(server.DropPackets, d => d.Items.Any(s => s.Item == "berries"));
            Assert.Contains(server.BegPhaseForTest(id), new[] { "Leave", "None" });
        }
    }

    [Fact]
    public void Feed_IsRefused_WhenNoAnimalBegs()
    {
        var server = Started(out var repo);
        using (repo)
        {
            ForceBeggar(server);
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 8);
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);
            p.State.Inventory.SetSlot(0, new ItemStack("berries", 5));
            p.State.SelectedHotbarSlot = 0;
            Tick(server, 5);
            int berries = p.State.Inventory.CountOf("berries");

            server.ThrowFoodForTest(p.State.PlayerId, 0);
            Assert.Equal(berries, p.State.Inventory.CountOf("berries"));
            Assert.Null(server.ThrownFoodCellForTest());
        }
    }

    [Fact]
    public void ABigHerd_SpawnsWhole_IsCheapForTheBudget_AndComesOneAtATime()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceBeggar(server, group: 12);
            var p = server.AddLocalPlayer("Bait");
            p.State.AboardShip = false;
            const int cx = 300, cz = 300;
            int padY = BuildPad(server, cx, cz, 22);
            p.State.Position = new Vector3f(cx + 0.5f, padY + 1, cz + 0.5f);

            Assert.True(server.SpeciesMaySpawnForTest(sp.Id), "no herd alive yet — the species may spawn");
            server.SpawnGroupAroundForTest(sp.Id, cx, cz); // the members around a leader spot on the flat pad
            int herd = server.Creatures.Count(c => c.SpeciesId == sp.Id);
            Assert.True(herd >= 8, $"a herd of twelve on open ground must place at least eight members, placed {herd}");

            int raw = server.Creatures.Count(c => string.IsNullOrEmpty(c.OwnerId) && !c.IsGiant);
            Assert.True(server.WeightedWildCountForTest() < raw, "the herd costs a third of its bodies against the cap");
            Assert.Equal(raw - herd + (herd + 2) / 3, server.WeightedWildCountForTest());
            Assert.False(server.SpeciesMaySpawnForTest(sp.Id), "one herd at a time");

            // The crowding prune reads the same weighted count: the herd in view stays whole across ticks.
            Tick(server, 20);
            Assert.True(server.Creatures.Count(c => c.SpeciesId == sp.Id) >= herd, "the prune must not eat the herd it just budgeted");
        }
    }
}
