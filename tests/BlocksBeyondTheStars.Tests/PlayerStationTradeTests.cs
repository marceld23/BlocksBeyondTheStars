// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Screelit, 2026-10-15, on his own player-built station: "I have a doctor and a vendor who don't sell what they should!"
/// The doctor's goods always traded on the server; the client's screen re-picked "the nearest vendor within 3.6" on every
/// rebuild while the doctor strolled about his post, and listed only the two themeless swaps. Now (#2626, #2627, #2630):
/// <list type="bullet">
/// <item>the vendor screen lists the vendor's own goods first (<see cref="MarketStock.ListFor"/>), and the server accepts
/// every row of it — from anywhere within <see cref="MarketStock.VendorReach"/> of that vendor, which is how long the
/// client keeps the screen bound to him (Client.Core <c>VendorReach</c>, tested in the client suite);</item>
/// <item>a trader docked beside the station's shop keeper brings goods of its own and sells them even with the keeper a
/// step closer (any vendor in reach that sells a deal counts, not just the nearest);</item>
/// <item>a keeper at work holds the counter while a customer stands near, and strolls as before when nobody does.</item>
/// </list>
/// The station is built the way a player builds one: a sealed hall around the core by EVA, the posts on its floor.
/// </summary>
public sealed class PlayerStationTradeTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_stationtrade_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    private const string Pilot = "Screelit";

    /// <summary>The hall's air in world coordinates (cells −1…17 × −1…13, see <see cref="BuildHall"/>), with a margin.</summary>
    private const float HallMinX = 9.3f, HallMaxX = 27.7f, HallMinZ = 9.3f, HallMaxZ = 23.7f;

    /// <summary>The feet height on the hall's floor.</summary>
    private const float FeetY = 65.03f;

    private sealed record Station(SvGameServer Server, NpcLifeWorld.RecordingTransport Transport, PlayerSession Player, string Id);

    private Station BoardedStation(string name, params (int X, int Z, string Item)[] posts)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var transport = new NpcLifeWorld.RecordingTransport();
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, Content, transport, repo);
        server.Start();
        var player = server.AddLocalPlayer(Pilot);
        string id = BuildHall(server, player, posts);
        Board(server, id);
        TickDay(server, player, 4, 0.25);
        return new Station(server, transport, player, id);
    }

    private static void Edit(SvGameServer server, string id, int x, int y, int z, string item)
        => server.HandleStructureEditForTest(Pilot, new StructureEditIntent { StructureId = id, X = x, Y = y, Z = z, Mine = false, ItemKey = item });

    /// <summary>A sealed hall (cells x −2…18, y −2…2, z −2…14) around the core at (0,0,0), a slide door in the +X wall,
    /// the posts on the floor (y −1). World = cell + (10, 66, 10).</summary>
    private static string BuildHall(SvGameServer server, PlayerSession player, IEnumerable<(int X, int Z, string Item)> posts)
    {
        server.EnterSpace(Pilot);
        player.State.InEva = true;
        player.State.InstantBuild = true;
        server.DeployStationCoreForTest(Pilot);
        string id = server.OwnedStationIdForTest(Pilot)!;
        for (int x = -2; x <= 18; x++)
            for (int y = -2; y <= 2; y++)
                for (int z = -2; z <= 14; z++)
                {
                    bool shell = x == -2 || x == 18 || Math.Abs(y) == 2 || z == -2 || z == 14;
                    bool doorway = x == 18 && y == -1 && z == 6;
                    if (shell && !doorway)
                    {
                        Edit(server, id, x, y, z, "iron_wall");
                    }
                }

        Edit(server, id, 18, -1, 6, "door_slide");
        foreach (var (x, z, item) in posts)
        {
            Edit(server, id, x, -1, z, item);
        }

        Assert.True(server.StationIsBoardableForTest(id));
        return id;
    }

    private static void Board(SvGameServer server, string stationId)
    {
        if (!server.InSpace(Pilot))
        {
            server.EnterSpace(Pilot);
        }

        var contact = server.SpaceEntitiesFor(Pilot).First(e => e.Id == stationId);
        server.ShipMove(Pilot, contact.Position.X, contact.Position.Y, contact.Position.Z - 6f);
        server.BoardStation(Pilot, stationId);
        Assert.True(server.InStation(Pilot));
    }

    /// <summary>Ticks at station noon-ish (everyone at work), the player pinned at <paramref name="at"/> when given.</summary>
    private static void TickDay(SvGameServer server, PlayerSession player, int ticks, double dt, Vector3f? at = null, Action? every = null)
    {
        for (int i = 0; i < ticks; i++)
        {
            server.SetLocalDayFractionForTest(0.45, 0f);
            if (at is { } p)
            {
                player.State.Position = p;
            }

            server.TickForTest(dt);
            every?.Invoke();
        }
    }

    private static NetNpc[] LastNpcList(Station s) => s.Transport.Sent.Select(x => x.Msg).OfType<NpcList>().LastOrDefault()?.Npcs ?? Array.Empty<NetNpc>();

    private static string Craft(Station s, string recipe)
    {
        int mark = s.Transport.Sent.Count;
        s.Server.Craft(Pilot, recipe);
        var res = s.Transport.Sent.Skip(mark).Select(x => x.Msg).OfType<CraftResult>().LastOrDefault();
        return res is null ? "(no result)" : res.Success ? "ok" : res.Reason ?? string.Empty;
    }

    private static void Stock(PlayerSession p)
    {
        foreach (var (item, n) in new[]
                 {
                     ("iron_ore", 120), ("titanium_ore", 20), ("copper_ore", 30), ("silicate", 30), ("carbon", 30), ("crystal", 20),
                     ("gold_ingot", 20), ("diamond", 20), ("wood_log", 20), ("iron_plate", 20), ("plant_fiber", 20), ("creature_meat", 20),
                 })
        {
            p.State.Inventory.Add(item, n, 999);
        }
    }

    private static bool Affordable(PlayerSession p, RecipeDefinition r) => r.Inputs.All(i => p.State.Inventory.CountOf(i.Item) >= i.Count);

    private static float FlatDist(Vector3f a, Vector3f b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    private static bool InHall(Vector3f p) => p.X is >= HallMinX and <= HallMaxX && p.Z is >= HallMinZ and <= HallMaxZ;

    /// <summary>A spot in the hall <paramref name="distance"/> from <paramref name="from"/> and farther than
    /// <paramref name="awayFrom"/> from every point given.</summary>
    private static Vector3f SpotAt(Vector3f from, float distance, float awayFrom, IEnumerable<Vector3f> points)
    {
        var avoid = points.ToList();
        for (int dir = 0; dir < 32; dir++)
        {
            double a = dir * Math.PI / 16;
            var p = new Vector3f(from.X + distance * (float)Math.Cos(a), FeetY, from.Z + distance * (float)Math.Sin(a));
            if (InHall(p) && avoid.All(q => FlatDist(p, q) > awayFrom))
            {
                return p;
            }
        }

        throw new InvalidOperationException($"no spot {distance} from {from} clear of the posts");
    }

    [Fact]
    public void TheStationDoctor_ListsHisOwnGoodsFirst_AndTheServerTakesEveryRow_WhileHeIsInReach()
    {
        var s = BoardedStation("trade_doctor", (1, 4, "clinic_post"), (7, -1, "station_vendor"));
        var doctor = LastNpcList(s).Single(n => n.NameKey == "npc.role.doctor");
        Assert.Equal("vendor", doctor.Role);
        Assert.Equal("medics", doctor.Theme);

        // The screen's rows (the client lists MarketStock.ListFor of the vendor it is bound to): his medicine first, then
        // the two swaps every vendor has.
        long day = s.Server.MarketDayForTest;
        var rows = MarketStock.ListFor(Content.Recipes.Values, doctor.Theme, day);
        int common = rows.FindIndex(r => r.MarketTheme.Length == 0);
        Assert.True(common >= 3, $"the doctor's own goods come first ({string.Join(", ", rows.Select(r => r.Key))})");
        Assert.All(rows.Take(common), r => Assert.Equal("medics", r.MarketTheme));
        Assert.All(rows.Skip(common), r => Assert.Equal(string.Empty, r.MarketTheme));
        Assert.Equal(new[] { "market_iron_to_titanium", "market_titanium_to_iron" }, rows.Skip(common).Select(r => r.Key));

        // Beside him the server takes every row of it.
        Stock(s.Player);
        var at = s.Server.NpcStateForTest(doctor.Id)!.Value.Pos;
        s.Player.State.Position = new Vector3f(at.X + 1.2f, FeetY, at.Z);
        foreach (var r in rows)
        {
            Assert.True(Affordable(s.Player, r), $"{r.Key}: the test stock should cover it");
            Assert.Equal("ok", Craft(s, r.Key));
        }

        // A medics offer off today's rotation is not on the list — and the server says "not today", never "wrong vendor".
        var offToday = Content.Recipes.Values.First(r => r.MarketTheme == "medics" && !r.OfferedOnDay(day));
        Assert.DoesNotContain(offToday, rows);
        Assert.Equal("@srv.craft.not_today", Craft(s, offToday.Key));

        // The bound screen keeps listing his goods while he stands within the vendor reach — the server agrees even with
        // every post more than the 4-block trade reach away.
        var markers = s.Server.SpaceStationMarkers.Where(m => NpcProfessions.IsTradeMarker(m.Type)).Select(m => m.Pos).ToList();
        var docNow = s.Server.NpcStateForTest(doctor.Id)!.Value.Pos;
        var stillServed = SpotAt(docNow, MarketStock.VendorReach - 0.6f, MarketStock.TradeReach + 0.3f, markers);
        s.Player.State.Position = stillServed;
        Assert.Equal("ok", Craft(s, "market_doctor_medpack"));
        Assert.Equal("ok", Craft(s, "market_iron_to_titanium"));

        // Out of his reach (and of every post and the shop's keeper): the screen says "step closer", the server refuses.
        var others = LastNpcList(s).Where(n => n.Role == "vendor").Select(n => s.Server.NpcStateForTest(n.Id)!.Value.Pos);
        var tooFar = SpotAt(docNow, MarketStock.VendorReach + 1.5f, MarketStock.VendorReach + 0.3f, markers.Concat(others));
        s.Player.State.Position = tooFar;
        Assert.NotEqual("ok", Craft(s, "market_doctor_medpack"));
        Assert.NotEqual("ok", Craft(s, "market_iron_to_titanium"));
    }

    [Fact]
    public void ADockedTrader_BringsGoodsOfItsOwn_AndSellsThem_WithTheShopKeeperAStepCloser()
    {
        var s = BoardedStation("trade_docked", (7, -1, "station_vendor"));
        s.Server.DockTraderForTest(s.Id);
        string theme = s.Server.VisitingTraderThemeForTest(s.Id)!;
        Assert.NotEqual("traders", theme); // the shop keeps "traders" — the visitor brings something else
        Assert.Contains(theme, SvGameServer.VisitingTraderThemes);

        s.Server.LeaveStation(Pilot);
        Board(s.Server, s.Id);
        TickDay(s.Server, s.Player, 4, 0.25);
        var npcs = LastNpcList(s);
        var visitor = npcs.Single(n => n.Name == "Docked Trader");
        var keeper = npcs.Single(n => n.Role == "vendor" && n.Theme == "traders");
        Assert.Equal(theme, visitor.Theme);
        Assert.Equal("vendor", visitor.Role);

        // The player picked the visitor (within the trade reach) but stands nearer the shop's keeper.
        Stock(s.Player);
        var v = s.Server.NpcStateForTest(visitor.Id)!.Value.Pos;
        var k = s.Server.NpcStateForTest(keeper.Id)!.Value.Pos;
        var between = new Vector3f(v.X + (k.X - v.X) * 0.65f, FeetY, v.Z + (k.Z - v.Z) * 0.65f);
        s.Player.State.Position = between;
        Assert.True(FlatDist(between, v) <= MarketStock.TradeReach, "the visitor is in the trade reach");
        Assert.True(FlatDist(between, k) < FlatDist(between, v), "the keeper stands a step closer");

        var own = MarketStock.ListFor(Content.Recipes.Values, theme, s.Server.MarketDayForTest)
            .First(r => r.MarketTheme == theme && Affordable(s.Player, r));
        Assert.Equal("ok", Craft(s, own.Key));
        Assert.Equal("ok", Craft(s, "market_sell_gold")); // and the keeper's own goods, both in reach
    }

    [Fact]
    public void AKeeperAtWork_HoldsTheCounter_WhileACustomerIsNear_AndStrollsWhenNobodyIs()
    {
        var s = BoardedStation("trade_hold", (1, 4, "clinic_post"), (7, -1, "station_vendor"));
        int doctor = LastNpcList(s).Single(n => n.NameKey == "npc.role.doctor").Id;
        var far = new Vector3f(26.0f, FeetY, 22.5f); // the hall's far corner, well out of the keeper's way
        TickDay(s.Server, s.Player, 40, 0.5, far); // to work
        var work = s.Server.NpcStateForTest(doctor)!.Value.Work;

        // A customer three blocks from the work spot: the doctor walks back to it and stays there.
        var customer = new Vector3f(work.X + 0.6f, FeetY, work.Z + 2.9f);
        Assert.True(InHall(customer));
        TickDay(s.Server, s.Player, 10, 0.5, customer); // back to the counter
        float held = 0f;
        TickDay(s.Server, s.Player, 240, 0.5, customer, () => held = Math.Max(held, FlatDist(s.Server.NpcStateForTest(doctor)!.Value.Pos, work)));
        Assert.True(held <= 0.5f, $"the keeper strayed {held:0.00} from the counter with a customer beside it");

        // Nobody near: he strolls about his post as before.
        float strolled = 0f;
        TickDay(s.Server, s.Player, 240, 0.5, far, () => strolled = Math.Max(strolled, FlatDist(s.Server.NpcStateForTest(doctor)!.Value.Pos, work)));
        Assert.True(strolled > 0.5f, $"without a customer the keeper should stroll (max {strolled:0.00})");
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }
}
