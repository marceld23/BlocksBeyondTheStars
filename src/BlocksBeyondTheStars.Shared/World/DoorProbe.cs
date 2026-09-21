// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// How a door finds its wall (#1975). A door is authored as ONE cell — a structure marker or a placed door
/// block on the doorway's floor — and everything else is read off the blocks around it: the jambs beside the
/// cell decide the wall axis, the contiguous air run along that wall decides the width. The server has hung
/// doors this way since the first settlements; the rule lives here so the placement ghost and the build
/// editors can show the very door the server will hang, over their own grids, without a second copy of it.
/// <para>The <c>solid</c> callback answers "does this cell block a door" for a world cell, a ship's structure
/// cell or an editor cell — each caller brings its own grid.</para>
/// </summary>
public static class DoorProbe
{
    /// <summary>Cells scanned each side of the probed cell along the wall: bounds the width of a stamped door so a
    /// fully open area cannot run away into a seven-block gate by accident.</summary>
    public const int DefaultReach = 3;

    /// <summary>What the probe found: the wall axis, the gap extent relative to the probed cell and whether any
    /// jamb was there at all.</summary>
    public readonly struct Fit
    {
        public Fit(bool axisX, int lo, int hi, bool hasJamb)
        {
            AxisX = axisX;
            Lo = lo;
            Hi = hi;
            HasJamb = hasJamb;
        }

        /// <summary>True when the wall runs along X (the passage is along Z).</summary>
        public bool AxisX { get; }

        /// <summary>First gap cell along the wall axis, relative to the probed cell (0 or negative).</summary>
        public int Lo { get; }

        /// <summary>Last gap cell along the wall axis, relative to the probed cell (0 or positive).</summary>
        public int Hi { get; }

        /// <summary>A solid neighbour on at least one axis — false for a door marker on open floor, which the
        /// server would still hang (along Z) but an author almost never means.</summary>
        public bool HasJamb { get; }

        /// <summary>Gap width in blocks along the wall axis (at least 1).</summary>
        public float Width => Hi - Lo + 1;

        /// <summary>Centre of the gap relative to the probed cell, along the wall axis.</summary>
        public float Centre => (Lo + Hi) * 0.5f;

        /// <summary>World X of the door's centre (the gap centre, block-centre convention) for a probed cell at
        /// <paramref name="x"/>.</summary>
        public float CentreX(int x) => (AxisX ? x + Centre : x) + 0.5f;

        /// <summary>World Z of the door's centre for a probed cell at <paramref name="z"/>.</summary>
        public float CentreZ(int z) => (AxisX ? z : z + Centre) + 0.5f;
    }

    /// <summary>
    /// Measures the door a marker at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) would get.
    /// The jambs are solid along the wall axis and the passage is open along the other: jambs on X only → the wall
    /// runs along X; on Z only → along Z; on both → X; on neither → Z (the historical default). A caller that
    /// already knows the axis (the ship hatch, a wide gap where a ±1 probe at the centre is ambiguous) passes
    /// <paramref name="forceAxisX"/>. The gap is the contiguous air run along the wall, at most
    /// <paramref name="reach"/> cells each side.
    /// </summary>
    public static Fit Measure(Func<int, int, int, bool> solid, int x, int y, int z, bool? forceAxisX = null, int reach = DefaultReach)
    {
        bool xJamb = solid(x - 1, y, z) || solid(x + 1, y, z);
        bool zJamb = solid(x, y, z - 1) || solid(x, y, z + 1);
        bool axisX = forceAxisX ?? (xJamb && !zJamb ? true : (zJamb && !xJamb ? false : xJamb));

        int lo = 0, hi = 0;
        for (int s = 1; s <= reach; s++)
        {
            if (solid(axisX ? x - s : x, y, axisX ? z : z - s))
            {
                break;
            }

            lo = -s;
        }

        for (int s = 1; s <= reach; s++)
        {
            if (solid(axisX ? x + s : x, y, axisX ? z : z + s))
            {
                break;
            }

            hi = s;
        }

        return new Fit(axisX, lo, hi, xJamb || zJamb);
    }

    /// <summary>
    /// The wall axis of a door a PLAYER places at a cell (width 1): jambs on exactly one axis decide; with jambs
    /// on both or on neither the door faces the player — the wall runs across their look direction, so a player
    /// looking along ±X gets a door across X.
    /// </summary>
    public static bool AxisForPlacedDoor(Func<int, int, int, bool> solid, int x, int y, int z, double yawDegrees)
    {
        bool xJamb = solid(x - 1, y, z) || solid(x + 1, y, z);
        bool zJamb = solid(x, y, z - 1) || solid(x, y, z + 1);
        if (xJamb != zJamb)
        {
            return xJamb; // jambs on exactly one axis → the wall runs that way
        }

        double yaw = yawDegrees * Math.PI / 180.0; // wall faces the player's look direction
        return Math.Abs(Math.Cos(yaw)) >= Math.Abs(Math.Sin(yaw));
    }
}
