// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
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

/// <summary>The frontier (#1122) + the growing galaxy (#1123): outer systems tier up (richer rare veins,
/// star-map tag), and a world created with GalaxyGrowth appends a new system when the edge is jumped —
/// deterministically, persisted as a COUNT only, reproduced byte-identically on restart.</summary>
public sealed class FrontierGalaxyTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public FrontierGalaxyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_frontier_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void Generate_PrefixProperty_FirstNSystemsAreIdentical_AndGrownOnesSitOutward()
    {
        var desc = new WorldDescription { StarSystemCount = 8, SystemVariance = true, AsteroidBelts = true };
        var fixed8 = new UniverseGenerator(42, desc, _content).Generate();
        var grown9 = new UniverseGenerator(42, desc, _content).Generate(9);

        Assert.Equal(8, fixed8.Systems.Count);
        Assert.Equal(9, grown9.Systems.Count);
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(fixed8.Systems[i].Id, grown9.Systems[i].Id);
            Assert.Equal(fixed8.Systems[i].Name, grown9.Systems[i].Name);
            Assert.Equal(fixed8.Systems[i].MapX, grown9.Systems[i].MapX);
            Assert.Equal(fixed8.Systems[i].MapY, grown9.Systems[i].MapY);
            Assert.Equal(
                fixed8.Systems[i].Bodies.Select(b => (b.Id, b.PlanetType, b.Name)),
                grown9.Systems[i].Bodies.Select(b => (b.Id, b.PlanetType, b.Name)));
        }

        // The grown system lies OUTWARD of home — beyond the far-frontier threshold, and unique by name.
        var home = grown9.Systems[0];
        var grown = grown9.Systems[8];
        float dx = grown.MapX - home.MapX, dy = grown.MapY - home.MapY;
        Assert.True(Math.Sqrt(dx * dx + dy * dy) >= 700.0, "a grown system must sit in the frontier tier");
        Assert.DoesNotContain(grown9.Systems.Take(8), s => s.Name == grown.Name);

        // And repeated growth is deterministic: the same index always yields the same system.
        var again = new UniverseGenerator(42, desc, _content).Generate(9);
        Assert.Equal(grown.Name, again.Systems[8].Name);
        Assert.Equal(grown.MapX, again.Systems[8].MapX);
    }

    [Fact]
    public void FrontierTier_HomeIsZero_TiersAreSeedStable()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tier"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var server = new SvGameServer(new ServerConfig { WorldName = "tier", Seed = 7, AutoSaveIntervalMinutes = 9999 }, _content, st, repo);
        server.Start();

        Assert.Equal(0, server.FrontierTierForTest("sys0"));
        foreach (var sys in server.Galaxy.Systems)
        {
            int tier = server.FrontierTierForTest(sys.Id);
            Assert.InRange(tier, 0, 2);
            Assert.Equal(tier, server.FrontierTierForTest(sys.Id)); // pure — no hidden state
        }

        // Unknown location ids (station/ship interiors, tests) read as home turf.
        Assert.Equal(0, server.FrontierTierForTest("not-a-system"));
    }

    [Fact]
    public void GalaxyGrowth_EdgeJump_AppendsASystem_PersistsAcrossRestart()
    {
        string grownName;
        int countAfterGrowth;
        {
            using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "grow"));
            var link = new LoopbackLink();
            using var st = new LoopbackServerTransport(link);
            using var client = new LoopbackClientTransport(link);
            var config = new ServerConfig { WorldName = "grow", Seed = 42, AutoSaveIntervalMinutes = 9999 };
            config.World.GalaxyGrowth = true; // creation-time choice, baked into the save's metadata
            var server = new SvGameServer(config, _content, st, repo);
            server.Start();
            client.Connect("loopback", 0);
            client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Scout" }), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);

            int before = server.Galaxy.Systems.Count;
            string edge = server.Galaxy.Systems.First(s => server.IsEdgeSystemForTest(s.Id)).Id;
            Assert.True(server.TryGrowGalaxyForTest(server.Sessions[1], edge));

            countAfterGrowth = server.Galaxy.Systems.Count;
            Assert.Equal(before + 1, countAfterGrowth);
            Assert.Equal(1, server.Metadata.GalaxyGrownSystems);
            var grown = server.Galaxy.Systems[^1];
            grownName = grown.Name;
            Assert.StartsWith("sys", grown.Id);
            Assert.True(server.FrontierTierForTest(grown.Id) == 2, "grown systems appear in the frontier tier");

            // The new bodies' types were pinned right away (#468 freeze).
            Assert.All(grown.Bodies.Where(b => !string.IsNullOrEmpty(b.PlanetType)),
                b => Assert.True(server.Metadata.BodyPlanetTypes.ContainsKey(b.Id)));
            repo.Flush();
        }

        {
            using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "grow"));
            var st = new LoopbackServerTransport(new LoopbackLink());
            var server = new SvGameServer(new ServerConfig { WorldName = "grow", Seed = 42, AutoSaveIntervalMinutes = 9999 }, _content, st, repo);
            server.Start();

            // Only the COUNT persists — the restart regenerates the grown system byte-identically.
            Assert.Equal(countAfterGrowth, server.Galaxy.Systems.Count);
            Assert.Equal(grownName, server.Galaxy.Systems[^1].Name);
        }
    }

    [Fact]
    public void GalaxyGrowth_IsOffByDefault_AndLegacyMetadataStaysFixed()
    {
        // Old saves: a metadata blob without the field deserializes to false — the galaxy stays fixed.
        var legacy = System.Text.Json.JsonSerializer.Deserialize<WorldDescription>("{}")!;
        Assert.False(legacy.GalaxyGrowth);
        Assert.False(new ServerConfig().World.GalaxyGrowth); // and new worlds only grow when CHOSEN at creation

        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "fixed"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var server = new SvGameServer(new ServerConfig { WorldName = "fixed", Seed = 42, AutoSaveIntervalMinutes = 9999 }, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Scout" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        int before = server.Galaxy.Systems.Count;
        string edge = server.Galaxy.Systems.First(s => server.IsEdgeSystemForTest(s.Id)).Id;
        Assert.False(server.TryGrowGalaxyForTest(server.Sessions[1], edge));
        Assert.Equal(before, server.Galaxy.Systems.Count);
    }

    [Fact]
    public void GalaxyGrowth_AtTheSoftCap_TheFrontierGoesQuiet()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "cap"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        ServerMessage? quiet = null;
        client.PayloadReceived += p =>
        {
            if (NetCodec.Decode(p) is ServerMessage m && m.Text.Contains("frontier_quiet")) { quiet = m; }
        };

        var config = new ServerConfig { WorldName = "cap", Seed = 42, AutoSaveIntervalMinutes = 9999 };
        config.World.GalaxyGrowth = true;
        config.World.StarSystemCount = 48; // already at the soft cap
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Scout" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);

        string edge = server.Galaxy.Systems.First(s => server.IsEdgeSystemForTest(s.Id)).Id;
        Assert.False(server.TryGrowGalaxyForTest(server.Sessions[1], edge));
        client.Poll();
        Assert.NotNull(quiet);
    }

    [Fact]
    public void FrontierOreBoost_MakesRareVeinsRicher_LeavesStarterOresAlone()
    {
        // The lava profile carries several RareTier veins (uranium/platinum/tungsten/titanium/diamond).
        var planet = _content.GetPlanet("lava")!;
        Assert.Contains(planet.Ores, o => o.RareTier);   // GameContent derived the flag from the tool gate
        Assert.Contains(planet.Ores, o => !o.RareTier);

        var rareIds = planet.Ores.Where(o => o.RareTier)
            .Select(o => _content.GetBlock(o.Block)!.NumericId.Value).ToHashSet();
        var starterIds = planet.Ores.Where(o => !o.RareTier)
            .Select(o => _content.GetBlock(o.Block)!.NumericId.Value).ToHashSet();

        (int rare, int starter) Count(double boost)
        {
            var gen = new WorldGenerator(1234, _content);
            gen.SetWorldMode(WorldConstants.Circumference, cratered: false, landingPads: null,
                locationId: "sys5-p1", frontierOreBoost: boost);
            int rare = 0, starter = 0;
            for (int cx = 0; cx < 3; cx++)
            {
                for (int cy = 0; cy <= 1; cy++)
                {
                    for (int cz = 0; cz < 3; cz++)
                    {
                        var chunk = gen.Generate(planet, new ChunkCoord(cx, cy, cz));
                        foreach (var b in chunk.RawBlocks)
                        {
                            if (rareIds.Contains(b))
                            {
                                rare++;
                            }
                            else if (starterIds.Contains(b))
                            {
                                starter++;
                            }
                        }
                    }
                }
            }

            return (rare, starter);
        }

        var baseline = Count(1.0);
        var boosted = Count(1.6);
        Assert.True(boosted.rare > baseline.rare,
            $"boost must enrich rare veins (baseline {baseline.rare}, boosted {boosted.rare})");
        // First-hit-wins vein priority means a boosted rare vein may CLAIM cells a later starter vein
        // would have taken — but the boost must never mint additional starter ore.
        Assert.True(boosted.starter <= baseline.starter,
            $"boost must not add starter ore (baseline {baseline.starter}, boosted {boosted.starter})");
    }

    [Fact]
    public void FrontierDanger_RidesThePresetAndTheLiveEdit()
    {
        Assert.True(ServerPresets.Get("dangerous")!.FrontierDanger);
        Assert.False(ServerPresets.Get("family")!.FrontierDanger);
        Assert.False(ServerPresets.Get("coop-survival")!.FrontierDanger);

        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "rule"));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);

        ServerRules? rules = null;
        client.PayloadReceived += p => { if (NetCodec.Decode(p) is ServerRules r) { rules = r; } };

        var server = new SvGameServer(new ServerConfig { WorldName = "rule", Seed = 1, AutoSaveIntervalMinutes = 9999 }, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = "Admin" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        Assert.False(rules!.FrontierDanger);

        client.Send(NetCodec.Encode(new SetWorldRulesIntent { FrontierDanger = "On" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        Assert.True(rules!.FrontierDanger);
        Assert.True(server.Metadata.RulesOverride!.FrontierDanger); // persisted with the save's rules
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
