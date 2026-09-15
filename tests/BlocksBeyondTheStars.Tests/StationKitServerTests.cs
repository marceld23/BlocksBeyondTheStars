// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
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
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1874 on the server: a fresh station is composed from a kit and PINNED (kit + composition); a replay after the kit
/// changed bakes the same station; the crew is one resident per cabin who sleeps in its own bed at station night, sits
/// in the canteen in the evening and staffs the posts by day; the option Off keeps the procedural interior.
/// </summary>
public sealed class StationKitServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_kitstations_" + Guid.NewGuid().ToString("N"));

    /// <summary>Content whose ONLY station structures are the kit's modules (no complete templates), so the joint
    /// random table always yields the kit. <paramref name="cabinsMin"/> lets a second content change the kit.</summary>
    private static GameContent KitContent(int cabinsMin, bool extraStorage)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var modules = new List<StructureTemplate>
        {
            StationKitComposerTests.Room("kit_hub", "hub", markers: new[] { ("spawn", 3, 1, 3), ("vendor", 2, 1, 4), ("mission_board", 4, 1, 4) }),
            StationKitComposerTests.Room("kit_cabin", "cabins", faces: new[] { "z-" }),
            StationKitComposerTests.Room("kit_canteen", "canteen", faces: new[] { "x-" }),
            StationKitComposerTests.Room("kit_storage", "storage", faces: new[] { "x+" }),
        };
        content.SetStructureTemplates(modules, content.SettlementTemplates);
        var kit = new StructureKit
        {
            Key = "k",
            Name = "Test Kit",
            Kind = "station",
            Tier = "small",
            Start = "kit_hub",
            Entries =
            {
                new KitEntry { Module = "kit_hub", Required = true, Max = 1 },
                new KitEntry { Module = "kit_cabin", Required = true, Min = cabinsMin, Max = cabinsMin },
                new KitEntry { Module = "kit_canteen", Required = true, Max = 1 },
                new KitEntry { Module = "kit_storage", Required = extraStorage, Max = extraStorage ? 1 : 0 },
            },
        };
        var warnings = new List<string>();
        content.SetStructureKits(new[] { kit }, warnings.Add);
        Assert.Empty(warnings);
        return content;
    }

    private SvGameServer Started(GameContent content, out SqliteWorldRepository repo, Frequency templateUse = Frequency.Rare, string world = "kitstations")
    {
        // Every station tier draws the same kit: tiers are rolled per station, so the kit is registered for all of them.
        foreach (string tier in new[] { "medium", "large", "huge", "colossal" })
        {
            if (content.KitByKey("k")!.Tier != tier && content.StructureKits.All(k => k.Tier != tier))
            {
                var copy = System.Text.Json.JsonSerializer.Deserialize<StructureKit>(System.Text.Json.JsonSerializer.Serialize(content.KitByKey("k")), ContentLoader.JsonOptions)!;
                copy.Key = "k_" + tier;
                copy.Tier = tier;
                var all = new List<StructureKit>(content.StructureKits) { copy };
                content.SetStructureKits(all);
            }
        }

        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 42,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = new WorldDescription { SpaceStations = Frequency.Frequent, StationTemplateUse = templateUse },
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, content, st, repo);
        server.Start();
        return server;
    }

    private static string BoardFirstStation(SvGameServer server, string playerId)
    {
        server.EnterSpace(playerId);
        var station = server.SpaceEntitiesFor(playerId).First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);
        server.ShipMove(playerId, station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation(playerId, station.Id);
        return station.Id;
    }

    private static string MarkersOf(SvGameServer server)
        => string.Join(";", server.SpaceStationMarkers.Select(m => $"{m.Type}@{m.Pos.X:F1},{m.Pos.Y:F1},{m.Pos.Z:F1}").OrderBy(s => s, StringComparer.Ordinal));

    /// <summary>Every block and shape of the station world's build box (the kit stations of this test fit in it).</summary>
    private static string BlocksOf(SvGameServer server)
    {
        var sb = new System.Text.StringBuilder();
        for (int x = 0; x < 80; x++)
            for (int y = 56; y < 90; y++)
                for (int z = 0; z < 80; z++)
                {
                    var p = new Vector3i(x, y, z);
                    var b = server.World.GetBlock(p);
                    if (!b.IsAir)
                    {
                        sb.Append(x).Append(',').Append(y).Append(',').Append(z).Append(':').Append(b.Value).Append(':').Append(server.World.GetShape(p)).Append(';');
                    }
                }

        return sb.ToString();
    }

    private static Vector3i Feet(Vector3f pos) => new((int)Math.Floor(pos.X), (int)Math.Floor(pos.Y), (int)Math.Floor(pos.Z));

    private static void TickUntil(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession pilot, double localTime, Func<bool> done, int maxTicks)
    {
        for (int i = 0; i < maxTicks && !done(); i++)
        {
            server.SetLocalDayFractionForTest(localTime, 0f);
            pilot.State.Health = 100f;
            pilot.State.Oxygen = 100f;
            pilot.State.Hunger = 100f;
            server.TickForTest(0.5);
        }
    }

    [Fact]
    public void AFreshStation_IsComposedFromTheKit_PinnedAndReplayed_AndTheCrewLivesInItsCabins()
    {
        string stationId;
        string markers, blocks;
        int crew;
        var server = Started(KitContent(cabinsMin: 2, extraStorage: false), out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            stationId = BoardFirstStation(server, "Pilot");
            Assert.True(server.InStation("Pilot"));
            Assert.Equal("k", server.StationKitForTest(stationId));
            var rec = Assert.Contains(stationId, repo.LoadMetadata()!.StationKits);
            Assert.Equal("k", rec.Kit);
            Assert.Equal(4, rec.Modules.Count); // hub + 2 cabins + canteen
            Assert.Equal("kit_hub", rec.Modules[0].Key);

            markers = MarkersOf(server);
            blocks = BlocksOf(server);
            Assert.Contains("cabin@", markers);
            Assert.Contains("lounge@", markers);
            Assert.Contains("door_slide@", markers);

            // The crew: one resident per cabin, the posts staffed by residents (vendor, quartermaster), everyone
            // with a bed of its own.
            var npcs = server.NpcSnapshots;
            crew = npcs.Count;
            Assert.Equal(2, crew);
            Assert.Contains(npcs, n => n.Role == "vendor");
            Assert.Contains(npcs, n => n.Role == "quartermaster");

            // Station night: everyone lies in a bed inside the hull.
            TickUntil(server, pilot, 0.9, () => server.NpcSnapshots.All(n => server.NpcRoutineForTest(n.Id).Pose == 2), 200);
            foreach (var n in server.NpcSnapshots)
            {
                var (pose, activity, pos, _) = server.NpcRoutineForTest(n.Id);
                Assert.Equal(2, pose);
                Assert.Equal("npc.activity.sleeping", activity);
                var feet = Feet(pos);
                Assert.Equal("bed", server.World.GetBlock(feet).IsAir ? "air" : "bed"); // lying ON the bed block
            }

            // Evening: the crew sits down in the canteen (its seats, not the cabin chairs).
            TickUntil(server, pilot, 0.72, () => server.NpcSnapshots.All(n => server.NpcRoutineForTest(n.Id).Pose == 1), 400);
            var lounge = server.SpaceStationMarkers.First(m => m.Type == "lounge").Pos;
            foreach (var n in server.NpcSnapshots)
            {
                var (pose, _, pos, _) = server.NpcRoutineForTest(n.Id);
                Assert.Equal(1, pose);
                Assert.True(Math.Abs(pos.X - lounge.X) <= 4 && Math.Abs(pos.Z - lounge.Z) <= 4, $"{n.Role} sits at {pos}, not in the canteen at {lounge}");
            }

            // Day: the vendor stands at the post and trades.
            var vendorMarker = server.SpaceStationMarkers.First(m => m.Type == "vendor").Pos;
            TickUntil(server, pilot, 0.45, () => server.NpcSnapshots.All(n => server.NpcRoutineForTest(n.Id).Pose == 0
                && Math.Abs(server.NpcRoutineForTest(n.Id).Pos.X - vendorMarker.X) < 40), 400);
            var vendor = server.NpcSnapshots.First(n => n.Role == "vendor");
            Assert.Equal("npc.activity.trading", server.NpcRoutineForTest(vendor.Id).Activity);
            pilot.State.Position = new Vector3f(vendorMarker.X, vendorMarker.Y - 0.5f, vendorMarker.Z);
            Assert.True(server.NearSpaceStationVendor(pilot.State));
            server.LeaveStation("Pilot"); // undock, so the restarted server boards the same station again from space
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        // The kit changed (three cabins, a required store room): the station replays its pinned composition.
        var server2 = Started(KitContent(cabinsMin: 3, extraStorage: true), out var repo2);
        using (repo2)
        {
            server2.AddLocalPlayer("Pilot");
            Assert.Equal(stationId, BoardFirstStation(server2, "Pilot"));
            Assert.Equal("k", server2.StationKitForTest(stationId));
            Assert.Equal(markers, MarkersOf(server2));
            Assert.Equal(blocks, BlocksOf(server2));
            Assert.Equal(crew, server2.NpcSnapshots.Count);
            server2.Stop();
        }
    }

    /// <summary>The door lanes of the boarded station, measured on the world (the build box of <see cref="BlocksOf"/>), with
    /// the usable (x, z) cells — air over a deck — mapped back to world cells at the lane's foot height.</summary>
    private static List<(List<Vector3i> Clear, List<Vector3i> Corners)> WorldLanes(SvGameServer server)
    {
        const int BoxY = 56, W = 80, H = 34, L = 80;
        ushort Get(int x, int y, int z) => server.World.GetBlock(new Vector3i(x, y + BoxY, z)).Value;
        bool Usable(Vector3i c) => server.World.GetBlock(c).IsAir && !server.World.GetBlock(new Vector3i(c.X, c.Y - 1, c.Z)).IsAir;
        var result = new List<(List<Vector3i>, List<Vector3i>)>();
        foreach (var (type, pos) in server.SpaceStationMarkers)
        {
            var cell = Feet(pos);
            if (!RoomFurnisher.IsDoorMarker(type) || RoomFurnisher.DoorLaneAt(Get, W, H, L, cell.X, cell.Y - BoxY, cell.Z) is not { } lane)
            {
                continue;
            }

            var clear = lane.Clear.Where(c => !lane.Gap.Contains(c)).Select(c => new Vector3i(c.X, lane.FootY + BoxY, c.Z)).Where(Usable).ToList();
            var corners = lane.Keep.Where(c => !lane.Clear.Contains(c)).Select(c => new Vector3i(c.X, lane.FootY + BoxY, c.Z)).Where(Usable).ToList();
            result.Add((clear, corners));
        }

        return result;
    }

    [Fact]
    public void AStationFromBeforeTheDoorLanes_LosesItsStaleFurnitureOnce_WhileBedsAndPlayerBuildsStay()
    {
        // #1901: a station stamps only its non-air cells, so a chair an older composer put in a doorway stayed in the world
        // forever. A station pinned before the fix (kit record revision 0) sheds such pieces on its next stamp — once.
        var content = KitContent(cabinsMin: 2, extraStorage: false);
        ushort steel = content.GetBlock("steel_floor")!.NumericId.Value;
        var crate = content.GetBlock("crate")!.NumericId;
        var bed = content.GetBlock("bed")!.NumericId;
        string stationId;
        Vector3i chairCell, cornerCell, playerCell, bedCell;

        var server = Started(content, out var repo, world: "kitlanes");
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            stationId = BoardFirstStation(server, "Pilot");
            Assert.Equal(StationKitRecord.CurrentRevision, repo.LoadMetadata()!.StationKits[stationId].Revision); // a fresh station needs no cleanup

            // The freshly baked station's lanes are walkable; the old composer's leftovers are faked into them.
            var lanes = WorldLanes(server);
            Assert.NotEmpty(lanes);
            var cells = lanes.SelectMany(l => l.Clear).Distinct().ToList();
            var corners = lanes.SelectMany(l => l.Corners).Where(c => !cells.Contains(c)).Distinct().ToList();
            Assert.True(cells.Count >= 3 && corners.Count >= 1, $"lane cells {cells.Count}, corners {corners.Count}");
            (chairCell, playerCell, bedCell, cornerCell) = (cells[0], cells[1], cells[2], corners[0]);

            server.World.SetBlock(chairCell, new BlockId(steel), shape: ShapeCode.Pack(BlockShape.Chair, 1)); // worldgen: no owner
            server.World.SetBlock(cornerCell, crate);
            server.World.SetBlock(bedCell, bed, shape: ShapeCode.Pack(BlockShape.BedHead, 0));
            server.World.SetBlock(playerCell, crate, owner: "Pilot"); // the pilot's own crate
            server.LeaveStation("Pilot");
            server.Stop();

            var meta = repo.LoadMetadata()!;
            meta.StationKits[stationId].Revision = 0; // as saved before #1901
            repo.SaveMetadata(meta);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var server2 = Started(content, out var repo2, world: "kitlanes");
        using (repo2)
        {
            server2.AddLocalPlayer("Pilot");
            Assert.Equal(stationId, BoardFirstStation(server2, "Pilot"));
            Assert.True(server2.World.GetBlock(chairCell).IsAir, "the stale chair leaves the doorway");
            Assert.True(server2.World.GetBlock(cornerCell).IsAir, "a stale crate where the bake now leaves air goes too");
            Assert.Equal(bed, server2.World.GetBlock(bedCell)); // beds are never touched
            Assert.Equal(crate, server2.World.GetBlock(playerCell)); // nor anything a player placed
            Assert.Equal(StationKitRecord.CurrentRevision, repo2.LoadMetadata()!.StationKits[stationId].Revision);

            server2.World.SetBlock(chairCell, new BlockId(steel), shape: ShapeCode.Pack(BlockShape.Chair, 1));
            server2.LeaveStation("Pilot");
            server2.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var server3 = Started(content, out var repo3, world: "kitlanes");
        using (repo3)
        {
            server3.AddLocalPlayer("Pilot");
            Assert.Equal(stationId, BoardFirstStation(server3, "Pilot"));
            Assert.False(server3.World.GetBlock(chairCell).IsAir, "the cleanup runs once — whatever stands there later stays");
            server3.Stop();
        }
    }

    [Fact]
    public void TemplateUseOff_KeepsTheProceduralStation_WithoutAKitPin()
    {
        var server = Started(KitContent(cabinsMin: 2, extraStorage: false), out var repo, Frequency.Off, "kitoff");
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            string id = BoardFirstStation(server, "Pilot");
            Assert.Equal(string.Empty, server.StationKitForTest(id));
            Assert.Empty(repo.LoadMetadata()!.StationKits);
            Assert.DoesNotContain(server.SpaceStationMarkers, m => m.Type == "cabin");
            Assert.True(server.NpcSnapshots.Count >= 3, "the procedural station still spawns its post keepers and filler crew");
            server.Stop();
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }
}
