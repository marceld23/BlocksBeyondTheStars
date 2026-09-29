// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>One ore vein as the planet scanner reads it (#2140).</summary>
public sealed class SurveyedVein
{
    /// <summary>The vein's block key ("iron_ore").</summary>
    public string Block { get; set; } = string.Empty;

    /// <summary>The depth below the surface where the vein starts.</summary>
    public int MinDepth { get; set; }

    /// <summary>This world's effective vein density — the type's rarity × the frontier boost (rare-tier veins only) ×
    /// the world's ore richness: exactly the number the terrain's ore slot is built with.</summary>
    public double Density { get; set; }

    /// <summary>The vein needs a tier-2 drill.</summary>
    public bool RareTier { get; set; }
}

/// <summary>Everything the planet scanner (#2140) reports about one body's resources.</summary>
public sealed class ResourceSurvey
{
    /// <summary>This world's ore-richness multiplier (per-world roll × the world's ore option), 1.0 = the type's rarities.</summary>
    public double Richness { get; set; }

    /// <summary>The planet type's veins, as dense as this world generates them — richest first.</summary>
    public List<SurveyedVein> Veins { get; set; } = new();

    /// <summary>Oil pockets deep underground (living worlds, generation 18+).</summary>
    public bool OilPockets { get; set; }

    /// <summary>Data caches in the deep rock.</summary>
    public bool DataCaches { get; set; }

    /// <summary>Clumps of the rare-tier veins lie on the surface (a toxic world's rolled trait, generation 13+).</summary>
    public bool SurfaceOutcrops { get; set; }

    /// <summary>Rare metals on deep crater floors (cratered bodies and airless moons).</summary>
    public bool CraterMetals { get; set; }

    /// <summary>A gas giant — the veins lie only in the floating islands above the gas sea.</summary>
    public bool GasWorld { get; set; }
}

/// <summary>
/// The planet scanner's reading of a body (#2140 — Bloody Mary's "Ressourcenscan": "which resources does this planet
/// have?"). A pure query: it recomputes the per-body seed and the per-world rolls from the same functions the terrain
/// uses (<see cref="PerWorldOreRichness"/>, the world ore option, the frontier boost, <see cref="WorldTraits"/>, the oil
/// rule), so the scan can never promise an ore the ground doesn't hold — and it never touches the shared generator's
/// world mode, so asking about the moon next door leaves the active world's caches alone.
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>Surveys <paramref name="planet"/> as the body <paramref name="locationId"/> generates it.
    /// <paramref name="frontierOreBoost"/> and <paramref name="cratered"/> are the values the server stamps on that body's
    /// world (the frontier tier's rare-vein boost; an airless moon's crater regolith).</summary>
    public ResourceSurvey SurveyResources(PlanetType planet, string locationId, double frontierOreBoost, bool cratered)
    {
        long locationSalt = string.IsNullOrEmpty(locationId) ? 0L : StableHash(locationId);
        long seed = _worldSeed ^ StableHash(planet.Key) ^ locationSalt; // = PlanetSeed for that body
        double richness = PerWorldOreRichness(seed) * _oreFactor;       // = the Columns pass's oreRichness

        var survey = new ResourceSurvey { Richness = richness, GasWorld = planet.IsGasWorld };
        if (planet.Void)
        {
            return survey;
        }

        foreach (var ore in planet.Ores)
        {
            double rarity = ore.RareTier ? ore.Rarity * frontierOreBoost : ore.Rarity; // = BuildOreSlots
            survey.Veins.Add(new SurveyedVein
            {
                Block = ore.Block,
                MinDepth = ore.MinDepth,
                Density = rarity * richness,
                RareTier = ore.RareTier,
            });
        }

        survey.Veins.Sort((a, b) => b.Density.CompareTo(a.Density));
        survey.DataCaches = planet.DataCacheRarity > 0;
        survey.CraterMetals = planet.Cratered || cratered;
        survey.OilPockets = _terrainGeneration >= WorldDescription.OilGeneration
                            && planet.HasLife && !planet.Cratered && !cratered; // = HasOilPockets for that body
        survey.SurfaceOutcrops = _terrainGeneration >= WorldDescription.ToxicWorldsGeneration
                                 && !planet.Cratered
                                 && WorldTraits.For(planet, RosterSeedFor(_worldSeed, locationId), _terrainGeneration).OreOutcrops;
        return survey;
    }
}
