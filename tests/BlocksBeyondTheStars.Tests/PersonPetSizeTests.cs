// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3879 (F1 report of 2026-11-21, v2026.11.26: "a big animal in a settlement house — its body shows through the walls"): a
/// villager's household pet was small only relative to its species (× 0.6), so a 3.35-block arachnid followed its person
/// into the shop 4.4 blocks long. A person's pet — a household's or the animal tamer's — is never a giant and never wider
/// than <see cref="SvGameServer.PetMaxRadius"/>; a big species comes as a young one, and an older, bigger pet shrinks.
/// </summary>
public sealed class PersonPetSizeTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Village = "Petsham";

    private static NpcLifeWorld World(string name)
    {
        var w = new NpcLifeWorld(Content.Value, name);
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        w.Server.SetLocalDayFractionForTest(0.40, NpcLifeWorld.Cx);
        return w;
    }

    private static Vector3f At(NpcLifeWorld w, float dx, float dz) => new(NpcLifeWorld.Cx + dx + 0.5f, w.Feet, NpcLifeWorld.Cz + dz + 0.5f);

    /// <summary>Every species of the world becomes the reported animal: a passive land arachnid of size 3.35 — whatever a
    /// person's pet is picked from, it is the big one.</summary>
    private static void OnlyBigGentleAnimals(SvGameServer server)
    {
        foreach (var sp in server.SpeciesRoster)
        {
            sp.Habitat = CreatureHabitat.Land;
            sp.Temperament = CreatureTemperament.Passive;
            sp.BodyPlan = CreatureBodyPlan.Arachnid;
            sp.Size = 3.35f;
            sp.GiantHeight = 0f;
        }
    }

    private static CombatEntity PetOf(SvGameServer server, int npcId)
        => Assert.Single(server.Creatures, c => c.OwnerId == "npc:" + npcId);

    private static float RadiusOf(SvGameServer server, CombatEntity pet)
        => CreatureFootprint.Radius(server.SpeciesRoster.First(sp => sp.Id == pet.SpeciesId), pet.SizeScale);

    [Fact]
    public void AHouseholdPet_OfABigSpecies_ComesSmallEnoughForAHouse_AndAnOlderBigOneShrinks()
    {
        using var w = World("pet_size_house");
        OnlyBigGentleAnimals(w.Server);
        int keeper = w.Server.SpawnVillagerForTest(Village, At(w, 5, 0));
        w.Server.MakePetKeeperForTest(keeper);
        w.Player.State.Position = At(w, 0, 0);

        w.Server.TickProfessionsForTest();
        var pet = PetOf(w.Server, keeper);
        Assert.InRange(RadiusOf(w.Server, pet), 0.1f, SvGameServer.PetMaxRadius + 1e-3f);
        Assert.True(pet.SizeScale < 0.6f, $"a big species' pet is a young one (scale {pet.SizeScale})");

        pet.SizeScale = 0.6f; // the size it had before #3879: 2.2 blocks of radius
        Assert.True(RadiusOf(w.Server, pet) > 2f);
        w.Server.TickProfessionsForTest();
        Assert.InRange(RadiusOf(w.Server, PetOf(w.Server, keeper)), 0.1f, SvGameServer.PetMaxRadius + 1e-3f);
    }

    [Fact]
    public void ASmallSpeciesPet_KeepsItsUsualSize()
    {
        var sp = new CreatureSpecies { Id = "tiny", BodyPlan = CreatureBodyPlan.Standard, Size = 0.8f, BodySegments = 1 };
        Assert.True(CreatureFootprint.Radius(sp, 0.6f) <= SvGameServer.PetMaxRadius);
        Assert.Equal(0.6f, SvGameServer.PetScaleFor(sp, 0.6f));
    }

    [Fact]
    public void TheTamersPet_IsNeverAGiant_AndFitsAHouseToo()
    {
        using var w = new NpcLifeWorld(Content.Value, "pet_size_tamer");
        OnlyBigGentleAnimals(w.Server);
        foreach (var sp in w.Server.SpeciesRoster.Skip(1))
        {
            sp.GiantHeight = 30f; // every species but the first is a giant — the pet must be the first
        }

        int baseId = w.FoundBase();
        var post = new Vector3i(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4);
        w.Set(post.X, post.Y, post.Z, "tamer_post");
        w.Server.ScanBaseLifeForTest();
        var tamer = Assert.Single(w.Server.BaseResidentsForTest(baseId), r => r.Job == "tamer");

        w.Server.TickProfessionsForTest();
        var pet = PetOf(w.Server, tamer.NpcId);
        Assert.Equal(w.Server.SpeciesRoster[0].Id, pet.SpeciesId);
        Assert.InRange(RadiusOf(w.Server, pet), 0.1f, SvGameServer.PetMaxRadius + 1e-3f);

        // A giant pet from before (#3879) is replaced by one a person may keep.
        pet.SpeciesId = w.Server.SpeciesRoster[1].Id;
        w.Server.TickProfessionsForTest();
        w.Server.TickProfessionsForTest();
        Assert.Equal(w.Server.SpeciesRoster[0].Id, PetOf(w.Server, tamer.NpcId).SpeciesId);
    }
}
