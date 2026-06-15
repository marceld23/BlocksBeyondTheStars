using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Creature taming + companions (design: docs/CREATURE_TAMING_PLAN.md): the Creature Translator ritual
/// (decode mood → respond → trust), difficulty by temperament, per-species first-tame knowledge, and
/// companions that are bound to their home world, persist, and respect the per-world cap.
/// </summary>
public sealed class CreatureTamingTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CreatureTamingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_taming_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "taming"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "taming",
            Seed = 4242,
            StartPlanet = "jungle", // "many" fauna
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static BlocksBeyondTheStars.GameServer.PlayerSession Ranger(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Ranger");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 64, 0);
        p.State.Inventory.Add("creature_translator", 1, 1);
        p.State.Inventory.Add("forage_bait", 50, 50);
        p.State.Inventory.Add("meat_bait", 50, 50);
        p.State.Inventory.Add("nectar_lure", 50, 50);
        p.State.SuitEnergy = 100f;
        return p;
    }

    /// <summary>Drives the taming ritual on the creature nearest the player to completion (always answering
    /// the creature's current need). Returns true if it ended in a tame.</summary>
    private static bool TameNearest(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession p, CombatEntityRef target)
    {
        p.State.Position = target.Position;
        int before = server.TamedCreaturesForTest("Ranger").Count;
        server.TameDecodeForTest("Ranger");
        for (int i = 0; i < 30; i++)
        {
            string need = server.TameCurrentNeedForTest("Ranger");
            if (string.IsNullOrEmpty(need))
            {
                break; // attempt ended (tamed, spooked or gone)
            }

            server.TameRespondForTest("Ranger", need);
        }

        return server.TamedCreaturesForTest("Ranger").Count > before;
    }

    /// <summary>The first wild (non-companion) creature, forced to a known temperament for a deterministic test.</summary>
    private static CombatEntityRef FirstWild(SvGameServer server, CreatureTemperament temperament)
    {
        var creature = server.Creatures.First(c => c.OwnerId.Length == 0);
        var sp = server.SpeciesRoster.First(s => s.Id == creature.SpeciesId);
        sp.Temperament = temperament;
        return new CombatEntityRef(creature.Id, creature.Position);
    }

    private readonly record struct CombatEntityRef(string Id, Vector3f Position);

    [Fact]
    public void Taming_APassiveCreature_CreatesANamedCompanionInPlace()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            server.Tick(6.0);
            var target = FirstWild(server, CreatureTemperament.Passive);

            bool tamed = TameNearest(server, p, target);

            Assert.True(tamed, "a forgiving passive creature should tame");
            var companion = Assert.Single(server.TamedCreaturesForTest("Ranger"));
            Assert.False(string.IsNullOrWhiteSpace(companion.Name));
            Assert.False(string.IsNullOrEmpty(companion.HomeBodyId));

            // The wild animal is gone and a live companion entity owned by the player took its place.
            Assert.DoesNotContain(server.Creatures, c => c.Id == target.Id);
            var ent = Assert.Single(server.CompanionEntitiesForTest("Ranger"));
            Assert.Equal("Ranger", ent.OwnerId);
            Assert.Equal(companion.Id, ent.CompanionId);
            Assert.False(ent.Hostile);
        }
    }

    [Fact]
    public void Taming_AwardsKnowledge_FullFirstTime_TrickleAfterPerSpecies()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            server.Tick(6.0);
            server.Tick(6.0);

            // Two wild creatures of the SAME species (passive, so the ritual is reliable).
            var wild = server.Creatures.Where(c => c.OwnerId.Length == 0).Take(2).ToList();
            Assert.True(wild.Count >= 2, "the lush world should have spawned at least two creatures");
            var sp = server.SpeciesRoster.First();
            sp.Temperament = CreatureTemperament.Passive;
            foreach (var c in wild)
            {
                c.SpeciesId = sp.Id;
            }

            int k0 = p.State.KnowledgePoints;
            Assert.True(TameNearest(server, p, new CombatEntityRef(wild[0].Id, wild[0].Position)));
            int gain1 = p.State.KnowledgePoints - k0;

            int k1 = p.State.KnowledgePoints;
            Assert.True(TameNearest(server, p, new CombatEntityRef(wild[1].Id, wild[1].Position)));
            int gain2 = p.State.KnowledgePoints - k1;

            Assert.True(gain1 >= 2, $"first tame of a species should pay a full research bonus (got {gain1})");
            Assert.Equal(1, gain2); // a second of the same species pays only the small trickle
        }
    }

    [Fact]
    public void Skittish_BoltsOnTheFirstWrongResponse()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            server.Tick(6.0);
            var target = FirstWild(server, CreatureTemperament.Skittish);

            p.State.Position = target.Position;
            server.TameDecodeForTest("Ranger");
            string need = server.TameCurrentNeedForTest("Ranger");
            Assert.False(string.IsNullOrEmpty(need));

            // Answer with the WRONG response — a skittish creature bolts immediately, ending the attempt.
            string wrong = new[] { "feed", "calm", "approach", "space" }.First(r => r != need);
            server.TameRespondForTest("Ranger", wrong);

            Assert.Equal(string.Empty, server.TameCurrentNeedForTest("Ranger")); // attempt ended
            Assert.Empty(server.TamedCreaturesForTest("Ranger"));
        }
    }

    [Fact]
    public void Passive_ForgivesAWrongResponse_AndKeepsTheAttemptOpen()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            server.Tick(6.0);
            var target = FirstWild(server, CreatureTemperament.Passive);

            p.State.Position = target.Position;
            server.TameDecodeForTest("Ranger");
            string need = server.TameCurrentNeedForTest("Ranger");
            string wrong = new[] { "feed", "calm", "approach", "space" }.First(r => r != need);
            server.TameRespondForTest("Ranger", wrong);

            // A passive creature tolerates a mistake — the attempt is still open.
            Assert.False(string.IsNullOrEmpty(server.TameCurrentNeedForTest("Ranger")));
            Assert.Empty(server.TamedCreaturesForTest("Ranger"));
        }
    }

    [Fact]
    public void Companion_IsBoundToItsHomeBody_DespawnsElsewhere()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            server.Tick(6.0);
            var target = FirstWild(server, CreatureTemperament.Passive);
            Assert.True(TameNearest(server, p, target));
            Assert.Single(server.CompanionEntitiesForTest("Ranger"));

            // Pretend the companion belongs to another world → reconciliation despawns it here.
            var tc = server.TamedCreaturesForTest("Ranger")[0];
            string home = tc.HomeBodyId;
            tc.HomeBodyId = "some-other-body";
            server.ReconcileCompanionsForTest();
            Assert.Empty(server.CompanionEntitiesForTest("Ranger"));

            // Back on its home world it materialises again.
            tc.HomeBodyId = home;
            server.ReconcileCompanionsForTest();
            Assert.Single(server.CompanionEntitiesForTest("Ranger"));
        }
    }

    [Fact]
    public void Companions_RespectThePerWorldCap()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            server.Tick(6.0);
            var target = FirstWild(server, CreatureTemperament.Passive);
            Assert.True(TameNearest(server, p, target));
            string home = server.TamedCreaturesForTest("Ranger")[0].HomeBodyId;
            var sp = server.SpeciesRoster.First();

            // Pile on more companion records than the per-world cap allows.
            for (int i = 0; i < 8; i++)
            {
                p.State.TamedCreatures.Add(new BlocksBeyondTheStars.Shared.State.TamedCreature
                {
                    Id = "extra" + i,
                    HomeBodyId = home,
                    Name = "Extra " + i,
                    SpeciesId = sp.Id,
                    Species = sp,
                    SizeScale = 1f,
                });
            }

            server.ReconcileCompanionsForTest();
            Assert.Equal(6, server.CompanionEntitiesForTest("Ranger").Count); // MaxCompanionsPerWorld
        }
    }

    [Fact]
    public void Companions_PersistAcrossSaveAndReload()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            server.Tick(6.0);
            var target = FirstWild(server, CreatureTemperament.Passive);
            Assert.True(TameNearest(server, p, target)); // CompleteTame persists the player

            var reloaded = repo.LoadPlayer("Ranger");
            Assert.NotNull(reloaded);
            var tc = Assert.Single(reloaded!.TamedCreatures);
            Assert.False(string.IsNullOrWhiteSpace(tc.Name));
            Assert.False(string.IsNullOrEmpty(tc.SpeciesId));
            Assert.Equal(tc.SpeciesId, tc.Species.Id); // the full species snapshot round-trips
            Assert.NotEmpty(reloaded.TamedSpecies);     // first-tame bookkeeping persisted
        }
    }

    [Fact]
    public void Rename_And_Release_Companion()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = Ranger(server);
            server.Tick(6.0);
            var target = FirstWild(server, CreatureTemperament.Passive);
            Assert.True(TameNearest(server, p, target));
            var tc = server.TamedCreaturesForTest("Ranger")[0];

            server.RenameCompanionForTest("Ranger", tc.Id, "Sparky");
            Assert.Equal("Sparky", server.TamedCreaturesForTest("Ranger")[0].Name);
            Assert.Contains(server.CompanionEntitiesForTest("Ranger"), c => c.CustomName == "Sparky");

            server.ReleaseCompanionForTest("Ranger", tc.Id);
            Assert.Empty(server.TamedCreaturesForTest("Ranger"));
            Assert.Empty(server.CompanionEntitiesForTest("Ranger"));
        }
    }

    [Fact]
    public void FollowStep_ClosesDistanceToOwner_WhenFarAway()
    {
        var pet = new Vector3f(0, 64, 0);
        var owner = new Vector3f(10, 64, 0);
        var next = CreatureBehaviour.FollowStep(pet, owner, speed: 3f, followDistance: 4f, dt: 0.25, wanderPhase: 0.0);

        float d0 = MathF.Sqrt((owner.X - pet.X) * (owner.X - pet.X));
        float d1 = MathF.Sqrt((owner.X - next.X) * (owner.X - next.X));
        Assert.True(d1 < d0, "a companion that has fallen behind should move toward its owner");
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
