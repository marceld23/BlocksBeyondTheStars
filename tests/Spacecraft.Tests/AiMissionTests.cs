using Spacecraft.GameServer;
using Spacecraft.Networking.Transport;
using Spacecraft.Persistence;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Missions;
using Xunit;
using SvGameServer = Spacecraft.GameServer.GameServer;

namespace Spacecraft.Tests;

public sealed class AiMissionTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public AiMissionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacecraft_ai_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private sealed class FakeProvider : IAiMissionProvider
    {
        private readonly MissionPlan? _plan;
        public FakeProvider(MissionPlan? plan) => _plan = plan;
        public MissionPlan? Generate(string context) => _plan;
    }

    private (SvGameServer server, SqliteWorldRepository repo) Start(AiLevel level, IAiMissionProvider provider, string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = world, Seed = 1, AutoSaveIntervalMinutes = 9999, AiLevel = level };
        var server = new SvGameServer(config, _content, st, repo, logger: null, aiProvider: provider);
        server.Start();
        return (server, repo);
    }

    private static MissionPlan ValidPlan(int rewardCount = 5) => new()
    {
        Title = "AI Ore Run",
        Description = "Bring back iron ore.",
        Objectives = { new MissionObjective { Type = MissionObjectiveType.Mine, Target = "iron_ore", Required = 10 } },
        SuggestedRewards = { new ItemAmount("iron_plate", rewardCount) },
    };

    [Fact]
    public void Auto_PublishesValidAiMission()
    {
        var (server, repo) = Start(AiLevel.Auto, new FakeProvider(ValidPlan()), "auto");
        using (repo)
        {
            var (ok, _) = server.TryGenerateAiMission("players need iron");
            Assert.True(ok);
            var m = Assert.Single(repo.ListMissions());
            Assert.Equal(MissionSource.Admin, m.Source);
            Assert.True(m.Active);
        }
    }

    [Fact]
    public void Suggest_StoresInactiveDraft()
    {
        var (server, repo) = Start(AiLevel.Suggest, new FakeProvider(ValidPlan()), "suggest");
        using (repo)
        {
            var (ok, _) = server.TryGenerateAiMission("ctx");
            Assert.True(ok);
            Assert.False(repo.ListMissions().Single().Active); // draft, awaiting review
        }
    }

    [Fact]
    public void Off_DoesNotGenerate()
    {
        var (server, repo) = Start(AiLevel.Off, new FakeProvider(ValidPlan()), "off");
        using (repo)
        {
            var (ok, _) = server.TryGenerateAiMission("ctx");
            Assert.False(ok);
            Assert.Empty(repo.ListMissions());
        }
    }

    [Fact]
    public void UnavailableBackend_FallsBackGracefully()
    {
        var (server, repo) = Start(AiLevel.Auto, new FakeProvider(null), "down");
        using (repo)
        {
            var (ok, message) = server.TryGenerateAiMission("ctx");
            Assert.False(ok);
            Assert.Contains("unavailable", message);
            Assert.Empty(repo.ListMissions());
        }
    }

    [Fact]
    public void InvalidPlan_IsRejectedByValidation()
    {
        var plan = new MissionPlan
        {
            Title = "Bad",
            Objectives = { new MissionObjective { Type = MissionObjectiveType.Mine, Target = "ghost_block", Required = 1 } },
        };
        var (server, repo) = Start(AiLevel.Auto, new FakeProvider(plan), "invalid");
        using (repo)
        {
            var (ok, _) = server.TryGenerateAiMission("ctx");
            Assert.False(ok);
            Assert.Empty(repo.ListMissions());
        }
    }

    [Fact]
    public void RewardCount_IsClampedByConverter()
    {
        var def = MissionPlanConverter.ToDefinition(ValidPlan(rewardCount: 9999), "ai_x");
        Assert.Equal(MissionPlanConverter.MaxRewardCount, def.Rewards.Single().Count);
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
