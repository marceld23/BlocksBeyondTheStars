// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
/// Suit teleporter → allied player (#1056) and the shared "land beside, not inside" helper (#1055): the jump
/// needs the device gates AND an alliance, the same body, a target who is neither in space nor aboard their own
/// ship; the arrival stands next to the target on real ground. Plus the StarterTeleporter world rule.
/// </summary>
public sealed class TeleportToPlayerTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TeleportToPlayerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tpp_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string tag = "tpp", GameRules? rules = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = tag, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        if (rules is not null)
        {
            config.Rules = rules;
        }

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>A flat 7×7 stone floor at <paramref name="floorY"/> with clear air above, centred on (cx, cz) —
    /// high above the terrain so nothing generated interferes.</summary>
    private void Floor(SvGameServer server, int cx, int floorY, int cz)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = cx - 3; x <= cx + 3; x++)
        {
            for (int z = cz - 3; z <= cz + 3; z++)
            {
                server.World.SetBlock(new Vector3i(x, floorY, z), stone);
                for (int y = floorY + 1; y <= floorY + 3; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
                }
            }
        }
    }

    private static void Ally(SvGameServer server, string a, string b)
    {
        server.RequestAlliance(a, b);
        server.RespondAlliance(b, a, accept: true);
        Assert.True(server.AreAllied(a, b));
    }

    private static void Equip(BlocksBeyondTheStars.GameServer.PlayerSession p)
    {
        p.State.SuitEnergy = 100f;
        p.State.AboardShip = false;
        p.State.Inventory.Add("suit_teleporter", 1, 1);
    }

    [Fact]
    public void AllyOnSameBody_LandsBesideThem_OnGround()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            Ally(server, "Alice", "Bob");
            Floor(server, 40, 300, 40);
            bob.State.Position = new Vector3f(40.5f, 301f, 40.5f);
            bob.State.AboardShip = false; // no starter ship in these tests, and AboardShip defaults to true then
            alice.State.Position = new Vector3f(-200f, 64f, -200f);
            Equip(alice);

            server.TeleportToPlayer("Alice", "Bob");

            var at = alice.State.Position;
            float dx = at.X - bob.State.Position.X, dz = at.Z - bob.State.Position.Z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);
            Assert.InRange(dist, 1f, 2.6f);              // beside, not inside (#1055)
            Assert.Equal(301f, at.Y);                     // standing on the stone floor
            Assert.NotEqual(bob.State.Position, at);
            Assert.True(alice.State.SuitEnergy < 100f);   // energy spent
        }
    }

    [Fact]
    public void NotAllied_Refused_PositionUnchanged()
    {
        var server = Started(out var repo, "tpp_na");
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            bob.State.Position = new Vector3f(40.5f, 301f, 40.5f);
            bob.State.AboardShip = false; // no starter ship in these tests, and AboardShip defaults to true then
            alice.State.Position = new Vector3f(-200f, 64f, -200f);
            Equip(alice);

            server.TeleportToPlayer("Alice", "Bob");

            Assert.Equal(-200f, alice.State.Position.X);
            Assert.Equal(100f, alice.State.SuitEnergy); // nothing charged
        }
    }

    [Fact]
    public void WithoutDevice_Refused()
    {
        var server = Started(out var repo, "tpp_nodev");
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            Ally(server, "Alice", "Bob");
            bob.State.Position = new Vector3f(40.5f, 301f, 40.5f);
            bob.State.AboardShip = false; // no starter ship in these tests, and AboardShip defaults to true then
            alice.State.Position = new Vector3f(-200f, 64f, -200f);
            alice.State.SuitEnergy = 100f;

            server.TeleportToPlayer("Alice", "Bob");

            Assert.Equal(-200f, alice.State.Position.X);
        }
    }

    [Fact]
    public void TargetOnAnotherBody_Refused()
    {
        var server = Started(out var repo, "tpp_body");
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            Ally(server, "Alice", "Bob");
            bob.State.Position = new Vector3f(40.5f, 301f, 40.5f);
            bob.State.AboardShip = false; // no starter ship in these tests, and AboardShip defaults to true then
            bob.CurrentLocationId = "elsewhere";
            alice.State.Position = new Vector3f(-200f, 64f, -200f);
            Equip(alice);

            server.TeleportToPlayer("Alice", "Bob");

            Assert.Equal(-200f, alice.State.Position.X);
            Assert.Equal(100f, alice.State.SuitEnergy);
        }
    }

    [Fact]
    public void TargetAboardTheirShip_Refused_ShipsStayPrivate()
    {
        var server = Started(out var repo, "tpp_aboard");
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            Ally(server, "Alice", "Bob");
            bob.State.Position = new Vector3f(40.5f, 301f, 40.5f);
            bob.State.AboardShip = true;
            alice.State.Position = new Vector3f(-200f, 64f, -200f);
            Equip(alice);

            server.TeleportToPlayer("Alice", "Bob");

            Assert.Equal(-200f, alice.State.Position.X);
            Assert.Equal(100f, alice.State.SuitEnergy);
        }
    }

    [Fact]
    public void SelfOrUnknownTarget_Refused()
    {
        var server = Started(out var repo, "tpp_self");
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            alice.State.Position = new Vector3f(-200f, 64f, -200f);
            Equip(alice);

            server.TeleportToPlayer("Alice", "Alice");
            server.TeleportToPlayer("Alice", "Nobody");

            Assert.Equal(-200f, alice.State.Position.X);
            Assert.Equal(100f, alice.State.SuitEnergy);
        }
    }

    [Fact]
    public void CooldownIsSharedWithTheShipRecall()
    {
        var server = Started(out var repo, "tpp_cd");
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            Ally(server, "Alice", "Bob");
            Floor(server, 40, 300, 40);
            bob.State.Position = new Vector3f(40.5f, 301f, 40.5f);
            bob.State.AboardShip = false; // no starter ship in these tests, and AboardShip defaults to true then
            alice.State.RespawnPoint = new Vector3f(5, 70, 5);
            Equip(alice);

            server.TeleportToShip("Alice");            // recall → starts the shared cooldown
            Assert.Equal(5f, alice.State.Position.X);

            server.TeleportToPlayer("Alice", "Bob");   // still recharging
            Assert.Equal(5f, alice.State.Position.X);

            server.Tick(31.0);
            alice.State.AboardShip = false;
            server.TeleportToPlayer("Alice", "Bob");   // recharged
            Assert.InRange(MathF.Abs(alice.State.Position.X - 40.5f), 0f, 2.6f);
        }
    }

    [Fact]
    public void LandingSpotNear_FallsBackToTarget_WhenNothingAroundFits()
    {
        var server = Started(out var repo, "tpp_shaft");
        using (repo)
        {
            // A 1-wide shaft: stone all around the target column at feet + head height, floor below.
            var stone = _content.GetBlock("stone")!.NumericId;
            for (int x = 37; x <= 43; x++)
            {
                for (int z = 37; z <= 43; z++)
                {
                    for (int y = 297; y <= 304; y++)
                    {
                        bool column = x == 40 && z == 40 && y >= 301;
                        server.World.SetBlock(new Vector3i(x, y, z), column ? BlockId.Air : stone);
                    }
                }
            }

            var target = new Vector3f(40.5f, 301f, 40.5f);
            Assert.Equal(target, server.LandingSpotNearForTest(target));
        }
    }

    [Fact]
    public void LandingSpotNear_PrefersTheCellInFrontOfTheTarget()
    {
        var server = Started(out var repo, "tpp_front");
        using (repo)
        {
            Floor(server, 40, 300, 40);
            var target = new Vector3f(40.5f, 301f, 40.5f);

            var facingPlusZ = server.LandingSpotNearForTest(target, targetYaw: 0f);
            Assert.True(facingPlusZ.Z > target.Z + 1f, $"expected +Z, got {facingPlusZ}");

            var facingPlusX = server.LandingSpotNearForTest(target, targetYaw: 90f);
            Assert.True(facingPlusX.X > target.X + 1f, $"expected +X, got {facingPlusX}");
        }
    }

    [Fact]
    public void StarterTeleporterRule_HandsOutTheDeviceOnJoin_Idempotently()
    {
        var server = Started(out var repo, "tpp_rule", new GameRules { StarterTeleporter = true });
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            Assert.True(alice.State.Inventory.Has("suit_teleporter", 1));

            Assert.False(server.GrantStarterTeleporterForTest(alice)); // already has one → nothing added
            Assert.Equal(1, alice.State.Inventory.CountOf("suit_teleporter"));
        }
    }

    [Fact]
    public void StarterTeleporterRule_Off_HandsOutNothing()
    {
        var server = Started(out var repo, "tpp_norule");
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            Assert.False(alice.State.Inventory.Has("suit_teleporter", 1));
            Assert.False(server.GrantStarterTeleporterForTest(alice));
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
