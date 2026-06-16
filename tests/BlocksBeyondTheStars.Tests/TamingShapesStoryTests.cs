using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Story integration of two systems (analysis: memory taming-shapes-story-integration-analysis):
/// (1) a present tamed companion makes the Guardian machines stand down — they read a creature-bonded human
/// as part of the protected biosphere — and VEGA voices the realisation; (2) forming a non-cube block is the
/// cue for VEGA's recovered memory that the Service only ever built in cubes because the Guardian reads any
/// other form as an anomaly. Both VEGA lines are once-only and gated behind the Guardian reveal (beat B5) so
/// an early tame/shape can't pre-empt it; the companion-ward *mechanic* works regardless of story progress.
/// </summary>
public sealed class TamingShapesStoryTests : IDisposable
{
    private const string CompanionWardKey = "story:insight:companion-ward";
    private const string ShapeAnomalyKey = "story:insight:shape-anomaly";

    private readonly string _root;
    private readonly GameContent _content;

    public TamingShapesStoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_tsstory_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "tsstory"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "tsstory",
            Seed = 4242,
            StartPlanet = "jungle", // "many" fauna — easy to find a wild creature to tame
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Ranger(SvGameServer server, string name = "Ranger")
    {
        var p = server.AddLocalPlayer(name);
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 64, 0);
        p.State.Inventory.Add("creature_translator", 1, 1);
        p.State.Inventory.Add("forage_bait", 50, 50);
        p.State.Inventory.Add("meat_bait", 50, 50);
        p.State.Inventory.Add("nectar_lure", 50, 50);
        p.State.SuitEnergy = 100f;
        return p;
    }

    private readonly record struct CombatEntityRef(string Id, Vector3f Position);

    /// <summary>The first wild (non-companion) creature, forced to a known temperament for determinism.</summary>
    private static CombatEntityRef FirstWild(SvGameServer server, CreatureTemperament temperament)
    {
        var creature = server.Creatures.First(c => c.OwnerId.Length == 0);
        var sp = server.SpeciesRoster.First(s => s.Id == creature.SpeciesId);
        sp.Temperament = temperament;
        return new CombatEntityRef(creature.Id, creature.Position);
    }

    /// <summary>Runs the taming ritual to completion on the creature nearest the player. Returns true if tamed.</summary>
    private static bool TameNearest(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p, string playerId, CombatEntityRef target)
    {
        p.State.Position = target.Position;
        int before = server.TamedCreaturesForTest(playerId).Count;
        server.TameDecodeForTest(playerId);
        for (int i = 0; i < 30; i++)
        {
            string need = server.TameCurrentNeedForTest(playerId);
            if (string.IsNullOrEmpty(need))
            {
                break;
            }

            server.TameRespondForTest(playerId, need);
        }

        return server.TamedCreaturesForTest(playerId).Count > before;
    }

    /// <summary>Advances the shared arc past the Guardian reveal (beat B5 ⇒ BeatsRevealed ≥ 6).</summary>
    private static void RevealGuardian(SvGameServer server)
    {
        for (int i = 0; i < 100 && server.StorySnapshot.BeatsRevealed < 6; i++)
        {
            server.AdminAdvanceStory(5);
        }

        Assert.True(server.StorySnapshot.BeatsRevealed >= 6, "the Guardian beat (B5) should be revealed");
    }

    // -----------------------------------------------------------------------------------------------------
    // Narrative: both VEGA insights are gated behind the Guardian reveal and fire exactly once.
    // -----------------------------------------------------------------------------------------------------

    [Fact]
    public void StoryInsights_StaySilentBeforeGuardianReveal_ThenSpeakOnce()
    {
        var server = Started(out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Ranger");

            // Fresh arc (only B0 revealed): neither insight speaks, and crucially neither per-player flag is
            // consumed — so a player who tames/shapes early still hears it later.
            Assert.False(server.RevealCompanionWardInsightForTest("Ranger"));
            Assert.False(server.RevealShapeAnomalyMemoryForTest("Ranger"));
            Assert.False(server.HasStoryMilestoneForTest("Ranger", CompanionWardKey));
            Assert.False(server.HasStoryMilestoneForTest("Ranger", ShapeAnomalyKey));

            RevealGuardian(server);

            // Gate open: each speaks exactly once.
            Assert.True(server.RevealCompanionWardInsightForTest("Ranger"));
            Assert.False(server.RevealCompanionWardInsightForTest("Ranger"));
            Assert.True(server.HasStoryMilestoneForTest("Ranger", CompanionWardKey));

            Assert.True(server.RevealShapeAnomalyMemoryForTest("Ranger"));
            Assert.False(server.RevealShapeAnomalyMemoryForTest("Ranger"));
            Assert.True(server.HasStoryMilestoneForTest("Ranger", ShapeAnomalyKey));
        }
    }

    [Fact]
    public void ShapeCraft_FormingANonCube_SpeaksTheShapeMemory_OnlyAfterReveal()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Inventory.Add("stone", 10, 99);

            // Before the Guardian reveal, forming a shape must not pre-empt it (flag stays unset).
            server.ShapeCraft("Builder", "stone", (int)BlockShape.Sphere);
            Assert.False(server.HasStoryMilestoneForTest("Builder", ShapeAnomalyKey));

            RevealGuardian(server);

            // The first non-cube craft after the reveal fires the memory.
            server.ShapeCraft("Builder", "stone", (int)BlockShape.Pyramid);
            Assert.True(server.HasStoryMilestoneForTest("Builder", ShapeAnomalyKey));
        }
    }

    [Fact]
    public void ShapeCraft_ReformingToACube_NeverTriggersTheShapeMemory()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Builder");
            p.State.AboardShip = false;
            p.State.Inventory.Add("stone#s04", 4, 99); // a sphere to un-shape back to a plain cube
            RevealGuardian(server);

            // Re-forming an existing shape back to a cube (shape 0) is not "forming a non-cube" → no memory.
            server.ShapeCraft("Builder", "stone#s04", (int)BlockShape.Cube);
            Assert.False(server.HasStoryMilestoneForTest("Builder", ShapeAnomalyKey));
        }
    }

    // -----------------------------------------------------------------------------------------------------
    // Mechanic: a present, nearby companion wards its owner from the planet machines.
    // -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Companion_WardsItsOwner_OnlyWhenPresentAndNear()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            server.Tick(6.0);
            Assert.True(TameNearest(server, p, "Ranger", FirstWild(server, CreatureTemperament.Passive)),
                "a forgiving passive creature should tame");

            // The fresh companion stands beside its owner → warded.
            Assert.True(server.PlayerWardedByCompanionForTest("Ranger"));

            // Step well beyond the ward range (no tick, so the companion stays put) → no longer warded.
            p.State.Position = new Vector3f(p.State.Position.X + 50, p.State.Position.Y, p.State.Position.Z);
            Assert.False(server.PlayerWardedByCompanionForTest("Ranger"));
        }
    }

    [Fact]
    public void Companion_StopsPlanetMachineDamage_ToItsWardedOwner()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            server.Tick(6.0);
            Assert.True(TameNearest(server, p, "Ranger", FirstWild(server, CreatureTemperament.Passive)));
            Assert.True(server.PlayerWardedByCompanionForTest("Ranger"));

            // A machine appears right on top of the warded player.
            server.SpawnPlanetEnemyAtForTest(p.State.Position, damagePerSecond: 40f);

            p.State.Health = 100f;
            server.Tick(1.0);
            Assert.True(p.State.Health > 99f, "a warded player should take no bite (companion stands the machine down)");

            // Release the companion — the very same machine now bites.
            var companion = server.TamedCreaturesForTest("Ranger").First();
            server.ReleaseCompanionForTest("Ranger", companion.Id);
            server.ReconcileCompanionsForTest();
            Assert.False(server.PlayerWardedByCompanionForTest("Ranger"));

            p.State.Health = 100f;
            server.Tick(1.0);
            Assert.True(p.State.Health < 90f, "an unwarded player is bitten by the adjacent machine");
        }
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
        catch
        {
            // best-effort temp cleanup
        }
    }
}
