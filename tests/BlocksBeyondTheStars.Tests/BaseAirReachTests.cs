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
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Which rooms a base breathes into. Player report 2026-10-14 (Lyxette): a big walled cave hall got no air — the fill gave
/// up at 16 000 cells (#2539) and at a fixed 48-block box (#2540), pooled nothing between allies (#2541), said nothing
/// (#2542) and refilled at full cost (#2543). Player report 2026-10-15 (Screelit, #2649): an upper floor up the stairs
/// behind an ordinary door had no air at all. Since #2651 the rule is the stations' one: every door holds air, every
/// enclosed room of a base breathes — no size limit, no reach limit, no "connected" rule; rooms are found from the core's
/// neighbours, from both sides of every door in reach and through the doors of every sealed room found. Rocky (toxic
/// atmosphere) makes oxygen matter; everything stands mid-air, well below its atmosphere line.
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
    /// 1-wide, 3-tall hole in the x0-1 wall at z 0 (floor at <see cref="CoreY"/>) filled with <paramref name="doorKey"/>.
    /// With an <paramref name="owner"/> the shell is that player's build — which is what grows a base's reach.</summary>
    private void BuildBox(SvGameServer server, PlayerSession builder, int x0, int x1, int y0, int y1, int z0, int z1,
        string owner = "", bool door = true, string doorKey = "door_energy")
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
            PlaceDoor(server, builder, x0 - 1, 0, doorKey);
        }
    }

    /// <summary>Places a door into the 1-wide doorway at (x, CoreY, z), standing just west of it.</summary>
    private static void PlaceDoor(SvGameServer server, PlayerSession builder, int x, int z, string doorKey, int y = CoreY)
    {
        builder.State.Inventory.Add(doorKey, 2, 16);
        builder.State.Position = new Vector3f(x - 1, y, z + 0.5f);
        int before = server.DoorSnapshots.Count;
        server.PlaceBlock(builder.State.PlayerId, x, y, z, doorKey);
        Assert.Equal(before + 1, server.DoorSnapshots.Count);
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

    /// <summary>Whether the player breathes at <paramref name="at"/> after a few ticks there — oxygen goes up.</summary>
    private static bool BreathesAt(SvGameServer server, PlayerSession p, Vector3f at)
    {
        float before = p.State.Oxygen = 50f;
        TickAt(server, p, at);
        return p.State.Oxygen > before;
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
            Assert.True(BreathesAt(server, p, farEnd), $"The closed 16 800-cell room should breathe at its far end (oxygen {p.State.Oxygen}).");
            var (cells, rooms) = server.BaseAirForTest(baseId);
            Assert.True(cells >= 16800, $"cells {cells}");
            Assert.Equal(1, rooms);
        }
    }

    [Fact]
    public void AHallGrownPastTheOld64kBudget_KeepsBreathing_AndNobodyHearsTooLarge()
    {
        // #2539's "too large" is gone (#2649): the same hall that used to lose its air at 64 000 cells keeps it.
        var transport = new RecordingTransport();
        var server = Start(out var repo, "airtoolarge", transport);
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);

            // Interior 41×18×89, a partition at z 42: the main hall (z −44…41) holds 63 468 cells, the annex behind the
            // partition (z 43…44) another 1 476 — no door leads into the annex, so it is no base room yet.
            BuildBox(server, p, 6, 46, CoreY, CoreY + 17, -44, 44);
            var stone = _content.GetBlock("stone")!.NumericId;
            for (int x = 6; x <= 46; x++)
                for (int y = CoreY; y <= CoreY + 17; y++)
                    server.World.SetBlock(new Vector3i(x, y, 42), stone);

            TickAt(server, p, InTheCoreZone);
            var (cells, rooms) = server.BaseAirForTest(baseId);
            Assert.True(cells >= 63468, $"the 63 468-cell hall should seal (cells {cells})");
            Assert.Equal(1, rooms);

            // One block out of the partition: hall + annex = 64 945 cells, past the old budget — still one closed room.
            transport.Sent.Clear();
            server.World.SetBlock(new Vector3i(20, CoreY + 5, 42), BlockId.Air);
            TickAt(server, p, InTheCoreZone);
            (cells, rooms) = server.BaseAirForTest(baseId);
            Assert.True(cells >= 64945, $"hall and annex breathe as one room (cells {cells})");
            Assert.Equal(1, rooms);
            Assert.DoesNotContain(transport.Lines(), l => l.StartsWith("@srv.base.air", StringComparison.Ordinal) || l == "@base_air_lost");
            Assert.True(BreathesAt(server, p, new Vector3f(20.5f, CoreY, 43.5f)), "the annex breathes now");
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

            Assert.True(BreathesAt(server, p, new Vector3f(68.5f, CoreY + 0.5f, 0.5f)),
                $"The corridor the player built should breathe 67 blocks out (oxygen {p.State.Oxygen}).");
            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);
        }
    }

    [Fact]
    public void ANaturalCorridorPast48Blocks_Breathes_TooFarIsGone()
    {
        // #2540's "too far" is gone (#2651): a closed corridor behind a door in reach breathes however far it runs.
        var server = Start(out var repo, "airnaturalreach");
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);
            BuildBox(server, p, 6, 70, CoreY, CoreY + 2, -1, 1); // nobody's build: the reach stays 48, the corridor runs to 71

            Assert.True(BreathesAt(server, p, new Vector3f(68.5f, CoreY + 0.5f, 0.5f)), $"oxygen {p.State.Oxygen}");
            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);
            Assert.Equal(SvGameServer.BaseAirStatus.Supplied, server.DiagnoseBaseAirForTest(68, CoreY, 0).Status);
        }
    }

    [Fact]
    public void AnUpperFloorUpTheStairsBehindAnOrdinaryDoor_Breathes()
    {
        // Screelit's report (#2649): the ground floor breathed, the room one floor up — stairs, then a door — did not.
        var server = Start(out var repo, "airupstairs");
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);
            var stone = _content.GetBlock("stone")!.NumericId;

            // A two-storey house east of the core's zone: ground floor x 12..20, y CoreY..+2; upper floor y +4..+6 over the
            // slab y +3; the upper floor is split at x 17 into the landing (x 18..20, over the stair hole) and the room
            // (x 12..16) behind a hinge door. The house door (slide) faces the core.
            for (int x = 11; x <= 21; x++)
                for (int y = CoreY - 1; y <= CoreY + 7; y++)
                    for (int z = -3; z <= 3; z++)
                    {
                        bool shell = x == 11 || x == 21 || y == CoreY - 1 || y == CoreY + 3 || y == CoreY + 7 || z == -3 || z == 3;
                        bool partition = x == 17 && y > CoreY + 3;
                        server.World.SetBlock(new Vector3i(x, y, z), shell || partition ? stone : BlockId.Air);
                    }

            for (int y = CoreY; y <= CoreY + 2; y++)
            {
                server.World.SetBlock(new Vector3i(11, y, 0), BlockId.Air); // the house doorway
            }

            server.World.SetBlock(new Vector3i(19, CoreY + 3, 0), BlockId.Air); // the stair hole in the slab
            server.World.SetBlock(new Vector3i(19, CoreY, 1), _content.GetBlock("stairs")!.NumericId);
            for (int y = CoreY + 4; y <= CoreY + 6; y++)
            {
                server.World.SetBlock(new Vector3i(17, y, 0), BlockId.Air); // the upper doorway in the partition
            }

            PlaceDoor(server, p, 11, 0, "door_slide");
            p.State.Position = new Vector3f(18.5f, CoreY + 4, 0.5f);
            p.State.Inventory.Add("door_hinge", 2, 16);
            server.PlaceBlock(p.State.PlayerId, 17, CoreY + 4, 0, "door_hinge");

            var upstairs = new Vector3f(14.5f, CoreY + 4, 0.5f);
            Assert.True(BreathesAt(server, p, upstairs), $"The upper room behind the hinge door should breathe (oxygen {p.State.Oxygen}).");
            Assert.Equal(2, server.BaseAirForTest(baseId).Rooms); // ground floor + landing, and the upper room
            Assert.True(BreathesAt(server, p, new Vector3f(15.5f, CoreY, 0.5f)), "and the ground floor too");
        }
    }

    [Fact]
    public void ARoomEnteredFromOutsideThroughItsOwnDoor_Breathes()
    {
        // A shed east of the core whose only door faces away from the base, out into the open: the door is in reach, so
        // both its sides are seeds — the outside leaks, the shed seals.
        var server = Start(out var repo, "airowndoor");
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);
            var stone = _content.GetBlock("stone")!.NumericId;
            for (int x = 24; x <= 30; x++)
                for (int y = CoreY - 1; y <= CoreY + 3; y++)
                    for (int z = -3; z <= 3; z++)
                    {
                        bool shell = x == 24 || x == 30 || y == CoreY - 1 || y == CoreY + 3 || z == -3 || z == 3;
                        bool doorway = x == 30 && z == 0 && y >= CoreY && y <= CoreY + 2;
                        server.World.SetBlock(new Vector3i(x, y, z), shell && !doorway ? stone : BlockId.Air);
                    }

            p.State.Inventory.Add("door_wood", 2, 16);
            p.State.Position = new Vector3f(31.5f, CoreY, 0.5f);
            server.PlaceBlock(p.State.PlayerId, 30, CoreY, 0, "door_wood");

            Assert.True(BreathesAt(server, p, new Vector3f(27.5f, CoreY, 0.5f)), $"oxygen {p.State.Oxygen}");
            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);
        }
    }

    [Fact]
    public void AClosedRoomWithNoBaseCore_DoesNotBreathe()
    {
        var server = Start(out var repo, "airnocore");
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            BuildBox(server, p, 6, 14, CoreY, CoreY + 2, -2, 2, doorKey: "door_slide"); // closed, with a door — but no core
            Assert.False(BreathesAt(server, p, new Vector3f(10.5f, CoreY, 0.5f)), "rooms of no base never breathe");
            Assert.Equal(SvGameServer.BaseAirStatus.NoBase, server.DiagnoseBaseAirForTest(10, CoreY, 0).Status);
        }
    }

    [Fact]
    public void ADoorChainFartherThan192Blocks_Breathes()
    {
        // A corridor from the core out to x 240, split into rooms by hinge doors at x 60, 120 and 180: only the entrance
        // door is in the base's reach; every room after it is found through the doors of the sealed room before.
        var server = Start(out var repo, "airchain");
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);
            BuildBox(server, p, 6, 240, CoreY, CoreY + 2, 0, 0, doorKey: "door_slide");
            var stone = _content.GetBlock("stone")!.NumericId;
            foreach (int wall in new[] { 60, 120, 180 })
            {
                server.World.SetBlock(new Vector3i(wall, CoreY + 3, 0), stone); // the corridor's roof is there already
                for (int y = CoreY; y <= CoreY + 2; y++)
                {
                    server.World.SetBlock(new Vector3i(wall, y, 0), BlockId.Air);
                }

                PlaceDoor(server, p, wall, 0, "door_hinge");
            }

            var farRoom = new Vector3f(230.5f, CoreY, 0.5f);
            Assert.True(BreathesAt(server, p, farRoom), $"The room 229 blocks from the core should breathe (oxygen {p.State.Oxygen}).");
            Assert.Equal(4, server.BaseAirForTest(baseId).Rooms);
        }
    }

    [Fact]
    public void AShaftUpATowerTallerThanTheReachCap_Breathes()
    {
        // The sky line follows the highest build (#2651): a closed room whose shaft climbs 210 blocks, past the 192-block
        // reach cap, is not "open to the sky" at the cap.
        var server = Start(out var repo, "airtower");
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            int baseId = Found(server, p, CoreX, CoreY, CoreZ);
            string owner = p.State.PlayerId;
            var stone = _content.GetBlock("stone")!.NumericId;
            BuildBox(server, p, 12, 16, CoreY, CoreY + 2, -2, 2, owner: owner);
            int top = CoreY + 210;
            for (int y = CoreY + 3; y <= top; y++)
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        bool shaft = dx == 0 && dz == 0 && y < top;
                        server.World.SetBlock(new Vector3i(14 + dx, y, dz), shaft ? BlockId.Air : stone, owner: owner);
                    }
            }

            Assert.Equal(1, server.BaseAirForTest(baseId).Rooms);
            Assert.Equal(SvGameServer.BaseAirStatus.Supplied, server.DiagnoseBaseAirForTest(14, top - 5, 0).Status);
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
            int benBase = Found(server, ben, 250, CoreY, 0); // past Ann's reach cap: neither box ever holds the other's builds
            // Ben's workshop beside his core, its door in Ben's reach only.
            BuildBox(server, ben, 260, 266, CoreY, CoreY + 2, -2, 2, doorKey: "door_slide");

            TickAt(server, ben, new Vector3f(263.5f, CoreY, 0.5f));
            Assert.Equal(1, server.BaseAirForTest(benBase).Rooms);
            Assert.Equal(0, server.BaseAirForTest(annBase).Rooms); // unallied: Ann's base knows nothing of Ben's rooms
            Assert.True(BreathesAt(server, ann, new Vector3f(263.5f, CoreY, 0.5f)), "visitors breathe in anyone's base");

            server.RequestAllianceForTest(ann.State.PlayerId, ben.State.PlayerId);
            server.RespondAllianceForTest(ben.State.PlayerId, ann.State.PlayerId, accept: true);
            Assert.True(server.AreAllied(ann.State.PlayerId, ben.State.PlayerId));

            TickAt(server, ann, InTheCoreZone, halfSeconds: 20); // past the 8 s backstop that picks up the alliance
            Assert.Equal(1, server.BaseAirForTest(annBase).Rooms); // allied: one pool — the core's readout counts it
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
            // Every seed around the core stands in a column open to the sky: one span each, nothing scanned to a budget.
            Assert.True(server.BaseAirSpansVisitedForTest <= 8, $"spans {server.BaseAirSpansVisitedForTest}");
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
    public void ASealedRoomWithoutADoor_StaysWithoutAir_AndBasewallsSaysWhy()
    {
        // No door, not touching the core: no seed reaches it, so it is no base room. The old "not connected" warning is
        // gone (#2654); /basewalls says what is missing.
        var transport = new RecordingTransport();
        var server = Start(out var repo, "airdoorless", transport);
        using (repo)
        {
            var p = OnFoot(server, "Builder");
            Found(server, p, CoreX, CoreY, CoreZ);
            BuildBox(server, p, 20, 26, CoreY, CoreY + 2, -2, 2, door: false);
            TickAt(server, p, InTheCoreZone, halfSeconds: 2);

            transport.Sent.Clear();
            var inside = new Vector3f(23.5f, CoreY + 0.5f, 0.5f);
            Assert.False(BreathesAt(server, p, inside));
            Assert.DoesNotContain(transport.Lines(), l => l.StartsWith("@srv.base.air", StringComparison.Ordinal));
            Assert.Equal(SvGameServer.BaseAirStatus.Unknown, server.DiagnoseBaseAirForTest(23, CoreY, 0).Status);

            p.State.Position = inside;
            var report = server.BaseWallsReportForTest(p);
            Assert.Contains(report, l => l.Contains("no door leads into it from the base", StringComparison.Ordinal));
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
    public void BaseAirTexts_DescribeTheNewRule_AndTheOldLimitsAreGone()
    {
        string[] gone =
        {
            "srv.base.air_too_large", "srv.base.air_too_far", "srv.base.air_disconnected", "ui.station.air_too_large",
            "srv.basewalls.air_too_large", "srv.basewalls.air_too_far", "srv.basewalls.air_disconnected",
        };
        foreach (string code in new[] { "en", "de" })
        {
            var locale = Locale(code);
            string Text(string key) => locale.GetProperty(key).GetString()!;
            foreach (string key in gone)
            {
                Assert.False(locale.TryGetProperty(key, out _), $"{code}.json still has {key}");
            }

            // No budget anywhere: the readout, VEGA's hint and the /basewalls rules carry no size limit.
            Assert.DoesNotContain("{budget}", Text("ui.base.air_none"), StringComparison.Ordinal);
            Assert.DoesNotContain("{budget}", Text("srv.basewalls.air_rules"), StringComparison.Ordinal);
            Assert.DoesNotContain("64", Text("vega.hint.base_airless"), StringComparison.Ordinal);
            Assert.Contains("{reach}", Text("srv.basewalls.air_rules"), StringComparison.Ordinal);

            // The energy door is a bulkhead you walk through — no longer "the only door that holds air".
            string bulkhead = code == "de" ? "Schott" : "bulkhead";
            Assert.Contains(bulkhead, Text("blueprint.door_energy.desc"), StringComparison.Ordinal);
            Assert.Contains(bulkhead, Text("srv.basewalls.air_rules"), StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(Text("srv.basewalls.air_unknown")));
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
