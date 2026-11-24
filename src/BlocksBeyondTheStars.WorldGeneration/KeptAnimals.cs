// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The Earth animals the people of a world keep (#3976): the species a record marks <see cref="AuthoredCreature.Pet"/> (the
/// dog, the cat) for a household's or the animal tamer's pet, and the ones it marks <see cref="AuthoredCreature.Pen"/> (the
/// cow, the sheep, the chicken) for the pens — also where the world's roster does not hold them.
/// <list type="bullet">
/// <item><b>Where.</b> On a world of generation <see cref="WorldDescription.EarthSpeciesGeneration"/> or later with air to
/// breathe, every kept record; on a home world of a record (its <see cref="AuthoredCreature.Homes"/> — Earth) that record
/// whatever the generation. Nowhere else, so every older world keeps its pets and pens.</item>
/// <item><b>Which instance.</b> The roster's own species when the roster holds it (Earth, a world with wild dogs), else the
/// record built for this world with its key-salted sub-seed (<see cref="CreatureGenerator.MakeAuthored"/> — the same species
/// a stray of that world would be). The server registers the latter in its species table, not in the roster, so they are
/// drawn and moved like any animal but never spawn wild and never call from afar.</item>
/// <item><b>The mix</b> (<see cref="PoolFor"/>). On a home world of a pool's species every pick is one of them — Earth's
/// households keep dogs and cats, its pens cows, sheep and chickens. Elsewhere a stable hash of the person or the pen sends
/// half of the picks to the kept species and half to the world's own candidates; a world without a candidate of its own
/// takes the kept species every time.</item>
/// <item><b>The product.</b> A pen of a kept species lays its record's item (<see cref="PenProductOf"/>) instead of the shape
/// rule's.</item>
/// </list>
/// </summary>
public sealed class KeptAnimals
{
    /// <summary>No kept species: every pick is the world's own (an older world, a world without air).</summary>
    public static readonly KeptAnimals None = new(new List<CreatureSpecies>(), false, new List<CreatureSpecies>(), false,
        new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string> _products;
    private readonly HashSet<string> _petIds;

    private KeptAnimals(List<CreatureSpecies> pets, bool petsAtHome, List<CreatureSpecies> penAnimals, bool pensAtHome,
        IReadOnlyDictionary<string, string> products)
    {
        Pets = pets;
        PetsAtHome = petsAtHome;
        PenAnimals = penAnimals;
        PensAtHome = pensAtHome;
        _products = products;
        _petIds = new HashSet<string>(pets.Select(p => p.Id), System.StringComparer.Ordinal);
    }

    /// <summary>The kept pet species of this world, in the order of <c>data/creatures.json</c>.</summary>
    public IReadOnlyList<CreatureSpecies> Pets { get; }

    /// <summary>True when this world is a home of a pet species: every pet is a kept one.</summary>
    public bool PetsAtHome { get; }

    /// <summary>The kept pen species of this world, in the order of <c>data/creatures.json</c>.</summary>
    public IReadOnlyList<CreatureSpecies> PenAnimals { get; }

    /// <summary>True when this world is a home of a pen species: every pen keeps a kept one.</summary>
    public bool PensAtHome { get; }

    /// <summary>Whether the species <paramref name="speciesId"/> is a kept pet of this world.</summary>
    public bool IsKeptPet(string speciesId) => _petIds.Contains(speciesId);

    /// <summary>Whether the species <paramref name="speciesId"/> is a kept species of this world (a pet or a pen animal).</summary>
    public bool IsKept(string speciesId) => _petIds.Contains(speciesId) || _products.ContainsKey(speciesId);

    /// <summary>The item a pen of <paramref name="speciesId"/> lays, or null for a species that is no kept pen animal (the
    /// shape rule decides then).</summary>
    public string? PenProductOf(string speciesId) => _products.TryGetValue(speciesId, out var item) ? item : null;

    /// <summary>Whether <paramref name="record"/> is kept on a world of <paramref name="planet"/>'s type and generation
    /// <paramref name="generation"/> (see the type summary): never in a void; at its home always; elsewhere from the
    /// generation on, where there is air to breathe.</summary>
    public static bool KeptOn(AuthoredCreature record, PlanetType planet, int generation)
        => !planet.Void
           && (record.Homes.Contains(planet.Key)
               || (generation >= WorldDescription.EarthSpeciesGeneration && planet.IsBreathable));

    /// <summary>The kept species of a world: <paramref name="records"/> are the kept records
    /// (<c>GameContent.KeptAnimalRecords</c>), <paramref name="roster"/> the world's roster, <paramref name="rosterSeed"/> its
    /// roster seed (<see cref="WorldGenerator.RosterSeedFor"/>). <see cref="None"/> when no record is kept there.</summary>
    public static KeptAnimals For(PlanetType planet, long rosterSeed, int generation, IReadOnlyList<CreatureSpecies> roster,
        IReadOnlyList<AuthoredCreature> records)
    {
        var pets = new List<CreatureSpecies>();
        var pens = new List<CreatureSpecies>();
        var products = new Dictionary<string, string>(System.StringComparer.Ordinal);
        bool petsAtHome = false, pensAtHome = false;
        foreach (var record in records)
        {
            if (!KeptOn(record, planet, generation))
            {
                continue;
            }

            string id = AuthoredSpeciesMapping.IdOf(record.Key);
            var sp = roster.FirstOrDefault(s => s.Id == id) ?? CreatureGenerator.MakeAuthored(record, planet, rosterSeed);
            bool home = record.Homes.Contains(planet.Key);
            if (record.Pet)
            {
                pets.Add(sp);
                petsAtHome |= home;
            }

            if (!string.IsNullOrEmpty(record.Pen))
            {
                pens.Add(sp);
                products[sp.Id] = record.Pen;
                pensAtHome |= home;
            }
        }

        return pets.Count == 0 && pens.Count == 0 ? None : new KeptAnimals(pets, petsAtHome, pens, pensAtHome, products);
    }

    /// <summary>The species a pick of <paramref name="key"/> (a person's or a pen's own key) chooses among: the world's own
    /// <paramref name="native"/> candidates when there is no <paramref name="kept"/> species (an older world: exactly as
    /// before); the kept ones on their home world (<paramref name="atHome"/>) or where the world has no candidate of its own;
    /// otherwise one or the other by a stable hash of the key and <paramref name="seed"/> (half and half).</summary>
    public static IReadOnlyList<CreatureSpecies> PoolFor(string key, long seed, IReadOnlyList<CreatureSpecies> kept,
        IReadOnlyList<CreatureSpecies> native, bool atHome)
    {
        if (kept.Count == 0)
        {
            return native;
        }

        if (atHome || native.Count == 0)
        {
            return kept;
        }

        return StableHashing.Index("kept-animal:" + key, seed, 2) == 0 ? kept : native;
    }
}
