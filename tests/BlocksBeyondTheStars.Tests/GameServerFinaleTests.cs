using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// P6 finale server backbone: the arc completing reveals the Guardian system; the core is hacked open (a
/// channel) and then argued into shutdown via the dialogue duel — never destroyed by weapons. Winning the duel
/// pacifies the galaxy (the same one-way flag as <see cref="SvGameServer.MarkGuardianDefeatedForTest"/>).
/// </summary>
public sealed class GameServerFinaleTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GameServerFinaleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_finale_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "rocky"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "rocky", Seed = 4242, StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Drives the shared story to full completion (every beat revealed) via milestones.</summary>
    private static void CompleteTheArc(SvGameServer server)
    {
        for (int i = 0; i < 300 && !server.IsGuardianSystemRevealedForTest; i++)
        {
            server.RecordStoryMilestoneForTest();
        }
    }

    [Fact]
    public void Completing_the_arc_reveals_the_guardian_system_once()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            Assert.False(server.IsGuardianSystemRevealedForTest);

            CompleteTheArc(server);

            Assert.True(server.IsGuardianSystemRevealedForTest);
            Assert.True(server.StorySnapshot.BeatsRevealed >= 13); // the whole 13-beat arc is spoken
        }
    }

    [Fact]
    public void The_core_cannot_be_hacked_before_the_system_is_revealed()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");

            for (int i = 0; i < 20; i++)
            {
                server.CoreHackTickForTest("Pilot");
            }

            Assert.False(server.IsCoreHackedForTest); // no hack until the finale is in reach
        }
    }

    [Fact]
    public void Channelling_the_hack_opens_the_core_then_the_duel_can_begin()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            CompleteTheArc(server);

            for (int i = 0; i < 20 && !server.IsCoreHackedForTest; i++)
            {
                server.CoreHackTickForTest("Pilot");
            }

            Assert.True(server.IsCoreHackedForTest);
            Assert.Equal(0, server.DuelNodeForTest);          // the duel opened at the first node
            Assert.False(server.StorySnapshot.Defeated);      // not won merely by hacking
        }
    }

    [Fact]
    public void A_wrong_rebuttal_does_not_advance_the_duel_and_cannot_lose_it()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            CompleteTheArc(server);
            for (int i = 0; i < 20 && !server.IsCoreHackedForTest; i++)
            {
                server.CoreHackTickForTest("Pilot");
            }
            Assert.True(server.IsCoreHackedForTest);

            // node 0's correct rebuttal is index 0; pick a wrong one repeatedly.
            server.CoreDialogueChoiceForTest("Pilot", 1);
            server.CoreDialogueChoiceForTest("Pilot", 2);

            Assert.Equal(0, server.DuelNodeForTest);     // still stuck on the first node
            Assert.False(server.StorySnapshot.Defeated); // the duel can stall but never be lost
        }
    }

    [Fact]
    public void Winning_the_argument_duel_shuts_down_the_core_and_pacifies_the_galaxy()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            CompleteTheArc(server);
            for (int i = 0; i < 20 && !server.IsCoreHackedForTest; i++)
            {
                server.CoreHackTickForTest("Pilot");
            }
            Assert.True(server.IsCoreHackedForTest);

            // The authored correct (contradiction) rebuttals, in order: node0=0, node1=1, node2=2, node3=0.
            server.CoreDialogueChoiceForTest("Pilot", 0);
            Assert.Equal(1, server.DuelNodeForTest);
            server.CoreDialogueChoiceForTest("Pilot", 1);
            Assert.Equal(2, server.DuelNodeForTest);
            server.CoreDialogueChoiceForTest("Pilot", 2);
            Assert.Equal(3, server.DuelNodeForTest);

            Assert.False(server.StorySnapshot.Defeated); // not yet — one node to go
            server.CoreDialogueChoiceForTest("Pilot", 0);

            Assert.True(server.StorySnapshot.Defeated);  // the core is argued into shutdown (pacification)
        }
    }

    [Fact]
    public void Revealing_the_finale_adds_a_landable_guardian_system_to_the_galaxy()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            Assert.False(server.GalaxyHasGuardianSystemForTest); // hidden until the arc completes

            CompleteTheArc(server);

            Assert.True(server.GalaxyHasGuardianSystemForTest);   // now a jump target on the star map
            Assert.True(server.GuardianCoreIsLandableForTest);    // with a landable core body to set down on
        }
    }

    [Fact]
    public void A_death_in_the_guardian_system_respawns_at_the_pre_finale_world()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            string priorWorld = pilot.CurrentLocationId; // the world they launched into the finale from
            CompleteTheArc(server);

            // Simulate having jumped INTO the finale from priorWorld.
            server.RecordFinaleReturnForTest("Pilot", priorWorld);

            // Dying with the ship parked at the core would normally respawn there — the rule redirects home.
            Assert.Equal(priorWorld, server.ResolveRespawnHomeForTest("Pilot", SvGameServer.GuardianCoreBodyId));

            // The record is consumed (one-shot): a second death there falls back to the ship's location.
            Assert.Equal(SvGameServer.GuardianCoreBodyId,
                server.ResolveRespawnHomeForTest("Pilot", SvGameServer.GuardianCoreBodyId));

            // Outside the finale the rule never fires — a normal death respawns at the ship as usual.
            server.RecordFinaleReturnForTest("Pilot", priorWorld);
            Assert.Equal(priorWorld, server.ResolveRespawnHomeForTest("Pilot", priorWorld));
        }
    }

    [Fact]
    public void A_choice_before_the_hack_does_nothing()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            CompleteTheArc(server);

            server.CoreDialogueChoiceForTest("Pilot", 0); // duel not open yet (not hacked)

            Assert.False(server.IsCoreHackedForTest);
            Assert.False(server.StorySnapshot.Defeated);
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
