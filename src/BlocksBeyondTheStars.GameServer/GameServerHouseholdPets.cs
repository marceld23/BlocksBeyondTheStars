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
/// The lively places, the household pets (docs/developer/LIVELY_PLACES.md): up to two households of a settlement keep a
/// small peaceful animal of the world that walks with its person — on the errand round, to the plaza, home again. It is
/// the animal tamer's pet (<see cref="SyncTamerPets"/>) for people without that profession: owned by the NPC
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

    /// <summary>A pet appears when a player on foot comes this close to its person …</summary>
    private const float HouseholdPetAppear = 72f;

    /// <summary>… and is gone again when nobody is within this range (more than the first, so it does not flicker).</summary>
    private const float HouseholdPetVanish = 128f;

    /// <summary>A household pet is a small one.</summary>
    private const float HouseholdPetScale = 0.6f;

    /// <summary>Picks the pet keepers among a settlement's people without a post (not the night watch, who sleeps by day):
    /// the ones whose person key hashes lowest — stable for a world, and no draw from any shared generator.</summary>
    private static void AssignHouseholdPets(List<ServerNpc> plainResidents)
    {
        var keepers = plainResidents
            .Where(n => n.Job.Length == 0 && n.PersonKey.Length > 0 && !n.IsRobot)
            .OrderBy(n => (uint)WorldGenerator.StableHash("house-pet:" + n.PersonKey))
            .Take(HouseholdPetsPerPlace);
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

        List<CreatureSpecies>? candidates = null;
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

                continue;
            }

            if (!AnyPlayerOnFootWithin(npc.Pos, HouseholdPetAppear))
            {
                continue;
            }

            candidates ??= _speciesRoster
                .Where(sp => sp.Habitat == CreatureHabitat.Land && sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish)
                .ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            var species = candidates[(int)((uint)WorldGenerator.StableHash("house-pet:" + npc.Name) % (uint)candidates.Count)];
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
        SizeScale = HouseholdPetScale,
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
