// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// What the generator would put into a water cell (#2515) — the class the fluid automaton needs to tell an endless
/// body (fed by something the world never runs out of) from still water that holds only what it holds.
/// </summary>
public enum GeneratedWaterKind : byte
{
    /// <summary>Still water above the sea level — a pond, a lake, a generation-1 body, a hall or island pool — or a
    /// cell the generator does not fill at all (water a settle moved there). Finite.</summary>
    Still = 0,

    /// <summary>At or below the planet's sea level ("NN"): the sea itself and everything that low — endless.</summary>
    BelowSeaLevel = 1,

    /// <summary>A routed river reach on the surface — endless (fed from upstream).</summary>
    River = 2,

    /// <summary>The standing column of a river waterfall above the lower reach — endless, and it only falls.</summary>
    RiverFall = 3,

    /// <summary>A generated waterfall band (a sky-island, pillar or balcony fall) — endless, and it only falls.</summary>
    Waterfall = 4,

    /// <summary>The water of an underground river reach (terrain generation 3) — endless.</summary>
    UndergroundRiver = 5,
}

/// <summary>
/// The endless water of one column (#2515): the sea line, up to three generated waterfall bands and the river's water span
/// (with the height its fall starts at). <see cref="KindAt"/> answers for any height without asking the generator again,
/// so a caller can memoise one value per column.
/// </summary>
public readonly struct WaterColumnKinds
{
    private readonly int _seaLevel;
    private readonly int _fall0Lo, _fall0Hi, _fall1Lo, _fall1Hi, _fall2Lo, _fall2Hi;
    private readonly int _riverLo, _riverHi, _riverFallFrom;
    private readonly GeneratedWaterKind _riverKind;

    internal WaterColumnKinds(int seaLevel, System.Span<(int Lo, int Hi)> falls, int fallCount,
        int riverLo, int riverHi, int riverFallFrom, GeneratedWaterKind riverKind)
    {
        _seaLevel = seaLevel;
        _fall0Lo = fallCount > 0 ? falls[0].Lo : 1;
        _fall0Hi = fallCount > 0 ? falls[0].Hi : 0;
        _fall1Lo = fallCount > 1 ? falls[1].Lo : 1;
        _fall1Hi = fallCount > 1 ? falls[1].Hi : 0;
        _fall2Lo = fallCount > 2 ? falls[2].Lo : 1;
        _fall2Hi = fallCount > 2 ? falls[2].Hi : 0;
        _riverLo = riverLo;
        _riverHi = riverHi;
        _riverFallFrom = riverFallFrom;
        _riverKind = riverKind;
    }

    /// <summary>The class of generated water at height <paramref name="y"/> of this column.</summary>
    public GeneratedWaterKind KindAt(int y)
    {
        if (_seaLevel != int.MinValue && y <= _seaLevel)
        {
            return GeneratedWaterKind.BelowSeaLevel;
        }

        if ((y >= _fall0Lo && y <= _fall0Hi) || (y >= _fall1Lo && y <= _fall1Hi) || (y >= _fall2Lo && y <= _fall2Hi))
        {
            return GeneratedWaterKind.Waterfall;
        }

        if (y < _riverLo || y > _riverHi)
        {
            return GeneratedWaterKind.Still;
        }

        return _riverKind == GeneratedWaterKind.River && y >= _riverFallFrom ? GeneratedWaterKind.RiverFall : _riverKind;
    }
}

/// <summary>The water-class query (#2515). (partial of <see cref="WorldGenerator"/>)</summary>
public sealed partial class WorldGenerator
{
    /// <summary>True for the classes the automaton treats as bottomless: everything but <see cref="GeneratedWaterKind.Still"/>.</summary>
    public static bool IsEndless(GeneratedWaterKind kind) => kind != GeneratedWaterKind.Still;

    /// <summary>True for the classes that fall but never spread sideways — generated waterfall columns.</summary>
    public static bool IsConfinedFall(GeneratedWaterKind kind)
        => kind == GeneratedWaterKind.RiverFall || kind == GeneratedWaterKind.Waterfall;

    /// <summary>
    /// The class of water the generator places at this cell (#2515). It answers by position, from the same queries the
    /// column phase reads, so it agrees with what a chunk holds without generating one: the sea line, the extra bands,
    /// the routed river field and the underground reaches. It does not look at the live world — the server asks it only
    /// for cells that actually hold water, and anything the generator does not account for is still water.
    /// </summary>
    public GeneratedWaterKind WaterKindAt(PlanetType planet, int worldX, int worldY, int worldZ)
    {
        int seaLevel = ResolveSeaFluid(planet).Level;
        return seaLevel != int.MinValue && worldY <= seaLevel
            ? GeneratedWaterKind.BelowSeaLevel
            : WaterColumnAt(planet, worldX, worldZ).KindAt(worldY);
    }

    /// <summary>The endless water of a whole column — what <see cref="WaterKindAt"/> reads, in one value per column.</summary>
    public WaterColumnKinds WaterColumnAt(PlanetType planet, int worldX, int worldZ)
    {
        int seaLevel = ResolveSeaFluid(planet).Level;
        System.Span<(int Lo, int Hi)> falls = stackalloc (int Lo, int Hi)[3];
        int fallCount = 0;
        if (HasExtraBands(planet))
        {
            System.Span<ColumnBand> bands = stackalloc ColumnBand[MaxColumnBands];
            int n = GetExtraBands(planet, worldX, worldZ, bands);
            for (int i = 0; i < n && fallCount < falls.Length; i++)
            {
                if (bands[i].Kind == BandKind.Waterfall)
                {
                    falls[fallCount++] = (bands[i].Bottom, bands[i].Top);
                }
            }
        }

        int riverLo = 1, riverHi = 0, fallFrom = int.MaxValue;
        var riverKind = GeneratedWaterKind.Still;
        var field = RiverFieldFor(planet);
        var waterId = _content.GetBlock("water")?.NumericId ?? BlockId.Air;
        if (!waterId.IsAir && field.FillFluid == waterId && field.TryGet(worldX, worldZ, out var col))
        {
            if (col.Underground)
            {
                if (TryGetUndergroundRiver(planet, worldX, worldZ, out int topY, out int bedY, out _))
                {
                    riverLo = bedY + 1;
                    riverHi = topY;
                    riverKind = GeneratedWaterKind.UndergroundRiver;
                }
            }
            else if (SurfacePondDepth(planet, worldX, worldZ) == 0 && SurfaceHeight(planet, worldX, worldZ) > seaLevel
                     && !TryGetVolcanoCrater(planet, worldX, worldZ, out _))
            {
                // The column phase's precedence: a pond or a volcano crater owns the column, and the sea owns everything at
                // or below its line — the river entry is ignored there.
                riverLo = (col.WaterfallDrop > 0 ? col.BedY - System.Math.Min(6, col.WaterfallDrop) : col.BedY) + 1;
                riverHi = col.WaterSurfaceY + col.WaterfallDrop;
                fallFrom = col.WaterfallDrop > 0 ? col.WaterSurfaceY + 1 : int.MaxValue;
                riverKind = GeneratedWaterKind.River;
            }
        }

        return new WaterColumnKinds(seaLevel, falls, fallCount, riverLo, riverHi, fallFrom, riverKind);
    }
}
