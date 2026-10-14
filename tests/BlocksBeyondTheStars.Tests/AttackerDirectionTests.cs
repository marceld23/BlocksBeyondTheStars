// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// "Gegner finden" (player report 2026-10-14). #2564: every blow on a player records the real attacker — entity id,
/// position and a sequence number — and the next state update carries it, so the HUD's damage marker points at the
/// attacker instead of guessing the nearest hostile. #2565: a creature bites within the shared
/// <see cref="CreatureReach"/> rule, the reach the client's lunge and attack call use too.
/// </summary>
public sealed class AttackerDirectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_attacker_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();
    private int _worlds;

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { /* best effort */ }
    }

    /// <summary>Records every server send so a test can read the state updates.</summary>
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

    private SvGameServer Started(RecordingTransport transport)
    {
        string world = "attacker_" + (++_worlds); // every server its own save
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            World = { TerrainGeneration = 0 },
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A stone pad eight blocks above every natural top around the origin (x −10..12, z −4..4) with six blocks of
    /// air over it, the player standing on it at (0.5, pad + 1, 0.5). Returns the player and the pad's feet level.</summary>
    private (PlayerSession Player, int Feet) Pad(SvGameServer server)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        int top = int.MinValue;
        for (int x = -14; x <= 16; x += 2)
            for (int z = -8; z <= 8; z += 2)
            {
                top = System.Math.Max(top, SurfaceTopY(server, x, z));
            }

        int padY = top + 8;
        for (int x = -10; x <= 12; x++)
            for (int z = -4; z <= 4; z++)
            {
                server.World.SetBlock(new Vector3i(x, padY, z), stone);
                for (int y = padY + 1; y <= padY + 6; y++)
                {
                    server.World.SetBlock(new Vector3i(x, y, z), BlockId.Air);
                }
            }

        var p = server.AddLocalPlayer("Verena");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0.5f, padY + 1, 0.5f);
        return (p, padY + 1);
    }

    private static int SurfaceTopY(SvGameServer server, int x, int z)
    {
        for (int y = 200; y > -200; y--)
        {
            if (!server.World.GetBlock(new Vector3i(x, y, z)).IsAir)
            {
                return y;
            }
        }

        return 0;
    }

    /// <summary>The smallest ordinary walker of the world's roster (no giant, no lurking arachnid, no special body).</summary>
    private static CreatureSpecies Walker(SvGameServer server)
        => server.SpeciesRoster
            .Where(sp => CreatureMotion.ClassOf(sp) == MotionClass.Walker && !CreatureMotion.IsGiant(sp)
                && sp.BodyPlan == CreatureBodyPlan.Standard && !ArachnidRules.Lurks(sp))
            .OrderBy(sp => sp.Size)
            .First();

    /// <summary>An angry, wide-awake walker on the pad at x = <paramref name="x"/>: it bites anyone in reach.</summary>
    private static CombatEntity AngryAnimal(SvGameServer server, int feet, float x, out CreatureSpecies species)
    {
        species = Walker(server);
        string id = server.SpawnCreatureAtForTest(new Vector3f(x, feet, 0.5f), species.Id);
        var c = server.Creatures.First(e => e.Id == id);
        c.HullMax = 200f;
        c.Hull = 200f;
        c.DamagePerSecond = 20f;
        c.ProvokeTimer = 60.0;        // angry
        c.AwakeOverrideTimer = 60.0;  // and awake, whatever the time of day
        return c;
    }

    private static PlayerStateUpdate LastStateOf(RecordingTransport transport, PlayerSession p)
        => transport.Sent.Where(x => x.Conn == p.ConnectionId && x.Msg is PlayerStateUpdate)
            .Select(x => (PlayerStateUpdate)x.Msg)
            .Last();

    private static float Distance(Vector3f a, Vector3f b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return (float)System.Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    // ---------------- #2564: the real attacker ----------------

    [Fact]
    public void ACreatureBite_RecordsTheAnimal_AndTheStateUpdateCarriesIt()
    {
        var transport = new RecordingTransport();
        var server = Started(transport);
        var (p, feet) = Pad(server);
        Assert.Equal(0, p.LastHitSeq); // nothing has hit this player yet
        var c = AngryAnimal(server, feet, 2.5f, out _);

        float before = p.State.Health;
        transport.Sent.Clear();
        server.Tick(0.1);

        Assert.True(p.State.Health < before, "the angry animal in reach did not bite");
        Assert.Equal(c.Id, p.LastHitEntityId);
        Assert.True(p.LastHitSeq > 0);
        Assert.True(Distance(p.LastHitFrom, c.Position) < 1f, "the recorded spot is not where the animal bit from");

        // The hit marks the state dirty: the update goes out at the end of the same tick, attacker and all.
        var u = LastStateOf(transport, p);
        Assert.Equal(c.Id, u.LastHitEntityId);
        Assert.Equal(p.LastHitSeq, u.LastHitSeq);
        Assert.Equal(p.LastHitFrom.X, u.LastHitFromX, 3);
        Assert.Equal(p.LastHitFrom.Y, u.LastHitFromY, 3);
        Assert.Equal(p.LastHitFrom.Z, u.LastHitFromZ, 3);
    }

    [Fact]
    public void EveryBlow_MovesTheSequenceOn_SoTheClientSeesAFreshHit()
    {
        var transport = new RecordingTransport();
        var server = Started(transport);
        var (p, feet) = Pad(server);
        AngryAnimal(server, feet, 2.5f, out _);

        server.Tick(0.1);
        int first = p.LastHitSeq;
        server.Tick(0.1);
        Assert.True(p.LastHitSeq > first, "a second bite did not move the sequence on");
        Assert.Equal(p.LastHitSeq, LastStateOf(transport, p).LastHitSeq);
    }

    [Fact]
    public void ADroneHit_RecordsTheMachine_AndTheStateUpdateCarriesIt()
    {
        var transport = new RecordingTransport();
        var server = Started(transport);
        var (p, feet) = Pad(server);
        server.SpawnPlanetEnemyAtForTest(new Vector3f(4.5f, feet + 3f, 0.5f), CombatEntityKind.ScanDrone, damagePerSecond: 20f);
        var drone = server.PlanetEnemies.Last();

        float before = p.State.Health;
        transport.Sent.Clear();
        server.Tick(0.1);

        Assert.True(p.State.Health < before, "the scan drone in range did not hurt the player");
        Assert.Equal(drone.Id, p.LastHitEntityId);
        Assert.True(p.LastHitFrom.Y > feet + 1f, "the drone hovers above — the recorded spot must say so");
        Assert.True(Distance(p.LastHitFrom, drone.Position) < 1f, "the recorded spot is not where the drone fired from");

        var u = LastStateOf(transport, p);
        Assert.Equal(drone.Id, u.LastHitEntityId);
        Assert.Equal(p.LastHitSeq, u.LastHitSeq);
        Assert.True(u.LastHitSeq > 0);
        Assert.Equal(p.LastHitFrom.Y, u.LastHitFromY, 3);
    }

    // ---------------- #2565: one bite reach for server and client ----------------

    [Fact]
    public void BiteRange_GrowsByHalfABlockPerSizePastTwo()
    {
        Assert.Equal(4f, CreatureReach.BiteRange(0.5f));
        Assert.Equal(4f, CreatureReach.BiteRange(2f));
        Assert.Equal(4.5f, CreatureReach.BiteRange(3f));
        Assert.Equal(5f, CreatureReach.BiteRange(4f));
        Assert.Equal(6f, CreatureReach.BiteRange(6f)); // a size-6 giant: exactly the player's melee reach
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(2.9f)]
    public void TheServerBites_WithinTheSharedReach_AndNotBeyondIt(float size)
    {
        float reach = CreatureReach.BiteRange(size);

        // Just inside: one short tick (the hunter barely moves) and the bite lands.
        var server = Started(new RecordingTransport());
        var (p, feet) = Pad(server);
        var c = AngryAnimal(server, feet, 0.5f + reach - 0.25f, out var species);
        species.Size = size;
        float before = p.State.Health;
        server.Tick(0.02);
        Assert.True(p.State.Health < before, $"a size-{size} animal {reach - 0.25f:0.00} blocks away did not bite");
        Assert.Equal(c.Id, p.LastHitEntityId);

        // Just outside: no bite.
        var far = Started(new RecordingTransport());
        var (q, farFeet) = Pad(far);
        var d = AngryAnimal(far, farFeet, 0.5f + reach + 0.35f, out var farSpecies);
        farSpecies.Size = size;
        before = q.State.Health;
        far.Tick(0.02);
        Assert.Equal(before, q.State.Health);
        Assert.NotEqual(d.Id, q.LastHitEntityId);
    }

    [Fact]
    public void TheCreatureBroadcast_CarriesTheSpeciesSize_TheReachIsMeasuredBy()
    {
        var server = Started(new RecordingTransport());
        var (_, feet) = Pad(server);
        var c = AngryAnimal(server, feet, 3.5f, out var species);

        var net = server.NetCreatureForTest(c.Id);
        Assert.Equal(species.Size, net.SpeciesSize);
        Assert.Equal(species.Size * c.SizeScale, net.Size, 4); // the drawn size keeps the individual's variance
    }
}
