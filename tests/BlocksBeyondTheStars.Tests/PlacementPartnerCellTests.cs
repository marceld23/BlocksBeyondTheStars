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
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2927: the further cells of a placement — a bed's foot half (#1846), the other cells of a multi-cell form (#1961) —
/// go through the anchor's rules: no doorway (door_here), no fluid the block cannot take (fluid_cell), and not the
/// player's own head cell. That head cell is compared canonically, so a player standing on the far side of the world
/// seam (a raw X west of 0) cannot wall their head in, neither with the anchor (above_head) nor with a further cell.
/// </summary>
public sealed class PlacementPartnerCellTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_partnercell_" + Guid.NewGuid().ToString("N"));
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

    private SvGameServer NewServer(string name, RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceNpcEnemies = AlienActivity.Off;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A builder on foot up in the air (every cell around y 200 is empty) with doors, beds, walls, mud and the
    /// shaping tool.</summary>
    private static PlayerSession Builder(SvGameServer server, float x = 0.5f)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(x, 200, 0.5f);
        p.State.Yaw = 0f;
        p.State.Inventory.Add("door_hinge", 2, 99);
        p.State.Inventory.Add("bed", 2, 64);
        p.State.Inventory.Add("iron_wall", 4, 99);
        p.State.Inventory.Add("shape_tool", 1, 1);
        p.State.Inventory.Add("mud", 8, 999);
        return p;
    }

    /// <summary>The player-designed form item made from <paramref name="material"/>.</summary>
    private static string FormItem(PlayerSession p, string material)
        => p.State.Inventory.Slots.First(s => s is { IsEmpty: false } && ItemKey.Base(s.Item) == material && ItemKey.Shape(s.Item) != 0)!.Item;

    private static string? LastRejection(RecordingTransport t, PlayerSession who) => t.To<ActionRejected>(who).LastOrDefault()?.Reason;

    private static bool IsAir(SvGameServer server, int x, int y, int z) => server.World.GetBlock(new Vector3i(x, y, z)).IsAir;

    [Fact]
    public void ABedsFootOrAFormsFurtherCell_IsRefusedInADoorway_AndCostsNothing()
    {
        var t = new RecordingTransport();
        var server = NewServer("partner_doorway", t, out var repo);
        using (repo)
        {
            var p = Builder(server);
            server.PlaceBlock(p.State.PlayerId, 1, 200, 0, "door_hinge"); // its opening: (1, 200..202, 0)
            Assert.Equal(1, server.DoorCount);

            // A bed whose head is free but whose foot (yaw 0 → +Z) lands in the doorway.
            server.PlaceBlock(p.State.PlayerId, 1, 200, -1, "bed", yaw: 0);
            Assert.Equal("@srv.place.bed_room", LastRejection(t, p));
            Assert.True(IsAir(server, 1, 200, -1), "no lone head half may be left in front of the door");
            Assert.Equal(2, p.State.Inventory.CountOf("bed"));

            // A bench of two cells whose anchor is free but whose second cell (yaw 2 → −X) lands in the doorway.
            server.CustomShapeCraft(p.State.PlayerId, "mud", MultiCellFormTests.Form(2, 1, 1), "Bank", count: 2);
            string bench = FormItem(p, "mud");
            server.PlaceBlock(p.State.PlayerId, 2, 201, 0, bench, yaw: 2);
            Assert.Equal("@srv.place.form_room", LastRejection(t, p));
            Assert.True(IsAir(server, 2, 201, 0));
            Assert.Equal(1, p.State.Inventory.CountOf(bench));

            // Turned away from the doorway the same bench goes down.
            server.PlaceBlock(p.State.PlayerId, 2, 201, 0, bench, yaw: 0);
            Assert.False(IsAir(server, 2, 201, 0));
            Assert.False(IsAir(server, 3, 201, 0));
        }
    }

    [Fact]
    public void AFurtherCell_TakesNoFluidTheAnchorsRulesRefuse()
    {
        // The fluid_cell rule refuses a door anywhere in a fluid and a torch in water. Shipped content shapes neither
        // (lights and doors keep their own geometry), so a torch is made shapeable here: the one way a further cell can
        // carry a block that rule refuses.
        _content.GetBlock("torch")!.Shapeable = true;
        var t = new RecordingTransport();
        var server = NewServer("partner_fluid", t, out var repo);
        using (repo)
        {
            var p = Builder(server);
            p.State.Inventory.Add("torch", 4, 99);
            server.CustomShapeCraft(p.State.PlayerId, "torch", MultiCellFormTests.Form(2, 1, 1), "Fackeln", count: 2);
            string pair = FormItem(p, "torch");
            var water = _content.GetBlock("water")!.NumericId;
            server.World.SetBlock(new Vector3i(3, 200, 0), water); // where cell 1 of an unturned form at (2, 200, 0) goes

            server.PlaceBlock(p.State.PlayerId, 2, 200, 0, pair, yaw: 0);

            Assert.Equal("@srv.place.form_room", LastRejection(t, p));
            Assert.True(IsAir(server, 2, 200, 0));
            Assert.Equal(water.Value, server.World.GetBlock(new Vector3i(3, 200, 0)).Value);
            Assert.Equal(1, p.State.Inventory.CountOf(pair));
        }
    }

    [Fact]
    public void ABlock_InThePlayersOwnHeadCell_IsRefused_AcrossTheWorldSeam()
    {
        var t = new RecordingTransport();
        var server = NewServer("partner_seam_head", t, out var repo);
        using (repo)
        {
            var p = Builder(server, x: -0.5f); // a raw position west of x 0: the head cell is (circumference − 1, 201, 0)
            int west = server.World.Circumference - 1;

            server.PlaceBlock(p.State.PlayerId, -1, 201, 0, "iron_wall");

            Assert.Equal("@srv.place.above_head", LastRejection(t, p));
            Assert.True(IsAir(server, west, 201, 0));
            Assert.Equal(4, p.State.Inventory.CountOf("iron_wall"));

            // The feet cell stays allowed (pillar-jumping), across the seam too.
            server.PlaceBlock(p.State.PlayerId, -1, 200, 0, "iron_wall");
            Assert.False(IsAir(server, west, 200, 0));
        }
    }

    [Fact]
    public void AFormsFurtherCell_InThePlayersOwnHeadCell_IsRefused_AcrossTheWorldSeam()
    {
        var t = new RecordingTransport();
        var server = NewServer("partner_seam_form", t, out var repo);
        using (repo)
        {
            var p = Builder(server, x: -0.5f);
            int circumference = server.World.Circumference;
            server.CustomShapeCraft(p.State.PlayerId, "mud", MultiCellFormTests.Form(2, 1, 1), "Bank", count: 2);
            string bench = FormItem(p, "mud");

            // Anchor at (−2, 201, 0), unturned: its second cell (−1, 201, 0) is the player's head.
            server.PlaceBlock(p.State.PlayerId, -2, 201, 0, bench, yaw: 0);

            Assert.Equal("@srv.place.form_room", LastRejection(t, p));
            Assert.True(IsAir(server, circumference - 2, 201, 0));
            Assert.True(IsAir(server, circumference - 1, 201, 0));
            Assert.Equal(1, p.State.Inventory.CountOf(bench));
        }
    }
}
