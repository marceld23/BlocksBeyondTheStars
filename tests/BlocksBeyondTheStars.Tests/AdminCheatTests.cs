// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

public sealed class AdminCheatTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public AdminCheatTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_cheat_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private (SvGameServer server, LoopbackClientTransport client) Start(GameRules rules, string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        var st = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, Rules = rules };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Creator" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        return (server, client);
    }

    [Fact]
    public void FirstPlayer_BecomesWorldAdmin()
    {
        var (server, _) = Start(new GameRules { AdminCheats = true }, "wa");
        Assert.Equal(PlayerRole.WorldAdmin, server.Sessions[1].State.Role);
    }

    [Fact]
    public void GiveItem_Works_ForAdmin_WhenCheatsEnabled()
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true }; // survival + cheats on
        var (server, client) = Start(rules, "give");
        var p = server.Sessions[1].State;

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "give_item", StringArg = "titanium_plate", IntArg = 5 }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(5, p.Inventory.CountOf("titanium_plate"));
    }

    [Fact]
    public void Cheat_Rejected_WhenCheatsDisabled()
    {
        var rules = new GameRules { AdminCheats = false }; // cheats off
        var (server, client) = Start(rules, "nocheat");

        ActionRejected? rejected = null;
        client.PayloadReceived += pl => { if (NetCodec.Decode(pl) is ActionRejected r) rejected = r; };

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "give_item", StringArg = "titanium_plate", IntArg = 5 }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.Equal(0, server.Sessions[1].State.Inventory.CountOf("titanium_plate"));
        Assert.NotNull(rejected);
    }

    [Fact]
    public void Cheat_Rejected_ForNonAdmin()
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        var (server, client) = Start(rules, "nonadmin");

        // Demote the player to a regular player, simulating a non-admin client.
        server.Sessions[1].State.Role = PlayerRole.Player;

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "give_item", StringArg = "titanium_plate", IntArg = 5 }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(0, server.Sessions[1].State.Inventory.CountOf("titanium_plate"));
    }

    [Fact]
    public void Teleport_ToLocation_MovesPlayer()
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        var (server, client) = Start(rules, "tp");

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "teleport_to_location", X = 100, Y = 70, Z = -50 }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        var pos = server.Sessions[1].State.Position;
        Assert.Equal(100f, pos.X);
        Assert.Equal(70f, pos.Y);
        Assert.Equal(-50f, pos.Z);
    }

    [Fact]
    public void Teleport_ToLocation_SendsRespawnSnap()
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        var (server, client) = Start(rules, "tpsnap");

        // Without the RespawnNotice snap the client discards the new position and its next MoveIntent
        // reverts the teleport server-side — /tp silently does nothing (#414 M7).
        RespawnNotice? snap = null;
        client.PayloadReceived += pl => { if (NetCodec.Decode(pl) is RespawnNotice r) snap = r; };

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "teleport_to_location", X = 100, Y = 70, Z = -50 }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.NotNull(snap);
        Assert.Equal(100f, snap!.X);
        Assert.Equal(70f, snap.Y);
        Assert.Equal(-50f, snap.Z);
        Assert.False(snap.Died); // a relocation, not a death — must not trigger the death flash/prompt
    }

    [Fact]
    public void Teleport_ToPlayer_SendsRespawnSnap()
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        var (server, client) = Start(rules, "tpplayer");

        var target = server.AddLocalPlayer("Target");
        target.State.Position = new Shared.Geometry.Vector3f(321, 70, 123);

        RespawnNotice? snap = null;
        client.PayloadReceived += pl => { if (NetCodec.Decode(pl) is RespawnNotice r) snap = r; };

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "teleport_to_player", TargetPlayer = "Target" }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.NotNull(snap);
        Assert.Equal(321f, snap!.X);
        Assert.Equal(123f, snap.Z);
        Assert.False(snap.Died);
    }

    /// <summary>Names with spaces and a different capitalisation still resolve (#980): the client now sends
    /// the whole rest of the typed line, and the server matches it the way every other admin lookup does.
    /// An exact-case compare used to answer "target player not found" for a player who was right there.</summary>
    [Theory]
    [InlineData("mincraft Fan", "exact")]
    [InlineData("MINCRAFT fan", "case")]
    [InlineData("  \"mincraft Fan\"  ", "quoted")]
    public void Teleport_ToPlayer_MatchesSpacedAndDifferentlyCasedNames(string typed, string world)
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        var (server, client) = Start(rules, "tpname_" + world);

        var target = server.AddLocalPlayer("mincraft Fan");
        target.State.Position = new Shared.Geometry.Vector3f(321, 70, 123);

        RespawnNotice? snap = null;
        client.PayloadReceived += pl => { if (NetCodec.Decode(pl) is RespawnNotice r) snap = r; };

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "teleport_to_player", TargetPlayer = typed }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.NotNull(snap);
        Assert.Equal(321f, snap!.X);
        Assert.Equal(123f, snap.Z);
    }

    /// <summary>An empty or unknown target still has to be refused — the tolerant match must not fall
    /// back to "some session" (or to the admin's own).</summary>
    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "blank")]
    [InlineData("Nobody", "unknown")]
    public void Teleport_ToPlayer_RejectsUnknownTarget(string typed, string world)
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        var (server, client) = Start(rules, "tpmiss_" + world);
        var before = server.Sessions[1].State.Position;

        ActionRejected? rejected = null;
        client.PayloadReceived += pl => { if (NetCodec.Decode(pl) is ActionRejected r) rejected = r; };

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "teleport_to_player", TargetPlayer = typed }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.NotNull(rejected);
        Assert.Equal(before, server.Sessions[1].State.Position);
    }

    /// <summary><c>/give</c> resolves its target through the same lookup, so a spaced name must land in
    /// THAT player's inventory rather than silently in the admin's own (the null fallback).</summary>
    [Fact]
    public void GiveItem_ToSpacedName_ReachesThatPlayer()
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        var (server, client) = Start(rules, "givename");
        var target = server.AddLocalPlayer("mincraft Fan");

        client.Send(NetCodec.Encode(new AdminCommandIntent
        {
            Command = "give_item",
            StringArg = "titanium_plate",
            IntArg = 5,
            TargetPlayer = "MINCRAFT Fan",
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        Assert.Equal(5, target.State.Inventory.CountOf("titanium_plate"));
        Assert.Equal(0, server.Sessions[1].State.Inventory.CountOf("titanium_plate"));
    }

    [Fact]
    public void GodMode_PreventsDeath()
    {
        var rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        var (server, client) = Start(rules, "god");
        var p = server.Sessions[1].State;

        client.Send(NetCodec.Encode(new AdminCommandIntent { Command = "godmode" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        p.AboardShip = false;
        p.Health = 0f;
        server.Tick(0.1);

        Assert.Equal(100f, p.Health); // invulnerable, restored rather than dead
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
