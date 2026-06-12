using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Stamping crashed-ship wrecks into the world: a rare, deterministic surface feature that places a
/// decayed hull with loot/module markers, left scavengeable (not protected). Deterministic per seed.
/// </summary>
public sealed class WreckStampTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WreckStampTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_wreckstamp_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, planet + "_" + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = planet + "_" + seed, Seed = seed, StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false,
            PlaceSettlements = false, PlaceWrecks = true,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private SvGameServer StartedWithWreck(string planet, out SqliteWorldRepository repo)
    {
        for (long seed = 1; seed <= 60; seed++)
        {
            var server = Started(planet, seed, out repo);
            if (server.HasWreck)
            {
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException($"No wreck found on '{planet}' across 60 seeds.");
    }

    [Fact]
    public void Wreck_StampsWithName_AndLootMarkers()
    {
        var server = StartedWithWreck("rocky", out var repo);
        using (repo)
        {
            Assert.True(server.HasWreck);
            Assert.False(string.IsNullOrWhiteSpace(server.WreckName));
            Assert.Contains(server.WreckMarkers, m => m.Type == "loot");
            Assert.Contains(server.WreckMarkers, m => m.Type == "module");
        }
    }

    [Fact]
    public void Wreck_StampsRealBlocksIntoTheWorld()
    {
        var server = StartedWithWreck("rocky", out var repo);
        using (repo)
        {
            var marker = server.WreckMarkers.First();
            var basePos = new Vector3i((int)marker.Pos.X, (int)marker.Pos.Y, (int)marker.Pos.Z);

            bool solidNearby = false;
            for (int dx = -5; dx <= 5 && !solidNearby; dx++)
            for (int dy = -1; dy <= 5 && !solidNearby; dy++)
            for (int dz = -5; dz <= 5 && !solidNearby; dz++)
            {
                if (!server.World.GetBlock(new Vector3i(basePos.X + dx, basePos.Y + dy, basePos.Z + dz)).IsAir)
                {
                    solidNearby = true;
                }
            }

            Assert.True(solidNearby, "A stamped wreck should place solid blocks in the world.");
        }
    }

    [Fact]
    public void Wreck_IsNotProtected_CanBeMined()
    {
        var server = StartedWithWreck("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Scavenger");
            // Find a solid wreck cell near a marker and confirm it is NOT settlement/ship-protected.
            var marker = server.WreckMarkers.First();
            var basePos = new Vector3i((int)marker.Pos.X, (int)marker.Pos.Y, (int)marker.Pos.Z);

            Vector3i? solid = null;
            for (int dx = -5; dx <= 5 && solid is null; dx++)
            for (int dy = -1; dy <= 5 && solid is null; dy++)
            for (int dz = -5; dz <= 5 && solid is null; dz++)
            {
                var c = new Vector3i(basePos.X + dx, basePos.Y + dy, basePos.Z + dz);
                if (!server.World.GetBlock(c).IsAir)
                {
                    solid = c;
                }
            }

            Assert.NotNull(solid);
            Assert.False(server.IsSettlementBlock(solid!.Value)); // wrecks aren't settlement-protected
            Assert.False(server.IsProtectedShipBlock(solid.Value.X, solid.Value.Y, solid.Value.Z));
        }
    }

    [Fact]
    public void Stamp_IsDeterministic_ForSameSeed()
    {
        var a = Started("rocky", 12, out var repoA);
        bool hadA = a.HasWreck;
        string nameA = a.WreckName;
        repoA.Dispose();

        var b = Started("rocky", 12, out var repoB);
        using (repoB)
        {
            Assert.Equal(hadA, b.HasWreck);
            Assert.Equal(nameA, b.WreckName);
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
