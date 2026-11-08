// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Numerics;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3420: the flown ship against a rock — an asteroid, a wreckage fragment or the wreck (every voxel body of the flight
/// scene that is not a station; a station hull has its own rule). The ship is a sphere of <see cref="ShipRadius"/>: it may
/// touch a rock's blocks but never enter them. A move into the rock ends at its surface with the part of the move that
/// pointed into the rock taken away, so the ship stops softly and slides along the rock — no bounce back, no jitter while
/// it rests there. A move away from the rock is never held back. A ship that is already inside a rock (one that appeared
/// around it) is eased out at a gentle pace instead of popping out. Everything happens in the rock's grid, where cell
/// <c>k</c> fills the unit box from <c>k</c> to <c>k + 1</c> (<see cref="VoxelSurface"/>). Unity-free; the flight view runs
/// it every frame for the rocks near the ship, and the server's ram check measures the same surface.
/// </summary>
public static class RockKeepOut
{
    /// <summary>The flown ship's collision radius against a rock (the server's ram check reads the same number).</summary>
    public const float ShipRadius = SpaceCombatRules.ShipRockRadius;

    /// <summary>How fast (units per second) a ship caught inside a rock is eased out of it while it does not move.</summary>
    public const float EaseOutSpeed = 6f;

    /// <summary>A long move is checked in steps of at most half the ship's radius, so a frame hitch cannot carry the ship
    /// through a small rock — but never in more than this many.</summary>
    private const int MaxSteps = 16;

    /// <summary>Pushes per step: one per block face the ship leans on (a corner of the rock takes two or three).</summary>
    private const int MaxPushes = 4;

    /// <summary>Closer than the radius by less than this is resting contact, not overlap.</summary>
    private const float Touch = 1e-3f;

    /// <summary>Moves the ship from <paramref name="from"/> towards <paramref name="to"/> (both in the rock's grid) and
    /// returns where it ends: <paramref name="to"/> when the way is clear, else the point at the rock's surface the move
    /// slides to. <paramref name="rockCentre"/> is the middle of the rock in its grid — the way out of a block the ship is
    /// stuck in. <paramref name="easeOut"/> is how far a stuck ship may be eased out in this call on top of its own move
    /// (<see cref="EaseOutSpeed"/> times the frame time).</summary>
    public static Vector3 Resolve(IReadOnlyDictionary<Vector3i, BlockId> cells, Vector3 rockCentre, Vector3 from, Vector3 to,
        float easeOut, float radius = ShipRadius)
    {
        if (cells is null || cells.Count == 0 || !(radius > 0f))
        {
            return to;
        }

        Vector3 move = to - from;
        int steps = (int)Math.Ceiling(move.Length() / (radius * 0.5f));
        steps = Math.Max(1, Math.Min(MaxSteps, steps));
        Vector3 step = move / steps;

        // A step moves the ship at most its own length deeper, a corner of the rock may need a little more to undo;
        // the rest of the budget is the gentle ease out of a rock the ship is stuck in.
        float budget = 2f * step.Length() + Math.Max(0f, easeOut) / steps;
        Vector3 p = from;
        bool pushed = false;
        for (int i = 0; i < steps; i++)
        {
            Vector3 free = p + step;
            p = PushOut(cells, rockCentre, free, radius, budget);
            pushed |= p != free;
        }

        return pushed ? p : to; // a clear way ends exactly where it was meant to
    }

    /// <summary>Pushes a ship at <paramref name="p"/> out of the rock's blocks by at most <paramref name="budget"/>: away
    /// from the nearest block (straight out of a face, round an edge or a corner), and out from the rock's middle while
    /// the ship's centre sits inside a block.</summary>
    public static Vector3 PushOut(IReadOnlyDictionary<Vector3i, BlockId> cells, Vector3 rockCentre, Vector3 p, float radius,
        float budget)
    {
        for (int i = 0; i < MaxPushes && budget > 0f; i++)
        {
            if (!VoxelSurface.TryNearest(cells, new Vector3f(p.X, p.Y, p.Z), radius, out var nearest, out float distance)
                || distance >= radius - Touch)
            {
                break;
            }

            Vector3 dir;
            float depth;
            if (distance > Touch)
            {
                dir = (p - new Vector3(nearest.X, nearest.Y, nearest.Z)) / distance;
                depth = radius - distance;
            }
            else
            {
                dir = OutFrom(rockCentre, p);
                depth = budget; // how deep is unknown: ease out as far as this call allows
            }

            float push = Math.Min(depth, budget);
            p += dir * push;
            budget -= push;
        }

        return p;
    }

    /// <summary>The way out of the rock for a ship whose centre is inside one of its blocks: away from the rock's middle,
    /// straight up when it sits right on it.</summary>
    private static Vector3 OutFrom(Vector3 rockCentre, Vector3 p)
    {
        Vector3 d = p - rockCentre;
        float length = d.Length();
        return length > 1e-4f ? d / length : Vector3.UnitY;
    }
}
