// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The hidden star map (#3997): the whole galaxy at creation, each rolled system on a player's map only once it is
/// charted — by entering a neighbour, a radar array, a named place, an ally — and never a dead end.</summary>
public sealed class HiddenStarMapTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public HiddenStarMapTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_chart_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // a SQLite handle may linger on Windows — the temp folder is cleaned up eventually
        }
    }

    private SvGameServer NewServer(string tag, out SqliteWorldRepository repo, bool hidden = true, int systems = 36, long seed = 42)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var config = new ServerConfig { WorldName = tag, Seed = seed, AutoSaveIntervalMinutes = 9999 };
        if (hidden)
        {
            config.ApplyNewWorldDefaults();
        }

        config.World.StarSystemCount = systems;
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static string HomeSystem(SvGameServer server) => server.Galaxy.FindBody(server.ActiveLocationId)!.SystemId;

    private static List<string> Rolled(SvGameServer server, PlayerSession s)
        => server.StarMapSystemIdsForTest(s).Where(StarSystemIds.IsRolled).ToList();

    [Fact]
    public void NewWorld_ShowsHomeAndItsNeighbours_TheRestIsHidden()
    {
        var server = NewServer("start", out var repo);
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            string home = HomeSystem(server);
            var visible = Rolled(server, alice);

            Assert.Equal(36, server.Galaxy.Systems.Count(s => s.IsRolled)); // the whole galaxy exists from the start
            Assert.Contains(home, visible);
            Assert.InRange(visible.Count, 1 + StarChartRules.HomeMinimum, 1 + 4);
            Assert.Equal(visible.Count - 1, alice.State.ChartedSystems.Count);

            // The charted ones are the nearest to home.
            var homeSys = server.Galaxy.Systems.First(s => s.Id == home);
            var nearest = StarChartRules.Nearest(server.Galaxy.Systems.Where(s => s.IsRolled && s.Id != home), homeSys.MapX, homeSys.MapY, visible.Count - 1);
            Assert.Equal(nearest.Select(s => s.Id).OrderBy(x => x), alice.State.ChartedSystems.OrderBy(x => x));
        }
    }

    [Fact]
    public void FirstEntry_ChartsOneToFourNeighbours_ASecondEntryNothing()
    {
        var server = NewServer("entry", out var repo);
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            string target = alice.State.ChartedSystems.First();
            int before = Rolled(server, alice).Count;

            server.EnterSystemForTest(alice, target);
            int after = Rolled(server, alice).Count;
            Assert.Equal(StarChartRules.NeighbourCount(42, target, isHome: false), after - before); // the nearest uncharted ones

            server.EnterSystemForTest(alice, target);
            Assert.Equal(after, Rolled(server, alice).Count);
        }
    }

    [Fact]
    public void HiddenSystem_IsNoTravelTarget_UntilCharted()
    {
        var server = NewServer("travel", out var repo);
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            server.Ship.Modules.Add("jump_generator");
            var hidden = server.Galaxy.Systems.First(s => s.IsRolled && server.ChartHiddenForTest(alice, s.Id));
            var body = hidden.Bodies.First(b => b.Kind == CelestialKind.Planet);

            string home = alice.CurrentLocationId;
            server.Travel(alice.State.PlayerId, body.Id);
            Assert.Equal(home, alice.CurrentLocationId); // refused: no such destination on Alice's chart
            Assert.DoesNotContain(hidden.Id, server.StarMapSystemIdsForTest(alice));

            server.ChartNamedPlaceForTest(alice, body.Id); // e.g. a story lead names the world
            Assert.False(server.ChartHiddenForTest(alice, hidden.Id));
            Assert.Contains(hidden.Id, server.StarMapSystemIdsForTest(alice));

            server.Travel(alice.State.PlayerId, body.Id);
            Assert.Equal(body.Id, alice.CurrentLocationId);
        }
    }

    [Theory]
    [InlineData(24, 3)]
    [InlineData(36, 7)]
    [InlineData(48, 11)]
    [InlineData(64, 42)]
    [InlineData(64, 1234)]
    public void PlayingOn_ChartsTheWholeGalaxy(int systems, long seed)
    {
        var server = NewServer($"all{systems}_{seed}", out var repo, systems: systems, seed: seed);
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            for (int step = 0; step < systems * 2; step++)
            {
                var next = server.Galaxy.Systems.FirstOrDefault(s => s.IsRolled && !alice.State.KnownSystems.Contains(s.Id)
                                                                     && !server.ChartHiddenForTest(alice, s.Id));
                if (next is null)
                {
                    break;
                }

                server.EnterSystemForTest(alice, next.Id);
            }

            // Every system was reachable — the never-stuck rule fills any gap.
            Assert.All(server.Galaxy.Systems.Where(s => s.IsRolled), s => Assert.False(server.ChartHiddenForTest(alice, s.Id), s.Id));
            Assert.Equal(systems, server.Galaxy.Systems.Count(s => s.IsRolled && alice.State.KnownSystems.Contains(s.Id)));
        }
    }

    [Fact]
    public void RadarArray_ChartsTwoMore_OnEveryJump()
    {
        var server = NewServer("radar", out var repo);
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            string target = alice.State.ChartedSystems.First();
            server.EnterSystemForTest(alice, target);
            int before = Rolled(server, alice).Count;

            server.Ship.Modules.Add("radar_array");
            server.EnterSystemForTest(alice, target); // a known system — the radar still finds two more

            Assert.Equal(before + StarChartRules.RadarBonus, Rolled(server, alice).Count);
        }
    }

    [Fact]
    public void Allies_ShareTheirChart_WhileAllied()
    {
        var server = NewServer("allies", out var repo);
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            string target = alice.State.ChartedSystems.First();
            server.EnterSystemForTest(alice, target);
            var aliceOnly = alice.State.ChartedSystems.Concat(alice.State.KnownSystems)
                .Where(id => StarSystemIds.IsRolled(id) && server.ChartHiddenForTest(bob, id)).ToList();
            Assert.NotEmpty(aliceOnly);

            server.RequestAlliance("Alice", "Bob");
            server.RespondAlliance("Bob", "Alice", accept: true);
            Assert.All(aliceOnly, id => Assert.False(server.ChartHiddenForTest(bob, id), id));

            // Alice goes offline: her save still counts for Bob.
            server.DisconnectLocalPlayerForTest(alice.State.PlayerId);
            Assert.All(aliceOnly, id => Assert.False(server.ChartHiddenForTest(bob, id), id));

            server.DropOfflineChartsForTest();
            Assert.All(aliceOnly, id => Assert.False(server.ChartHiddenForTest(bob, id), id)); // read back from the save

            server.DissolveAlliance("Bob", "Alice");
            Assert.All(aliceOnly, id => Assert.True(server.ChartHiddenForTest(bob, id), id));
        }
    }

    [Fact]
    public void Sol_JoinsAfterEightSystems_ForThatPlayerOnly()
    {
        var server = NewServer("sol", out var repo, systems: 24);
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            var bob = server.AddLocalPlayer("Bob");
            Assert.False(server.GalaxyHasSystemForTest("sol"));

            for (int i = 0; i < 12 && alice.State.KnownSystems.Count(StarSystemIds.IsRolled) < 8; i++)
            {
                var next = server.Galaxy.Systems.First(s => s.IsRolled && !alice.State.KnownSystems.Contains(s.Id) && !server.ChartHiddenForTest(alice, s.Id));
                server.EnterSystemForTest(alice, next.Id);
            }

            Assert.True(server.GalaxyHasSystemForTest("sol"));
            Assert.Contains("sol", server.StarMapSystemIdsForTest(alice));
            Assert.DoesNotContain("sol", server.StarMapSystemIdsForTest(bob)); // Bob has not flown that far
        }
    }

    [Fact]
    public void OpenWorld_StaysOpen_AndNeverGetsSolBySystems()
    {
        var server = NewServer("open", out var repo, hidden: false, systems: 12);
        using (repo)
        {
            var alice = server.AddLocalPlayer("Alice");
            Assert.Equal(12, Rolled(server, alice).Count);
            Assert.Empty(alice.State.ChartedSystems);

            foreach (var sys in server.Galaxy.Systems.Where(s => s.IsRolled).Take(9).ToList())
            {
                server.EnterSystemForTest(alice, sys.Id);
            }

            Assert.False(server.GalaxyHasSystemForTest("sol"));
            Assert.Empty(alice.State.ChartedSystems);
        }
    }

    [Fact]
    public void HiddenWorld_NeverGrows()
    {
        var server = NewServer("nogrow", out var repo);
        using (repo)
        {
            server.Metadata.Description.GalaxyGrowth = true; // even with the growth flag
            var alice = server.AddLocalPlayer("Alice");
            string edge = server.Galaxy.Systems.First(s => server.IsEdgeSystemForTest(s.Id)).Id;
            Assert.False(server.TryGrowGalaxyForTest(alice, edge));
        }
    }

    [Fact]
    public void Chart_SurvivesARestart()
    {
        HashSet<string> charted;
        {
            var server = NewServer("persist", out var repo);
            using (repo)
            {
                var alice = server.AddLocalPlayer("Alice");
                server.EnterSystemForTest(alice, alice.State.ChartedSystems.First());
                charted = new HashSet<string>(alice.State.ChartedSystems);
                server.DisconnectLocalPlayerForTest(alice.State.PlayerId);
                repo.Flush();
            }
        }

        {
            var server = NewServer("persist", out var repo);
            using (repo)
            {
                var alice = server.AddLocalPlayer("Alice");
                Assert.True(charted.IsSubsetOf(alice.State.ChartedSystems));
                Assert.True(server.Metadata.Description.HiddenStarMap);
            }
        }
    }

    [Fact]
    public void NewWorldDefaults_ComeFromStartup_NotFromAPlainConfig()
    {
        Assert.False(new ServerConfig().World.HiddenStarMap); // hundreds of tests rely on the open 12-system galaxy
        Assert.Equal(12, new ServerConfig().World.StarSystemCount);

        var fromCode = ServerConfig.LoadForStartup(new[] { "--no-config", "true" }, Path.Combine(_root, "none", "server.json"));
        Assert.True(fromCode.World.HiddenStarMap);
        Assert.Equal(WorldDescription.NewWorldStarSystemCount, fromCode.World.StarSystemCount);

        string fresh = Path.Combine(_root, "fresh", "server.json");
        Directory.CreateDirectory(Path.GetDirectoryName(fresh)!);
        Assert.True(ServerConfig.Load(fresh).World.HiddenStarMap); // a first server.json is written with it
        Assert.True(ServerConfig.Load(fresh).World.HiddenStarMap); // and read back

        Assert.False(System.Text.Json.JsonSerializer.Deserialize<WorldDescription>("{}")!.HiddenStarMap); // old saves stay open
    }

    [Fact]
    public void BiggestGalaxy_HasUniqueNames_AndStaysSmallerThanTheOldHugeOne()
    {
        var normal = new WorldDescription { StarSystemCount = 64, SystemVariance = true, AsteroidBelts = true, HiddenStarMap = true };
        var huge = new WorldDescription
        {
            StarSystemCount = 32,
            PlanetsPerSystemMin = 3,
            PlanetsPerSystemMax = 10,
            MoonsPerPlanetMax = 4,
            SystemVariance = true,
            AsteroidBelts = true,
        };

        var big = new UniverseGenerator(42, normal, _content).Generate();
        var old = new UniverseGenerator(42, huge, _content).Generate();

        Assert.Equal(64, big.Systems.Count);
        Assert.Equal(big.Systems.Count, big.Systems.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(big.AllBodies().Count() <= old.AllBodies().Count() * 1.5,
            $"64 normal systems hold {big.AllBodies().Count()} bodies, the old 32 huge ones {old.AllBodies().Count()}");
    }
}
