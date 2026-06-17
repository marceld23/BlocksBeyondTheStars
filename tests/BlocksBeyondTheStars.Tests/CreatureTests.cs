using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Procedural creatures (World systems / §12): a seed-derived species roster per world (sized by
/// biodiversity), live spawning of mostly-non-hostile fauna, species-specific drops, and the
/// consume system (food heals, poison harms).
/// </summary>
public sealed class CreatureTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CreatureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_creature_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "creature"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "creature",
            Seed = 4242,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    // ---------------- Roster generation ----------------

    [Fact]
    public void Roster_IsDeterministic_ForSameSeedAndPlanet()
    {
        var planet = _content.GetPlanet("jungle")!;
        var a = CreatureGenerator.GenerateRoster(planet, 12345);
        var b = CreatureGenerator.GenerateRoster(planet, 12345);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Id, b[i].Id);
            Assert.Equal(a[i].Habitat, b[i].Habitat);
            Assert.Equal(a[i].Temperament, b[i].Temperament);
            Assert.Equal(a[i].Activity, b[i].Activity);
            Assert.Equal(a[i].MaxHealth, b[i].MaxHealth);
            Assert.Equal(a[i].DropItem, b[i].DropItem);
            Assert.Equal(a[i].LocoStyle, b[i].LocoStyle); // movement signature is seeded too → reproducible
        }
    }

    [Fact]
    public void Roster_AssignsLocomotionStyle_BiasedByTraits()
    {
        // Across a few planets every species gets a style, and the strong body/habitat biases hold: limbless
        // ground fauna slither, fliers glide/drift, water fauna school/slither/drift.
        foreach (var key in new[] { "jungle", "ocean", "desert" })
        {
            var planet = _content.GetPlanet(key);
            if (planet is null) continue;

            foreach (var sp in CreatureGenerator.GenerateRoster(planet, 999))
            {
                // Sanity: the style yields a usable movement profile.
                var prof = BlocksBeyondTheStars.Shared.Definitions.LocomotionController.ForSpecies(sp);
                Assert.True(prof.CruiseSpeed > 0f);

                if (sp.Habitat == CreatureHabitat.Air)
                {
                    Assert.Contains(sp.LocoStyle, new[] { LocomotionStyle.Glider, LocomotionStyle.Drifter, LocomotionStyle.Strider });
                }
                else if (sp.Habitat == CreatureHabitat.Water)
                {
                    Assert.Contains(sp.LocoStyle, new[] { LocomotionStyle.Schooler, LocomotionStyle.Slitherer, LocomotionStyle.Drifter });
                }
                else if (sp.Legs == 0)
                {
                    // limbless ground movers never stride/hop on legs they don't have
                    Assert.NotEqual(LocomotionStyle.Hopper, sp.LocoStyle);
                }
            }
        }
    }

    [Fact]
    public void Abundance_ControlsSpeciesCount()
    {
        var planet = ContentLoader.LoadFromDirectory(TestPaths.DataDir()).GetPlanet("jungle")!;

        planet.CreatureAbundance = "none";
        Assert.Empty(CreatureGenerator.GenerateRoster(planet, 7));

        planet.CreatureAbundance = "few";
        Assert.Equal(3, CreatureGenerator.GenerateRoster(planet, 7).Count);

        planet.CreatureAbundance = "many";
        Assert.Equal(6, CreatureGenerator.GenerateRoster(planet, 7).Count);
    }

    [Fact]
    public void Roster_IsNotAllHostile()
    {
        var planet = _content.GetPlanet("jungle")!; // "many"
        var roster = CreatureGenerator.GenerateRoster(planet, 4242);

        int hostile = roster.Count(s => s.Hostile);
        int peaceful = roster.Count(s => !s.Hostile);
        Assert.True(peaceful > 0, "A world must have some non-hostile creatures.");
        Assert.True(peaceful >= hostile, "Most creatures should be non-hostile.");
    }

    [Fact]
    public void Roster_DropsAreValid_AndCoverFoodOrPoison()
    {
        var planet = _content.GetPlanet("jungle")!;
        var roster = CreatureGenerator.GenerateRoster(planet, 4242);

        foreach (var s in roster)
        {
            Assert.False(string.IsNullOrEmpty(s.DropItem));
            Assert.True(s.DropCount >= 1);
            Assert.NotNull(_content.GetItem(s.DropItem)); // every drop is a real item
        }

        // Food/poison are the common kinds; a material substitute is the rare one.
        Assert.Contains(roster, s => s.DropKind is CreatureDropKind.Food or CreatureDropKind.Poison);
    }

    [Fact]
    public void Roster_NamesEverySpecies_WithVariety()
    {
        var planet = _content.GetPlanet("jungle")!; // "many" → 6 species
        var roster = CreatureGenerator.GenerateRoster(planet, 4242);

        foreach (var s in roster)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name), "Every species should get a coined name.");
        }

        // Names are coined per species, so a world's roster shows several distinct ones.
        Assert.True(roster.Select(s => s.Name).Distinct().Count() >= roster.Count - 1, "Names should vary across the roster.");
    }

    [Fact]
    public void Roster_AssignsBiomeAffinity_SpreadAcrossAMultiBiomeWorld()
    {
        var varied = _content.GetPlanet("varied")!; // a multi-biome world
        int biomeCount = System.Math.Max(1, varied.Biomes.Count);
        Assert.True(biomeCount >= 2, "the 'varied' planet should have several biomes for this test");

        var roster = CreatureGenerator.GenerateRoster(varied, 4242);
        Assert.All(roster, s => Assert.InRange(s.BiomeAffinity, 0, biomeCount - 1)); // a real biome on a multi-biome world
        Assert.True(roster.Select(s => s.BiomeAffinity).Distinct().Count() >= 2, "fauna should spread across biomes");
    }

    // ---------------- Live spawning & combat ----------------

    [Fact]
    public void Creatures_SpawnOnALushWorld()
    {
        var server = Started("jungle", out var repo); // "many"
        using (repo)
        {
            var p = server.AddLocalPlayer("Ranger");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(6.0); // spawn interval elapses
            Assert.NotEmpty(server.Creatures);
        }
    }

    [Fact]
    public void StasisProjector_FreezesACreature_SoItCanBeScannedSafely()
    {
        // Item 36: the stasis projector holds creatures still (no movement, no biting) for a few seconds.
        var server = Started("jungle", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Ranger");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);
            p.State.Inventory.Add("stasis_projector", 1, 1);

            server.Tick(6.0);
            var creature = server.Creatures.First();
            Assert.Equal(0, server.CreatureFrozenForTest(creature.Id)); // not frozen yet

            server.UseGadgetForTest("Ranger", "stasis_projector", creature.Position);

            Assert.True(server.CreatureFrozenForTest(creature.Id) > 0, "the creature is held in stasis");
        }
    }

    [Fact]
    public void AttackCreature_KillsIt_AndYieldsItsSpeciesDrop()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Ranger");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(6.0);
            var creature = server.Creatures.First();
            var species = server.SpeciesRoster.First(s => s.Id == creature.SpeciesId);
            int before = p.State.Inventory.CountOf(species.DropItem);

            // Fauna now spawns spread around the player, so step up to it before attacking.
            p.State.Position = creature.Position;

            for (int i = 0; i < 6 && server.Creatures.Any(c => c.Id == creature.Id); i++)
            {
                server.AttackEntity("Ranger", creature.Id);
            }

            Assert.DoesNotContain(server.Creatures, c => c.Id == creature.Id);
            Assert.True(p.State.Inventory.CountOf(species.DropItem) >= before + species.DropCount);
        }
    }

    [Fact]
    public void Creatures_DespawnWhenLeftBehind_SoFaunaSpreadsAcrossThePlanet()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Ranger");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(4.0);
            var nearby = server.Creatures.First();
            Assert.True(nearby.Position.DistanceSquared(p.State.Position) < 70f * 70f); // spawned near the player

            // Wander far away — the left-behind creature must despawn (freeing the cap for new fauna elsewhere).
            p.State.Position = new Vector3f(600, 64, 600);
            server.Tick(0.1);

            Assert.DoesNotContain(server.Creatures, c => c.Id == nearby.Id);
        }
    }

    // ---------------- Eating (food heals, poison harms) ----------------

    [Fact]
    public void EatingFood_RestoresHealth_AndConsumesIt()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Eater");
            p.State.Health = 50f;
            p.State.Inventory.Add("creature_meat", 2, 20);

            server.ConsumeItem("Eater", "creature_meat"); // +25 health
            Assert.Equal(75f, p.State.Health);
            Assert.Equal(1, p.State.Inventory.CountOf("creature_meat"));
        }
    }

    [Fact]
    public void EatingFood_IsCappedAtFullHealth()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Eater");
            p.State.Health = 90f;
            p.State.Inventory.Add("creature_meat", 1, 20);

            server.ConsumeItem("Eater", "creature_meat");
            Assert.Equal(100f, p.State.Health); // clamped
        }
    }

    [Fact]
    public void EatingPoison_HarmsThePlayer()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Eater");
            p.State.Health = 100f;
            p.State.Inventory.Add("toxic_gland", 1, 20);

            server.ConsumeItem("Eater", "toxic_gland"); // -20 health
            Assert.Equal(80f, p.State.Health);
            Assert.Equal(0, p.State.Inventory.CountOf("toxic_gland"));
        }
    }

    [Fact]
    public void Consume_RejectsNonConsumable_AndMissingItem()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Eater");
            p.State.Health = 70f;

            server.ConsumeItem("Eater", "stone");        // not a consumable
            server.ConsumeItem("Eater", "creature_meat"); // none in inventory
            Assert.Equal(70f, p.State.Health);            // nothing happened
        }
    }

    // ---------------- Movement / behaviour (pure, deterministic) ----------------

    private static float XzDist(Vector3f a, Vector3f b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    [Fact]
    public void Aggressive_MovesTowardNearbyPlayer()
    {
        var cur = new Vector3f(0, 64, 0);
        var player = new Vector3f(5, 64, 0);
        var next = CreatureBehaviour.Step(cur, CreatureTemperament.Aggressive, speed: 3f, active: true,
            player: player, aggroRange: 10f, fleeRange: 6f, dt: 0.25, wanderPhase: 0.0);

        Assert.True(XzDist(next, player) < XzDist(cur, player), "Hunter should close the distance.");
    }

    [Fact]
    public void Skittish_FleesFromNearbyPlayer()
    {
        var cur = new Vector3f(0, 64, 0);
        var player = new Vector3f(2, 64, 0);
        var next = CreatureBehaviour.Step(cur, CreatureTemperament.Skittish, speed: 3f, active: true,
            player: player, aggroRange: 10f, fleeRange: 6f, dt: 0.25, wanderPhase: 0.0);

        Assert.True(XzDist(next, player) > XzDist(cur, player), "Skittish should increase the distance.");
    }

    [Fact]
    public void Passive_WandersByRoughlySpeedTimesDt_RegardlessOfPlayer()
    {
        var cur = new Vector3f(0, 64, 0);
        var player = new Vector3f(1, 64, 0); // close, but passive ignores it
        var next = CreatureBehaviour.Step(cur, CreatureTemperament.Passive, speed: 4f, active: true,
            player: player, aggroRange: 10f, fleeRange: 6f, dt: 0.25, wanderPhase: 1.0);

        float moved = XzDist(next, cur);
        Assert.Equal(4f * 0.25f, moved, 3); // |dir| == 1, so distance == speed*dt
    }

    [Fact]
    public void Sleeping_DoesNotMove()
    {
        var cur = new Vector3f(3, 64, 7);
        var next = CreatureBehaviour.Step(cur, CreatureTemperament.Aggressive, speed: 5f, active: false,
            player: new Vector3f(4, 64, 7), aggroRange: 10f, fleeRange: 6f, dt: 1.0, wanderPhase: 0.5);

        Assert.Equal(cur.X, next.X);
        Assert.Equal(cur.Z, next.Z);
    }

    [Fact]
    public void Aggressive_BeyondAggroRange_DoesNotBeeline()
    {
        var cur = new Vector3f(0, 64, 0);
        var player = new Vector3f(50, 64, 0); // far outside aggro range -> wanders instead
        var next = CreatureBehaviour.Step(cur, CreatureTemperament.Aggressive, speed: 3f, active: true,
            player: player, aggroRange: 10f, fleeRange: 6f, dt: 0.25, wanderPhase: 3.14159);

        // Wander phase π points roughly -X (away from the player at +X), so it should not have closed in.
        Assert.True(XzDist(next, player) >= XzDist(cur, player) - 0.01f);
    }

    // ---------------- Territorial retaliation (provoke) ----------------

    [Fact]
    public void Retaliation_OnlyHostileAndTerritorialFightBack()
    {
        Assert.True(CreatureBehaviour.RetaliatesWhenAttacked(CreatureTemperament.Territorial));
        Assert.True(CreatureBehaviour.RetaliatesWhenAttacked(CreatureTemperament.Aggressive));
        Assert.True(CreatureBehaviour.RetaliatesWhenAttacked(CreatureTemperament.PackHunter));
        Assert.False(CreatureBehaviour.RetaliatesWhenAttacked(CreatureTemperament.Passive));
        Assert.False(CreatureBehaviour.RetaliatesWhenAttacked(CreatureTemperament.Skittish));
    }

    [Fact]
    public void ProvokedTerritorial_ActsAggressive_OthersUnchanged()
    {
        Assert.Equal(CreatureTemperament.Aggressive, CreatureBehaviour.EffectiveTemperament(CreatureTemperament.Territorial, provoked: true));
        Assert.Equal(CreatureTemperament.Territorial, CreatureBehaviour.EffectiveTemperament(CreatureTemperament.Territorial, provoked: false));
        Assert.Equal(CreatureTemperament.Skittish, CreatureBehaviour.EffectiveTemperament(CreatureTemperament.Skittish, provoked: true));
        Assert.Equal(CreatureTemperament.Passive, CreatureBehaviour.EffectiveTemperament(CreatureTemperament.Passive, provoked: true));
    }

    [Fact]
    public void AttackingCreature_ProvokesIt_OnlyIfItRetaliates()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Hunter");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(6.0);
            var creature = server.Creatures.First();
            var species = server.SpeciesRoster.First(s => s.Id == creature.SpeciesId);
            creature.Hull = 9999f;                       // ensure it survives the hit
            creature.Position = new Vector3f(0, 64, 2);  // within default attack reach

            server.AttackEntity("Hunter", creature.Id);

            bool retaliates = CreatureBehaviour.RetaliatesWhenAttacked(species.Temperament);
            Assert.Equal(retaliates, creature.ProvokeTimer > 0);
        }
    }

    [Fact]
    public void Provoke_DecaysOverTime()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Prey");
            p.State.AboardShip = false;
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(6.0);
            var creature = server.Creatures.First();
            creature.ProvokeTimer = 5.0;

            server.Tick(2.0);
            Assert.True(creature.ProvokeTimer is < 5.0 and > 0.0);

            server.Tick(5.0);
            Assert.Equal(0.0, creature.ProvokeTimer);
        }
    }

    // ---------------- Give-up leash (aggressors don't hound the player forever) ----------------

    [Fact]
    public void Aggressor_GivesUpAfterChasingTooLong_ThenItsCooldownDecays()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Prey");
            p.State.AboardShip = false;
            p.State.GodMode = true; // stay alive + in place so we observe the chase, not a death
            p.State.Position = new Vector3f(0, 64, 0);

            server.Tick(6.0); // seed fauna
            // Force an aggressor: with the (B18) lower hostile weights a given seed's roster may have none, so
            // make the first species Aggressive rather than relying on one rolling hostile.
            var hostile = server.SpeciesRoster.First();
            hostile.Temperament = CreatureTemperament.Aggressive;
            var creature = server.Creatures.First();
            creature.SpeciesId = hostile.Id;
            creature.Position = new Vector3f(2, 64, 0); // right next to the player (well within aggro)
            creature.ChaseTimer = 0;
            creature.GiveUpTimer = 0;

            // Hold the prey still and let the aggressor chase past the give-up cap (~7s).
            for (int i = 0; i < 100 && creature.GiveUpTimer <= 0; i++)
            {
                p.State.Position = new Vector3f(0, 64, 0);
                server.Tick(0.2);
            }

            Assert.True(creature.GiveUpTimer > 0, "An aggressor should give up after chasing too long.");
            Assert.Equal(0.0, creature.ChaseTimer); // reset when it backed off

            // The give-up cooldown ticks down and eventually lapses so the creature can re-engage later.
            for (int i = 0; i < 120 && creature.GiveUpTimer > 0; i++)
            {
                server.Tick(0.2);
            }
            Assert.Equal(0.0, creature.GiveUpTimer);
        }
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
