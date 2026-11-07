// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Wild Soundis (#3365): duel taming and pack defence. The translator starts a one-on-one duel instead of the ritual; only
/// that animal fights, and only the duelling player; at the surrender line it gives up — it never dies — and is tamed as a
/// planet pet. Walking away or dying ends the duel. Outside a duel, hitting one makes its kin within reach defend it against
/// that attacker for a while; then they calm down.
/// </summary>
public sealed class CreatureDuelTests : IDisposable
{
    private static readonly Vector3i Centre = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_duel_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort temp cleanup
        }
    }

    private sealed record DuelWorld(SvGameServer Server, RecordingTransport Transport, PlayerSession Ada, CreatureSpecies Soundi);

    /// <summary>A flat pad, no wildlife of its own, one roster species turned into a duel-tamed pack defender (the wild
    /// Soundi's flags), Ada on the pad with the translator.</summary>
    private DuelWorld World(string name)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var transport = new RecordingTransport();
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "jungle", // breathable air: the only health a test player loses is a bite
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.CreatureAbundance = AlienActivity.Off; // only the animals the test plants
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = -12; x <= 12; x++)
        {
            for (int z = -12; z <= 12; z++)
            {
                server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y - 1, Centre.Z + z), stone);
                for (int y = 0; y < 3; y++)
                {
                    server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y + y, Centre.Z + z), BlockId.Air);
                }
            }
        }

        foreach (var other in server.SpeciesRoster)
        {
            other.Temperament = CreatureTemperament.Passive; // no other biter wanders into a measurement
            other.AttackDamage = 0f;
        }

        var sp = server.SpeciesRoster.First();
        sp.Habitat = CreatureHabitat.Land;
        sp.HasWings = false;
        sp.HasGasSac = false;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.Size = 1f;
        sp.MaxHealth = 50f;
        sp.AttackDamage = 5f;
        sp.DuelTame = true;
        sp.PackDefender = true;

        var ada = server.AddLocalPlayer("Ada");
        ada.State.AboardShip = false;
        ada.State.Position = At(0, 0);
        ada.State.Inventory.Add("creature_translator", 1, 1);
        return new DuelWorld(server, transport, ada, sp);
    }

    private static Vector3f At(float dx, float dz) => new(Centre.X + 0.5f + dx, Centre.Y, Centre.Z + 0.5f + dz);

    private static string Plant(DuelWorld w, float dx, float dz) => w.Server.SpawnCreatureAtForTest(At(dx, dz), w.Soundi.Id);

    private static CombatEntity Creature(DuelWorld w, string id) => w.Server.Creatures.Single(c => c.Id == id);

    private static PlayerSession Bob(DuelWorld w)
    {
        var bob = w.Server.AddLocalPlayer("Bob");
        bob.State.AboardShip = false;
        bob.State.Position = At(0, 2);
        return bob;
    }

    [Fact]
    public void TheTranslator_StartsADuel_NotTheRitual()
    {
        var w = World("duel_start");
        string id = Plant(w, 2, 0);
        Creature(w, id).Hull = 10f;

        w.Server.TameDecodeForTest("Ada");

        Assert.Equal("Ada", w.Server.CreatureDuelPartnerForTest(id));
        Assert.Equal("Ada", w.Server.CreatureFightTargetForTest(id));
        Assert.Equal(string.Empty, w.Server.TameTargetForTest("Ada")); // no ritual
        Assert.Equal(Creature(w, id).HullMax, Creature(w, id).Hull); // a fair duel starts fresh
        Assert.Contains("@srv.creature_duel.start:" + w.Soundi.Name, w.Transport.TextsTo(w.Ada));
        Assert.False(w.Transport.To<TameProgress>(w.Ada).Last().Active);
    }

    [Fact]
    public void ADuel_IsOneOnOne_TheAnimalFightsOnlyItsPartner_AndTheRestOfThePackStaysOut()
    {
        var w = World("duel_isolated");
        string duellist = Plant(w, 2, 0);
        string kin = Plant(w, 4, 2);
        var bob = Bob(w);
        w.Server.TameDecodeForTest("Ada");

        Assert.True(w.Server.CreatureMayFightForTest(duellist, "Ada"));
        Assert.False(w.Server.CreatureMayFightForTest(duellist, "Bob"));

        Assert.True(w.Server.DuelHitForTest("Ada", duellist, 5f));
        Assert.Equal(string.Empty, w.Server.CreatureFightTargetForTest(kin)); // a duel hit rallies nobody

        Assert.True(w.Server.DuelHitForTest("Bob", duellist, 500f)); // somebody else's hit is no part of the duel
        Assert.Equal("Ada", w.Server.CreatureDuelPartnerForTest(duellist));
        Assert.True(Creature(w, duellist).Hull > 0f);
        Assert.Contains("@srv.creature_duel.busy:" + w.Soundi.Name, w.Transport.TextsTo(bob));

        // In the running world the duellist bites Ada and never Bob, though both stand within its reach.
        w.Ada.State.Health = 100f;
        bob.State.Health = 100f;
        for (int i = 0; i < 20; i++)
        {
            w.Ada.State.Position = At(1, 0);
            bob.State.Position = At(1, 1);
            Creature(w, duellist).Position = At(2, 0);
            w.Server.TickForTest(0.1);
        }

        Assert.True(w.Ada.State.Health < 100f, "the duellist fights its partner");
        Assert.Equal(100f, bob.State.Health);
        Assert.Equal("Ada", w.Server.CreatureDuelPartnerForTest(duellist)); // the duel goes on while Ada stays
    }

    [Fact]
    public void AtTheSurrenderLine_ItGivesUp_AndBecomesAPlanetPet()
    {
        var w = World("duel_won");
        string id = Plant(w, 2, 0);
        w.Server.TameDecodeForTest("Ada");

        Assert.True(w.Server.DuelHitForTest("Ada", id, 1000f)); // far past the line — and still it never dies

        Assert.DoesNotContain(w.Server.Creatures, c => c.Id == id && !c.IsCompanion);
        var pet = Assert.Single(w.Server.TamedCreaturesForTest("Ada"));
        Assert.Equal(w.Soundi.Id, pet.SpeciesId);
        Assert.False(pet.Roams); // a planet pet: it stays on its world
        Assert.True(pet.HasHomeSpot);
        Assert.Equal(w.Server.ActiveLocationId, pet.HomeBodyId);
        Assert.Contains("@srv.creature_duel.won:" + w.Soundi.Name, w.Transport.TextsTo(w.Ada));
        var result = w.Transport.To<TameResult>(w.Ada).Last();
        Assert.True(result.Success);
        Assert.Equal("creature.tame.msg.duel", result.MessageKey);
        Assert.Contains(w.Server.CompanionEntitiesForTest("Ada"), c => c.CompanionId == pet.Id);
    }

    [Fact]
    public void ThroughTheRealSwing_TheLastHitWinsTheDuel()
    {
        var w = World("duel_swing");
        string id = Plant(w, 2, 0);
        w.Server.TameDecodeForTest("Ada");
        var soundi = Creature(w, id);
        soundi.Hull = CreatureBehaviour.DuelSurrenderHull(soundi.HullMax) + 1f; // one bare-hand blow from giving up

        w.Server.AttackEntity("Ada", id);

        Assert.Single(w.Server.TamedCreaturesForTest("Ada"));
    }

    [Fact]
    public void WalkingAway_EndsTheDuel_AndTheAnimalIsWholeAgain()
    {
        var w = World("duel_walk");
        string id = Plant(w, 2, 0);
        w.Server.TameDecodeForTest("Ada");
        w.Server.DuelHitForTest("Ada", id, 10f);

        w.Ada.State.Position = At(0, 0);
        w.Server.TickStoryCompanionsForTest();
        Assert.Equal("Ada", w.Server.CreatureDuelPartnerForTest(id)); // still close: the duel goes on

        w.Ada.State.Position = new Vector3f(Centre.X + 40.5f, Centre.Y, Centre.Z + 0.5f);
        w.Server.TickStoryCompanionsForTest();

        var soundi = Creature(w, id);
        Assert.Equal(string.Empty, soundi.DuelWithId);
        Assert.Equal(string.Empty, soundi.FightTargetId);
        Assert.Equal(soundi.HullMax, soundi.Hull);
        Assert.Contains("@srv.creature_duel.walked_away:" + w.Soundi.Name, w.Transport.TextsTo(w.Ada));
        Assert.Empty(w.Server.TamedCreaturesForTest("Ada"));
    }

    [Fact]
    public void Dying_EndsTheDuel_TryAgainAnyTime()
    {
        var w = World("duel_lost");
        string id = Plant(w, 2, 0);
        w.Server.TameDecodeForTest("Ada");

        w.Ada.State.Health = 0f;
        w.Server.TickStoryCompanionsForTest();

        Assert.Equal(string.Empty, w.Server.CreatureDuelPartnerForTest(id));
        Assert.Contains("@srv.creature_duel.lost:" + w.Soundi.Name, w.Transport.TextsTo(w.Ada));

        w.Ada.State.Health = 100f;
        w.Server.TameDecodeForTest("Ada"); // a new challenge any time
        Assert.Equal("Ada", w.Server.CreatureDuelPartnerForTest(id));
    }

    [Fact]
    public void OutsideADuel_ThePackDefendsItsMember_AgainstThatAttackerOnly_ThenCalmsDown()
    {
        var w = World("duel_pack");
        string hit = Plant(w, 2, 0);
        string near = Plant(w, 6, 6);
        string far = Plant(w, 2, 20);

        Assert.False(w.Server.DuelHitForTest("Ada", hit, 5f)); // no duel: the ordinary hit goes on

        Assert.Equal("Ada", w.Server.CreatureFightTargetForTest(hit));
        Assert.Equal("Ada", w.Server.CreatureFightTargetForTest(near));
        Assert.Equal(string.Empty, w.Server.CreatureFightTargetForTest(far)); // beyond the pack's reach
        Assert.False(w.Server.CreatureMayFightForTest(near, "Bob"));
        Assert.True(Creature(w, near).ProvokeTimer > 0);
        Assert.Contains("@srv.creature_duel.pack:" + w.Soundi.Name, w.Transport.TextsTo(w.Ada));

        w.Server.AdvanceUptimeForTest(CreatureBehaviour.PackDefenceSeconds + 1);
        w.Server.TickStoryCompanionsForTest();

        Assert.Equal(string.Empty, w.Server.CreatureFightTargetForTest(hit));
        Assert.Equal(string.Empty, w.Server.CreatureFightTargetForTest(near));
        Assert.Equal(0, Creature(w, near).ProvokeTimer);
        Assert.Equal(w.Soundi.AttackDamage, Creature(w, near).DamagePerSecond, 3); // its own bite back
    }

    [Fact]
    public void ADuelAnimal_NeverDiesAtAPlayersHand_EvenOutsideADuel()
    {
        var w = World("duel_never_dies");
        string id = Plant(w, 2, 0);
        var soundi = Creature(w, id);
        soundi.Hull = 1f;

        w.Server.AttackEntity("Ada", id);

        Assert.Contains(w.Server.Creatures, c => c.Id == id);
        Assert.Equal(CreatureBehaviour.DuelSurrenderHull(soundi.HullMax), soundi.Hull);
        Assert.Equal("Ada", w.Server.CreatureFightTargetForTest(id)); // and its pack (itself included) defends it
    }

    [Fact]
    public void StartingADuel_CalmsThePackThatDefendedAgainstThePlayer()
    {
        var w = World("duel_calm_pack");
        string first = Plant(w, 2, 0);
        string second = Plant(w, 4, 0);
        w.Server.DuelHitForTest("Ada", first, 1f); // the pack turns on Ada
        Assert.Equal("Ada", w.Server.CreatureFightTargetForTest(second));

        w.Ada.State.Position = At(2, 1); // nearest to the first
        w.Server.TameDecodeForTest("Ada");

        Assert.Equal("Ada", w.Server.CreatureDuelPartnerForTest(first));
        Assert.Equal(string.Empty, w.Server.CreatureFightTargetForTest(second)); // one-on-one
    }
}
