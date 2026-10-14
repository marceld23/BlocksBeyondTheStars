// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2585: aboard a ship a crafting station is the ship's MODULE — a forge built into the cabin is decoration (#2248) —
/// and the game now says so. A player inside his ship wanted titanium plates, read "needs a forge nearby", built a forge
/// right beside the workbench, and the plates still would not come: the refusal kept naming the forge ("you need a
/// forge nearby"), the forge in the ship got no word, and the next refusal (no titanium ore) would only have said "That
/// didn't work.". Pinned here: the refusal aboard names the module, off the ship the forge rule and its token are
/// unchanged, a material refusal names the missing items with what the player has and needs, and building a station
/// block into the own ship without its module gets one VEGA line per block kind.
/// </summary>
public sealed class ShipStationCraftingTests : IDisposable
{
    private const string StationNotice = "vega.hint.ship_station_module";
    private const string NoticeMilestone = "vega:hint:ship_station:";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_shipstation_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer NewServer(string name, NpcLifeWorld.RecordingTransport transport, out SqliteWorldRepository repo, bool starterShip)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = starterShip,
            PlaceSettlements = false,
        };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private BlockId Block(string key) => _content.GetBlock(key)!.NumericId;

    private static void EmptyPack(PlayerSession p)
    {
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            p.State.Inventory.SetSlot(i, null);
        }
    }

    private static void Give(PlayerSession p, string item, int count = 1)
        => Assert.Equal(0, p.State.Inventory.Add(item, count, 1024));

    private static Shared.State.ShipState ActiveShip(PlayerSession p) => p.Ships[p.ActiveShipId];

    /// <summary>One craft order, and the refusal that came back — null when it was crafted.</summary>
    private static string? Craft(SvGameServer server, NpcLifeWorld.RecordingTransport t, PlayerSession who, string recipe)
    {
        int before = t.Sent.Count;
        server.Craft(who.State.PlayerId, recipe);
        var result = t.Sent.Skip(before).Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<CraftResult>().Last();
        return result.Success ? null : result.Reason;
    }

    /// <summary>One build into the own ship; the refusal that came back, or null.</summary>
    private static string? Build(SvGameServer server, NpcLifeWorld.RecordingTransport t, PlayerSession who, Vector3i cell, string item)
    {
        int before = t.Sent.Count;
        server.HandleStructureEditForTest(who.State.PlayerId,
            new StructureEditIntent { StructureId = "ship:" + who.State.PlayerId, X = cell.X, Y = cell.Y, Z = cell.Z, ItemKey = item });
        return t.Sent.Skip(before).Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().LastOrDefault()?.Reason;
    }

    /// <summary>The station notices VEGA has sent this player — each a system line (kind 3), which muted hints do not hide.</summary>
    private static ShipAiLine[] StationNotices(NpcLifeWorld.RecordingTransport t, PlayerSession who)
    {
        var lines = t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ShipAiLine>()
            .Where(line => line.LineKey == StationNotice).ToArray();
        Assert.All(lines, line => Assert.Equal(3, line.Kind));
        return lines;
    }

    /// <summary>A starter ship's pilot just inside the rear hatch — parked on the planet, or in the walkable interior out in
    /// space (where the report came from) — and a free cabin cell beside them.</summary>
    private static (PlayerSession Pilot, Vector3i Cell) InTheCabin(SvGameServer server, bool inSpace, string locale = "en")
    {
        var pilot = server.AddLocalPlayer("Pilot", locale);
        EmptyPack(pilot);
        if (inSpace)
        {
            server.EnterSpace("Pilot");
            server.EnterShipInterior("Pilot");
            Assert.True(server.InShipInterior("Pilot"));
        }

        var hatch = server.BuildShipStructureForTest("Pilot").DoorCells.Single(c => c.Z == 0);
        var (origin, _) = server.LandedShipBoundsForTest("Pilot");
        pilot.State.Position = new Vector3f(origin.X + hatch.X + 0.5f, origin.Y + 1f, origin.Z + 1.5f);
        Assert.True(pilot.State.AboardShip);
        return (pilot, new Vector3i(hatch.X, hatch.Y, hatch.Z + 2));
    }

    /// <summary>A player on foot on the planet, mid-air below the atmosphere line, with nothing around.</summary>
    private static PlayerSession OnTheGround(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Walker");
        EmptyPack(p);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 120, 0);
        return p;
    }

    // ---------------- The refusal names the module aboard, and the forge off the ship ----------------

    /// <summary>The report itself: inside the ship (out in space, and parked), no refinery module — a forge built right beside
    /// the player in the cabin changes nothing, and the refusal names the ship's refinery MODULE, in the player's language.</summary>
    [Theory]
    [InlineData(true, "en", "Refinery")]
    [InlineData(false, "en", "Refinery")]
    [InlineData(true, "de", "Raffinerie")]
    public void Aboard_WithoutTheRefineryModule_TheRefusalNamesTheModule_EvenWithAForgeBesideYou(bool inSpace, string locale, string module)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipstation_aboard_" + inSpace + locale, t, out var repo, starterShip: true);
        using (repo)
        {
            var (pilot, cell) = InTheCabin(server, inSpace, locale);
            Assert.False(ActiveShip(pilot).HasModule("refinery"));
            Give(pilot, "titanium_ore", 2);

            Assert.Equal("@srv.craft.need_module:" + module, Craft(server, t, pilot, "titanium_plate"));

            pilot.State.InstantBuild = true;
            Assert.Null(Build(server, t, pilot, cell, "forge"));
            Assert.Equal(Block("forge"), server.BuildShipStructureForTest("Pilot").Get(cell));
            Assert.Equal("@srv.craft.need_module:" + module, Craft(server, t, pilot, "titanium_plate"));
            Assert.Equal(2, pilot.State.Inventory.CountOf("titanium_ore"));
        }
    }

    /// <summary>Aboard, a station no module provides (the campfire) says it works on a world only, naming the block.</summary>
    [Fact]
    public void Aboard_AStationNoModuleProvides_SaysItWorksOnAWorldOnly()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipstation_campfire", t, out var repo, starterShip: true);
        using (repo)
        {
            var (pilot, _) = InTheCabin(server, inSpace: false);
            var recipe = _content.Recipes.Values.First(r => r.Station == Shared.Definitions.CraftingStation.Campfire
                && string.IsNullOrEmpty(r.RequiredBlueprint));

            Assert.Equal("@srv.craft.world_only:Campfire", Craft(server, t, pilot, recipe.Key));
        }
    }

    /// <summary>The factory keeps its own token aboard and off the ship alike: "only on a world, next to this block" would
    /// point at a terminal block, and a player-placed terminal never produces (#1265).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFactoryRecipe_KeepsTheStationToken_AboardAndOffTheShip(bool aboard)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipstation_factory_" + aboard, t, out var repo, starterShip: aboard);
        using (repo)
        {
            var p = aboard ? InTheCabin(server, inSpace: false).Pilot : OnTheGround(server);
            var recipe = _content.Recipes.Values.First(r => r.Station == Shared.Definitions.CraftingStation.Factory
                && string.IsNullOrEmpty(r.RequiredBlueprint));

            Assert.Equal("@need_station:factory", Craft(server, t, p, recipe.Key));
        }
    }

    /// <summary>With the refinery module fitted the plate is made aboard — and the ore may come from the ship's hold.</summary>
    [Fact]
    public void Aboard_WithTheRefineryModule_ThePlateIsMade_FromTheHoldToo()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipstation_module", t, out var repo, starterShip: true);
        using (repo)
        {
            var (pilot, _) = InTheCabin(server, inSpace: true);
            var ship = ActiveShip(pilot);
            ship.Modules.Add("refinery");
            Give(pilot, "titanium_ore", 1);
            Assert.Equal(0, ship.Cargo.Add("titanium_ore", 1, 1024));

            Assert.Null(Craft(server, t, pilot, "titanium_plate"));
            Assert.Equal(1, pilot.State.Inventory.CountOf("titanium_plate"));
        }
    }

    /// <summary>Off the ship nothing changed: without a forge in reach the refusal is the station token it always was …</summary>
    [Fact]
    public void OffTheShip_WithoutAForge_TheRefusalIsTheStationTokenAsBefore()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipstation_ground", t, out var repo, starterShip: false);
        using (repo)
        {
            var p = OnTheGround(server);
            Give(p, "titanium_ore", 2);

            Assert.Equal("@need_station:refinery", Craft(server, t, p, "titanium_plate"));
            Assert.Equal(2, p.State.Inventory.CountOf("titanium_ore"));
        }
    }

    /// <summary>… and a forge in reach makes the plate.</summary>
    [Fact]
    public void OffTheShip_WithAForgeInReach_ThePlateIsMade()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipstation_forge", t, out var repo, starterShip: false);
        using (repo)
        {
            var p = OnTheGround(server);
            server.World.SetBlock(new Vector3i(1, 120, 0), Block("forge"));
            Give(p, "titanium_ore", 2);

            Assert.Null(Craft(server, t, p, "titanium_plate"));
            Assert.Equal(1, p.State.Inventory.CountOf("titanium_plate"));
            Assert.Equal(0, p.State.Inventory.CountOf("titanium_ore"));
        }
    }

    // ---------------- A material refusal names what is missing ----------------

    /// <summary>At the forge without ore: the refusal names the titanium ore with what the player has and what the craft
    /// needs — in the player's language, and scaled for a batch.</summary>
    [Theory]
    [InlineData("en", 0, 1, "Titanium Ore (0/2)")]
    [InlineData("en", 1, 1, "Titanium Ore (1/2)")]
    [InlineData("de", 3, 2, "Titanerz (3/4)")]
    public void MissingOre_TheRefusalNamesIt_WithHaveAndNeed(string locale, int have, int batch, string expected)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipstation_missing_" + locale + have, t, out var repo, starterShip: false);
        using (repo)
        {
            var p = server.AddLocalPlayer("Walker", locale);
            EmptyPack(p);
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 120, 0);
            server.World.SetBlock(new Vector3i(1, 120, 0), Block("forge"));
            if (have > 0)
            {
                Give(p, "titanium_ore", have);
            }

            int before = t.Sent.Count;
            server.Craft(p.State.PlayerId, "titanium_plate", batch);
            var result = t.Sent.Skip(before).Where(s => s.Conn == p.ConnectionId).Select(s => s.Msg).OfType<CraftResult>().Last();

            Assert.False(result.Success);
            Assert.Equal("@srv.craft.missing_named:" + expected, result.Reason);
            Assert.Equal(have, p.State.Inventory.CountOf("titanium_ore"));
        }
    }

    /// <summary>Only what is short is named, each with its own count, comma-separated.</summary>
    [Fact]
    public void MissingMaterials_OnlyTheShortOnesAreNamed()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipstation_missing_two", t, out var repo, starterShip: false);
        using (repo)
        {
            var p = OnTheGround(server);
            server.World.SetBlock(new Vector3i(1, 120, 0), Block("workbench"));
            var recipe = _content.Recipes.Values.First(r => r.Station == Shared.Definitions.CraftingStation.Workshop
                && string.IsNullOrEmpty(r.RequiredBlueprint) && r.Inputs.Count >= 2
                && r.Inputs.Select(i => i.Item).Distinct().Count() == r.Inputs.Count);
            var first = recipe.Inputs[0];
            Give(p, first.Item, first.Count); // the first input is complete, every other one is missing

            string? reason = Craft(server, t, p, recipe.Key);

            var en = TestLocales.Load("en");
            string Name(string item) => en[_content.GetItem(item)!.NameKey];
            string expected = string.Join(", ", recipe.Inputs.Skip(1).Select(i => $"{Name(i.Item)} (0/{i.Count})"));
            Assert.Equal("@srv.craft.missing_named:" + expected, reason);
            Assert.DoesNotContain(Name(first.Item), reason!, StringComparison.Ordinal);
        }
    }

    // ---------------- One VEGA line per station block built into the own ship ----------------

    /// <summary>A forge built into the ship without the refinery module: VEGA names the module once — a second forge says
    /// nothing more. A workbench (the starter ship has the workshop module) is a harmless second bench and says nothing; a
    /// detoxifier (no detoxifier module) gets its own line. Every block is built and paid for, and the shared decoration
    /// flag stays untouched — it belongs to the bio lab and the Crystal Net devices.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStationBlockInTheOwnShip_WithoutItsModule_GetsOneNoticePerBlockKind(bool inSpace)
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipstation_notice_" + inSpace, t, out var repo, starterShip: true);
        using (repo)
        {
            var (pilot, cell) = InTheCabin(server, inSpace);
            var ship = ActiveShip(pilot);
            Assert.True(ship.HasModule("workshop"));
            Assert.False(ship.HasModule("refinery") || ship.HasModule("detoxifier"));
            var above = new Vector3i(cell.X, cell.Y + 1, cell.Z);

            Give(pilot, "forge");
            Assert.Null(Build(server, t, pilot, cell, "forge"));
            Assert.Equal(0, pilot.State.Inventory.CountOf("forge"));
            var notices = StationNotices(t, pilot);
            Assert.Equal("Refinery", Assert.Single(notices).LineArg);
            Assert.Contains(NoticeMilestone + "forge", pilot.State.Milestones);
            Assert.Contains(NoticeMilestone + "forge", repo.LoadPlayer("Pilot")!.Milestones); // saved: a restart does not repeat it

            Give(pilot, "forge");
            Assert.Null(Build(server, t, pilot, above, "forge"));
            Assert.Single(StationNotices(t, pilot));

            var beside = new Vector3i(cell.X + 1, cell.Y, cell.Z);
            Give(pilot, "workbench");
            Assert.Null(Build(server, t, pilot, beside, "workbench"));
            Assert.Equal(Block("workbench"), server.BuildShipStructureForTest("Pilot").Get(beside));
            Assert.Single(StationNotices(t, pilot));
            Assert.DoesNotContain(NoticeMilestone + "workbench", pilot.State.Milestones);

            var other = new Vector3i(cell.X - 1, cell.Y, cell.Z);
            Give(pilot, "detoxifier");
            Assert.Null(Build(server, t, pilot, other, "detoxifier"));
            Assert.Equal(new[] { "Refinery", "Detoxifier" }, StationNotices(t, pilot).Select(n => n.LineArg));

            Assert.DoesNotContain("vega:hint:ship_decor", pilot.State.Milestones);
        }
    }

    /// <summary>With the refinery module fitted, a forge in the cabin is a harmless second station: nothing is said.</summary>
    [Fact]
    public void AForgeInTheOwnShip_WithTheRefineryModule_SaysNothing()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipstation_notice_module", t, out var repo, starterShip: true);
        using (repo)
        {
            var (pilot, cell) = InTheCabin(server, inSpace: false);
            ActiveShip(pilot).Modules.Add("refinery");

            Give(pilot, "forge");
            Assert.Null(Build(server, t, pilot, cell, "forge"));

            Assert.Equal(Block("forge"), server.BuildShipStructureForTest("Pilot").Get(cell));
            Assert.Empty(StationNotices(t, pilot));
            Assert.DoesNotContain(NoticeMilestone + "forge", pilot.State.Milestones);
        }
    }

    /// <summary>A forge on the ground is a working forge: no ship, no notice.</summary>
    [Fact]
    public void AForgeOnTheGround_SaysNothing()
    {
        var t = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("shipstation_notice_ground", t, out var repo, starterShip: false);
        using (repo)
        {
            var p = OnTheGround(server);
            Give(p, "forge");
            server.PlaceBlock("Walker", 1, 120, 0, "forge");

            Assert.Equal(Block("forge"), server.World.GetBlock(new Vector3i(1, 120, 0)));
            Assert.Empty(StationNotices(t, p));
        }
    }

    /// <summary>The block ↔ module pairs the notice and the refusal rely on: every station a module provides has a block
    /// that maps back to that module, and the campfire and the algae tank map to none.</summary>
    [Theory]
    [InlineData("forge", "refinery")]
    [InlineData("workbench", "workshop")]
    [InlineData("detoxifier", "detoxifier")]
    [InlineData("matter_forge", "transmuter")]
    [InlineData("decontaminator", "decontaminator")]
    [InlineData("campfire", null)]
    [InlineData("algae_tank", null)]
    [InlineData("iron_wall", null)]
    public void EveryStationBlock_MapsToTheModuleThatDoesItsJobAboard(string block, string? module)
    {
        Assert.Equal(module, SvGameServer.StationModuleForBlock(block));
        if (module is not null)
        {
            Assert.NotNull(_content.GetShipModule(module));
            Assert.NotNull(_content.GetBlock(block));
        }
    }
}
