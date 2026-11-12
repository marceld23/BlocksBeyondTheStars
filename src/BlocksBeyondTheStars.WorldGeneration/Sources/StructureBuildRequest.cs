// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>
/// What a structure source is asked to build (#3512): the content, the instance seed (the seed the server hands the
/// generator for one instance), the parameter values (missing ones take their defaults), the biome surface the
/// materials follow, the planet type (filters the module pools and gives a lodge its ores; empty = no planet filter),
/// the terrain generation the build stands for and whether the build shows the structure as it stands in a world
/// (with the generator's own decay) or intact where the source has an intact form
/// (<see cref="IStructureSource.SupportsIntact"/>).
/// </summary>
public sealed class StructureBuildRequest
{
    private static readonly IReadOnlyDictionary<string, string> NoParams = new Dictionary<string, string>(StringComparer.Ordinal);

    public StructureBuildRequest(GameContent content, long seed)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Seed = seed;
    }

    public GameContent Content { get; }

    /// <summary>The instance seed handed to the generator.</summary>
    public long Seed { get; set; }

    /// <summary>Parameter values by key (see <see cref="IStructureSource.GetParams"/>); an unknown key is ignored, an
    /// invalid value falls back to the parameter's default.</summary>
    public IReadOnlyDictionary<string, string> Params { get; set; } = NoParams;

    /// <summary>The biome surface block key the materials, rubble and overgrowth follow (the server passes the planet's
    /// first biome surface).</summary>
    public string Surface { get; set; } = "stone";

    /// <summary>The planet type key, or empty for none: filters the settlement module pool like the server does for that
    /// planet and gives a lodge the planet's tier-1 ores (none = a quarry face of plain stone).</summary>
    public string PlanetType { get; set; } = string.Empty;

    /// <summary>The terrain generation the build stands for (<see cref="WorldDescription.TerrainGeneration"/>).</summary>
    public int Generation { get; set; } = WorldDescription.CurrentTerrainGeneration;

    /// <summary>True: the structure as it stands in a world (with the generator's own decay or ruin pass). False: intact,
    /// where the source has an intact form (<see cref="IStructureSource.SupportsIntact"/>); ignored otherwise.</summary>
    public bool AsInWorld { get; set; } = true;

    /// <summary>True when the build stands for a generator entry in a pool (<see cref="SourceTemplates.Build"/>): its output
    /// must depend on the seed and the parameters alone, so a settlement or city builds purely procedurally — no plot
    /// modules, no kit — and a later module or kit in the content never changes what an existing world replays. False (the
    /// editor's "generate"): the default world's module pool and the chosen kit, like a fresh instance.</summary>
    public bool AsEntry { get; set; }
}
