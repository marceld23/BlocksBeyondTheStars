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
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1876: settlement and city kits — the kit shapes the grid and fills the plots / districts, and every
/// composition is pinned so a reload never changes a settlement.</summary>
public sealed class SettlementKitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_setkits_" + Guid.NewGuid().ToString("N"));
    private static readonly GameContent Base = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static StructureTemplate Box(string key, string function, string tier, int w, int h, int l, string material,
        params (string Id, int X, int Y, int Z)[] markers)
    {
        var t = new StructureTemplate { Key = key, Name = key, Tier = tier, Kind = "settlement", Kit = "vk", Function = function, Width = w, Height = h, Length = l };
        for (int x = 0; x < w; x++)
            for (int z = 0; z < l; z++)
                for (int y = 0; y < h; y++)
                {
                    bool shell = y == 0 || y == h - 1 || x == 0 || x == w - 1 || z == 0 || z == l - 1;
                    bool door = z == 0 && (x == w / 2 - 1 || x == w / 2) && y >= 1 && y <= Math.Min(3, h - 2);
                    if (shell && !door)
                    {
                        t.Cells.Add(new TemplateCell { X = x, Y = y, Z = z, Kind = "block", Id = material });
                    }
                }

        foreach (var m in markers)
        {
            t.Cells.Add(new TemplateCell { X = m.X, Y = m.Y, Z = m.Z, Kind = "marker", Id = m.Id });
        }

        return t;
    }

    private static List<StructureTemplate> Pool() => new()
    {
        Box("gold_house", StructureRoles.House, "village", 6, 7, 6, "gold_block", ("npc", 3, 1, 3), ("door_hinge", 2, 1, 0)),
        Box("gold_market", StructureRoles.Market, "village", 6, 7, 6, "gold_block", ("vendor", 3, 1, 3), ("door_hinge", 2, 1, 0)),
    };

    private static StructureKit Kit(int houseMin, int houseMax, bool modulesOnly, int plot = 0, int building = 0, int cols = 0, int rows = 0) => new()
    {
        Key = "vk",
        Kind = "settlement",
        Tier = "village",
        Weight = 99,
        PlotStride = plot,
        Building = building,
        ColsMin = cols,
        ColsMax = cols,
        RowsMin = rows,
        RowsMax = rows,
        ModulesOnly = modulesOnly,
        Entries = { new KitEntry { Module = "gold_market", Required = true }, new KitEntry { Module = "gold_house", Min = houseMin, Max = houseMax } },
    };

    private static string Signature(SettlementStructure s)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(s.Width).Append('x').Append(s.Height).Append('x').Append(s.Length).Append('|');
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    ushort b = s.Get(x, y, z);
                    if (b != 0)
                    {
                        sb.Append(x).Append(',').Append(y).Append(',').Append(z).Append(':').Append(b).Append(':').Append(s.GetShape(x, y, z)).Append(';');
                    }
                }

        return sb.ToString();
    }

    [Fact]
    public void AKit_ShapesTheGrid_AndFillsThePlots_RequiredFirst_ThenUpToMax()
    {
        var pool = Pool();
        var kit = Kit(houseMin: 2, houseMax: 3, modulesOnly: false, plot: 10, building: 8, cols: 3, rows: 2);
        var layout = SettlementLayoutSpec.FromKit(kit, "village", new Random(1));
        Assert.Equal((3, 2, 10, 8), (layout.Cols, layout.Rows, layout.Plot, layout.Building));

        var composition = new List<string>();
        var s = SettlementGenerator.Generate("village", false, 77, "grass", Base, null, 0, composition, null, layout, kit, pool);
        Assert.Equal((3 * 10 + 1, 2 * 10 + 1), (s.Width, s.Length));
        Assert.Equal(6, composition.Count);
        Assert.Equal("gold_market", composition[0]); // the required market on plot 0
        Assert.Equal(string.Empty, composition[1]);   // no board module in the kit → the procedural office
        Assert.InRange(composition.Count(k => k == "gold_house"), 2, 3);
        Assert.Contains(s.Markers, m => m.Type == "vendor");
        Assert.Contains(s.Markers, m => m.Type == "mission_board");

        // The pinned grid + composition replay byte for byte, even with a changed kit and no kit at all.
        var replayed = SettlementGenerator.Generate("village", false, 77, "grass", Base, null, 0, new List<string>(composition), null, layout, null, pool);
        Assert.Equal(Signature(s), Signature(replayed));
        var changed = Kit(houseMin: 0, houseMax: 0, modulesOnly: true, plot: 12, building: 10, cols: 4, rows: 4);
        var replayed2 = SettlementGenerator.Generate("village", false, 77, "grass", Base, null, 0, new List<string>(composition), null, layout, changed, pool);
        Assert.Equal(Signature(s), Signature(replayed2));
        Assert.True(SettlementLayoutSpec.TryParse(layout.Serialize(), out var parsed));
        Assert.Equal(layout.Serialize(), parsed.Serialize());
    }

    [Fact]
    public void ModulesOnly_LeavesUnfilledDwellingPlotsOpen_ButKeepsTheServices()
    {
        var pool = Pool();
        var kit = Kit(houseMin: 1, houseMax: 1, modulesOnly: true, cols: 3, rows: 2);
        var layout = SettlementLayoutSpec.FromKit(kit, "village", new Random(1));
        var composition = new List<string>();
        var s = SettlementGenerator.Generate("village", false, 5, "grass", Base, null, 0, composition, null, layout, kit, pool);
        // market (module) + board (procedural, a service) + the greenhouses (services too) + one house module —
        // every other dwelling plot stays a square.
        Assert.Equal(3 + s.Markers.Count(m => m.Type == "greenhouse"), s.BuildingCount);
        Assert.Equal(1, composition.Count(k => k == "gold_house"));
    }

    [Fact]
    public void ACityKit_ShapesTheDistrictGrid_AndItsMap()
    {
        var kit = new StructureKit
        {
            Key = "ck", Kind = "city", Tier = "metropolis", Grid = 5, DistrictSize = 24, Street = 3, Height = 18,
            RoleMap = { "TRGRT", "RGMGR", "RMPMR", "RGHGR", "TRRRT" },
        };
        var layout = CityLayoutSpec.FromKit(kit);
        Assert.Equal((5, 24, 3, 18), (layout.Grid, layout.DistrictSize, layout.Street, layout.Height));
        Assert.Equal(5 * 24 + 6 * 3, layout.Footprint);
        Assert.Equal(CityGenerator.Role.Plaza, CityGenerator.RoleAtFor(2, 2, 5, layout.RoleMap));
        Assert.Equal(CityGenerator.Role.Hall, CityGenerator.RoleAtFor(2, 3, 5, layout.RoleMap)); // row 3, column 2
        Assert.Equal(CityGenerator.Role.Tower, CityGenerator.RoleAtFor(0, 0, 5, layout.RoleMap));
        Assert.Equal(CityGenerator.RoleAt(3, 3), CityGenerator.RoleAtFor(3, 3, 7, null)); // the default map is unchanged

        var composition = new List<string>();
        var s = CityGenerator.Generate(9, Base, Array.Empty<CityGenerator.OpenZone>(), null, 0, composition, null, layout, kit, new List<StructureTemplate>());
        Assert.Equal(layout.Footprint, s.Width);
        Assert.Equal(layout.Height, s.Height);
        Assert.Equal(25, composition.Count);
        Assert.Contains(s.Markers, m => m.Type == "vendor");
        Assert.Contains(s.Markers, m => m.Type == "guard_post");
        Assert.True(CityLayoutSpec.TryParse(layout.Serialize(), out var parsed));
        Assert.Equal(layout.Serialize(), parsed.Serialize());
    }

    private SvGameServer Started(GameContent content, string world, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = world, Seed = seed, StartPlanet = "meadowlands", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false, PlaceSettlements = true };
        config.World.SettlementTemplateUse = Frequency.Frequent;
        var server = new SvGameServer(config, content, st, repo);
        server.Start();
        return server;
    }

    private static GameContent KitContent(int houseMax, int plot)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var settlements = new List<StructureTemplate>(content.SettlementTemplates);
        settlements.AddRange(Pool());
        content.SetStructureTemplates(content.StationTemplates, settlements);
        var kits = new List<StructureKit>();
        foreach (string tier in new[] { "hamlet", "village", "town", "city" })
        {
            var k = Kit(houseMin: 1, houseMax: houseMax, modulesOnly: false, plot: plot);
            k.Key = "vk_" + tier;
            k.Tier = tier;
            kits.Add(k);
        }

        content.SetStructureKits(kits);
        return content;
    }

    private static Dictionary<(int, int), ushort[]> Snapshot(SvGameServer server)
    {
        var shot = new Dictionary<(int, int), ushort[]>();
        foreach (var box in server.SettlementsForTest)
        {
            int gy = server.PlacementRecordsForTest.First(r => r.Kind == "settlement" && r.X == box.MinX && r.Z == box.MinZ).GroundY;
            var cells = new List<ushort>();
            for (int x = box.MinX; x <= box.MaxX; x++)
                for (int z = box.MinZ; z <= box.MaxZ; z++)
                    for (int y = gy; y <= gy + 14; y++)
                    {
                        cells.Add(server.World.GetBlock(new Vector3i(x, y, z)).Value);
                    }

            shot[(box.MinX, box.MinZ)] = cells.ToArray();
        }

        return shot;
    }

    [Fact]
    public void AFreshWorld_PinsTheKitGridAndComposition_AndAChangedKit_ReplaysByteForByte()
    {
        for (long seed = 1; seed <= 12; seed++)
        {
            string world = $"kits_{seed}";
            var server = Started(KitContent(houseMax: 2, plot: 10), world, seed, out var repo);
            Dictionary<(int, int), ushort[]> first;
            List<(int Index, string Kit, string Layout, List<string> Composition)> pinned;
            using (repo)
            {
                var recs = server.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed && r.Modules >= 2).ToList();
                if (recs.Count == 0)
                {
                    server.Stop();
                    continue; // only complete templates / no settlements on this world — next seed
                }

                Assert.All(recs, r =>
                {
                    Assert.StartsWith("vk_", r.Kit, StringComparison.Ordinal);
                    Assert.True(SettlementLayoutSpec.TryParse(r.KitLayout, out var spec) && spec.Plot == 10, $"record {r.Index} layout '{r.KitLayout}'");
                    Assert.NotNull(r.Composition);
                });
                pinned = recs.Select(r => (r.Index, r.Kit, r.KitLayout, new List<string>(r.Composition!))).ToList();
                first = Snapshot(server);
                server.Stop();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // The kit changed (a wider stride, more houses): every settlement replays exactly as stamped.
            var server2 = Started(KitContent(houseMax: 4, plot: 14), world, seed, out var repo2);
            using (repo2)
            {
                Assert.Equal(first, Snapshot(server2));
                foreach (var (index, kit, layoutText, list) in pinned)
                {
                    var r = server2.PlacementRecordsForTest.First(x => x.Kind == "settlement" && x.Index == index);
                    Assert.Equal((kit, layoutText), (r.Kit, r.KitLayout));
                    Assert.Equal(list, r.Composition);
                }

                server2.Stop();
            }

            return;
        }

        Assert.Fail("no seed in 1..12 stamped a kit settlement on meadowlands at Frequent");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }
}
