// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2971 (Justus: "a tablet with the enemies — damage, health, abilities, names"): the hostiles come from
/// <c>data/enemies.json</c>, the server builds them from it, and meeting one fills its page in the Codex.
/// </summary>
public sealed class EnemyCodexTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;
    private readonly List<SqliteWorldRepository> _repos = new();

    public EnemyCodexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_enemies_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(string world, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 9,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
            PlaceStarterShip = false,
        };
        config.Rules.PlanetEnemies = AlienActivity.Off;
        config.Rules.Bandits = AlienActivity.Off;
        config.Rules.CreatureAbundance = AlienActivity.Off;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void TheShippedFile_HoldsTheBalanceThatShippedBeforeIt()
    {
        // The numbers moved from the server's constants into data/enemies.json without a change: the file must equal the
        // built-in defaults, tier by tier. A deliberate rebalance changes both, here and in EnemyCatalog.
        var defaults = EnemyCatalog.Defaults();
        Assert.Equal(defaults.Select(d => d.Key), _content.Enemies.Select(e => e.Key));
        foreach (var expected in defaults)
        {
            var actual = _content.Enemy(expected.Key);
            Assert.Equal(expected.Where, actual.Where);
            Assert.Equal(expected.Tiers.Keys.OrderBy(k => k), actual.Tiers.Keys.OrderBy(k => k));
            foreach (var (name, tier) in expected.Tiers)
            {
                var got = actual.Tiers[name];
                Assert.Equal(tier.Hull, got.Hull);
                Assert.Equal(tier.HullMax, got.HullMax);
                Assert.Equal(tier.DamagePerSecond, got.DamagePerSecond);
                Assert.Equal(tier.Loot.Select(l => (l.Item, l.Count)), got.Loot.Select(l => (l.Item, l.Count)));
            }
        }
    }

    [Fact]
    public void EveryEnemy_HasANameADescriptionAndATip_InEnglishAndGerman()
    {
        foreach (var locale in new[] { GameLocale.English, GameLocale.German })
        {
            var l = _content.CreateLocalizer(locale);
            foreach (var e in _content.Enemies)
            {
                foreach (var key in new[] { e.NameKey, e.DescriptionKey, e.TipKey })
                {
                    Assert.NotEqual(key, l.Get(key));
                }
            }
        }
    }

    [Fact]
    public void ARobber_IsBuiltFromTheData_AndMeetingHim_FillsTheCodexOnce()
    {
        var transport = new RecordingTransport();
        var server = Started("enemies_meet", transport);
        var p = server.AddLocalPlayer("Justus");
        p.State.GodMode = true;
        p.State.AboardShip = false;
        var at = p.State.Position;

        // Far away: not met.
        server.SpawnBanditAtForTest(new Vector3f(at.X + 200f, at.Y, at.Z), "Justus", gunner: true);
        var robber = Assert.Single(server.Bandits);
        Assert.Equal(EnemyCatalog.BanditGunner, robber.EnemyKey);
        Assert.Equal(_content.Enemy(EnemyCatalog.BanditGunner).Tier(EnemyTiers.Normal).DamagePerSecond, robber.DamagePerSecond);
        server.TickEnemyEncountersForTest();
        Assert.Empty(p.State.EnemiesMet);

        // Within a gun's reach: met, and the client hears it once.
        robber.Position = new Vector3f(at.X + 20f, at.Y, at.Z);
        transport.Sent.Clear();
        server.TickEnemyEncountersForTest();
        server.TickEnemyEncountersForTest();
        Assert.Contains(EnemyCatalog.BanditGunner, p.State.EnemiesMet);
        var log = Assert.Single(transport.Sent.Where(x => x.Conn == p.ConnectionId).Select(x => x.Msg).OfType<EnemyLog>());
        Assert.False(log.Full);
        Assert.Equal(new[] { EnemyCatalog.BanditGunner }, log.Keys);
    }

    [Fact]
    public void TheEnemiesMet_ArePersisted()
    {
        var state = new PlayerState { PlayerId = "Justus" };
        state.EnemiesMet.Add(EnemyCatalog.Ufo);
        state.EnemiesMet.Add(EnemyCatalog.Hunter);

        var back = StateMapper.FromSnapshot(StateMapper.ToSnapshot(state));

        Assert.Equal(new[] { EnemyCatalog.Hunter, EnemyCatalog.Ufo }, back.EnemiesMet.OrderBy(k => k));
    }

    [Fact]
    public void AContentWithoutTheFile_FightsWithTheDefaults()
    {
        var content = new GameContent(
            new List<BlockDefinition>(), new List<ItemDefinition>(), new List<RecipeDefinition>(), new List<BlueprintDefinition>(),
            new List<ShipModuleDefinition>(), new Dictionary<GameLocale, Dictionary<string, string>>());
        Assert.Equal(EnemyCatalog.Defaults().Count, content.Enemies.Count);
        Assert.Equal(260f, content.Enemy(EnemyCatalog.Cruiser).Tier(EnemyTiers.Normal).Hull);
        Assert.Equal(70f, content.Enemy(EnemyCatalog.SpaceDrone).Tier(EnemyTiers.Elite).Hull);
        Assert.Equal(40f, content.Enemy(EnemyCatalog.SpaceDrone).Tier("no-such-tier").Hull); // falls back to normal
    }
}
