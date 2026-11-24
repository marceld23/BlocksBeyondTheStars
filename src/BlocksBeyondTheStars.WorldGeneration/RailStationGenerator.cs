// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
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
/// The stop is a floor plate beside the track at <see cref="StopU"/>. A halted train centres on its stop but never hangs past
/// the line's end (#3463), so here — four blocks from the end — the cab and its passenger wagon stand between the end
/// pylon and the hall's middle, both inside the hall (before #3463 the leading wagon stood at the plate and the other one
/// behind it — the cab when it pulled in, the passenger wagon when it pushed in, #2741); so near the line's end, the
/// train turns round while it stands.
/// Benches (seat shapes) line both sides, posts carry a roof with a
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

    /// <summary>The line's end pylon (the end of the line), and the exit pylon at the far end of the hall.</summary>
    public const int EndPylonU = 1;
    public const int ExitPylonU = Length - 1;

    /// <summary>The stop plate: four blocks in from the line's end, beside the track — the train halts as near to centred on it as the line's end allows (#3463).</summary>
    public const int StopU = 5;
    public const int StopV = TrackV + 2;

    /// <summary>A through station's stop plate (#3985): in the hall's middle. The line runs on behind a through station's end
    /// pylon, so nothing holds the halted train in from that end — centred on <see cref="StopU"/> its rear hung about 1.3
    /// blocks out of the hall; centred here the cab and its passenger wagon (12.6 blocks) stand 4 blocks inside both ends.</summary>
    public const int ThroughStopU = Length / 2;

    /// <summary>A side-platform hall's width across the track (#3985): the 3-wide track bed and one platform with its bench
    /// and posts — the full hall cut along the track bed's far edge.</summary>
    public const int SidePlatformWidth = TrackV + 2;

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

    /// <summary>The structure's footprint (X extent, Z extent) for a heading and a hall's platforms.</summary>
    public static (int W, int L) Footprint(int heading, RailHallPlatforms platforms = RailHallPlatforms.Both)
    {
        int across = AcrossWidth(platforms);
        return heading < 2 ? (Length, across) : (across, Length);
    }

    /// <summary>The hall's width across the track: the full hall, or a side-platform hall (#3985).</summary>
    public static int AcrossWidth(RailHallPlatforms platforms) => platforms == RailHallPlatforms.Both ? Width : SidePlatformWidth;

    /// <summary>The first full-hall cross index a hall keeps (a high-side platform hall drops the low platform).</summary>
    public static int FirstV(RailHallPlatforms platforms) => platforms == RailHallPlatforms.High ? TrackV - 1 : 0;

    /// <summary>The stop plate's cross index in the full hall: beside the track on the platform's side.</summary>
    public static int StopVFor(RailHallPlatforms platforms) => platforms == RailHallPlatforms.Low ? TrackV - 2 : StopV;

    /// <summary>Station-local (u, v) → structure-local (x, z) for a heading. The cross index maps the same way whatever the
    /// hall's width: v = 0 is the low world coordinate across the track.</summary>
    public static (int X, int Z) ToStructure(int heading, int u, int v) => heading switch
    {
        1 => (Length - 1 - u, v),
        2 => (v, u),
        3 => (v, Length - 1 - u),
        _ => (u, v),
    };

    /// <summary>The station hall (#2125): both platforms, the stop four blocks from the line's end.</summary>
    public static SettlementStructure Generate(int heading, GameContent content) => Generate(heading, content, RailHallPlatforms.Both, through: false);

    /// <summary>A station hall with the given platforms (#3985: a city's hall beside a street keeps one) — a
    /// <paramref name="through"/> station with its stop in the hall's middle (<see cref="ThroughStopU"/>).</summary>
    public static SettlementStructure Generate(int heading, GameContent content, RailHallPlatforms platforms, bool through)
    {
        var (w, l) = Footprint(heading, platforms);
        int across = AcrossWidth(platforms), v0 = FirstV(platforms);
        int h = Height;
        var blocks = new ushort[w * h * l];
        var shapes = new Dictionary<int, int>();
        var markers = new List<SettlementMarker>();

        void Set(int u, int y, int v, ushort id, int shape)
        {
            if (v < v0 || v >= v0 + across)
            {
                return; // a side-platform hall: the far platform is not there
            }

            var (x, z) = ToStructure(heading, u, v - v0);
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

        int stopU = through ? ThroughStopU : StopU, stopV = StopVFor(platforms);
        LayHall(heading, content, Set, stopU, stopV);

        // The line's markers: the end pylon, the exit pylon and the stop plate.
        var (ex, ez) = ToStructure(heading, EndPylonU, TrackV - v0);
        var (xx, xz) = ToStructure(heading, ExitPylonU, TrackV - v0);
        var (sx, sz) = ToStructure(heading, stopU, stopV - v0);
        markers.Add(new SettlementMarker(EndPylonMarker, new Vector3i(ex, 0, ez)));
        markers.Add(new SettlementMarker(ExitPylonMarker, new Vector3i(xx, 0, xz)));
        markers.Add(new SettlementMarker(StopMarker, new Vector3i(sx, 0, sz)));

        return new SettlementStructure(w, h, l, Tier, ruined: false, inhabitant: string.Empty,
            blocks, markers, buildingCount: 1, mods: null, shapes: shapes);
    }

    /// <summary>
    /// Lays the station's whole ground plan in station-local coordinates through <paramref name="set"/> (u, y, v, block id,
    /// shape code): the floor, the posts, the roof with its skylight, the lamps, the benches (their backrests toward the
    /// outer wall of <paramref name="heading"/>), both pylons and the stop plate. Shared by the working station and the
    /// abandoned one (<see cref="RailRuinGenerator"/>, #2166), so the ruin is the same hall, only fallen. The stop plate
    /// stands at (<paramref name="stopU"/>, <paramref name="stopV"/>) — a through or a side-platform hall moves it (#3985).
    /// </summary>
    internal static void LayHall(int heading, GameContent content, Action<int, int, int, ushort, int> set, int stopU = StopU, int stopV = StopV)
    {
        int h = Height;
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

        void Set(int u, int y, int v, ushort id, int shape = 0) => set(u, y, v, id, shape);

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
        Set(stopU, 0, stopV, stop);
    }
}

/// <summary>Which platforms a station hall has (#3985): both (the intercity station between towns), or only the one on the
/// low or the high side across the track (a city's hall on the edge of its square, the track in the street beside it). Low is
/// the lower world coordinate across the track: Z for a hall along X, X for a hall along Z.</summary>
public enum RailHallPlatforms
{
    Both = 0,
    Low = 1,
    High = 2,
}
