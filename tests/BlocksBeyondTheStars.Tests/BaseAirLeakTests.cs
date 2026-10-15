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
/// Player report 2026-10-15 (Lyxette, #2614/#2615): a cave hall behind an energy door in his base stayed without air —
/// its ceiling was sand, and sand never seals (#794) — and the game never said so: walking into a leaking room was
/// silent, and <c>/basewalls</c> said "leaky" without saying where, right under a wall-ring verdict ("walled in") that
/// read as its contradiction. Now the walk-in names the leak when the spot is clearly a room, and so does
/// <c>/basewalls</c>. Rocky (toxic atmosphere) makes oxygen matter; everything stands mid-air, well below its
/// atmosphere line, so the open sky is straight above every roof.
/// </summary>
public sealed class BaseAirLeakTests : IDisposable
{
    private const int CoreX = 1, CoreY = 120, CoreZ = 0;
    private const int Roof = CoreY + 10; // hall B's roof plane

    private static readonly Vector3f InTheCoreZone = new(CoreX - 2.5f, CoreY, CoreZ + 0.5f);
    private static readonly Vector3f InRoomA = new(10.5f, CoreY, 0.5f);
    private static readonly Vector3f InHall = new(30.5f, CoreY, 0.5f);
    private static readonly Vector3f InHallByTheDoor = new(20.5f, CoreY, 0.5f);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_baseairleak_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

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

        /// <summary>The walk-in leak warnings told (English).</summary>
        public List<string> Leaks() => Lines().Where(l => l.Contains("this room leaks", StringComparison.Ordinal)).ToList();
    }

    private SvGameServer Start(string name, RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var server = new SvGameServer(new ServerConfig
        {
            WorldName = name,
            Seed = 7,
            StartPlanet = "rocky", // toxic atmosphere → oxygen drains outside a life-support field
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
        }, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot who founded a base at the core cell.</summary>
    private static (PlayerSession Player, int BaseId) Found(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Lyx");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(CoreX - 1, CoreY, CoreZ);
        p.State.Inventory.Add("base_core", 2, 16);
        server.PlaceBlock(p.State.PlayerId, CoreX, CoreY, CoreZ, "base_core");
        return (p, server.BaseSnapshots.Last().Id);
    }

    /// <summary>A stone shell around the interior [x0..x1]×[y0..y1]×[z0..z1], built by <paramref name="owner"/>.</summary>
    private void Shell(SvGameServer server, int x0, int x1, int y0, int y1, int z0, int z1, string owner)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = x0 - 1; x <= x1 + 1; x++)
            for (int y = y0 - 1; y <= y1 + 1; y++)
                for (int z = z0 - 1; z <= z1 + 1; z++)
                {
                    bool interior = x >= x0 && x <= x1 && y >= y0 && y <= y1 && z >= z0 && z <= z1;
                    server.World.SetBlock(new Vector3i(x, y, z), interior ? BlockId.Air : stone, owner: owner);
                }
    }

    /// <summary>An energy door in the wall at x, z (floor at <see cref="CoreY"/>), placed from <paramref name="standAt"/>.</summary>
    private static void Door(SvGameServer server, PlayerSession p, int x, int z, Vector3f standAt)
    {
        for (int y = CoreY; y <= CoreY + 2; y++)
        {
            server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
        }

        p.State.Inventory.Add("door_energy", 2, 16);
        p.State.Position = standAt;
        server.PlaceBlock(p.State.PlayerId, x, CoreY, z, "door_energy");
    }

    /// <summary>
    /// Lyxette's layout: the core, room A beside it (x 6..14, touching the core's cube) behind an energy door, and hall
    /// B (x 16..40, y CoreY..CoreY+9, z −10..10) behind a second energy door in the shared wall x 15. The hall's roof
    /// plane (y <see cref="Roof"/>) is <paramref name="roof"/> with the open sky above; <paramref name="underRoof"/>
    /// (optional) is a layer right under it.
    /// </summary>
    private (SvGameServer Server, PlayerSession Player, int BaseId) Hall(string name, RecordingTransport transport,
        out SqliteWorldRepository repo, string roof, string? underRoof = null)
    {
        var server = Start(name, transport, out repo);
        var (p, baseId) = Found(server);
        string owner = p.State.PlayerId;
        Shell(server, 6, 14, CoreY, CoreY + 2, -2, 2, owner);
        Door(server, p, 5, 0, new Vector3f(4.5f, CoreY, 0.5f));
        Shell(server, 16, 40, CoreY, Roof - 1, -10, 10, owner);
        Door(server, p, 15, 0, new Vector3f(13.5f, CoreY, 0.5f));
        var roofId = _content.GetBlock(roof)!.NumericId;
        for (int x = 16; x <= 40; x++)
            for (int z = -10; z <= 10; z++)
            {
                server.World.SetBlock(new Vector3i(x, Roof, z), roofId, owner: owner);
                if (underRoof is not null)
                {
                    server.World.SetBlock(new Vector3i(x, Roof - 1, z), _content.GetBlock(underRoof)!.NumericId, owner: owner);
                }
            }

        return (server, p, baseId);
    }

    /// <summary>Ticks with the player pinned at <paramref name="at"/> (half-second steps).</summary>
    private static void TickAt(SvGameServer server, PlayerSession p, Vector3f at, int halfSeconds)
    {
        for (int i = 0; i < halfSeconds; i++)
        {
            p.State.Position = at;
            server.TickForTest(0.5);
        }
    }

    /// <summary>From the breathing room A through the energy door into the hall — what the leak warning answers.</summary>
    private static void WalkIn(SvGameServer server, PlayerSession p, RecordingTransport t, Vector3f into)
    {
        TickAt(server, p, InRoomA, 8); // past the 3 s between two diagnoses
        t.Sent.Clear();
        TickAt(server, p, into, 4);
    }

    [Theory]
    [InlineData("sand", "Sand")]
    [InlineData("snow", "Snow")]
    public void ALeakyRoofUnderTheOpenSky_IsToldOnWalkIn_AndNamesTheBlock(string roof, string name)
    {
        var t = new RecordingTransport();
        var (server, p, _) = Hall("roof_" + roof, t, out var repo, roof);
        using (repo)
        {
            WalkIn(server, p, t, InHall);
            var told = Assert.Single(t.Leaks());
            Assert.StartsWith("Warning", told, StringComparison.Ordinal); // held like the other warnings
            Assert.Contains($"through {name} at 30/{Roof}/0, 10 blocks above you", told, StringComparison.Ordinal);

            var diag = server.DiagnoseBaseAirForTest(30, CoreY, 0);
            Assert.Equal(SvGameServer.BaseAirStatus.Leak, diag.Status);
            Assert.True(diag.LooksLikeRoom);
            Assert.Equal<Vector3i?>(new Vector3i(30, Roof, 0), diag.LeakCell);
            Assert.Equal(_content.GetBlock(roof)!.NumericId, diag.LeakBlock);
        }
    }

    [Fact]
    public void AStoneLayerUnderTheSand_Breathes_AndNothingIsSaid()
    {
        var t = new RecordingTransport();
        var (server, p, baseId) = Hall("stonelayer", t, out var repo, "sand", underRoof: "stone");
        using (repo)
        {
            TickAt(server, p, InRoomA, 8);
            t.Sent.Clear();
            float before = p.State.Oxygen = 50f;
            TickAt(server, p, InHall, 6);
            Assert.True(p.State.Oxygen > before, $"The hall with a stone layer under its sand should breathe (oxygen {p.State.Oxygen}).");
            Assert.Equal(3, p.State.LifeSupportSource); // the base
            Assert.Equal(2, server.BaseAirForTest(baseId).Rooms);
            Assert.Empty(t.Leaks());
            Assert.DoesNotContain(t.Lines(), l => l.StartsWith("@srv.base.air", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheLeak_IsToldOncePerFiveMinutes()
    {
        var t = new RecordingTransport();
        var (server, p, _) = Hall("retell", t, out var repo, "sand");
        using (repo)
        {
            WalkIn(server, p, t, InHall);
            Assert.Single(t.Leaks());

            WalkIn(server, p, t, InHall); // back to A and in again a few seconds later
            Assert.Empty(t.Leaks());

            server.AdvanceUptimeForTest(301); // past the five minutes
            WalkIn(server, p, t, InHall);
            Assert.Single(t.Leaks());
        }
    }

    [Fact]
    public void ASandPatchInAnIceRoof_IsNamed_NotTheIce()
    {
        var t = new RecordingTransport();
        var (server, p, _) = Hall("patch", t, out var repo, "ice");
        using (repo)
        {
            // A 3×3 sand patch in the hall's far corner: the fill from the door runs under ice until it meets it.
            var sand = _content.GetBlock("sand")!.NumericId;
            for (int x = 38; x <= 40; x++)
                for (int z = 8; z <= 10; z++)
                    server.World.SetBlock(new Vector3i(x, Roof, z), sand, owner: p.State.PlayerId);

            WalkIn(server, p, t, InHallByTheDoor);
            var told = Assert.Single(t.Leaks());
            Assert.Contains($"through Sand at 38/{Roof}/8, 10 blocks above you", told, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AHoleInAStoneRoof_IsNamedAtTheHole()
    {
        var t = new RecordingTransport();
        var (server, p, _) = Hall("hole", t, out var repo, "stone");
        using (repo)
        {
            server.World.SetBlock(new Vector3i(36, Roof, 6), BlockId.Air); // one roof block missing
            WalkIn(server, p, t, InHall);
            var told = Assert.Single(t.Leaks());
            // #2654: the opening itself — the hole in the roof, at the roof's height — not the top of the reach box 48 up.
            Assert.Contains($"escapes at 36/{Roof}/6, 10 blocks above you", told, StringComparison.Ordinal);
            var diag = server.DiagnoseBaseAirForTest(30, CoreY, 0);
            Assert.Equal<Vector3i?>(new Vector3i(36, Roof, 6), diag.LeakCell);
            Assert.True(diag.LeakBlock.IsAir);
        }
    }

    [Fact]
    public void AHoleInTheSideWall_IsNamedAtItsOwnHeight()
    {
        var t = new RecordingTransport();
        var (server, p, _) = Hall("sidehole", t, out var repo, "stone");
        using (repo)
        {
            server.World.SetBlock(new Vector3i(41, CoreY + 1, 0), BlockId.Air); // one block out of the far wall, one above the floor
            WalkIn(server, p, t, InHall);
            var told = Assert.Single(t.Leaks());
            Assert.Contains($"escapes at 41/{CoreY + 1}/0, 1 block above you", told, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WalkingOutOfTheCoreZoneIntoTheOpen_StaysSilent()
    {
        var t = new RecordingTransport();
        var server = Start("open", t, out var repo);
        using (repo)
        {
            var (p, _) = Found(server);
            TickAt(server, p, InTheCoreZone, 8);
            t.Sent.Clear();
            TickAt(server, p, new Vector3f(20.5f, CoreY, 0.5f), 4); // past the cube, nothing overhead: simply outside

            Assert.Empty(t.Leaks());
            var diag = server.DiagnoseBaseAirForTest(20, CoreY, 0);
            Assert.Equal(SvGameServer.BaseAirStatus.Leak, diag.Status);
            Assert.False(diag.LooksLikeRoom);
        }
    }

    [Fact]
    public void BaseWalls_NamesTheLeak_AndSaysTheWallVerdictIsAboutAnimals()
    {
        var t = new RecordingTransport();
        var (server, p, _) = Hall("basewalls", t, out var repo, "sand");
        using (repo)
        {
            TickAt(server, p, InHall, 2);
            p.State.Position = InHall;
            var lines = server.BaseWallsReportForTest(p);
            Assert.StartsWith("Your cell, for animals: ", lines[2], StringComparison.Ordinal);
            Assert.StartsWith("Air at your cell: none — leaky: the air escapes through Sand at 30/130/0, 10 blocks above you", lines[4],
                StringComparison.Ordinal);

            p.Locale = "de";
            var de = server.BaseWallsReportForTest(p);
            Assert.StartsWith("Deine Zelle, für Tiere: ", de[2], StringComparison.Ordinal);
            Assert.Contains("durch Sand bei 30/130/0, 10 Blöcke über dir", de[4], StringComparison.Ordinal);
        }
    }

    private static JsonElement Locale(string code)
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }).RootElement;

    [Fact]
    public void LeakTexts_CarryTheirPlaceholders_AndNameSnowAndGeyserVents()
    {
        foreach (string code in new[] { "en", "de" })
        {
            var locale = Locale(code);
            string Text(string key) => locale.GetProperty(key).GetString()!;
            foreach (string key in new[] { "srv.base.air_leak_at", "srv.basewalls.air_leak_at" })
            {
                foreach (string token in new[] { "{block}", "{x}", "{y}", "{z}", "{height}" })
                {
                    Assert.Contains(token, Text(key), StringComparison.Ordinal);
                }
            }

            foreach (string key in new[] { "srv.base.air_leak_open_at", "srv.basewalls.air_leak_open_at" })
            {
                foreach (string token in new[] { "{x}", "{y}", "{z}", "{height}" })
                {
                    Assert.Contains(token, Text(key), StringComparison.Ordinal);
                }
            }

            Assert.Contains("{n}", Text("srv.base.air_leak_above"), StringComparison.Ordinal);
            Assert.Contains("{n}", Text("srv.base.air_leak_below"), StringComparison.Ordinal);
            Assert.Contains("{block}", Text("srv.base.air_leak_shaped"), StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(Text("srv.base.air_leak_level")));
            Assert.Contains("/basewalls", Text("ui.base.air_here_why"), StringComparison.Ordinal);

            // #2615: snow and geyser vents never seal either — the leak line and VEGA's air hint say so.
            string snow = code == "de" ? "Schnee" : "snow", vent = code == "de" ? "Geysir" : "geyser";
            foreach (string key in new[] { "srv.basewalls.air_leak", "vega.hint.base_airless" })
            {
                Assert.Contains(snow, Text(key), StringComparison.Ordinal);
                Assert.Contains(vent, Text(key), StringComparison.Ordinal);
            }
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
