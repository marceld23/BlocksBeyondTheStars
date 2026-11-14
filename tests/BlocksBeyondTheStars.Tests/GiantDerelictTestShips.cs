// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A synthetic giant ship for the space-wreck tests (#3614): a box hull of <c>decks</c> decks (a floor every six rows), split
/// along x into rooms by bulkheads every <c>span</c> blocks — each bulkhead a plane of constant x with one 2 × 3 doorway per
/// deck (a <c>door_slide</c> marker, <c>axis=x</c>), an open way in (2 × 3) in the z = 0 wall of the first room with the
/// <c>entrance</c> marker on its floor, ceiling lamps, chests and a sound. Plain arithmetic only.
/// </summary>
internal static class GiantDerelictTestShips
{
    public const string Key = "test_liner";

    /// <summary>The way in: x of its first column (the opening is x, x + 1 at z = 0, rows 1 … 3), the marker one block inside.</summary>
    public static Vector3i Entrance => new(6, 1, 1);

    /// <summary>The liner. <paramref name="kept"/> marks its walls and floors <c>keep</c>, so the ruin pass (whose look may still
    /// change) leaves the rooms as built and only the derelict decay opens them — the tests see that decay alone. Under 96 long
    /// so the hull never breaks into sections.</summary>
    public static StructureTemplate Liner(int w = 94, int decks = 1, int l = 24, int span = 20, double ruin = 0.05, bool kept = true)
    {
        int h = (6 * decks) + 1;
        var t = new StructureTemplate
        {
            Key = Key,
            GiantShip = true,
            Width = w,
            Height = h,
            Length = l,
            GroundRow = 0,
            PinOnly = true,
            RuinStrength = ruin,
        };
        int zc = l / 2;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                for (int z = 0; z < l; z++)
                {
                    if (Block(w, h, l, span, zc, x, y, z) is { } key)
                    {
                        t.Cells.Add(new TemplateCell { Kind = "block", Id = key, X = x, Y = y, Z = z, Keep = kept && key != "light_white" });
                    }
                }

        AddMarkers(t, w, decks, l, span, zc);
        TemplateBody.Compact(t);
        return t;
    }

    private static string? Block(int w, int h, int l, int span, int zc, int x, int y, int z)
    {
        bool entrance = z == 0 && (x == Entrance.X || x == Entrance.X + 1) && y >= 1 && y <= 3;
        bool shell = x == 0 || x == w - 1 || y == 0 || y == h - 1 || z == 0 || z == l - 1;
        if (shell)
        {
            return entrance ? null : "iron_wall";
        }

        if (y % 6 == 0)
        {
            return "steel_floor";
        }

        bool doorway = (z == zc || z == zc + 1) && y % 6 >= 1 && y % 6 <= 3;
        if (x % span == 0 && x < w - 1)
        {
            return doorway ? null : "iron_wall";
        }

        return y % 6 == 5 && x % 6 == 3 && z % 6 == 3 ? "light_white" : null;
    }

    private static void AddMarkers(StructureTemplate t, int w, int decks, int l, int span, int zc)
    {
        void Marker(string id, int x, int y, int z, string config = "")
            => t.Cells.Add(new TemplateCell { Kind = "marker", Id = id, X = x, Y = y, Z = z, Config = config });

        Marker(GiantShipRules.EntranceMarker, Entrance.X, Entrance.Y, Entrance.Z);
        for (int d = 0; d < decks; d++)
        {
            for (int x = span; x < w - 1; x += span)
            {
                Marker(GiantShipRules.DoorMarker, x, (6 * d) + 1, zc, "axis=x");
            }
        }

        int chests = 0;
        for (int x = span + (span / 2); x < w - 1 && chests < 3; x += span)
        {
            string config = chests switch
            {
                0 => "table=ruin_common",
                1 => "items=iron_plate:2-4",
                _ => "table=ruin_common;hidden=1",
            };
            Marker(LandmarkRules.ChestMarker, x, 1, 3, config);
            chests++;
        }

        Marker(LandmarkRules.SoundMarker, span / 2, 3, l - 4, "sound=amb_sounder;radius=24");
    }

    /// <summary>The content with the synthetic ship added to its giant pool (beside the shipped sample).</summary>
    public static void AddTo(GameContent content, StructureTemplate ship)
    {
        var pool = new List<StructureTemplate>(content.GiantShips) { ship };
        var dropped = new List<string>();
        content.SetGiantShips(pool, dropped.Add);
        Xunit.Assert.Empty(dropped);
    }
}
