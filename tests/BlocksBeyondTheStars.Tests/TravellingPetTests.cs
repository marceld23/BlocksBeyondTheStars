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
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Travelling pets (#4025, a school club report: "I tamed a Soundi on Soaniri, but when I flew back to the start galaxy the
/// Soundi didn't come along"). A pet of a species with the data flag <c>travels</c> — the duel-won Soundi — comes along to
/// every world while it follows, stays home on its home world when it does not, and takes a pet slot on every world: on each
/// world the pets living there plus all travelling pets stay within six. Old duel-won Soundis are migrated at load (oldest
/// first, while they fit); the rest stay home and come along once a slot frees up.
/// </summary>
public sealed class TravellingPetTests : IDisposable
{
    private const int Cap = 6;
    private static readonly Vector3i Centre = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_travel_pet_" + Guid.NewGuid().ToString("N"));
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

    private sealed record PetWorld(SvGameServer Server, RecordingTransport Transport, PlayerSession Ada, CreatureSpecies Soundi);

    private SvGameServer Server(string name, RecordingTransport transport)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceBanditCamps = false,
            ViewDistanceChunks = 1,
        };
        config.Rules.CreatureAbundance = AlienActivity.Off; // only the animals the test plants
        config.Rules.FreeSpaceFlight = true;                // the travel test flies to another world
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = -8; x <= 8; x++)
        {
            for (int z = -8; z <= 8; z++)
            {
                server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y - 1, Centre.Z + z), stone);
                for (int y = 0; y < 3; y++)
                {
                    server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y + y, Centre.Z + z), BlockId.Air);
                }
            }
        }

        return server;
    }

    /// <summary>A flat pad, one roster species given the wild Soundi's flags (duel-tamed, travels), Ada on the pad with the
    /// translator.</summary>
    private PetWorld World(string name)
    {
        var transport = new RecordingTransport();
        var server = Server(name, transport);
        foreach (var other in server.SpeciesRoster)
        {
            other.Temperament = CreatureTemperament.Passive;
            other.AttackDamage = 0f;
        }

        var sp = server.SpeciesRoster.First();
        sp.Habitat = CreatureHabitat.Land;
        sp.HasWings = false;
        sp.HasGasSac = false;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.Size = 1f;
        sp.MaxHealth = 50f;
        sp.DuelTame = true;
        sp.Travels = true;
        return new PetWorld(server, transport, Ada(server), sp);
    }

    private static PlayerSession Ada(SvGameServer server)
    {
        var ada = server.AddLocalPlayer("Ada");
        ada.State.AboardShip = false;
        ada.State.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 0.5f);
        ada.State.Inventory.Add("creature_translator", 1, 1);
        return ada;
    }

    /// <summary>Plants a wild animal of the flagged species beside Ada and wins the duel against it.</summary>
    private static TamedCreature DuelTame(PetWorld w)
    {
        string id = w.Server.SpawnCreatureAtForTest(new Vector3f(Centre.X + 2.5f, Centre.Y, Centre.Z + 0.5f), w.Soundi.Id);
        w.Server.TameDecodeForTest("Ada");
        Assert.True(w.Server.DuelHitForTest("Ada", id, 1000f));
        return w.Ada.State.TamedCreatures.Last();
    }

    /// <summary>A companion record as an older save holds it: a planet pet of the authored Soundi, no travel flag anywhere.</summary>
    private static TamedCreature OldSoundi(string id, string home, long tamedAt) => new()
    {
        Id = id,
        HomeBodyId = home,
        Name = "Soundi " + id,
        SpeciesId = "au_soundi",
        Species = new CreatureSpecies
        {
            Id = "au_soundi",
            Name = "Soundi",
            Habitat = CreatureHabitat.Land,
            Size = 0.5f,
            Speed = 2.6f,
            MaxHealth = 24f,
            Legs = 4,
            DuelTame = true,
        },
        SizeScale = 1f,
        Bond = 50,
        TamedAtUtc = tamedAt,
    };

    private static TamedCreature PlanetPet(string id, string home, CreatureSpecies sp) => new()
    {
        Id = id,
        HomeBodyId = home,
        Name = "Pet " + id,
        SpeciesId = sp.Id,
        Species = sp,
        SizeScale = 1f,
        Bond = 50,
    };

    /// <summary>The same body as a species that does not travel (an ordinary pet's).</summary>
    private static CreatureSpecies Plain(CreatureSpecies sp)
    {
        var plain = sp.Clone();
        plain.Id = sp.Id + "_plain";
        plain.DuelTame = false;
        plain.Travels = false;
        return plain;
    }

    private static bool Present(SvGameServer server, TamedCreature tc)
        => server.CompanionEntitiesForTest("Ada").Any(c => c.CompanionId == tc.Id);

    [Fact]
    public void TheSoundiRecord_CarriesTheTravelFlag()
    {
        Assert.True(_content.AuthoredCreatures["soundi"].Travels);
        Assert.False(_content.AuthoredCreatures["soundrak"].Travels); // only the Soundi comes along
    }

    [Fact]
    public void AWonDuel_MakesATravellingPet_AndTheLineSaysItComesAlong()
    {
        var w = World("travel_tame");

        var pet = DuelTame(w);

        Assert.True(pet.Travels);
        Assert.False(pet.Roams); // a pet, not a story companion
        Assert.True(Present(w.Server, pet));
        Assert.Equal("creature.tame.msg.duel_travels", w.Transport.To<TameResult>(w.Ada).Last().MessageKey);
        var listed = w.Transport.To<CompanionList>(w.Ada).Last().Companions.Single();
        Assert.True(listed.TravellingPet);
        Assert.False(listed.Roams);
    }

    [Fact]
    public void ATravellingPet_ComesAlongToAnotherWorld_APlanetPetDoesNot()
    {
        var w = World("travel_flight");
        var soundi = DuelTame(w);
        var homePet = PlanetPet("home_pet", w.Server.ActiveLocationId, Plain(w.Soundi));
        w.Ada.State.TamedCreatures.Add(homePet);
        string home = w.Ada.CurrentLocationId;
        var homeBody = w.Server.Galaxy.FindBody(home)!;
        var other = w.Server.Galaxy.AllBodies().First(b => b.Id != home && b.SystemId == homeBody.SystemId
            && b.Kind is CelestialKind.Planet or CelestialKind.Moon && !string.IsNullOrEmpty(b.PlanetType)
            && _content.GetPlanet(b.PlanetType!) is { Void: false });

        w.Server.Travel("Ada", other.Id);
        w.Server.ReconcileCompanionsForTest();

        Assert.Equal(other.Id, w.Ada.CurrentLocationId);
        Assert.True(Present(w.Server, soundi), "the Soundi steps out beside its friend on the new world");
        Assert.False(Present(w.Server, homePet));
        Assert.Equal(home, soundi.HomeBodyId); // its home stays where it was won

        // Left at home, it goes home: away from its world it leaves at once, and it does not come back here.
        w.Server.SetCompanionFollowForTest("Ada", soundi.Id, following: false);
        w.Server.ReconcileCompanionsForTest();
        Assert.False(Present(w.Server, soundi));
        Assert.Contains("@srv.companion.stays_home:" + soundi.Name, w.Transport.TextsTo(w.Ada));

        w.Server.SetCompanionFollowForTest("Ada", soundi.Id, following: true);
        w.Server.ReconcileCompanionsForTest();
        Assert.True(Present(w.Server, soundi));
    }

    [Fact]
    public void AnOldDuelWonSoundi_IsMigratedAtLoad_AndAppearsOnTheJoinWorld()
    {
        const string name = "travel_migrate";
        var server = Server(name, new RecordingTransport());
        var ada = Ada(server);
        var old = OldSoundi("old", "some-other-body", tamedAt: 1);
        ada.State.TamedCreatures.Add(old);
        server.SaveAllForTest();
        server.Stop();
        _repos[0].Dispose();
        _repos.Clear();
        SqliteConnection.ClearAllPools();

        var again = Server(name, new RecordingTransport());
        var back = Ada(again);
        again.ReconcileCompanionsForTest();

        var tc = Assert.Single(back.State.TamedCreatures);
        Assert.True(tc.Travels);
        Assert.Equal("some-other-body", tc.HomeBodyId);
        Assert.True(Present(again, tc), "the Soundi from Soaniri is beside its friend on this world");
    }

    [Fact]
    public void ATravellingPet_TakesAPetSlotOnEveryWorld()
    {
        var w = World("travel_slots");
        for (int i = 0; i < Cap; i++)
        {
            var tc = OldSoundi("s" + i, "body" + i, tamedAt: i + 1);
            tc.Travels = true;
            w.Ada.State.TamedCreatures.Add(tc);
        }

        string id = w.Server.SpawnCreatureAtForTest(new Vector3f(Centre.X + 2.5f, Centre.Y, Centre.Z + 0.5f), w.Soundi.Id);
        w.Server.TameDecodeForTest("Ada");
        Assert.True(w.Server.DuelHitForTest("Ada", id, 1000f));

        Assert.Equal(Cap, w.Ada.State.TamedCreatures.Count); // six travellers fill every world — no seventh pet here
        Assert.Equal("creature.tame.msg.full", w.Transport.To<TameResult>(w.Ada).Last().MessageKey);
    }

    [Fact]
    public void ASoundiThatWouldOverfillAnotherWorld_StaysHomeAsAPlanetPet_AndSaysWhy()
    {
        var w = World("travel_full");
        for (int i = 0; i < Cap; i++)
        {
            w.Ada.State.TamedCreatures.Add(PlanetPet("p" + i, "some-other-body", Plain(w.Soundi))); // a full world elsewhere
        }

        var pet = DuelTame(w);

        Assert.False(pet.Travels); // the duel is won all the same — it lives here
        Assert.Equal(w.Server.ActiveLocationId, pet.HomeBodyId);
        Assert.Equal("creature.tame.msg.duel", w.Transport.To<TameResult>(w.Ada).Last().MessageKey);
        Assert.Contains("@srv.companion.travel_full:" + pet.Name, w.Transport.TextsTo(w.Ada));

        // A pet released over there frees the slot: the Soundi comes along from now on.
        w.Server.ReleaseCompanionForTest("Ada", "p0");
        Assert.True(pet.Travels);
        Assert.Contains("@srv.companion.travels_now:" + pet.Name, w.Transport.TextsTo(w.Ada));
    }

    [Fact]
    public void TheMigration_PromotesOldestFirst_WhileTheyFit_TheRestStayHome()
    {
        var w = World("travel_overflow");
        for (int i = 0; i < 4; i++)
        {
            w.Ada.State.TamedCreatures.Add(OldSoundi("s" + i, "soaniri", tamedAt: 10 - i)); // s3 is the oldest
            w.Ada.State.TamedCreatures.Add(PlanetPet("dog" + i, "earth", Plain(w.Soundi)));
        }

        // Four dogs on Earth: each travelling Soundi takes one of Earth's six slots too, so two fit.
        Assert.Equal(2, w.Server.PromoteTravellingPetsForTest("Ada"));

        var records = w.Ada.State.TamedCreatures;
        Assert.True(records.Single(t => t.Id == "s3").Travels);
        Assert.True(records.Single(t => t.Id == "s2").Travels);
        Assert.False(records.Single(t => t.Id == "s1").Travels); // the two youngest stay home on Soaniri
        Assert.False(records.Single(t => t.Id == "s0").Travels);
        Assert.Equal(8, records.Count); // nobody is lost
        Assert.Equal(Cap, SvGameServer.PetSlotsUsedOn(w.Ada.State, "earth"));
        Assert.Equal(4, SvGameServer.PetSlotsUsedOn(w.Ada.State, "soaniri")); // two at home, two travelling
        Assert.Equal(0, w.Server.PromoteTravellingPetsForTest("Ada")); // idempotent
    }

    [Fact]
    public void TheStorySoundi_IsNoTravellingPet_AndKeepsItsFreeSlot()
    {
        var w = World("travel_story");
        var story = w.Server.GrantStoryCompanionForTest("Ada", TamedCreature.SoundiKind, "soundi", "Muffin")!;

        Assert.Equal(0, w.Server.PromoteTravellingPetsForTest("Ada"));
        Assert.False(story.Travels);
        Assert.Equal(0, SvGameServer.PetSlotsUsedOn(w.Ada.State, w.Server.ActiveLocationId));
    }
}
