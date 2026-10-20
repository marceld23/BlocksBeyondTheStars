// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Creature looks per world (#2868). The client dresses an animal by a planet theme and a per-world random motif; the
/// server tells it, for every creature, which world that look belongs to (<c>NetCreature.LookPlanet</c> /
/// <c>NetCreature.LookSeed</c>).
/// <list type="bullet">
/// <item><b>A roster animal</b> wears the look of the world it is on: its species carries nothing, and the wire takes the
/// active world's values (<see cref="ActiveFaunaLook"/>).</item>
/// <item><b>Whatever leaves its world is stamped at home.</b> A companion's species snapshot and a sampled species in the
/// register are taken on the animal's own world and keep that world's values — so a pet, and every clone grown from a
/// sample, keeps its home look wherever it is shown. A cross takes one parent's, chosen by the cross seed.</item>
/// <item><b>Older records</b> — a register entry or a tamed clone from before the stamp — are given their origin's look
/// when a species is built from them: the entry's origin body, or for a cross its look-giving parent's.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The active world's fauna look: its planet type key and the world look seed of its "system · body" name
    /// (the location key the flora colours use). Kept on the loaded world for its residency.</summary>
    private (string Planet, int Seed) ActiveFaunaLook()
    {
        var world = _worlds.Active;
        if (world.FaunaLook is { } known)
        {
            return known;
        }

        (string Planet, int Seed) look = (world.PlanetType, FaunaLooks.WorldLookSeed(_meta.Seed, ActiveLocationDisplayName()));
        world.FaunaLook = look;
        return look;
    }

    /// <summary>The fauna look of any location: the active world's, or a body the galaxy knows — its planet type and the
    /// seed of its "system · body" name, exactly what that world gives its animals while it is active. Null for an id the
    /// galaxy does not know, or a body without a planet type.</summary>
    private (string Planet, int Seed)? FaunaLookOf(string? locationId)
    {
        if (string.IsNullOrEmpty(locationId))
        {
            return null;
        }

        if (string.Equals(locationId, _worlds.Active.LocationId, StringComparison.Ordinal))
        {
            return ActiveFaunaLook();
        }

        if (ResolveLocationBody(locationId) is not { PlanetType: { Length: > 0 } type })
        {
            return null;
        }

        return (type, FaunaLooks.WorldLookSeed(_meta.Seed, LocationDisplayName(locationId)));
    }

    /// <summary>The name the client keys a world's flora colours by: "system · planet", as it composes it on join.</summary>
    private string LocationDisplayName(string locationId)
    {
        var (system, planet) = LocationNamesFor(locationId);
        return string.IsNullOrEmpty(system) ? planet : $"{system} · {planet}";
    }

    /// <summary>A species copy for a record that leaves its world — a companion's snapshot, a register entry: the look
    /// values it already carries (a clone's, a cross's), else the active world's, where the record is taken.</summary>
    private CreatureSpecies SnapshotSpecies(CreatureSpecies sp) => WithFaunaLook(CloneSpecies(sp), ActiveFaunaLook());

    /// <summary>Fills the look values <paramref name="sp"/> lacks from <paramref name="look"/> (each on its own); the values
    /// it carries stay. Returns the same species.</summary>
    private static CreatureSpecies WithFaunaLook(CreatureSpecies sp, (string Planet, int Seed) look)
    {
        if (string.IsNullOrEmpty(sp.LookPlanet))
        {
            sp.LookPlanet = look.Planet;
        }

        if (sp.LookSeed == 0)
        {
            sp.LookSeed = look.Seed;
        }

        return sp;
    }

    private static bool HasFaunaLook(CreatureSpecies sp) => !string.IsNullOrEmpty(sp.LookPlanet) && sp.LookSeed != 0;

    /// <summary>Whether a cross wears the look of its FIRST parent (the one with the lower seed, <c>ParentA</c>) or of the
    /// second — one bit of the cross seed that no other trait of <see cref="CrossCreatures"/> reads.</summary>
    private static bool CrossLookFromFirst(uint crossSeed) => ((BioHash.Draw(crossSeed, 30) >> 13) & 1) == 0;

    /// <summary>The look a register entry's animal wears: what its snapshot carries; for an entry from before the stamp, a
    /// cross's look-giving parent's, else its origin body's. Null when nothing names a world the galaxy knows — the animal
    /// then wears the look of the world it is on.</summary>
    private (string Planet, int Seed)? FaunaLookOfEntry(BioSpeciesEntry entry, int depth = 0)
    {
        if (entry.Creature is { } sp && HasFaunaLook(sp))
        {
            return (sp.LookPlanet, sp.LookSeed);
        }

        if (entry.IsCross && depth <= BioRules.MaxCrossGeneration
            && BioEntry(CrossLookFromFirst(entry.Seed) ? entry.ParentA : entry.ParentB) is { } parent
            && FaunaLookOfEntry(parent, depth + 1) is { } inherited)
        {
            return inherited;
        }

        return FaunaLookOf(entry.OriginBodyId);
    }

    /// <summary>A cross parent's species with its look resolved, so the cross of an older entry inherits a world too.</summary>
    private CreatureSpecies WithEntryLook(BioSpeciesEntry entry)
    {
        var sp = entry.Creature!;
        return HasFaunaLook(sp) || FaunaLookOfEntry(entry) is not { } look ? sp : WithFaunaLook(CloneSpecies(sp), look);
    }

    /// <summary>The species a companion brings into a world's id table: its snapshot — given its origin's look when it is a
    /// clone tamed before the stamp, so such a pet does not take on the look of the world its tank stood on.</summary>
    private CreatureSpecies CompanionSpeciesWithLook(CreatureSpecies snapshot)
        => HasFaunaLook(snapshot) || GuestSeed(snapshot.Id) is not { } seed || BioEntry(seed) is not { } entry
           || FaunaLookOfEntry(entry) is not { } look
            ? snapshot
            : WithFaunaLook(CloneSpecies(snapshot), look);
}
