// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>
/// An underground structure of an underground kit, as <c>GameServer.CommitAncientRuin</c> composes a fresh one: a G.D.S. ruin
/// kit (<see cref="AncientRuinGenerator.Compose"/>, the entrance on the crossing the open-land seat tries first) or an
/// author's city / settlement kit composed as on the surface and enclosed as built (<see cref="AncientRuinGenerator.Enclose"/>).
/// The box is the layout (floor row and up); a G.D.S. ruin's flooded basins (Ressaw) and the crystal ore under them come
/// along below <see cref="BuiltStructure.GroundRow"/>. Markers: <c>gds_cache</c> (data = crate number), an enclosed
/// structure's own markers, and <c>entrance</c> at the foot of the shaft (data = the doorway's face, 2 +X / 4 +Z).
/// </summary>
internal sealed class AncientRuinSource : StructureSourceBase
{
    public const string DefaultKit = "ruin_gds_1";

    public const string EntranceMarker = "entrance";

    public AncientRuinSource()
        : base("ancient_ruin", StructureSourceKind.Underground)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Choice("kit", SourceOptions.KitKeys(content.UndergroundKitsFor(null, null), withNone: false), DefaultKit),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        var kit = r.Content.KitByKey(p.Get("kit")) ?? throw new InvalidOperationException("The content holds no underground kit.");
        return Convert(Key, Compose(r, kit), r.Content);
    }

    /// <summary>The layout of a fresh plan for <paramref name="kit"/> (<c>PlanForKit</c> → <c>CommitAncientRuin</c>): the grid
    /// round-trips through its serialized form exactly as the placement record does.</summary>
    internal static AncientRuinLayout Compose(StructureBuildRequest r, StructureKit kit)
    {
        var content = r.Content;
        var pool = DefaultWorld.KitModulePool(content);
        var composition = new List<string>();
        if (AncientRuinGenerator.IsGdsRuinKit(kit) || kit.KindOrDefault == StructureKit.KindCity)
        {
            if (!CityLayoutSpec.TryParse(CityLayoutSpec.FromKit(kit).Serialize(), out var spec))
            {
                throw new InvalidOperationException($"The grid of kit '{kit.Key}' does not read back.");
            }

            if (AncientRuinGenerator.IsGdsRuinKit(kit))
            {
                var (i, j) = FirstCrossing(spec, r.Seed);
                return AncientRuinGenerator.Compose(r.Seed, content, spec, kit, pool, composition, i, j, terrainGeneration: r.Generation);
            }

            var city = CityGenerator.Generate(r.Seed, content, Array.Empty<CityGenerator.OpenZone>(), null, 0.0, composition, null, spec, kit, pool);
            return AncientRuinGenerator.Enclose(r.Seed, content, city, kit.Key, composition);
        }

        string tier = string.IsNullOrWhiteSpace(kit.Tier) ? "village" : kit.Tier;
        if (!SettlementLayoutSpec.TryParse(SettlementLayoutSpec.FromKit(kit, tier, LaneRng(r.Seed, "kitlayout")).Serialize(), out var layout))
        {
            throw new InvalidOperationException($"The grid of kit '{kit.Key}' does not read back.");
        }

        var built = SettlementGenerator.Generate(tier, false, r.Seed, r.Surface, content, null, 0.0, composition, null, layout, kit, pool);
        return AncientRuinGenerator.Enclose(r.Seed, content, built, kit.Key, composition);
    }

    /// <summary>The crossing the open-land seat tries first: the crossings ordered by their seed hash.</summary>
    internal static (int I, int J) FirstCrossing(CityLayoutSpec spec, long seed)
    {
        var best = (I: 1, J: 1);
        double bestU = double.MaxValue;
        foreach (var (i, j) in AncientRuinGenerator.EntranceCrossings(spec))
        {
            double u = StableHashing.Unit($"gds_ruin:cross:{i}:{j}", seed);
            if (u < bestU)
            {
                bestU = u;
                best = (i, j);
            }
        }

        return best;
    }

    /// <summary>The layout's box, its basins and ore below, and its markers.</summary>
    internal static BuiltStructure Convert(string key, AncientRuinLayout layout, GameContent content)
    {
        int shift = layout.BasinColumns.Count > 0 || layout.OreCells.Count > 0 ? AncientRuinGenerator.BasinDepth + 1 : 0;
        var b = new BuiltStructure(key, layout.Width, layout.Height + shift, layout.Length, groundRow: shift);
        BuiltConvert.CopyGrid(b, layout.Width, layout.Height, layout.Length, shift, layout.Get, layout.GetModifier, layout.GetShape);
        ushort ressaw = (content.GetBlock("ressaw") ?? content.GetBlock("water"))?.NumericId.Value ?? 0;
        ushort ore = content.GetBlock("ressaw_crystal_ore")?.NumericId.Value ?? 0;
        foreach (var (x, z) in layout.BasinColumns)
        {
            for (int y = -1; y >= -AncientRuinGenerator.BasinDepth; y--)
            {
                b.Set(x, y + shift, z, ressaw);
            }
        }

        foreach (var c in layout.OreCells)
        {
            if (c.Y + shift >= 0)
            {
                b.Set(c.X, c.Y + shift, c.Z, ore);
            }
        }

        for (int n = 0; n < layout.Caches.Count; n++)
        {
            var c = layout.Caches[n];
            b.Markers.Add(new BuiltMarker(AncientRuinGenerator.CacheMarker, c.X, c.Y + shift, c.Z, n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (layout.Source is { } source)
        {
            BuiltConvert.AddMarkers(b, source.Markers, layout.SourceOffset, shift, layout.SourceOffset);
        }

        b.Markers.Add(new BuiltMarker(EntranceMarker, layout.Shaft.X, shift, layout.Shaft.Z,
            layout.DoorFace.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return b;
    }
}
