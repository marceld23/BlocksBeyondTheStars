// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>
/// A pioneer outpost (<see cref="PioneerOutpostGenerator.Generate"/>) as <c>GameServer.StampOutposts</c> builds one — wall,
/// hidden shaft and crates rolled from the seed unless the request forces them (every roll is drawn anyway, so forcing one
/// never shifts the others). The cellar below the floor row comes along: the structure is lifted so it fits, and
/// <see cref="BuiltStructure.GroundRow"/> is the floor row; the cellar's air cells are its cavity. The pioneer's work
/// shapes only the resident, not the building, so it is no parameter. With <c>boat</c> the rowboat lies on the water in
/// front of the outpost (along X, two cells out, its water row one below the floor) — the first spot the server tries.
/// </summary>
internal sealed class PioneerOutpostSource : StructureSourceBase
{
    private const int BoatGap = 2;

    public PioneerOutpostSource()
        : base("pioneer_outpost", StructureSourceKind.Surface)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content)
    {
        var walls = new List<string> { SourceOptions.Auto };
        walls.AddRange(PioneerOutpostGenerator.WallKinds);
        return new[]
        {
            StructureParam.Choice("wall", walls, SourceOptions.Auto),
            StructureParam.Choice("hidden", SourceOptions.Tri, SourceOptions.Auto),
            StructureParam.Int("chests", 0, 0, PioneerOutpostGenerator.MaxChests),
            StructureParam.Bool("boat", false),
        };
    }

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
    {
        string wall = p.Get("wall");
        var outpost = PioneerOutpostGenerator.Generate(r.Seed, r.Surface, r.Content, wall == SourceOptions.Auto ? null : wall,
            p.Tri("hidden"), p.Int("chests"));
        return Convert(Key, outpost, p.Bool("boat") ? PioneerOutpostGenerator.Boat(alongX: true, r.Content) : null);
    }

    /// <summary>The outpost with its cellar (and the boat, when given) in one box.</summary>
    internal static BuiltStructure Convert(string key, PioneerOutpostGenerator.Outpost outpost,
        IReadOnlyList<(Vector3i Local, ushort Block, int Shape)>? boat)
    {
        var s = outpost.Structure;
        int minY = 0;
        foreach (var (local, _, _) in outpost.Below)
        {
            minY = Math.Min(minY, local.Y);
        }

        foreach (var m in outpost.BelowMarkers)
        {
            minY = Math.Min(minY, m.LocalPos.Y);
        }

        int shift = boat is null ? -minY : Math.Max(1, -minY);
        int boatZ = s.Length + BoatGap - 1;
        int length = boat is null ? s.Length : boatZ + PioneerOutpostGenerator.BoatWidth;
        int width = boat is null ? s.Width : Math.Max(s.Width, PioneerOutpostGenerator.BoatLength);
        var b = new BuiltStructure(key, width, s.Height + shift, length, groundRow: shift);
        BuiltConvert.CopyGrid(b, s.Width, s.Height, s.Length, shift, s.Get, s.GetModifier, s.GetShape);
        foreach (var (local, block, shape) in outpost.Below)
        {
            b.Set(local.X, local.Y + shift, local.Z, block, 0, 0, shape);
            if (block == 0)
            {
                b.Cavity.Add(b.Index(local.X, local.Y + shift, local.Z));
            }
            else
            {
                b.Cavity.Remove(b.Index(local.X, local.Y + shift, local.Z));
            }
        }

        BuiltConvert.AddMarkers(b, s.Markers, dy: shift);
        BuiltConvert.AddMarkers(b, outpost.BelowMarkers, dy: shift);
        foreach (var (local, block, shape) in boat ?? Array.Empty<(Vector3i, ushort, int)>())
        {
            b.Set(local.X, local.Y + shift - 1, local.Z + boatZ, block, 0, 0, shape);
        }

        return b;
    }
}

/// <summary>An intercity rail station (<see cref="RailStationGenerator.Generate"/>): the same hall for every seed, turned to
/// its heading (0 +X, 1 −X, 2 +Z, 3 −Z).</summary>
internal sealed class RailStationSource : StructureSourceBase
{
    public RailStationSource()
        : base("rail_station", StructureSourceKind.Surface)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Int("heading", 0, 0, 3),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
        => BuiltConvert.FromSettlement(Key, RailStationGenerator.Generate(p.Int("heading"), r.Content));
}

/// <summary>The abandoned monorail station (<see cref="RailRuinGenerator.Generate"/>), turned to its heading.</summary>
internal sealed class RailRuinSource : StructureSourceBase
{
    public RailRuinSource()
        : base("rail_ruin", StructureSourceKind.Surface)
    {
    }

    public override IReadOnlyList<StructureParam> GetParams(GameContent content) => new[]
    {
        StructureParam.Int("heading", 0, 0, 3),
    };

    protected override BuiltStructure BuildCore(StructureBuildRequest r, SourceParams p)
        => BuiltConvert.FromSettlement(Key, RailRuinGenerator.Generate(r.Seed, p.Int("heading"), r.Surface, r.Content));
}
