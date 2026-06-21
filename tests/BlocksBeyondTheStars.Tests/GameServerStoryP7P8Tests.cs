using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// P8 (story-selection world option + density + QA telemetry) and P7 (NPC flavour lines + mission threading).
/// </summary>
public sealed class GameServerStoryP7P8Tests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public GameServerStoryP7P8Tests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_p7p8_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, Action<GameRules>? rules = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "p7p8_" + Guid.NewGuid().ToString("N")));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "rocky",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        rules?.Invoke(config.Rules);
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------- P8: density ----------------

    [Fact]
    public void Story_density_scales_the_progress_score()
    {
        Assert.True(_content.TryGetStory("vega_protocol", out var def));
        var state = new StoryState { Milestones = 20 };

        int sparse = StoryEngine.Progress(def, state, 0.65f);
        int normal = StoryEngine.Progress(def, state, 1f);
        int dense = StoryEngine.Progress(def, state, 1.5f);

        Assert.True(dense > normal && normal > sparse, $"expected dense>{normal}>sparse, got {dense}/{normal}/{sparse}");
    }

    [Fact]
    public void A_dense_story_reveals_more_beats_for_the_same_play()
    {
        var dense = Started(out var r1, rules => rules.StoryDensity = StoryDensity.Dense);
        var sparse = Started(out var r2, rules => rules.StoryDensity = StoryDensity.Sparse);
        using (r1)
        using (r2)
        {
            for (int i = 0; i < 12; i++)
            {
                dense.RecordStoryMilestoneForTest();
                sparse.RecordStoryMilestoneForTest();
            }

            Assert.True(dense.StorySnapshot.BeatsRevealed > sparse.StorySnapshot.BeatsRevealed,
                $"dense {dense.StorySnapshot.BeatsRevealed} should exceed sparse {sparse.StorySnapshot.BeatsRevealed}");
        }
    }

    // ---------------- P8: story selection (world option) ----------------

    [Fact]
    public void World_option_none_starts_the_save_in_sandbox()
    {
        var server = Started(out var repo, rules => rules.StoryId = "none");
        using (repo)
        {
            Assert.Equal("none", server.StorySnapshot.StoryId);
            server.RecordStoryMilestoneForTest(); // no story → no advance
            Assert.Equal(0, server.StorySnapshot.BeatsRevealed);
        }
    }

    [Fact]
    public void World_option_can_pick_a_specific_pack()
    {
        var server = Started(out var repo, rules => rules.StoryId = "vega_protocol");
        using (repo)
        {
            Assert.Equal("vega_protocol", server.StorySnapshot.StoryId);
        }
    }

    // ---------------- P8: QA telemetry ----------------

    [Fact]
    public void Admin_reveal_finale_jumps_straight_to_the_finale()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("QA");
            Assert.False(server.IsGuardianSystemRevealedForTest);

            server.AdminRevealFinale();

            Assert.True(server.IsGuardianSystemRevealedForTest);
            Assert.True(server.GalaxyHasGuardianSystemForTest);
        }
    }

    [Fact]
    public void Admin_advance_story_reveals_more_beats()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("QA");
            int before = server.StorySnapshot.BeatsRevealed;

            server.AdminAdvanceStory(40);

            Assert.True(server.StorySnapshot.BeatsRevealed > before);
        }
    }

    // ---------------- P7: knowledge level + flavour lines ----------------

    [Fact]
    public void World_knowledge_level_climbs_with_progress()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Settler");
            Assert.True(server.WorldKnowledgeLevel() < 4); // a fresh world is nowhere near the core

            server.AdminRevealFinale();
            Assert.Equal(4, server.WorldKnowledgeLevel()); // arc complete → the core is known
        }
    }

    [Fact]
    public void The_pack_loads_flavour_lines_and_mission_threads()
    {
        Assert.True(_content.TryGetStory("vega_protocol", out var def));
        Assert.NotEmpty(def.FlavourLines);
        Assert.NotEmpty(def.MissionThreads);
    }

    // ---------------- P7: mission threading ----------------

    [Fact]
    public void A_threaded_mission_awards_its_story_fragment_once()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Helper");
            int before = server.StorySnapshot.Fragments;

            server.TryThreadMissionForTest("mission.settlement.gather.title"); // matches the "settlement" thread
            Assert.Equal(before + 1, server.StorySnapshot.Fragments);

            server.TryThreadMissionForTest("mission.settlement.gather.title"); // deduped — no second award
            Assert.Equal(before + 1, server.StorySnapshot.Fragments);

            server.TryThreadMissionForTest("mission.depot.delivery.title"); // no thread matches → no award
            Assert.Equal(before + 1, server.StorySnapshot.Fragments);
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
