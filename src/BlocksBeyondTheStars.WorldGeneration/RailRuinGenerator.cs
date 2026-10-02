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
/// An <b>abandoned monorail station</b> (#2166, Justus' "Verlassene Bahnhöfe!" from 2026-09-27): the hall of a working
/// station (<see cref="RailStationGenerator.LayHall"/>, the same ground plan) after the power died and nobody came back.
/// Built deterministically from a seed, in the station's own coordinates (<c>u</c> along the old track, <c>v</c> across
/// it, y = 0 the floor row) and turned by a heading like the working station:
/// <list type="bullet">
/// <item><b>The hall, fallen.</b> A stretch of the roof has caved in, the skylight is mostly broken, a few posts have
/// snapped, the lamps have come down, the platform edges and both pylon heads are dark, some benches are gone; the floor
/// is cracked, rubble and roof panels lie about and the biome's plants push through.</item>
/// <item><b>The derelict wagon.</b> A hover wagon built of blocks (the wagon the client draws, see <c>TrainView</c>)
/// that sank onto the track bed when the line lost its power: broken windows, holes in the roof, two benches by the
/// windows still inside, a bush growing over the roof.</item>
/// <item><b>The dead line.</b> Beyond the hall the old embankment runs on for <see cref="LeadLength"/> blocks with two
/// pylon stumps on it — one still standing, one toppled across the bed.</item>
/// <item><b>Salvage.</b> Two <see cref="CacheMarker"/> markers: one in the wagon, one on a platform beside a stack of
/// crates.</item>
/// </list>
/// Nothing in it glows (no lamp, strip light, pylon or stop block survives) and nothing is a working rail part: the
/// pylon heads are broken machines, so no linker ever mistakes them for a line. Marcel's rail rules hold here too — no
/// tickets, no ID cards, no vending machines. Reuses the <see cref="SettlementStructure"/> container so the settlement
/// seat pipeline (carve, foundation, skirt) stamps it unchanged.
/// </summary>
public static class RailRuinGenerator
{
    /// <summary>The old embankment beyond the hall's exit, with the two pylon stumps on it.</summary>
    public const int LeadLength = 12;

    /// <summary>Along the old track: the hall and the lead.</summary>
    public const int Length = RailStationGenerator.Length + LeadLength;

    /// <summary>Across the track and the height: the hall's.</summary>
    public const int Width = RailStationGenerator.Width;
    public const int Height = RailStationGenerator.Height;

    /// <summary>The structure tier the stamper sees.</summary>
    public const string Tier = "rail_ruin";

    /// <summary>The loot marker type (structure-local cells, one in the wagon and one on a platform).</summary>
    public const string CacheMarker = "rail_cache";

    /// <summary>The derelict wagon: six blocks along the track bed from <see cref="WagonU"/>, three across it, its floor on
    /// the bed (y = 1) and its roof at y = 5, under the hall's roof row.</summary>
    public const int WagonU = 3;
    public const int WagonLength = 6;

    /// <summary>The two pylon stumps on the lead.</summary>
    public const int StandingStumpU = RailStationGenerator.Length + 5;
    public const int ToppledStumpU = Length - 1;

    /// <summary>The structure's footprint (X extent, Z extent) for a heading (0 = +X, 1 = −X, 2 = +Z, 3 = −Z).</summary>
    public static (int W, int L) Footprint(int heading) => heading < 2 ? (Length, Width) : (Width, Length);

    /// <summary>Station-local (u, v) → structure-local (x, z) for a heading.</summary>
    public static (int X, int Z) ToStructure(int heading, int u, int v) => heading switch
    {
        1 => (Length - 1 - u, v),
        2 => (v, u),
        3 => (v, Length - 1 - u),
        _ => (u, v),
    };

    public static SettlementStructure Generate(long seed, int heading, string groundBlock, GameContent content)
    {
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32)) ^ 0x7A11));
        var (w, l) = Footprint(heading);
        int h = Height;
        var blocks = new ushort[w * h * l];
        var shapes = new Dictionary<int, int>();
        var markers = new List<SettlementMarker>();

        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        ushort stone = B("stone");
        ushort ground = B(groundBlock, stone);
        ushort concrete = B("concrete", stone);
        ushort steel = B("steel_floor", concrete);
        ushort stripLight = B("strip_light_warm", 0);
        ushort post = B("iron_wall", B("metal_panel", stone));
        ushort panel = B("metal_panel", post);
        ushort glass = B("glass", 0);
        ushort lamp = B("light_white", 0);
        ushort pylon = B(RailRules.PylonBlockKey, 0);
        ushort stop = B(RailRules.StopBlockKey, 0);
        ushort moss = B("moss_stone", stone);
        ushort rust = B("rusted_panel", panel);
        ushort scrap = B("scrap_metal", rust);
        ushort deadHead = B("broken_machine", rust);
        ushort crate = B("crate", 0);
        ushort leaves = B("tree_leaves", 0);
        ushort flora = B(SettlementGenerator.BiomeFloraKey(groundBlock), B("flora_plant", 0));

        bool In(int u, int y, int v) => u >= 0 && u < Length && v >= 0 && v < Width && y >= 0 && y < h;

        int Index(int u, int y, int v)
        {
            var (x, z) = ToStructure(heading, u, v);
            return (x * h + y) * l + z;
        }

        void Set(int u, int y, int v, ushort id, int shape)
        {
            if (!In(u, y, v))
            {
                return;
            }

            int idx = Index(u, y, v);
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

        void Put(int u, int y, int v, ushort id) => Set(u, y, v, id, 0);

        ushort Get(int u, int y, int v) => In(u, y, v) ? blocks[Index(u, y, v)] : (ushort)0;

        bool Empty(int u, int y, int v) => In(u, y, v) && Get(u, y, v) == 0;

        void Mark(int u, int y, int v)
        {
            var (x, z) = ToStructure(heading, u, v);
            markers.Add(new SettlementMarker(CacheMarker, new Vector3i(x, y, z)));
        }

        int tv = RailStationGenerator.TrackV;
        int hall = RailStationGenerator.Length;
        int roofY = h - 1;

        // ---- The hall as it was built ------------------------------------------------------------------------------
        RailStationGenerator.LayHall(heading, content, Set);

        // ---- The power died: nothing glows, nothing is a working rail part ----------------------------------------
        for (int u = 0; u < hall; u++)
        {
            for (int v = 0; v < Width; v++)
            {
                for (int y = 0; y < h; y++)
                {
                    ushort id = Get(u, y, v);
                    if (id == 0)
                    {
                        continue;
                    }

                    if (id == stripLight)
                    {
                        Put(u, y, v, rng.NextDouble() < 0.3 ? moss : concrete); // the dark platform edge
                    }
                    else if (id == pylon)
                    {
                        Put(u, y, v, deadHead); // the pylon heads are dead machines
                    }
                    else if (id == stop)
                    {
                        Put(u, y, v, rust); // the stop plate, rusted over
                    }
                    else if (id == lamp)
                    {
                        Put(u, y, v, 0); // the lamps came down …
                        if (rng.NextDouble() < 0.6 && Empty(u, 1, v))
                        {
                            Put(u, 1, v, scrap); // … and some still lie under where they hung
                        }
                    }
                }
            }
        }

        // ---- The cracked floor --------------------------------------------------------------------------------------
        for (int u = 0; u < hall; u++)
        {
            for (int v = 0; v < Width; v++)
            {
                ushort id = Get(u, 0, v);
                double r = rng.NextDouble();
                if (id == concrete)
                {
                    Put(u, 0, v, r < 0.15 ? moss : r < 0.23 ? ground : concrete);
                }
                else if (id == steel)
                {
                    Put(u, 0, v, r < 0.3 ? rust : steel); // the track bed
                }
            }
        }

        // ---- The derelict wagon, sunk onto the track bed ------------------------------------------------------------
        // Wagon-local: x across (0..2), z along (0..5); the floor on the bed, sill, two rows of windows, the roof. The door
        // openings (z 2..3) stay open to the lintel; the ends are open for the walk-through, as on the line.
        int outerLow = heading < 2 ? ShapeCode.YawToward(0, -1) : ShapeCode.YawToward(-1, 0);
        int outerHigh = heading < 2 ? ShapeCode.YawToward(0, 1) : ShapeCode.YawToward(1, 0);
        for (int z = 0; z < WagonLength; z++)
        {
            int u = WagonU + z;
            bool door = z == 2 || z == 3;
            for (int x = 0; x < 3; x++)
            {
                int v = tv - 1 + x;
                Put(u, 1, v, rng.NextDouble() < 0.25 ? rust : steel);                       // the floor
                Put(u, 5, v, rng.NextDouble() < 0.35 ? (ushort)0 : rng.NextDouble() < 0.25 ? rust : panel); // the roof, holed

                if (x == 1)
                {
                    continue; // the aisle
                }

                if (door)
                {
                    Put(u, 4, v, panel); // the lintel over the door
                    continue;
                }

                // Benches by the windows near both ends of the wagon, the sill everywhere else.
                if (z == 1 || z == 4)
                {
                    Set(u, 2, v, steel, ShapeCode.Pack(BlockShape.Bench, x == 0 ? outerLow : outerHigh));
                }
                else
                {
                    Put(u, 2, v, rng.NextDouble() < 0.3 ? rust : panel);
                }

                Put(u, 3, v, glass != 0 && rng.NextDouble() < 0.3 ? glass : (ushort)0); // most windows are broken
                Put(u, 4, v, glass != 0 && rng.NextDouble() < 0.3 ? glass : (ushort)0);
            }
        }

        // A bush has taken root on the roof, and the biome's plants grow in the open ends.
        if (leaves != 0)
        {
            int bush = WagonU + 1 + rng.Next(WagonLength - 2);
            for (int du = -1; du <= 1; du++)
            {
                for (int v = tv - 1; v <= tv + 1; v++)
                {
                    if (rng.NextDouble() < 0.7)
                    {
                        Put(bush + du, 5, v, leaves);
                    }
                }
            }
        }

        if (flora != 0)
        {
            Put(WagonU, 2, tv, flora);
        }

        // The salvage in the wagon: in the aisle behind the far door.
        Put(WagonU + 4, 2, tv, 0);
        Mark(WagonU + 4, 2, tv);

        // ---- The roof caved in ----------------------------------------------------------------------------------------
        // One stretch across the whole width is gone (always beyond the wagon, which still stands under its piece of
        // roof), the rest has holes; most of the skylight is broken; what is left is half rusted. A fallen panel or a heap
        // of scrap lies under some of the gaps.
        int caveR = 1 + rng.Next(3);
        int caveFirst = WagonU + WagonLength + caveR;
        int caveLast = hall - 2 - caveR;
        int caveU = caveFirst + rng.Next(System.Math.Max(1, caveLast - caveFirst + 1));
        for (int u = 0; u < hall; u++)
        {
            for (int v = 0; v < Width; v++)
            {
                ushort id = Get(u, roofY, v);
                if (id == 0)
                {
                    continue;
                }

                bool caved = System.Math.Abs(u - caveU) <= caveR;
                bool gone = caved || (id == glass ? rng.NextDouble() < 0.75 : rng.NextDouble() < 0.18);
                if (gone)
                {
                    Put(u, roofY, v, 0);
                    if ((caved ? rng.NextDouble() < 0.4 : rng.NextDouble() < 0.12) && Empty(u, 1, v) && Get(u, 0, v) != 0)
                    {
                        Put(u, 1, v, rng.NextDouble() < 0.5 ? rust : scrap);
                    }
                }
                else if (id == panel && rng.NextDouble() < 0.35)
                {
                    Put(u, roofY, v, rust);
                }
            }
        }

        // ---- Snapped posts ----------------------------------------------------------------------------------------------
        foreach (int u in new[] { 0, 6, 12, hall - 1 })
        {
            foreach (int v in new[] { 0, Width - 1 })
            {
                if (rng.NextDouble() >= 0.35)
                {
                    continue;
                }

                int keep = 2 + rng.Next(3); // the post stands up to here
                for (int y = keep; y < roofY; y++)
                {
                    Put(u, y, v, 0);
                }

                int inward = v == 0 ? 1 : Width - 2;
                if (Empty(u, 1, inward))
                {
                    Put(u, 1, inward, rust); // the broken-off piece
                }
            }
        }

        // ---- Missing benches ----------------------------------------------------------------------------------------------
        foreach (int u in new[] { 3, 4, 9, 10, 15, 16 })
        {
            foreach (int v in new[] { 1, Width - 2 })
            {
                if (Get(u, 1, v) == steel && rng.NextDouble() < 0.35)
                {
                    Put(u, 1, v, 0);
                }
            }
        }

        // ---- The salvage on the platform, beside a stack of old crates -------------------------------------------------
        int cacheU = 13 + rng.Next(2);
        int cacheV = rng.Next(2) == 0 ? 2 : Width - 3;
        Put(cacheU, 1, cacheV, 0);
        Mark(cacheU, 1, cacheV);
        if (crate != 0)
        {
            int side = cacheV < tv ? 1 : Width - 2;
            Put(cacheU + 1, 1, cacheV, crate);
            Put(cacheU + 1, 1, side, crate);
            if (Empty(cacheU + 1, 2, side))
            {
                Put(cacheU + 1, 2, side, crate);
            }
        }

        // ---- Overgrowth on the floor -----------------------------------------------------------------------------------------
        for (int u = 0; u < hall; u++)
        {
            for (int v = 1; v < Width - 1; v++)
            {
                if (flora != 0 && Empty(u, 1, v) && Get(u, 0, v) != 0 && !IsMarker(u, 1, v) && rng.NextDouble() < 0.1)
                {
                    Put(u, 1, v, flora);
                }
            }
        }

        // ---- The dead line: the old embankment, a standing stump and a toppled one ------------------------------------
        for (int u = hall; u < Length; u++)
        {
            for (int v = 0; v < Width; v++)
            {
                bool bed = System.Math.Abs(v - tv) <= 1;
                Put(u, 0, v, bed && rng.NextDouble() < 0.7 ? moss : ground);
            }
        }

        for (int y = 1; y <= 3; y++)
        {
            Put(StandingStumpU, y, tv, rust);
        }

        Put(StandingStumpU, 4, tv, deadHead);

        Put(ToppledStumpU, 1, tv, rust); // what is left standing of the other one …
        int fall = rng.Next(2) == 0 ? 1 : -1; // … and the rest lies across the bed
        for (int i = 1; i <= 3; i++)
        {
            Put(ToppledStumpU, 1, tv + fall * i, rust);
        }

        Put(ToppledStumpU, 1, tv + fall * 4, deadHead);

        for (int u = hall; u < Length; u++)
        {
            for (int v = 0; v < Width; v++)
            {
                if (!Empty(u, 1, v))
                {
                    continue;
                }

                double r = rng.NextDouble();
                if (r < 0.04)
                {
                    Put(u, 1, v, scrap);
                }
                else if (flora != 0 && r < 0.12)
                {
                    Put(u, 1, v, flora);
                }
            }
        }

        return new SettlementStructure(w, h, l, Tier, ruined: true, inhabitant: string.Empty,
            blocks, markers, buildingCount: 1, mods: null, shapes: shapes);

        bool IsMarker(int u, int y, int v)
        {
            var (x, z) = ToStructure(heading, u, v);
            foreach (var m in markers)
            {
                if (m.LocalPos.X == x && m.LocalPos.Y == y && m.LocalPos.Z == z)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
