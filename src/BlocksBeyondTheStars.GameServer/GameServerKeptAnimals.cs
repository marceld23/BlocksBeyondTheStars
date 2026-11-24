// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A body's species beyond what one call site rolls (#3976): the one roster formula every caller shares — with the Earth
/// animals that live wild elsewhere (<see cref="StrayAnimals"/>) — and the Earth animals the people of the world keep
/// (<see cref="KeptAnimals"/>): a dog or a cat as a person's pet, a cow, a sheep or a chicken in a pen, also where the roster
/// does not hold them. Such a species is registered in the world's species table (ids, movement tuning, so every creature
/// message carries its traits to the client) but not in the roster: it never spawns wild there, and it does not call from
/// afar (the distant-fauna roster is the roster) — a village dog barks beside its person, not in the wilderness.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The roster body <paramref name="bodyId"/> of type <paramref name="planet"/> rolls in this save — THE formula the
    /// live world (<see cref="InitCreatures"/>) and the ship's overview scan of another body both read, so the scan names the
    /// animals the landing meets.</summary>
    private IReadOnlyList<CreatureSpecies> RosterFor(PlanetType planet, string bodyId)
        => CreatureGenerator.GenerateRoster(planet, WorldGenerator.RosterSeedFor(_meta.Seed, bodyId),
            _meta.Description.TerrainGeneration, _content.AuthoredCreaturesFor(planet), _content.StrayAnimalRecords);

    /// <summary>The kept animals of the active world (<see cref="KeptAnimals.None"/> on an older world).</summary>
    private KeptAnimals Kept => _worlds.Active.Kept;

    /// <summary>Works out the active world's kept animals after its roster (<see cref="InitCreatures"/>) and registers every
    /// one the roster does not hold in the species table.</summary>
    private void InitKeptAnimals(PlanetType? planet)
    {
        _worlds.Active.Kept = planet is null
            ? KeptAnimals.None
            : KeptAnimals.For(planet, WorldGenerator.RosterSeedFor(_meta.Seed, _world.LocationId), _meta.Description.TerrainGeneration,
                _speciesRoster, _content.KeptAnimalRecords);
        foreach (var sp in Kept.Pets)
        {
            RegisterKeptSpecies(sp);
        }

        foreach (var sp in Kept.PenAnimals)
        {
            RegisterKeptSpecies(sp);
        }
    }

    private void RegisterKeptSpecies(CreatureSpecies sp)
    {
        if (!_speciesById.ContainsKey(sp.Id))
        {
            _speciesById[sp.Id] = sp;
            _locoProfiles[sp.Id] = LocomotionController.ForSpecies(sp);
        }
    }

    /// <summary>The size factor a person's pet of <paramref name="sp"/> prefers: a kept Earth pet comes at its own size (a dog
    /// is no puppy), every other species at <paramref name="preferred"/> — both capped by <see cref="PetScaleFor"/>.</summary>
    private float PreferredPetScale(CreatureSpecies sp, float preferred) => Kept.IsKeptPet(sp.Id) ? 1f : preferred;

    /// <summary>The size factor a pen's animal of <paramref name="sp"/> is drawn at: a kept Earth species at its own size, every
    /// other species a little smaller (0.8, as since #3597).</summary>
    private float PenAnimalScale(CreatureSpecies sp) => Kept.IsKept(sp.Id) ? 1f : 0.8f;

    /// <summary>What a pen of <paramref name="sp"/> lays: a kept species' own item (milk, wool, eggs), else the shape rule.</summary>
    private string PenProductFor(CreatureSpecies sp) => Kept.PenProductOf(sp.Id) ?? PenProductOf(sp);

    /// <summary>Test seam: the active world's kept pet and pen species ids.</summary>
    internal (IReadOnlyList<string> Pets, IReadOnlyList<string> PenAnimals) KeptAnimalsForTest
        => (Kept.Pets.Select(s => s.Id).ToList(), Kept.PenAnimals.Select(s => s.Id).ToList());

    /// <summary>Test seam: the entity id, species and size factor of every animal walking with an NPC on the active world.</summary>
    internal IReadOnlyList<(string Id, string SpeciesId, float Scale)> NpcPetScalesForTest
        => _creatures.Where(c => c.OwnerId.StartsWith(NpcPetOwnerPrefix, System.StringComparison.Ordinal))
            .Select(c => (c.Id, c.SpeciesId, c.SizeScale)).ToList();

    /// <summary>Test seam: every pen of the active world with the species it keeps and the item it lays (empty when none).</summary>
    internal IReadOnlyList<(string Key, string SpeciesId, string Product)> PenAnimalsForTest
        => _worlds.Active.SettlementPens
            .Select(p => PenSpecies(p) is { } sp ? (p.Key, sp.Id, PenProductFor(sp)) : (p.Key, string.Empty, string.Empty))
            .ToList();

    /// <summary>Test seam: whether the active world's species table knows <paramref name="speciesId"/>.</summary>
    internal bool KnowsSpeciesForTest(string speciesId) => _speciesById.ContainsKey(speciesId);
}
