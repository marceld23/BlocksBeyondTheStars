// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The first batch of wild animals after a join (#2960). It used to be placed inside the join handler, before any chunk
/// of the spawn ring was loaded. An unloaded chunk reads as air, so the floor rule (#2521) saw no floor there, and the
/// feet fell back to the generator surface: the top of a paved yard. A player found wild animals on the floor of their
/// spaceport after every load, and never during play. These tests reload a world with a paved yard and do a real
/// network join.
/// </summary>
public sealed class FaunaSeedTests : IDisposable
{
    private const int FloorRadius = 50; // the spawn ring reaches 45 blocks out, a herd a few more
    private const string Builder = "Builder";

    private readonly string _root;
    private readonly GameContent _content;

    public FaunaSeedTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_faunaseed_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
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

    private sealed class Host : IDisposable
    {
        public required SvGameServer Server { get; init; }
        public required SqliteWorldRepository Repo { get; init; }
        public required LoopbackServerTransport Transport { get; init; }
        public required LoopbackClientTransport Client { get; init; }

        public void Tick(double dt)
        {
            Server.Tick(dt);
            Client.Poll();
        }

        public void Dispose()
        {
            Server.Stop();
            Repo.Dispose();
            Transport.Dispose();
            Client.Dispose();
        }
    }

    /// <summary>A server on the save under <see cref="_root"/> (nothing of it loaded yet) and a real network join of the
    /// builder; the join is handled by the first tick.</summary>
    private Host StartAndJoin(bool starterShip = false)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "port"));
        var link = new LoopbackLink();
        var transport = new LoopbackServerTransport(link);
        var client = new LoopbackClientTransport(link);
        var config = new ServerConfig
        {
            WorldName = "port",
            Seed = 4242,
            StartPlanet = "jungle", // "many" fauna → a full roster with several land species
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = starterShip,
            PlaceSettlements = false,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = Builder }), DeliveryMode.ReliableOrdered);
        return new Host { Server = server, Repo = repo, Transport = transport, Client = client };
    }

    /// <summary>The first session paves the natural ground around the arrival spot: the top block of every plain dry column
    /// within <see cref="FloorRadius"/> becomes a block the builder placed, at the generator's own height. Then it saves
    /// and stops. Returns the paved columns with the height of their floor block.</summary>
    private Dictionary<(int X, int Z), int> PaveTheArrivalArea()
    {
        var floor = new Dictionary<(int X, int Z), int>();
        using var host = StartAndJoin();
        host.Tick(0.1);
        var server = host.Server;
        var at = server.Sessions.Values.Single().State.Position;
        var world = server.World;
        var sandstone = _content.GetBlock("sandstone")!.NumericId;
        int px = (int)Math.Floor(at.X), pz = (int)Math.Floor(at.Z);
        for (int dx = -FloorRadius; dx <= FloorRadius; dx++)
            for (int dz = -FloorRadius; dz <= FloorRadius; dz++)
            {
                int x = px + dx, z = pz + dz;
                int surface = world.Generator.SurfaceHeight(world.Planet, x, z);
                if (world.Generator.TryGetWaterSurface(world.Planet, x, z, out _, out _)
                    || world.GetBlock(new Vector3i(x, surface, z)).IsAir
                    || Enumerable.Range(surface + 1, 4).Any(y => !world.GetBlock(new Vector3i(x, y, z)).IsAir))
                {
                    continue; // a pond, a dug spot or a tree/plant on top: not a plain paving
                }

                world.SetBlock(new Vector3i(x, surface, z), sandstone, owner: Builder);
                floor[(x, z)] = surface;
            }

        server.SaveNow();
        return floor;
    }

    [Fact]
    public void AReloadedWorld_PutsNoWildGroundAnimal_OnAFloorThePlayerBuilt()
    {
        var floor = PaveTheArrivalArea();
        Assert.True(floor.Count > 2000, $"the paving should cover a good part of the ring ({floor.Count} columns)");
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using var host = StartAndJoin();
        var server = host.Server;
        var land = server.SpeciesRoster.Where(s => s.Habitat == CreatureHabitat.Land).Select(s => s.Id).ToHashSet();
        Assert.NotEmpty(land);

        // Each wild ground animal is checked in the tick it first appears, before the intruder sweep could hide a bad
        // placement. Fine ticks, so that no animal has walked onto the floor from a spot beside it yet. 20 s: the first
        // batch (ring slots on the floor get fliers, which floors never stop) and the running spawner's first land animals.
        var seen = new HashSet<string>();
        var onFloor = new List<string>();
        int landSeen = 0;
        for (int i = 0; i < 400; i++)
        {
            host.Tick(0.05);
            if (i == 0)
            {
                server.Sessions.Values.Single().State.AboardShip = false; // on foot: the running spawner fills in too
            }

            foreach (var c in server.Creatures)
            {
                if (c.IsCompanion || !land.Contains(c.SpeciesId) || !seen.Add(c.Id))
                {
                    continue;
                }

                landSeen++;
                var cell = c.Position.ToBlock();
                if (floor.TryGetValue((cell.X, cell.Z), out int top) && cell.Y == top + 1)
                {
                    onFloor.Add($"{c.Id} {c.SpeciesId} at {c.Position}");
                }
            }
        }

        Assert.False(server.FaunaSeedDueForTest(Builder), "the first batch has been placed");
        Assert.Contains(server.Creatures, c => !c.IsCompanion); // the world still comes alive around the arrival spot
        Assert.True(landSeen > 0, "ground animals appeared too — or the floor check below proves nothing");
        Assert.True(onFloor.Count == 0, "wild ground animals placed on the player's floor: " + string.Join("; ", onFloor));
    }

    [Fact]
    public void APlayerStillAboard_GetsTheFirstBatch_WithinItsTimeout()
    {
        // A new player joins aboard the starter ship and stays in the cockpit. The running spawner only serves players
        // on foot, so every animal here comes from the first batch — which must not wait for the player to step out.
        using var host = StartAndJoin(starterShip: true);
        var server = host.Server;
        host.Tick(0.1);
        var session = Assert.Single(server.Sessions.Values);
        Assert.True(session.State.AboardShip, "a new player starts in the ship");

        int ticks = 0;
        while (server.FaunaSeedDueForTest(Builder) && ticks++ < 80)
        {
            host.Tick(0.1);
        }

        Assert.False(server.FaunaSeedDueForTest(Builder), "placed within the 6 s timeout");
        Assert.True(session.State.AboardShip);
        Assert.Contains(server.Creatures, c => !c.IsCompanion);
    }
}
