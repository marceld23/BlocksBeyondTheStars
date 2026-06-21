using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// World-creation options: the CLI overrides parse into rules/description, the world bakes + owns its
/// rules across reloads (RulesOverride), creature abundance scales/zeroes the fauna, the exotic-worlds
/// frequency shapes the universe's planet mix, structure frequencies gate the stamps, and the live
/// admin edit applies + persists + is admin-gated.
/// </summary>
public sealed class WorldOptionsTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public WorldOptionsTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_wopt_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static LoopbackLink NewLink(out LoopbackLink link)
    {
        link = new LoopbackLink();
        return link;
    }

    private static void JoinAndDrain(SvGameServer server, LoopbackClientTransport client, string name)
    {
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    // ---- CLI parsing -----------------------------------------------------------------------

    [Fact]
    public void CommandLine_ParsesWorldOptionOverrides()
    {
        var config = new ServerConfig();
        config.ApplyCommandLine(new[]
        {
            "--creatures", "Extreme", "--planet-enemies", "Off", "--ufos", "Frequent",
            "--flora", "Frequent", "--ore", "Normal", "--settlements", "Off",
            "--planet-wrecks", "VeryRare", "--vaults", "Frequent", "--stations", "Normal",
            "--exotic", "Off", "--systems", "12", "--planets-min", "3", "--planets-max", "8",
            "--moons-max", "4", "--oxygen", "Slow", "--hunger", "false", "--hazards", "Hard",
            "--death-penalty", "None", "--planet-types", "corrupted=Frequent,ocean=Rare",
            "--start-planet", "lava",
        });

        Assert.Equal(AlienActivity.Extreme, config.Rules.CreatureAbundance);
        Assert.Equal(AlienActivity.Off, config.Rules.PlanetEnemies);
        Assert.Equal(AlienActivity.Frequent, config.Rules.AlienUfos);
        Assert.Equal(Frequency.Frequent, config.World.FloraDensity);
        Assert.Equal(Frequency.Normal, config.World.RareResources);
        Assert.Equal(Frequency.Off, config.World.Settlements);
        Assert.Equal(Frequency.VeryRare, config.World.PlanetWrecks);
        Assert.Equal(Frequency.Frequent, config.World.Vaults);
        Assert.Equal(Frequency.Normal, config.World.SpaceStations);
        Assert.Equal(Frequency.Off, config.World.ExoticWorlds);
        Assert.Equal(12, config.World.StarSystemCount);
        Assert.Equal(3, config.World.PlanetsPerSystemMin);
        Assert.Equal(8, config.World.PlanetsPerSystemMax);
        Assert.Equal(4, config.World.MoonsPerPlanetMax);
        Assert.Equal(OxygenConsumption.Slow, config.Rules.OxygenConsumption);
        Assert.False(config.Rules.Hunger);
        Assert.Equal(HazardLevel.Hard, config.Rules.EnvironmentalHazards);
        Assert.Equal(DeathPenalty.None, config.Rules.DeathPenalty);
        Assert.Equal(Frequency.Frequent, config.World.PlanetTypeFrequencies["corrupted"]);
        Assert.Equal(Frequency.Rare, config.World.PlanetTypeFrequencies["ocean"]);
        Assert.Equal("lava", config.StartPlanet);
    }

    // ---- The world owns its rules (RulesOverride bake + reload) -------------------------------

    [Fact]
    public void Rules_BakeIntoTheSave_AndSurviveARelaunchWithDefaultConfig()
    {
        var paths = new SaveGamePaths(_root, "owns");
        using (var repo = new SqliteWorldRepository(paths))
        {
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig { WorldName = "owns", Seed = 7, AutoSaveIntervalMinutes = 9999 };
            config.Rules.PlanetEnemies = AlienActivity.Off;       // chosen at creation
            config.Rules.CreatureAbundance = AlienActivity.Extreme;
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        using (var repo2 = new SqliteWorldRepository(paths))
        {
            var st2 = new LoopbackServerTransport(new LoopbackLink());
            var defaults = new ServerConfig { WorldName = "owns", Seed = 7, AutoSaveIntervalMinutes = 9999 };
            var server2 = new SvGameServer(defaults, _content, st2, repo2);
            server2.Start(); // a relaunch does NOT re-pass the creation options…

            Assert.Equal(AlienActivity.Off, defaults.Rules.PlanetEnemies);       // …the save restored them
            Assert.Equal(AlienActivity.Extreme, defaults.Rules.CreatureAbundance);
            server2.Stop();
        }
    }

    // ---- Creature abundance ---------------------------------------------------------------------

    [Fact]
    public void CreatureAbundance_Off_KeepsTheWorldLifeless()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "dead"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var config = new ServerConfig
        {
            WorldName = "dead",
            Seed = 123456,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            PlaceStarterShip = false,
        };
        config.Rules.CreatureAbundance = AlienActivity.Off;
        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Hermit"); // join-time seeding must respect the rule too

        for (int i = 0; i < 12; i++)
        {
            server.Tick(1.0);
        }

        Assert.Empty(server.Creatures);
    }

    [Fact]
    public void CreatureAbundance_IsLiveEditable_ByTheWorldAdmin_AndPersists()
    {
        var paths = new SaveGamePaths(_root, "live");
        using var repo = new SqliteWorldRepository(paths);
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var server = new SvGameServer(new ServerConfig { WorldName = "live", Seed = 5, AutoSaveIntervalMinutes = 9999, ViewDistanceChunks = 1 }, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Admin"); // the first player becomes the world admin

        client.Send(NetCodec.Encode(new SetWorldRulesIntent { CreatureAbundance = "Off", PlanetEnemies = "Extreme" }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        // Applied live + persisted into the save's rules.
        var meta = repo.LoadMetadata();
        Assert.NotNull(meta?.RulesOverride);
        Assert.Equal(AlienActivity.Off, meta!.RulesOverride!.CreatureAbundance);
        Assert.Equal(AlienActivity.Extreme, meta.RulesOverride.PlanetEnemies);
    }

    [Fact]
    public void SecondJoiner_IsNotWorldAdmin()
    {
        // The world-rules gate keys off IsAdmin; the founder gets WorldAdmin, later joiners stay Player.
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "roles"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var server = new SvGameServer(new ServerConfig { WorldName = "roles", Seed = 5, AutoSaveIntervalMinutes = 9999, ViewDistanceChunks = 1, MaxPlayers = 4 }, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Founder");

        var guest = server.AddLocalPlayer("Guest", "en");
        Assert.False(guest.State.IsAdmin);
        Assert.True(server.Sessions.Values.First(s => s.State.Name == "Founder").State.IsAdmin);
    }

    // ---- Exotic worlds shape the universe -----------------------------------------------------

    [Fact]
    public void ExoticWorlds_Off_RemovesExoticTypesFromTheGalaxy()
    {
        var desc = new WorldDescription { StarSystemCount = 12, ExoticWorlds = Frequency.Off };
        var galaxy = new UniverseGenerator(42, desc, _content).Generate();

        var exoticKeys = _content.Planets.Values.Where(p => p.Exotic).Select(p => p.Key).ToHashSet();
        Assert.NotEmpty(exoticKeys); // the data flags exist
        Assert.DoesNotContain(galaxy.AllBodies(), b => exoticKeys.Contains(b.PlanetType ?? string.Empty));
    }

    [Fact]
    public void ExoticWorlds_Frequent_YieldsMoreExoticPlanets_ThanNormal()
    {
        var exoticKeys = _content.Planets.Values.Where(p => p.Exotic).Select(p => p.Key).ToHashSet();
        int Count(Frequency f) => new UniverseGenerator(42, new WorldDescription { StarSystemCount = 16, ExoticWorlds = f }, _content)
            .Generate().AllBodies().Count(b => exoticKeys.Contains(b.PlanetType ?? string.Empty));

        Assert.True(Count(Frequency.Frequent) > Count(Frequency.Normal),
            "Frequent exotic worlds must produce more exotic planets than Normal.");
    }

    // ---- Worldgen factors ----------------------------------------------------------------------

    [Fact]
    public void FloraFactorZero_GeneratesABarrenSurface()
    {
        var planet = _content.GetPlanet("jungle")!;
        var lush = new WorldGenerator(99, _content);
        var barren = new WorldGenerator(99, _content);
        barren.SetWorldOptionFactors(floraFactor: 0.0, oreFactor: 1.0);

        bool HasFlora(WorldGenerator gen)
        {
            for (int cx = 0; cx < 3; cx++)
                for (int cz = 0; cz < 3; cz++)
                    for (int cy = 3; cy <= 5; cy++)
                    {
                        var chunk = gen.Generate(planet, new BlocksBeyondTheStars.Shared.World.ChunkCoord(cx, cy, cz));
                        for (int i = 0; i < chunk.RawBlocks.Length; i++)
                        {
                            var key = _content.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(chunk.RawBlocks[i]))?.Key;
                            if (key != null && (key.StartsWith("flora_", StringComparison.Ordinal) || key == "wood_log"))
                            {
                                return true;
                            }
                        }
                    }

            return false;
        }

        Assert.True(HasFlora(lush), "a jungle world at default factors should grow flora in the sampled area");
        Assert.False(HasFlora(barren), "flora factor 0 must generate a barren surface");
    }

    // ---- Structure frequency gates --------------------------------------------------------------

    [Fact]
    public void Settlements_Off_NeverStampOne()
    {
        // Find a seed whose jungle world HAS a settlement at Normal, then recreate it with Settlements=Off.
        for (long seed = 1; seed <= 60; seed++)
        {
            using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, $"s_{seed}"));
            var st = new LoopbackServerTransport(new LoopbackLink());
            var config = new ServerConfig
            {
                WorldName = $"s_{seed}",
                Seed = seed,
                StartPlanet = "jungle",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceWrecks = false,
            };
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();
            bool had = server.HasSettlement;
            server.Stop();
            if (!had)
            {
                continue;
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            using var repo2 = new SqliteWorldRepository(new SaveGamePaths(_root, $"s_{seed}_off"));
            var st2 = new LoopbackServerTransport(new LoopbackLink());
            var offConfig = new ServerConfig
            {
                WorldName = $"s_{seed}_off",
                Seed = seed,
                StartPlanet = "jungle",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceWrecks = false,
            };
            offConfig.World.Settlements = Frequency.Off;
            var server2 = new SvGameServer(offConfig, _content, st2, repo2);
            server2.Start();
            Assert.False(server2.HasSettlement, $"seed {seed}: Settlements=Off must suppress the settlement");
            server2.Stop();
            return;
        }

        throw new Xunit.Sdk.XunitException("No seed with a settlement found in 60 tries.");
    }
}
