using Spacecraft.Networking;
using Spacecraft.Networking.Messages;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.State;
using Spacecraft.Shared.World;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

public sealed class LandingZoneTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public LandingZoneTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_lz_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void Pvp_Preset_EnablesSpaceCombat()
    {
        var rules = ServerPresets.Get("pvp")!;
        Assert.Equal(SpaceCombatMode.Both, rules.SpaceCombat);
        Assert.Equal(ShipWeaponMode.PvpAllowed, rules.ShipWeapons);

        var peaceful = ServerPresets.Get("peaceful-creative")!;
        Assert.Equal(SpaceCombatMode.Off, peaceful.SpaceCombat);
        Assert.Equal(AlienActivity.Off, peaceful.PlanetEnemies);
    }

    [Fact]
    public void PersonalLandingZone_AssignedAndPersisted_OnJoin()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "lz"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var config = new ServerConfig { WorldName = "lz", Seed = 1, AutoSaveIntervalMinutes = 9999 };

        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Pilot" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        var zones = repo.ListLandingZones(server.ActiveLocationId); // keyed by body id, not planet type
        var z = Assert.Single(zones);
        Assert.Equal("Pilot", z.PlayerId);
        Assert.True(z.Protected); // default protection = StartZoneOnly
    }

    [Fact]
    public void ProtectedZone_BlocksForeignEditing()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "prot"));
        repo.Initialize();
        // Seed another player's protected landing zone at the origin.
        repo.SaveLandingZone(new LandingZone { PlayerId = "Alice", LocationId = "rocky", CenterX = 0, CenterZ = 0, Radius = 8, Protected = true });

        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        ActionRejected? rejected = null;
        client.PayloadReceived += p => { if (NetCodec.Decode(p) is ActionRejected r) rejected = r; };

        var config = new ServerConfig { WorldName = "prot", Seed = 1, AutoSaveIntervalMinutes = 9999 };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Bob" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        // Make Bob a normal player (admins bypass protection) and move him into Alice's zone.
        var bob = server.Sessions[1].State;
        bob.Role = PlayerRole.Player;
        bob.Position = new Vector3f(0.5f, 65.5f, 0.5f);

        var target = new Vector3i(0, 64, 0); // inside Alice's protected zone
        server.World.SetBlock(target, _content.GetBlock("stone")!.NumericId);

        client.Send(NetCodec.Encode(new MineBlockIntent { X = target.X, Y = target.Y, Z = target.Z }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.False(server.World.GetBlock(target).IsAir); // not mined
        Assert.NotNull(rejected);
        Assert.Equal("mine", rejected!.Action);
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
