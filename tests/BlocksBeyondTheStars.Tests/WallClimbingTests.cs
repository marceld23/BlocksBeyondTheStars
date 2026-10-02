// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Wall climbing (#2188–#2193), the server/shared half: which blocks hold a climber comes from the block data (glass
/// never, ice only with claws, the granular blocks are slippery, ladders, plants, doors and liquids are no wall); the
/// climbing gloves and claws are researchable, craftable module gear whose best worn piece counts; and the client's
/// "I am climbing" flag reaches every other player's presence (with the gloves on the avatar). The climb itself is
/// on-foot movement — the client's — and is tested in the client suite (ClimbProbeTests, ClimbGripTests).
/// </summary>
public sealed class WallClimbingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public WallClimbingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_climb_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    // ---------------- Surfaces (#2191) ----------------

    [Fact]
    public void GlassNeverHolds_IceOnlyWithClaws_AndRockHolds()
    {
        Assert.Equal(ClimbSurface.None, ClimbSurfaces.Of(_content.GetBlock("glass")));
        Assert.Equal(ClimbSurface.None, ClimbSurfaces.Of(_content.GetBlock("glass_clear")));
        Assert.Equal(ClimbSurface.Icy, ClimbSurfaces.Of(_content.GetBlock("ice")));
        Assert.Equal(ClimbSurface.Normal, ClimbSurfaces.Of(_content.GetBlock("stone")));
        Assert.Equal(ClimbSurface.Normal, ClimbSurfaces.Of(_content.GetBlock("wood_log")));

        Assert.Equal(ClimbSurface.None, ClimbSurfaces.ForClimber(ClimbSurface.Icy, iceGrip: false));
        Assert.Equal(ClimbSurface.Normal, ClimbSurfaces.ForClimber(ClimbSurface.Icy, iceGrip: true));
        Assert.Equal(ClimbSurface.Normal, ClimbSurfaces.ForClimber(ClimbSurface.Slippery, iceGrip: true));
        Assert.Equal(ClimbSurface.None, ClimbSurfaces.ForClimber(ClimbSurface.None, iceGrip: true)); // claws never grip glass
    }

    [Fact]
    public void EveryGranularBlock_IsSlippery()
    {
        var granular = _content.Blocks.Values.Where(b => b.Granular && b.Climb is null).ToList();
        Assert.NotEmpty(granular);
        Assert.All(granular, b => Assert.Equal(ClimbSurface.Slippery, ClimbSurfaces.Of(b)));
        Assert.Contains(granular, b => b.Key == "sand");
    }

    [Fact]
    public void LaddersPlantsDoorsAndLiquids_AreNoWall()
    {
        Assert.Equal(ClimbSurface.None, ClimbSurfaces.Of(_content.GetBlock("ladder")));
        Assert.Equal(ClimbSurface.None, ClimbSurfaces.Of(_content.GetBlock("water")));
        Assert.Equal(ClimbSurface.None, ClimbSurfaces.Of(_content.GetBlock("lava")));
        Assert.Equal(ClimbSurface.None, ClimbSurfaces.Of(null));
        Assert.All(_content.Blocks.Values.Where(b => b.Category == "door" || b.Liquid || b.Key.StartsWith("flora_", StringComparison.Ordinal)),
            b => Assert.Equal(ClimbSurface.None, ClimbSurfaces.Of(b)));
    }

    [Fact]
    public void EveryClimbValueInTheData_IsKnown()
        => Assert.All(_content.Blocks.Values, b => Assert.True(ClimbSurfaces.IsKnownValue(b.Climb), $"{b.Key}: climb '{b.Climb}'"));

    // ---------------- Gear (#2192) ----------------

    [Fact]
    public void TheGlovesAndClaws_AreModules_ClawsGripBetterAndHoldIce()
    {
        var gloves = _content.GetItem("climbing_gloves")!;
        var claws = _content.GetItem("climbing_claws")!;

        Assert.Equal(EquipSlots.Module, gloves.EquipSlot);
        Assert.Equal(EquipSlots.Module, claws.EquipSlot);
        Assert.True(gloves.ClimbGrip > 0f && claws.ClimbGrip > gloves.ClimbGrip);
        Assert.True(claws.ClimbGrip <= SuitEquipment.MaxClimbGrip);
        Assert.False(gloves.ClimbIce);
        Assert.True(claws.ClimbIce);
    }

    [Fact]
    public void OnlyWornGear_Counts_AndTheBestPieceWins_NoStacking()
    {
        var items = _content.Items.Values;
        var claws = _content.GetItem("climbing_claws")!;

        Assert.Equal(0f, SuitEquipment.ClimbGrip(items, _ => false));
        Assert.False(SuitEquipment.ClimbIce(items, _ => false));

        // Both modules worn at once: the claws' grip, not the sum.
        bool Both(string key) => key is "climbing_gloves" or "climbing_claws";
        Assert.Equal(claws.ClimbGrip, SuitEquipment.ClimbGrip(items, Both), 5);
        Assert.True(SuitEquipment.ClimbIce(items, Both));
    }

    [Fact]
    public void TheGear_IsResearchedAndCraftedAtTheWorkshop_ClawsFromTheGloves()
    {
        var glovesBp = _content.GetBlueprint("climbing_gloves")!;
        var clawsBp = _content.GetBlueprint("climbing_claws")!;
        Assert.True(glovesBp.KnowledgeCost > 0 && clawsBp.KnowledgeCost > glovesBp.KnowledgeCost);
        Assert.Contains("climbing_gloves", clawsBp.Prerequisites);

        var gloves = _content.GetRecipe("climbing_gloves")!;
        var claws = _content.GetRecipe("climbing_claws")!;
        Assert.Equal(CraftingStation.Workshop, gloves.Station);
        Assert.Equal("climbing_gloves", gloves.RequiredBlueprint);
        Assert.Equal("climbing_claws", claws.RequiredBlueprint);
        Assert.Contains(claws.Inputs, i => i.Item == "climbing_gloves");
    }

    [Fact]
    public void TheGear_HasItsIcons()
    {
        string icons = Path.Combine(Path.GetDirectoryName(TestPaths.DataDir())!, "client", "Assets", "Resources", "icons");
        Assert.True(File.Exists(Path.Combine(icons, "item_climbing_gloves.png")));
        Assert.True(File.Exists(Path.Combine(icons, "item_climbing_claws.png")));
    }

    // ---------------- The climbing pose on the wire (#2193) ----------------

    [Fact]
    public void TheClimbingFlag_SurvivesTheCodec()
    {
        var decoded = NetCodec.Decode(NetCodec.Encode(new MoveIntent { X = 1, Y = 2, Z = 3, Climbing = true }));
        Assert.True(Assert.IsType<MoveIntent>(decoded).Climbing);

        var presence = NetCodec.Decode(NetCodec.Encode(new PlayerPresence { PlayerId = "a", Climbing = true }));
        Assert.True(Assert.IsType<PlayerPresence>(presence).Climbing);
    }

    [Fact]
    public void AClimbingMove_SetsThePose_AndTheNextMoveClearsIt()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "climb_move"));
        _repos.Add(repo);
        var link = new LoopbackLink();
        using var serverTransport = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var server = new SvGameServer(Config("climb_move"), _content, serverTransport, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Climber" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var session = server.Sessions.Values.Single(s => s.Joined);
        var p = session.State.Position;
        client.Send(NetCodec.Encode(new MoveIntent { X = p.X, Y = p.Y + 1f, Z = p.Z, Climbing = true }), DeliveryMode.ReliableOrdered);
        server.Tick(0.05);
        Assert.True(session.State.Climbing);

        client.Send(NetCodec.Encode(new MoveIntent { X = p.X, Y = p.Y, Z = p.Z }), DeliveryMode.ReliableOrdered);
        server.Tick(0.05);
        Assert.False(session.State.Climbing);
    }

    [Fact]
    public void AnotherPlayer_SeesTheClimber_AndTheirGloves()
    {
        var transport = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "climb_presence"));
        _repos.Add(repo);
        var server = new SvGameServer(Config("climb_presence"), _content, transport, repo);
        server.Start();

        var alice = server.AddLocalPlayer("Alice");
        var bob = server.AddLocalPlayer("Bob");
        alice.State.AboardShip = false;
        bob.State.AboardShip = false;
        bob.State.Position = new Vector3f(6, 64, 4);
        bob.State.Climbing = true;
        bob.State.Equipment.SetSlot((int)EquipSlot.Module1, new ItemStack("climbing_gloves", 1));
        bob.State.Equipment.SetSlot((int)EquipSlot.Module2, new ItemStack("climbing_claws", 1));

        transport.Sent.Clear();
        server.Tick(0.2);

        var seen = transport.Sent
            .Where(x => x.Conn == alice.ConnectionId && x.Msg is PlayerPresence p && p.PlayerId == "Bob")
            .Select(x => (PlayerPresence)x.Msg)
            .LastOrDefault();
        Assert.NotNull(seen);
        Assert.True(seen!.Climbing);
        Assert.Equal(128, seen.Gear & 128); // gloves
        Assert.Equal(256, seen.Gear & 256); // claws
    }

    private static ServerConfig Config(string name) => new()
    {
        WorldName = name,
        Seed = 1,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999,
        ViewDistanceChunks = 1,
        PlaceStarterShip = false,
    };

    /// <summary>A transport that records every server send so a test can assert who received what.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }

        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((connectionId, m));
            }
        }

        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m)
            {
                Sent.Add((int.MinValue, m));
            }
        }

        public void Poll()
        {
            _ = ClientConnected;
            _ = ClientDisconnected;
            _ = PayloadReceived;
        }

        public void Stop() { }

        public void Dispose() { }
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked temp file is not a test failure.
        }
    }
}
