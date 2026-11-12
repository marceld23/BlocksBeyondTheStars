// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>
/// A monument (<see cref="MonumentGenerator.Generate"/>) as <c>GameServer.BuildMonument</c> builds one: the instance rng
/// draws whether the relic hides a cache (roughly one in three), then the generator runs — weathered as in a world of the
/// request's generation (no flora on a planet type without air).
/// </summary>
internal sealed class MonumentSource : StructureSourceBase
{
    /// <summary>The server's cache chance (<c>GameServer.BuildMonument</c>).</summary>
    private const double CacheChance = 0.35;

    public MonumentSource()
        : base("monument", StructureSourceKind.Surface)
    {
    }

    /// <summary>The archetype pool of a terrain generation, exactly as the server picks it.</summary>
    public static IReadOnlyList<string> PoolFor(int generation)
        => generation >= WorldDescription.SpectacleGeneration ? MonumentGenerator.ArchetypesGen21
            : generation >= 1 ? MonumentGenerator.ArchetypesGen1 : MonumentGenerator.Archetypes;

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Choice("archetype", PoolFor(WorldDescription.CurrentTerrainGeneration)),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        bool withCache = InstanceRng(r.Seed).NextDouble() < CacheChance;
        bool airless = PlanetKey(r) is { } key && r.Content.GetPlanet(key) is { IsAirless: true };
        return BuiltConvert.FromSettlement(Key, MonumentGenerator.Generate(p.Get("archetype"), r.Seed, r.Surface, r.Content, withCache,
            r.Generation, airless));
    }
}

/// <summary>A bandit camp (<see cref="BanditCampGenerator.Generate"/>).</summary>
internal sealed class BanditCampSource : StructureSourceBase
{
    public BanditCampSource()
        : base("bandit_camp", StructureSourceKind.Surface)
    {
    }

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
        => BuiltConvert.FromSettlement(Key, BanditCampGenerator.Generate(r.Seed, r.Surface, r.Content));
}

/// <summary>
/// A wilderness lodge (<see cref="LodgeGenerator.Generate"/>) as <c>GameServer.StampLodges</c> builds one: the profession's
/// yard, the planet's tier-1 ores (none without a planet — a quarry face of plain stone) and the alien trim drawn on the
/// <c>alien</c> lane (30 %) unless the request forces it.
/// </summary>
internal sealed class LodgeSource : StructureSourceBase
{
    /// <summary>The server's alien-trim chance (<c>GameServer.StampLodges</c>).</summary>
    private const double AlienChance = 0.3;

    public LodgeSource()
        : base("lodge", StructureSourceKind.Surface)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content)
    {
        var jobs = new List<string>();
        foreach (var l in content.Lodges)
        {
            jobs.Add(l.Key);
        }

        return new[]
        {
            StructureParam.Choice("profession", jobs),
            StructureParam.Choice("alien", SourceOptions.Tri, SourceOptions.Auto),
        };
    }

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var profession = r.Content.LodgeProfession(p.Get("profession"))
                         ?? throw new InvalidOperationException("The content holds no lodge profession (data/lodges.json).");
        bool alien = p.Tri("alien") ?? LaneRng(r.Seed, "alien").NextDouble() < AlienChance;
        var s = LodgeGenerator.Generate(r.Seed, profession.Yard, r.Surface, Tier1Ores(r), alien, r.Content);
        return BuiltConvert.FromSettlement(Key, s);
    }

    /// <summary>The planet's own tier-1 ore blocks, as <c>GameServer.Tier1OresOf</c> lists them.</summary>
    internal static List<string> Tier1Ores(StructureBuildRequest r)
    {
        var ores = new List<string>();
        if (PlanetKey(r) is not { } key || r.Content.GetPlanet(key) is not { } planet)
        {
            return ores;
        }

        foreach (var o in planet.Ores)
        {
            if (!o.RareTier && r.Content.GetBlock(o.Block) is { Category: "ore", MinToolTier: <= 1 } && !ores.Contains(o.Block))
            {
                ores.Add(o.Block);
            }
        }

        return ores;
    }
}

/// <summary>An SPS lab (<see cref="SpsLabGenerator.Generate"/>) — weathered as it stands in a world of the request's
/// generation.</summary>
internal sealed class SpsLabSource : StructureSourceBase
{
    public SpsLabSource()
        : base("sps_lab", StructureSourceKind.Surface)
    {
    }

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
        => BuiltConvert.FromSettlement(Key, SpsLabGenerator.Generate(r.Seed, r.Surface, r.Content, r.Generation));
}

/// <summary>A factory hall (<see cref="FactoryGenerator.Generate"/>): one machine bay per recipe of its roster.</summary>
internal sealed class FactorySource : StructureSourceBase
{
    public FactorySource()
        : base("factory", StructureSourceKind.Surface)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Int("machines", 3, 1, 6),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
        => BuiltConvert.FromSettlement(Key, FactoryGenerator.Generate(r.Seed, p.Int("machines"), r.Content));
}
