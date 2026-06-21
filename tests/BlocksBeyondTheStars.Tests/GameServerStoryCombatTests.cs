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
/// P4 (combat-as-progress) — defeating a Guardian machine advances the shared story. End-to-end through the
/// real attack path, so it verifies the kill hook actually fires (not just the engine counter).
/// </summary>
public sealed class GameServerStoryCombatTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GameServerStoryCombatTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_storycombat_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, planet));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = planet,
            Seed = 4242,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start(); // default rules: Survival + PlanetEnemies Normal
        return server;
    }

    [Fact]
    public void Killing_a_planet_machine_advances_the_story_exactly_once()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Fighter");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            for (int i = 0; i < 10 && server.PlanetEnemies.Count == 0; i++)
            {
                server.Tick(6.0); // spawn a planet machine near the player
            }
            Assert.NotEmpty(server.PlanetEnemies);

            var enemy = server.PlanetEnemies[0];
            int killsBefore = server.StorySnapshot.Kills;
            int beatsBefore = server.StorySnapshot.BeatsRevealed;

            // Bare-fist hits until it's destroyed; re-glue to the (possibly moving) enemy and tick past any
            // melee cooldown between swings.
            for (int i = 0; i < 15 && server.PlanetEnemies.Any(e => e.Id == enemy.Id); i++)
            {
                var cur = server.PlanetEnemies.FirstOrDefault(e => e.Id == enemy.Id);
                if (cur is null)
                {
                    break;
                }

                p.State.Position = new Vector3f(cur.Position.X, cur.Position.Y, cur.Position.Z);
                server.AttackEntity("Fighter", enemy.Id);
                server.Tick(2.0);
            }

            Assert.DoesNotContain(server.PlanetEnemies, e => e.Id == enemy.Id); // the machine is destroyed
            Assert.Equal(killsBefore + 1, server.StorySnapshot.Kills);          // exactly one machine-kill recorded
            Assert.True(server.StorySnapshot.BeatsRevealed >= beatsBefore);     // progress did not regress
        }
    }

    [Fact]
    public void Mapping_a_new_star_system_records_a_milestone()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            int before = server.StorySnapshot.Milestones;
            server.AddLocalPlayer("Explorer"); // joining places them on the home body → its system is mapped
            Assert.True(server.StorySnapshot.Milestones > before,
                "mapping the start system on join should record a story milestone (P3)");
        }
    }

    [Fact]
    public void Defeating_machines_unlocks_personal_memories_in_order_then_stops()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Clone");
            Assert.Equal(0, server.PlayerMemoryCount("Clone"));

            Assert.True(server.GrantNextPlayerMemoryForTest("Clone"));
            Assert.Equal(1, server.PlayerMemoryCount("Clone"));
            Assert.True(server.GrantNextPlayerMemoryForTest("Clone"));
            Assert.Equal(2, server.PlayerMemoryCount("Clone"));

            // unlock the remaining authored memories; then further grants return false (all unlocked)
            int guard = 0;
            while (server.GrantNextPlayerMemoryForTest("Clone") && guard++ < 50)
            {
            }

            Assert.Equal(4, server.PlayerMemoryCount("Clone")); // 4 authored memories in the pack
            Assert.False(server.GrantNextPlayerMemoryForTest("Clone"));
        }
    }

    [Fact]
    public void Defeating_the_guardian_pacifies_the_galaxy_so_no_more_planet_machines_spawn()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hero");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.MarkGuardianDefeatedForTest(); // win the finale → pacify the galaxy
            Assert.True(server.StorySnapshot.Defeated);

            for (int i = 0; i < 10; i++)
            {
                server.Tick(6.0);
                p.State.Health = 100f;
            }

            Assert.Empty(server.PlanetEnemies); // machines no longer spawn once the Guardian is down
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
