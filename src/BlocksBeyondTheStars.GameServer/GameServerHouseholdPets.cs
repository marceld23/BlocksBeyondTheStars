// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, the household pets (docs/developer/LIVELY_PLACES.md): up to two households of a settlement (six of a
/// metropolis, #4017) keep a
/// small peaceful animal of the world (from generation 49 also a dog or a cat, #3976 — <see cref="PersonPetSpecies"/>) that
/// walks with its person — on the errand round, to the plaza, home again. It is the animal tamer's pet
/// (<see cref="SyncTamerPets"/>) for people without that profession: owned by the NPC
/// (<c>npc:&lt;id&gt;</c>), so it follows through the same code, is spared by the town's creature rule like every owned
/// creature, and is nobody's to tame or to harm.
/// <para>Who keeps one is decided when the residents are spawned, from the people's own keys — no draw from the world's
/// name stream. The animal itself exists only while a player on foot is near its person: a world with eight villages does
/// not simulate sixteen pets nobody sees.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How many households of one settlement keep a pet.</summary>
    internal const int HouseholdPetsPerPlace = 2;

    /// <summary>#4017: how many households of a metropolis keep one — a few more for its eighty residents, still a handful, so
    /// a walk through the streets meets a dog or a cat now and then. The first two keepers are the ones a place of any size
    /// picks, so no village and no older keeper changes.</summary>
    internal const int MetropolisPetsPerPlace = 6;

    /// <summary>How many households of a settlement of <paramref name="tier"/> keep a pet.</summary>
    internal static int HouseholdPetsFor(string tier) => tier == CityGenerator.Tier ? MetropolisPetsPerPlace : HouseholdPetsPerPlace;

    /// <summary>A pet appears when a player on foot comes this close to its person …</summary>
    private const float HouseholdPetAppear = 72f;

    /// <summary>… and is gone again when nobody is within this range (more than the first, so it does not flicker).</summary>
    private const float HouseholdPetVanish = 128f;

    /// <summary>A household pet is a small one — at most this share of its species' size, and never wider than
    /// <see cref="PetMaxRadius"/> (<see cref="PetScaleFor"/>).</summary>
    private const float HouseholdPetScale = 0.6f;

    /// <summary>The size factor the animal tamer's pet prefers (<see cref="SyncTamerPets"/>), capped the same way.</summary>
    private const float TamerPetScale = 0.8f;

    /// <summary>#3879: the largest ground radius in blocks (<see cref="CreatureFootprint.Radius"/>) a person's pet may have,
    /// so its drawn body fits a house's rooms and doorway. A household pet used to be small only relative to its species
    /// (× 0.6): a 3.35-block arachnid followed its person into the shop 4.4 blocks long and stood half through the walls —
    /// the server tracks a creature as one point, and nothing else keeps a wide body off a wall.</summary>
    internal const float PetMaxRadius = 0.8f;

    /// <summary>The smallest size factor a pet is drawn at (the client's builders clamp a tiny body anyway).</summary>
    private const float PetMinScale = 0.1f;

    /// <summary>#3879: the species a person's pet may be — a peaceful land animal of the world, never a giant (the pens'
    /// rule; a giant moves by its own rules and is never anybody's pet).</summary>
    private static bool IsPersonPetSpecies(CreatureSpecies sp)
        => sp.Habitat == CreatureHabitat.Land
           && (sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish)
           && sp.GiantHeight <= 0f;

    /// <summary>The species of a person's pet — a household's or the animal tamer's — picked by <paramref name="key"/>, the
    /// person's own key (<c>house-pet:</c> or <c>tamer-pet:</c> and the name): a stable hash, no draw from any shared
    /// generator. Null when the world has no species a person may keep. #3976: from generation 49 the pick may be a kept Earth
    /// pet (a dog, a cat) — every pick on a home world of one, half of them elsewhere (<see cref="KeptAnimals.PoolFor"/>); an
    /// older world picks among its roster exactly as before.</summary>
    private CreatureSpecies? PersonPetSpecies(string key)
    {
        var native = _speciesRoster.Where(IsPersonPetSpecies).ToList();
        var pool = KeptAnimals.PoolFor(key, _meta.Seed, Kept.Pets, native, Kept.PetsAtHome);
        return pool.Count == 0 ? null : pool[(int)((uint)WorldGenerator.StableHash(key) % (uint)pool.Count)];
    }

    /// <summary>Whether the world has any species a person may keep (<see cref="PersonPetSpecies"/> is never null then).</summary>
    private bool AnyPersonPetSpecies => Kept.Pets.Count > 0 || _speciesRoster.Any(IsPersonPetSpecies);

    /// <summary>#3879: the size factor a person's pet of <paramref name="sp"/> is drawn at — <paramref name="preferred"/> when
    /// its body fits within <see cref="PetMaxRadius"/>, otherwise less until it does: a big species comes as a young one.
    /// The footprint follows the drawn size, but not quite in proportion near the builders' clamps, so it is refined.</summary>
    internal static float PetScaleFor(CreatureSpecies sp, float preferred)
    {
        float scale = preferred;
        for (int i = 0; i < 4; i++)
        {
            float radius = CreatureFootprint.Radius(sp, scale);
            if (radius <= PetMaxRadius || scale <= PetMinScale)
            {
                break;
            }

            scale = Math.Max(PetMinScale, scale * PetMaxRadius / radius * 0.98f);
        }

        return scale;
    }

    /// <summary>#3879: brings a person's existing pet to its size — one that appeared under the old rule shrinks on the next
    /// pass. True when it changed (the caller re-broadcasts).</summary>
    private bool FitPetSize(CombatEntity pet, float preferred)
    {
        if (!_speciesById.TryGetValue(pet.SpeciesId, out var sp))
        {
            return false;
        }

        float scale = PetScaleFor(sp, PreferredPetScale(sp, preferred));
        if (Math.Abs(pet.SizeScale - scale) < 1e-4f)
        {
            return false;
        }

        pet.SizeScale = scale;
        return true;
    }

    /// <summary>Picks the pet keepers among a settlement's people without a post (not the night watch, who sleeps by day):
    /// the ones whose person key hashes lowest — stable for a world, and no draw from any shared generator. How many follows
    /// the settlement's <paramref name="tier"/> (<see cref="HouseholdPetsFor"/>).</summary>
    private static void AssignHouseholdPets(List<ServerNpc> plainResidents, string tier)
    {
        var keepers = plainResidents
            .Where(n => n.Job.Length == 0 && n.PersonKey.Length > 0 && !n.IsRobot)
            .OrderBy(n => (uint)WorldGenerator.StableHash("house-pet:" + n.PersonKey))
            .Take(HouseholdPetsFor(tier));
        foreach (var npc in keepers)
        {
            npc.HasPet = true;
        }
    }

    /// <summary>Once a second per world (after <see cref="SyncTamerPets"/>, which also removes the pet of a keeper who is
    /// gone): gives every pet keeper near a player its animal and takes away the ones nobody is near.</summary>
    private void SyncHouseholdPets()
    {
        if (_world.Planet?.Void == true || Rules.CreatureAbundance == AlienActivity.Off)
        {
            return; // no animals on a station or in a world whose wildlife is switched off (the tamer pass removed them)
        }

        bool changed = false;
        foreach (var npc in _npcs)
        {
            if (!npc.HasPet || npc.VisitingAway)
            {
                continue;
            }

            string owner = NpcPetOwnerPrefix + npc.Id;
            int at = _creatures.FindIndex(c => c.OwnerId == owner);
            if (at >= 0)
            {
                if (!AnyPlayerOnFootWithin(npc.Pos, HouseholdPetVanish))
                {
                    _creatures.RemoveAt(at);
                    changed = true;
                }
                else
                {
                    changed |= FitPetSize(_creatures[at], HouseholdPetScale);
                }

                continue;
            }

            if (!AnyPlayerOnFootWithin(npc.Pos, HouseholdPetAppear))
            {
                continue;
            }

            if (PersonPetSpecies("house-pet:" + npc.Name) is not { } species)
            {
                return;
            }

            _creatures.Add(HouseholdPet(species, owner, npc.Pos));
            changed = true;
        }

        if (changed)
        {
            BroadcastCreatures();
        }
    }

    /// <summary>A keeper's animal: a small tame one of the species, beside its person.</summary>
    private CombatEntity HouseholdPet(CreatureSpecies species, string owner, Vector3f keeperPos) => new()
    {
        Id = NextEntityId(),
        Kind = CombatEntityKind.Creature,
        SpeciesId = species.Id,
        Hostile = false,
        Hull = species.MaxHealth,
        HullMax = species.MaxHealth,
        Position = CompanionSpotNear(species, owner, keeperPos),
        DamagePerSecond = 0f,
        SizeScale = PetScaleFor(species, PreferredPetScale(species, HouseholdPetScale)), // #3879: never wider than a house allows
        AnimalSeed = CoatVariants.AnimalSeedOf(owner), // #3972: always the same cat for its keeper
        OwnerId = owner,
        CompanionId = owner,
    };

    /// <summary>Test seam: the ids of the people of the active world who keep a household pet.</summary>
    internal IReadOnlyList<int> PetKeepersForTest => _npcs.Where(n => n.HasPet).Select(n => n.Id).ToList();

    /// <summary>Test seam: makes a villager a pet keeper.</summary>
    internal void MakePetKeeperForTest(int npcId)
    {
        foreach (var n in _npcs)
        {
            if (n.Id == npcId)
            {
                n.HasPet = true;
            }
        }
    }
}
