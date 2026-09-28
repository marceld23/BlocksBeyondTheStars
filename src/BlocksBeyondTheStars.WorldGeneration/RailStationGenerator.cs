// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The station of the intercity monorail (#2125): a roofed platform hall at a settlement's edge, the track running through
/// its middle. Reuses the <see cref="SettlementStructure"/> container so the settlement seat pipeline (carve, foundation,
/// skirt, apron) stamps it unchanged.
/// <para>Station-local coordinates: <c>u</c> runs along the track from the settlement side (0) toward the partner town
/// (<see cref="Length"/> − 1), <c>v</c> across it (the track at <see cref="TrackV"/>), y = 0 is the floor row (the
/// settlement's foundation row, so the station floor is level with the town's). The line's end pylon is a floor cell at
/// <see cref="EndPylonU"/>, the exit pylon one at the far end; the train hovers 0.7 over the floor (the line runs
/// <see cref="RailRules.LineRise"/> over a pylon's top cell, the wagon floor <see cref="RailRules.HoverHeight"/> higher).
/// The stop is a floor plate beside the track at <see cref="StopU"/> — a halted train's cab stands right there and its
/// passenger wagon behind it, both inside the hall. Benches (seat shapes) line both sides, posts carry a roof with a
/// skylight over the track, lamps hang over the platforms and the platform edges glow.</para>
/// </summary>
public static class RailStationGenerator
{
    /// <summary>Along the track: long enough for the halted cab and its passenger wagon (2 × 6 blocks + the gap).</summary>
    public const int Length = 20;

    /// <summary>Across the track: the 3-wide wagon, a 2-block platform on each side, the benches and the posts.</summary>
    public const int Width = 11;

    /// <summary>The floor row, four storeys of head room and the roof.</summary>
    public const int Height = 7;

    /// <summary>The track's cross index.</summary>
    public const int TrackV = Width / 2;

    /// <summary>The line's end pylon (the train reverses here), and the exit pylon at the far end of the hall.</summary>
    public const int EndPylonU = 1;
    public const int ExitPylonU = Length - 1;

    /// <summary>The stop plate: four blocks in from the line's end, beside the track — the halted cab's spot.</summary>
    public const int StopU = 5;
    public const int StopV = TrackV + 2;

    /// <summary>The structure tier the stamper sees.</summary>
    public const string Tier = "rail_station";

    /// <summary>Marker types: the end pylon, the exit pylon and the stop plate (structure-local cells at y = 0).</summary>
    public const string EndPylonMarker = "rail_end_pylon";
    public const string ExitPylonMarker = "rail_exit_pylon";
    public const string StopMarker = "rail_stop";

    /// <summary>The station's heading — the direction from its settlement toward the partner town: 0 = +X, 1 = −X,
    /// 2 = +Z, 3 = −Z.</summary>
    public static (int X, int Z) HeadingVector(int heading) => heading switch
    {
        1 => (-1, 0),
        2 => (0, 1),
        3 => (0, -1),
        _ => (1, 0),
    };

    /// <summary>The structure's footprint (X extent, Z extent) for a heading.</summary>
    public static (int W, int L) Footprint(int heading) => heading < 2 ? (Length, Width) : (Width, Length);

    /// <summary>Station-local (u, v) → structure-local (x, z) for a heading.</summary>
    public static (int X, int Z) ToStructure(int heading, int u, int v) => heading switch
    {
        1 => (Length - 1 - u, v),
        2 => (v, u),
        3 => (v, Length - 1 - u),
        _ => (u, v),
    };

    public static SettlementStructure Generate(int heading, GameContent content)
    {
        var (w, l) = Footprint(heading);
        int h = Height;
        var blocks = new ushort[w * h * l];
        var shapes = new Dictionary<int, int>();
        var markers = new List<SettlementMarker>();

        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        ushort stone = B("stone");
        ushort floor = B("concrete", stone);
        ushort bed = B("steel_floor", floor);
        ushort edge = B("strip_light_warm", bed);
        ushort post = B("iron_wall", B("metal_panel", stone));
        ushort roof = B("metal_panel", post);
        ushort sky = B("glass", roof);
        ushort lamp = B("light_white", 0);
        ushort seat = B("steel_floor", post);
        ushort pylon = B(RailRules.PylonBlockKey, post);
        ushort stop = B(RailRules.StopBlockKey, edge);

        void Set(int u, int y, int v, ushort id, int shape = 0)
        {
            var (x, z) = ToStructure(heading, u, v);
            if (x < 0 || y < 0 || z < 0 || x >= w || y >= h || z >= l)
            {
                return;
            }

            int idx = (x * h + y) * l + z;
            blocks[idx] = id;
            if (shape != 0 && id != 0)
            {
                shapes[idx] = shape;
            }
            else
            {
                shapes.Remove(idx);
            }
        }

        // The floor: concrete platforms, a steel track bed under the wagon, glowing platform edges.
        for (int u = 0; u < Length; u++)
        {
            for (int v = 0; v < Width; v++)
            {
                int off = v - TrackV;
                ushort id = off is >= -1 and <= 1 ? bed : (off == -2 || off == 2) ? edge : floor;
                Set(u, 0, v, id);
            }
        }

        // The posts along both outer sides and the roof with a skylight over the track (the wagon's top is 4.9 over the
        // floor row; the roof row is 6).
        foreach (int u in new[] { 0, 6, 12, Length - 1 })
        {
            for (int y = 1; y < h - 1; y++)
            {
                Set(u, y, 0, post);
                Set(u, y, Width - 1, post);
            }
        }

        for (int u = 0; u < Length; u++)
        {
            for (int v = 0; v < Width; v++)
            {
                int off = v - TrackV;
                Set(u, h - 1, v, off is >= -1 and <= 1 ? sky : roof);
            }
        }

        // Lamps under the roof over both platforms.
        if (lamp != 0)
        {
            foreach (int u in new[] { 3, 9, 15 })
            {
                Set(u, h - 2, 2, lamp);
                Set(u, h - 2, Width - 3, lamp);
            }
        }

        // Benches (seat shapes) along both sides, their backrests toward the outer wall: pairs, so they join.
        int outerLow = heading < 2 ? ShapeCode.YawToward(0, -1) : ShapeCode.YawToward(-1, 0);
        int outerHigh = heading < 2 ? ShapeCode.YawToward(0, 1) : ShapeCode.YawToward(1, 0);
        foreach (int u in new[] { 3, 4, 9, 10, 15, 16 })
        {
            Set(u, 1, 1, seat, ShapeCode.Pack(BlockShape.Bench, outerLow));
            Set(u, 1, Width - 2, seat, ShapeCode.Pack(BlockShape.Bench, outerHigh));
        }

        // The line: the end pylon (the train reverses over it), the exit pylon at the far end, the stop plate.
        Set(EndPylonU, 0, TrackV, pylon);
        Set(ExitPylonU, 0, TrackV, pylon);
        Set(StopU, 0, StopV, stop);
        var (ex, ez) = ToStructure(heading, EndPylonU, TrackV);
        var (xx, xz) = ToStructure(heading, ExitPylonU, TrackV);
        var (sx, sz) = ToStructure(heading, StopU, StopV);
        markers.Add(new SettlementMarker(EndPylonMarker, new Vector3i(ex, 0, ez)));
        markers.Add(new SettlementMarker(ExitPylonMarker, new Vector3i(xx, 0, xz)));
        markers.Add(new SettlementMarker(StopMarker, new Vector3i(sx, 0, sz)));

        return new SettlementStructure(w, h, l, Tier, ruined: false, inhabitant: string.Empty,
            blocks, markers, buildingCount: 1, mods: null, shapes: shapes);
    }
}
