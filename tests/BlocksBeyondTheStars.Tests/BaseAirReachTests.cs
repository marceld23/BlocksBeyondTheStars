// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Player report 2026-10-14 (Lyxette): a big, completely walled cave hall under an airless world got no base air. The
/// sealed-room fill gave up at 16 000 cells (#2539) and at a fixed 48-block box (#2540), pooled nothing between allied
/// bases (#2541), never said which of four reasons applied (#2542) and refilled every 1.5 s at full cost (#2543).
/// Rocky (toxic atmosphere) makes oxygen matter; everything stands mid-air, well below its atmosphere line.
/// </summary>
public sealed class BaseAirReachTests : IDisposable
{
    private const int CoreX = 1, CoreY = 120, CoreZ = 0;

    private readonly string _root;
    private readonly GameContent _content;

    public BaseAirReachTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_baseairreach_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Records every server send so a test can assert which warning was told.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }

        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }

        public List<string> Lines() => Sent.Where(x => x.Msg is ServerMessage).Select(x => ((ServerMessage)x.Msg).Text).ToList();
    }

    private SvGameServer Start(out SqliteWorldRepository repo, string name, IServerTransport? transport = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky", // toxic atmosphere → oxygen drains outside a life-support field
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Adds a player on foot (no starter ship → the aboard default must be dropped explicitly).</summary>
    private static PlayerSession OnFoot(SvGameServer server, string name)
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        return p;
    }

    /// <summary>Founds a base for the player at a core cell.</summary>
    private static int Found(SvGameServer server, PlayerSession p, int x, int y, int z)
    {
        p.State.Position = new Vector3f(x - 1, y, z);
        p.State.Inventory.Add("base_core", 2, 16);
        int before = server.BaseSnapshots.Count;
        server.PlaceBlock(p.State.PlayerId, x, y, z, "base_core");
        Assert.Equal(before + 1, server.BaseSnapshots.Count);
        return server.BaseSnapshots.Last().Id;
    }

    /// <summary>A closed stone shell around the interior [x0..x1]×[y0..y1]×[z0..z1]. With a door, the doorway is a
    /// 1-wide, 3-tall hole in the x0-1 wall at z 0 (floor at <see cref="CoreY"/>) shut with an energy door. With an
    /// <paramref name="owner"/> the shell is that player's build — which is what grows a base's reach (#1862/#2540).</summary>
    private void BuildBox(SvGameServer server, PlayerSession builder, int x0, int x1, int y0, int y1, int z0, int z1,
        string owner = "", bool door = true)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = x0 - 1; x <= x1 + 1; x++)
            for (int y = y0 - 1; y <= y1 + 1; y++)
                for (int z = z0 - 1; z <= z1 + 1; z++)
                {
                    bool interior = x >= x0 && x <= x1 && y >= y0 && y <= y1 && z >= z0 && z <= z1;
                    bool doorway = door && x == x0 - 1 && z == 0 && y >= CoreY && y <= CoreY + 2;
                    if (!interior && !doorway)
                    {
                        server.World.SetBlock(new Vector3i(x, y, z), stone, owner: owner);
                    }
                }

        if (door)
        {
            builder.State.Inventory.Add("door_energy", 2, 16);
            builder.State.Position = new Vector3f(x0 - 2, CoreY, 0.5f);
            server.PlaceBlock(builder.State.PlayerId, x0 - 1, CoreY, 0, "door_energy");
        }
    }

    /// <summary>Ticks with the player pinned at <paramref name="at"/> (half-second steps).</summary>
    private static void TickAt(SvGameServer server, PlayerSession p, Vector3f at, int halfSeconds = 6)
    {
        for (int i = 0; i < halfSeconds; i++)
        {
            p.State.Position = at;
            server.TickForTest(0.5);
        }
    }

    private static readonly Vector3f InTheCoreZone = new(CoreX - 2.5f, CoreY, CoreZ + 0.5f);

    [Fact]
    public void ARoomOverTheOld16kBudget_Breathes()
    {
        var server = Start(out var repo, "airbudget");
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);
            BuildBox(server, p, 6, 45, CoreY, CoreY + 9, -21, 20); // 40×10×42 = 16 800 cells — one over 16 000 used to leak

            var farEnd = new Vector3f(44.5f, CoreY + 0.5f, 0.5f);
            float before = p.State.Oxygen = 50f;
            TickAt(server, p, farEnd);
            Assert.True(p.State.Oxygen > before, $"The closed 16 800-cell room should breathe at its far end (oxygen {p.State.Oxygen}).");
            var (cells, rooms) = server.BaseAirForTest(baseId);
            Assert.True(cells >= 16800, $"cells {cells}");
            Assert.Equal(1, rooms);
        }
    }

    [Fact]
    public void AHallGrownPastTheBudget_LosesItsAir_AndEveryoneInReachIsToldWhy()
    {
        var transport = new RecordingTransport();
        var server = Start(out var repo, "airtoolarge", transport);
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);

            // Interior 41×18×89, a partition at z 42: the main hall (z −44…41, 86 slices) holds 63 468 cells — under the
            // budget — and the annex behind the partition (z 43…44) another 1 476.
            BuildBox(server, p, 6, 46, CoreY, CoreY + 17, -44, 44);
            var stone = _content.GetBlock("stone")!.NumericId;
            for (int x = 6; x <= 46; x++)
                for (int y = CoreY; y <= CoreY + 17; y++)
                    server.World.SetBlock(new Vector3i(x, y, 42), stone);

            TickAt(server, p, InTheCoreZone);
            var (cells, rooms) = server.BaseAirForTest(baseId);
            Assert.True(cells >= 63468, $"the 63 468-cell hall should seal (cells {cells})");
            Assert.Equal(1, rooms);

            // One block out of the partition: hall + annex = 64 945 cells, past the budget. Nobody mined a wall to the
            // outside — the room is simply too large, and that is what the players in reach hear.
            transport.Sent.Clear();
            server.World.SetBlock(new Vector3i(20, CoreY + 5, 42), BlockId.Air);
            TickAt(server, p, InTheCoreZone);
            Assert.Equal((0, 0), server.BaseAirForTest(baseId));
            Assert.Contains("@srv.base.air_too_large:" + WorldConstants.BaseSealedRoomMaxCells, transport.Lines());
            Assert.DoesNotContain("@base_air_lost", transport.Lines());

            // /basewalls in the hall names the cause too.
            Assert.Equal(SvGameServer.BaseAirStatus.TooLarge, server.DiagnoseBaseAirForTest(20, CoreY + 1, 0).Status);
            p.State.Position = new Vector3f(20.5f, CoreY, 0.5f);
            var report = server.BaseWallsReportForTest(p);
            Assert.Contains(report, l => l.Contains("too large", StringComparison.Ordinal) && l.Contains("64000", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ARoomThePlayerBuiltPast48Blocks_Breathes()
    {
        var server = Start(out var repo, "airbuiltreach");
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);
            BuildBox(server, p, 6, 70, CoreY, CoreY + 2, -1, 1, owner: p.State.PlayerId); // a corridor to x 71, 70 out

            var farEnd = new Vector3f(68.5f, CoreY + 0.5f, 0.5f);
            float before = p.State.Oxygen = 50f;
            TickAt(server, p, farEnd);
            Assert.True(p.State.Oxygen > before, $"The corridor the player built should breathe 67 blocks out (oxygen {p.State.Oxygen}).");
            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);
        }
    }

    [Fact]
    public void ANaturalSpacePast48Blocks_StaysWithoutAir_AndSaysTooFar()
    {
        var server = Start(out var repo, "airnaturalreach");
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);
            BuildBox(server, p, 6, 70, CoreY, CoreY + 2, -1, 1); // the same corridor, but no player's build: reach stays 48

            TickAt(server, p, InTheCoreZone);
            Assert.Equal((0, 0), server.BaseAirForTest(baseId));
            var diag = server.DiagnoseBaseAirForTest(45, CoreY + 1, 0); // in reach — the corridor's pocket runs past it
            Assert.Equal(SvGameServer.BaseAirStatus.TooFar, diag.Status);
            Assert.Equal(48, diag.Reach);
        }
    }

    [Fact]
    public void AlliedBases_PoolTheirAir()
    {
        var server = Start(out var repo, "airallies");
        using (repo)
        {
            var ann = OnFoot(server, "Ann");
            var ben = OnFoot(server, "Ben");
            int annBase = Found(server, ann, CoreX, CoreY, CoreZ);
            Found(server, ben, 95, CoreY, 12);
            // Ben's core is a player's block 94 out, so Ann's reach already grows to 100 (and Ben's to 100 the other way,
            // #1862/#2540). A natural corridor from Ann's core zone to x 125 runs past Ann's box (x <= 101) but stays
            // inside Ben's (x -5..195), never touching Ben's cube (z 4..20).
            BuildBox(server, ann, 6, 125, CoreY, CoreY + 2, -1, 1);

            var farEnd = new Vector3f(120.5f, CoreY + 0.5f, 0.5f);
            float before = ann.State.Oxygen = 60f;
            TickAt(server, ann, farEnd);
            Assert.True(ann.State.Oxygen < before, $"Unallied, the corridor is past Ann's reach (oxygen {ann.State.Oxygen}).");

            server.RequestAllianceForTest(ann.State.PlayerId, ben.State.PlayerId);
            server.RespondAllianceForTest(ben.State.PlayerId, ann.State.PlayerId, accept: true);
            Assert.True(server.AreAllied(ann.State.PlayerId, ben.State.PlayerId));

            TickAt(server, ann, InTheCoreZone, halfSeconds: 20); // past the 8 s backstop that picks up the alliance
            before = ann.State.Oxygen = 50f;
            TickAt(server, ann, farEnd);
            Assert.True(ann.State.Oxygen > before, $"Allied, the corridor lies in Ben's box and breathes (oxygen {ann.State.Oxygen}).");
            Assert.True(server.BaseAirForTest(annBase).Rooms >= 1);
        }
    }

    [Fact]
    public void ACoreInTheOpen_StopsItsFillAtTheSky()
    {
        var server = Start(out var repo, "airsky");
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);
            Assert.Equal((0, 0), server.BaseAirForTest(baseId));
            // Every seed around the core stands under the open sky — the old fill ran this pocket to the budget.
            Assert.True(server.BaseAirCellsVisitedForTest < 100, $"visited {server.BaseAirCellsVisitedForTest}");
        }
    }

    [Fact]
    public void AnIdleBase_DoesNotRefill_ButAHoleIsSeenAtOnce()
    {
        var server = Start(out var repo, "airidle");
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);
            BuildBox(server, p, 6, 14, CoreY, CoreY + 2, -2, 2);
            var inside = new Vector3f(13.5f, CoreY + 0.5f, 0.5f);
            TickAt(server, p, inside);
            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);

            int computes = server.BaseAirComputesForTest;
            TickAt(server, p, inside, halfSeconds: 12); // 6 s: four refills under the old blind 1.5 s rhythm
            Assert.Equal(computes, server.BaseAirComputesForTest);

            server.World.SetBlock(new Vector3i(13, CoreY + 3, 0), BlockId.Air); // a hole in the roof
            TickAt(server, p, inside, halfSeconds: 4);
            Assert.True(server.BaseAirComputesForTest > computes);
            Assert.Equal((0, 0), server.BaseAirForTest(baseId));
        }
    }

    [Fact]
    public void WalkingIntoASealedRoomWithoutADoor_SaysItIsNotConnected()
    {
        var transport = new RecordingTransport();
        var server = Start(out var repo, "airdisconnected", transport);
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            Found(server, p, CoreX, CoreY, CoreZ);
            BuildBox(server, p, 20, 26, CoreY, CoreY + 2, -2, 2, door: false); // closed all round, no energy door
            TickAt(server, p, InTheCoreZone, halfSeconds: 2);

            transport.Sent.Clear();
            TickAt(server, p, new Vector3f(23.5f, CoreY + 0.5f, 0.5f), halfSeconds: 4);
            Assert.Contains("@srv.base.air_disconnected", transport.Lines());
            Assert.Equal(SvGameServer.BaseAirStatus.Disconnected, server.DiagnoseBaseAirForTest(23, CoreY, 0).Status);
        }
    }

    private static JsonElement Locale(string code)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }).RootElement;

    [Fact]
    public void RecipeTitlesAndDescriptions_ResolveInEnglishAndGerman()
    {
        // #2544: a recipe may carry its own card title / description — the hand-built boat reads "Wooden boat".
        var keys = _content.Recipes.Values
            .SelectMany(r => new[] { r.NameKey, r.DescriptionKey })
            .Where(k => !string.IsNullOrEmpty(k))
            .ToList();
        Assert.Contains("recipe.boat_wood.name", keys);
        foreach (string code in new[] { "en", "de" })
        {
            var locale = Locale(code);
            foreach (var key in keys)
            {
                Assert.True(locale.TryGetProperty(key!, out _), $"{code}.json lacks {key}");
            }
        }

        Assert.Equal("Wooden boat", Locale("en").GetProperty("recipe.boat_wood.name").GetString());
        Assert.Equal("Holzboot", Locale("de").GetProperty("recipe.boat_wood.name").GetString());
        Assert.Null(_content.Recipes["boat"].NameKey); // the workshop recipe stays the item's own "Boat"
        Assert.Equal("boat", _content.Recipes["boat_wood"].Outputs.Single().Item); // and both make the same boat
    }

    [Fact]
    public void BaseAirTexts_QuoteTheRealBudget()
    {
        foreach (string code in new[] { "en", "de" })
        {
            var locale = Locale(code);
            Assert.Contains("{name}", locale.GetProperty("srv.base.air_too_large").GetString());
            Assert.Contains("{budget}", locale.GetProperty("ui.base.air_none").GetString());
            // The VEGA hint spells the number out ("64,000" / "64.000") — it must be the constant.
            string digits = new(locale.GetProperty("vega.hint.base_airless").GetString()!.Where(char.IsDigit).ToArray());
            Assert.Contains(WorldConstants.BaseSealedRoomMaxCells.ToString(System.Globalization.CultureInfo.InvariantCulture), digits);
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
