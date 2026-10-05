// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
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
/// The energy rope gun (#2317/#2319): a gadget whose shot the server accepts only within range, on a solid block
/// and in sight; the anchor rides the presence to other players and is forgotten on release, on a hotbar change, and
/// wherever the rope cannot hold. The item, its recipe, its research node and its texts are data.
/// </summary>
public sealed class RopeGunTests : IDisposable
{
    private const string Gun = "energy_rope_gun";

    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public RopeGunTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_rope_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private static ServerConfig Config(string name) => new()
    {
        WorldName = name,
        Seed = 3,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999,
        PlaceStarterShip = false,
    };

    /// <summary>A transport that records every server send.</summary>
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
    }

    private SvGameServer Started(string name, out RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        transport = new RecordingTransport();
        var server = new SvGameServer(Config(name), _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A player on foot with the gun in the selected slot, standing on a stone floor at y 40 with a stone
    /// wall three blocks east of them (x 20..22) — air everywhere else around.</summary>
    private (BlocksBeyondTheStars.GameServer.PlayerSession Session, Vector3i Wall) Shooter(SvGameServer server, string name)
    {
        var s = server.AddLocalPlayer(name);
        s.State.AboardShip = false;
        s.State.Inventory.Add(Gun, 1, 1);
        s.State.SelectedHotbarSlot = s.State.Inventory.Slots.ToList().FindIndex(x => x?.Item == Gun);
        Assert.True(s.State.SelectedHotbarSlot >= 0, "the gun sits in the hotbar");

        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = 10; x <= 30; x++)
            for (int z = 5; z <= 15; z++)
                for (int y = 36; y <= 50; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), y <= 39 || (x >= 20 && x <= 22 && y <= 45) ? stone : BlockId.Air);
                }

        s.State.Position = new Vector3f(15.5f, 40f, 10.5f);
        return (s, new Vector3i(20, 42, 10));
    }

    private static int RejectCount(RecordingTransport t, string reason)
        => t.Sent.Count(x => x.Msg is ActionRejected r && r.Reason == reason);

    // ---------------- Data ----------------

    [Fact]
    public void TheGun_IsAGadgetWithARange_ARecipe_AResearchNode_AndTexts()
    {
        var item = _content.GetItem(Gun);
        Assert.NotNull(item);
        Assert.Equal(ToolKind.Gadget, item!.Tool!.Kind); // never a weapon: a weapon with damage 0 still hits
        Assert.Equal(24f, item.Tool.Range);
        Assert.Equal(3f, item.Tool.EnergyPerUse);
        Assert.Equal(FxStyles.Rope, item.Tool.Fx!.Style);
        Assert.True(item.HeldModel is { Count: > 0 and <= HeldModelPart.MaxParts });

        var recipe = _content.Recipes[Gun];
        Assert.Equal(Gun, recipe.RequiredBlueprint);
        Assert.Contains(recipe.Inputs, i => i.Item == "iron_plate");

        var node = _content.Blueprints[Gun];
        Assert.Equal("Suit", node.Category);
        Assert.Contains("climbing_gloves", node.Prerequisites);
        Assert.Contains("blueprint.feature.energy_rope_gun", node.Features);

        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        foreach (var key in new[]
                 {
                     "item.energy_rope_gun.name", "item.energy_rope_gun.desc", "blueprint.energy_rope_gun.name",
                     "blueprint.energy_rope_gun.desc", "blueprint.feature.energy_rope_gun", "vega.hint.rope_gun",
                     "srv.rope.too_far", "srv.rope.no_hold",
                 })
        {
            Assert.True(en.ContainsKey(key), $"missing EN locale key: {key}");
            Assert.True(de.ContainsKey(key), $"missing DE locale key: {key}");
        }
    }

    [Fact]
    public void TheReleaseIntent_AndTheAnchorOnThePresence_SurviveTheCodec()
    {
        Assert.IsType<ReleaseRopeIntent>(NetCodec.Decode(NetCodec.Encode(new ReleaseRopeIntent())));
        var presence = Assert.IsType<PlayerPresence>(NetCodec.Decode(NetCodec.Encode(new PlayerPresence { PlayerId = "a", Roped = true, RopeX = 1.5f, RopeY = 2.5f, RopeZ = -3f })));
        Assert.True(presence.Roped);
        Assert.Equal(1.5f, presence.RopeX);
        Assert.Equal(2.5f, presence.RopeY);
        Assert.Equal(-3f, presence.RopeZ);
        Assert.False(new PlayerPresence().Roped); // an older server's presence means no rope
    }

    // ---------------- The shot ----------------

    [Fact]
    public void AShotAtAWallInSight_SticksAndChargesEnergy()
    {
        var server = Started("rope_hit", out var transport);
        var (s, wall) = Shooter(server, "Roper");
        float energy = s.State.SuitEnergy;

        server.UseGadgetForTest("Roper", Gun, new Vector3f(wall.X, wall.Y + 0.5f, wall.Z + 0.5f)); // the west face of the wall

        Assert.NotNull(s.State.RopeAnchor);
        Assert.Equal(wall.Y + 0.5f, s.State.RopeAnchor!.Value.Y);
        Assert.Equal(energy - 3f, s.State.SuitEnergy);
        Assert.True(server.GadgetCooldownForTest("Roper", Gun) > 0);
        Assert.Equal(0, transport.Sent.Count(x => x.Msg is ActionRejected));
        Assert.Contains(transport.Sent, x => x.Msg is ActionFx fx && fx.ItemKey == Gun && fx.Outcome); // the shooter's client starts the pull on this
        Assert.Contains("vega:hint:rope_gun", s.State.Milestones); // the first rope that held: the controls, once
    }

    [Fact]
    public void AShotTooFar_IntoAir_OrThroughAWall_IsRefused_AndCostsNothing()
    {
        var server = Started("rope_miss", out var transport);
        var (s, wall) = Shooter(server, "Roper");
        float energy = s.State.SuitEnergy;

        server.UseGadgetForTest("Roper", Gun, new Vector3f(wall.X, wall.Y + 0.5f, wall.Z + 0.5f + 40f)); // 40 blocks away
        Assert.Null(s.State.RopeAnchor);
        Assert.Equal(1, RejectCount(transport, "@srv.rope.too_far"));

        server.UseGadgetForTest("Roper", Gun, new Vector3f(17.5f, 44f, 10.5f)); // a point in the air
        Assert.Null(s.State.RopeAnchor);
        Assert.Equal(1, RejectCount(transport, "@srv.rope.no_hold"));

        server.UseGadgetForTest("Roper", Gun, new Vector3f(25.5f, 40f, 10.5f)); // the floor beyond the wall: solid, in range, but the wall blocks the sight
        Assert.Null(s.State.RopeAnchor);
        Assert.Equal(2, RejectCount(transport, "@srv.rope.too_far"));

        Assert.Equal(energy, s.State.SuitEnergy);
        Assert.Equal(0, server.GadgetCooldownForTest("Roper", Gun));
    }

    [Fact]
    public void NoEnergy_AndACoolingGun_DoNotShoot()
    {
        var server = Started("rope_energy", out var transport);
        var (s, wall) = Shooter(server, "Roper");
        var face = new Vector3f(wall.X, wall.Y + 0.5f, wall.Z + 0.5f);

        s.State.SuitEnergy = 2f;
        server.UseGadgetForTest("Roper", Gun, face);
        Assert.Null(s.State.RopeAnchor);
        Assert.Equal(1, RejectCount(transport, "@no_energy"));

        s.State.SuitEnergy = 100f;
        server.UseGadgetForTest("Roper", Gun, face);
        Assert.NotNull(s.State.RopeAnchor);
        s.State.RopeAnchor = null;
        server.UseGadgetForTest("Roper", Gun, face); // cooling down: ignored quietly
        Assert.Null(s.State.RopeAnchor);
    }

    [Fact]
    public void AboardOrSeated_TheRopeHasNothingToHold()
    {
        var server = Started("rope_aboard", out var transport);
        var (s, wall) = Shooter(server, "Roper");
        var face = new Vector3f(wall.X, wall.Y + 0.5f, wall.Z + 0.5f);

        s.State.AboardShip = true;
        server.UseGadgetForTest("Roper", Gun, face);
        Assert.Null(s.State.RopeAnchor);
        s.State.AboardShip = false;
        s.State.Seated = true;
        server.UseGadgetForTest("Roper", Gun, face);
        Assert.Null(s.State.RopeAnchor);
        Assert.Equal(2, RejectCount(transport, "@srv.rope.no_hold"));
    }

    // ---------------- Letting go ----------------

    [Fact]
    public void TheRelease_TheHotbar_AndAMoveWithoutTheGun_DropTheRope()
    {
        var server = Started("rope_drop", out _);
        var (s, wall) = Shooter(server, "Roper");
        var face = new Vector3f(wall.X, wall.Y + 0.5f, wall.Z + 0.5f);

        server.UseGadgetForTest("Roper", Gun, face);
        Assert.NotNull(s.State.RopeAnchor);
        server.ReleaseRopeForTest("Roper");
        Assert.Null(s.State.RopeAnchor);

        // Over the wire: the intent reaches the handler, and a hotbar change away from the gun drops the rope too.
        var link = new LoopbackLink();
        using var serverTransport = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "rope_wire"));
        _repos.Add(repo);
        var wired = new SvGameServer(Config("rope_wire"), _content, serverTransport, repo);
        wired.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Wired" }), DeliveryMode.ReliableOrdered);
        wired.Tick(0.1);
        client.Poll();
        var session = wired.Sessions.Values.Single(x => x.Joined);
        session.State.AboardShip = false;
        session.State.Inventory.Add(Gun, 1, 1);
        int slot = session.State.Inventory.Slots.ToList().FindIndex(x => x?.Item == Gun);
        session.State.SelectedHotbarSlot = slot;
        session.State.RopeAnchor = new Vector3f(1f, 2f, 3f);

        client.Send(NetCodec.Encode(new ReleaseRopeIntent()), DeliveryMode.ReliableOrdered);
        wired.Tick(0.05);
        Assert.Null(session.State.RopeAnchor);

        session.State.RopeAnchor = new Vector3f(1f, 2f, 3f);
        client.Send(NetCodec.Encode(new SelectHotbarIntent { Slot = slot == 0 ? 1 : 0 }), DeliveryMode.ReliableOrdered);
        wired.Tick(0.05);
        Assert.Null(session.State.RopeAnchor);

        session.State.SelectedHotbarSlot = slot;
        session.State.RopeAnchor = new Vector3f(1f, 2f, 3f);
        var p = session.State.Position;
        client.Send(NetCodec.Encode(new MoveIntent { X = p.X, Y = p.Y, Z = p.Z }), DeliveryMode.ReliableOrdered);
        wired.Tick(0.05);
        Assert.NotNull(session.State.RopeAnchor); // still held, still allowed: a move keeps it

        session.State.AboardShip = true;
        client.Send(NetCodec.Encode(new MoveIntent { X = p.X, Y = p.Y, Z = p.Z }), DeliveryMode.ReliableOrdered);
        wired.Tick(0.05);
        Assert.Null(session.State.RopeAnchor); // boarded: the rope is gone
    }

    // ---------------- Other players ----------------

    [Fact]
    public void AnotherPlayer_SeesTheRope_AndWhereItHolds()
    {
        var server = Started("rope_presence", out var transport);
        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        alice.State.AboardShip = false;
        bob.State.AboardShip = false;
        alice.State.Position = new Vector3f(4, 64, 4);
        bob.State.Position = new Vector3f(6, 64, 4);
        bob.State.RopeAnchor = new Vector3f(9.5f, 70f, 4f);

        transport.Sent.Clear();
        server.Tick(0.2);

        var seen = transport.Sent
            .Where(x => x.Conn == alice.ConnectionId && x.Msg is PlayerPresence p && p.PlayerId == "Bob")
            .Select(x => (PlayerPresence)x.Msg)
            .LastOrDefault();
        Assert.NotNull(seen);
        Assert.True(seen!.Roped);
        Assert.Equal(9.5f, seen.RopeX);
        Assert.Equal(70f, seen.RopeY);
        Assert.Equal(4f, seen.RopeZ);

        // Letting go is a change the next beat carries, even though Bob stood still.
        bob.State.RopeAnchor = null;
        transport.Sent.Clear();
        server.Tick(0.2);
        var after = transport.Sent
            .Where(x => x.Conn == alice.ConnectionId && x.Msg is PlayerPresence p && p.PlayerId == "Bob")
            .Select(x => (PlayerPresence)x.Msg)
            .LastOrDefault();
        Assert.NotNull(after);
        Assert.False(after!.Roped);
    }

    public void Dispose()
    {
        foreach (var r in _repos)
        {
            r.Dispose();
        }

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
}
