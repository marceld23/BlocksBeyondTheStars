// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>
/// A procedural or kit settlement (<see cref="SettlementGenerator.Generate"/>) as <c>GameServer.StampSettlement</c> builds a
/// fresh one: the default world's module pool and module chance, an empty composition (a fresh instance records its picks),
/// and — for a kit — the kit's grid drawn on the <c>kitlayout</c> lane and the kit module pool. A kit brings its own tier.
/// The inhabitants (human or alien) are drawn by the generator from the seed. Built as a generator entry
/// (<see cref="StructureBuildRequest.AsEntry"/>) it is purely procedural: no modules, no kit.
/// </summary>
internal sealed class SettlementSource : StructureSourceBase
{
    public static readonly string[] Tiers = { "hamlet", "village", "town", "city" };

    public SettlementSource()
        : base("settlement", StructureSourceKind.Surface)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Choice("tier", Tiers, "village"),
        StructureParam.Bool("ruined", false),
        StructureParam.Choice("kit", SourceOptions.KitKeys(content.KitsFor(StructureKit.KindSettlement, null, null, null), withNone: true), string.Empty),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var content = r.Content;
        string tier = p.Get("tier");
        if (r.AsEntry)
        {
            // An entry depends on its seed and parameters alone: no plot modules and no kit from the live pools.
            return BuiltConvert.FromSettlement(Key, SettlementGenerator.Generate(tier, p.Bool("ruined"), r.Seed, r.Surface, content,
                terrainGeneration: r.Generation));
        }

        string kitKey = p.Get("kit");
        var kit = kitKey.Length > 0 ? content.KitByKey(kitKey) : null;
        if (kit != null && !string.IsNullOrWhiteSpace(kit.Tier))
        {
            tier = kit.Tier;
        }

        var modules = DefaultWorld.SettlementModules(content, PlanetKey(r));
        double moduleChance = DefaultWorld.ModuleChance(modules.Count);
        SettlementLayoutSpec? layout = kit != null ? SettlementLayoutSpec.FromKit(kit, tier, LaneRng(r.Seed, "kitlayout")) : null;
        var s = SettlementGenerator.Generate(tier, p.Bool("ruined"), r.Seed, r.Surface, content, modules, moduleChance,
            new List<string>(), null, layout, kit, kit != null ? DefaultWorld.KitModulePool(content) : null,
            terrainGeneration: r.Generation);
        return BuiltConvert.FromSettlement(Key, s);
    }
}

/// <summary>A standalone fallen town or city (<c>GameServer.StampRuins</c>): the settlement generator's ruined build, no
/// modules.</summary>
internal sealed class FallenRuinSource : StructureSourceBase
{
    public FallenRuinSource()
        : base("fallen_ruin", StructureSourceKind.Surface)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Choice("tier", new[] { "town", "city" }, "town"),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
        => BuiltConvert.FromSettlement(Key, SettlementGenerator.Generate(p.Get("tier"), ruined: true, r.Seed, r.Surface, r.Content,
            terrainGeneration: r.Generation));
}

/// <summary>
/// The walled city of a city world (<c>GameServer.StampCityWorld</c>, <see cref="CityGenerator.Generate"/>): a fresh city,
/// modules on with the default world's pool and chance, the landing-pad ring at its centre and the wreck crash site as
/// open zones; with a kit, the kit's grid and the kit module pool. The seed is the body's settlement seed. Built as a generator
/// entry (<see cref="StructureBuildRequest.AsEntry"/>) it is purely procedural: no modules, no kit.
/// </summary>
internal sealed class CitySource : StructureSourceBase
{
    // Mirrors of the server's layout constants (GameServerSpace.LandingPadRadius, GameServerWrecks.WreckAnchorOffset and
    // WreckReservedHalfExtent): the city is centred on pad 0, the wreck anchor sits that far off it.
    private const int LandingPadRadius = 8;
    private const int PadMargin = 3;
    private const int WreckAnchorOffset = 56;
    private const int WreckReservedHalfExtent = 14;

    public CitySource()
        : base("city", StructureSourceKind.Surface)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Choice("kit", SourceOptions.KitKeys(content.KitsFor(StructureKit.KindCity, null, null, null), withNone: true), string.Empty),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var content = r.Content;
        string kitKey = r.AsEntry ? string.Empty : p.Get("kit"); // an entry depends on its seed and parameters alone
        var kit = kitKey.Length > 0 ? content.KitByKey(kitKey) : null;
        var spec = kit != null ? CityLayoutSpec.FromKit(kit) : CityLayoutSpec.Default;
        var modules = r.AsEntry ? null : DefaultWorld.SettlementModules(content, PlanetKey(r));
        double moduleChance = modules is null ? 0.0 : DefaultWorld.ModuleChance(modules.Count);
        var s = CityGenerator.Generate(r.Seed, content, OpenZones(spec.Footprint), modules, moduleChance,
            modules is null ? null : new List<string>(), null,
            kit != null ? spec : null, kit, kit != null ? DefaultWorld.KitModulePool(content) : null, legacyModuleRoll: false);
        return BuiltConvert.FromSettlement(Key, s);
    }

    /// <summary>The pad ring and the wreck crash site in city-local coordinates (the city's origin is pad 0 − size / 2).</summary>
    internal static List<CityGenerator.OpenZone> OpenZones(int footprint)
    {
        int c = footprint / 2;
        int padHalf = LandingPadRadius + PadMargin;
        int wx = c - WreckAnchorOffset, wz = c + WreckAnchorOffset;
        return new List<CityGenerator.OpenZone>
        {
            new(c - padHalf, c - padHalf, c + padHalf, c + padHalf),
            new(wx - WreckReservedHalfExtent, wz - WreckReservedHalfExtent, wx + WreckReservedHalfExtent, wz + WreckReservedHalfExtent),
        };
    }
}
