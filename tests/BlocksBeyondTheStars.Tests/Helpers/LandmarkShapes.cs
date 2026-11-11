// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Tests.Helpers;

/// <summary>
/// Small landmark templates for the landmark tests (#3483): a hollow tower, a lattice mast and a mine with an
/// entrance hut over tunnels. Built in code so the tests do not depend on shipped landmark content.
/// </summary>
public static class LandmarkShapes
{
    /// <summary>A hollow square tower: stone walls, a floor every eight rows, glass windows, torches, a ladder up one
    /// corner, a chest and a sound marker on the ground floor and a den on the first floor.</summary>
    public static StructureTemplate Tower(string key = "test_tower", int size = 16, int height = 40, bool ruin = true,
        string chest = "items=iron_plate:2-4,gold_ingot:1")
    {
        var t = new StructureTemplate
        {
            Key = key,
            Name = "Test Tower",
            Landmark = true,
            Ruin = ruin ? LandmarkRules.RuinAlways : LandmarkRules.RuinNever,
            RuinProfile = LandmarkRules.ProfileMasonry,
            Requires = new List<string> { LandmarkRules.RequiresAtmosphere },
            Width = size,
            Height = height,
            Length = size,
        };

        for (int y = 0; y < height; y++)
        {
            bool floor = y % 8 == 0;
            for (int x = 0; x < size; x++)
            {
                for (int z = 0; z < size; z++)
                {
                    bool wall = x == 0 || z == 0 || x == size - 1 || z == size - 1;
                    bool ladder = x == 1 && z == 1;
                    if (ladder && y > 0)
                    {
                        t.Cells.Add(Block(x, y, z, "ladder"));
                    }
                    else if (floor)
                    {
                        t.Cells.Add(Block(x, y, z, "stone"));
                    }
                    else if (wall)
                    {
                        bool window = y % 8 == 4 && (x % 4 == 2 || z % 4 == 2);
                        bool torch = y % 8 == 6 && (x == size / 2 || z == size / 2);
                        t.Cells.Add(Block(x, y, z, window ? "glass" : torch ? "torch" : "stone"));
                    }
                }
            }
        }

        t.Cells.Add(Marker(4, 1, 4, LandmarkRules.ChestMarker, chest));
        t.Cells.Add(Marker(size - 4, 1, size - 4, LandmarkRules.SoundMarker, "sound=amb_sounder;radius=60;volume=0.8"));
        t.Cells.Add(Marker(size / 2, 9, size / 2, LandmarkRules.DenMarker, "species=soundi;count=3;radius=12"));
        t.Cells.Add(Marker(size / 2, 1, 2, "npc"));
        return t;
    }

    /// <summary>A lattice mast: four legs that lean together, tied by a ring every six rows.</summary>
    public static StructureTemplate Lattice(string key = "test_lattice", int size = 21, int height = 48)
    {
        var t = new StructureTemplate
        {
            Key = key,
            Name = "Test Lattice",
            Landmark = true,
            Ruin = LandmarkRules.RuinAlways,
            RuinProfile = LandmarkRules.ProfileLattice,
            Width = size,
            Height = height,
            Length = size,
        };

        int half = size / 2;
        for (int y = 0; y < height; y++)
        {
            int inset = System.Math.Min(half - 1, y * (half - 1) / (height - 1));
            int lo = inset, hi = size - 1 - inset;
            foreach (var (x, z) in new[] { (lo, lo), (lo, hi), (hi, lo), (hi, hi) })
            {
                t.Cells.Add(Block(x, y, z, "iron_wall"));
            }

            if (y % 6 == 0)
            {
                for (int i = lo; i <= hi; i++)
                {
                    t.Cells.Add(Block(i, y, lo, "iron_wall"));
                    t.Cells.Add(Block(i, y, hi, "iron_wall"));
                    t.Cells.Add(Block(lo, y, i, "iron_wall"));
                    t.Cells.Add(Block(hi, y, i, "iron_wall"));
                }
            }
        }

        return t;
    }

    /// <summary>A mine: a stone hut on the ground row over a shaft, a long tunnel and a cavern, all as cavity cells,
    /// with a chest in the cavern. <paramref name="depth"/> rows lie below the ground row.</summary>
    public static StructureTemplate Mine(string key = "test_mine", int depth = 24)
    {
        const int Size = 40, Hut = 8;
        var t = new StructureTemplate
        {
            Key = key,
            Name = "Test Mine",
            Landmark = true,
            Ruin = LandmarkRules.RuinAlways,
            RuinProfile = LandmarkRules.ProfileUnderground,
            Width = Size,
            Height = depth + 6,
            Length = Size,
            GroundRow = depth,
        };

        // The hut: a floor on the ground row with a hole for the shaft, walls and a roof.
        for (int x = 0; x < Hut; x++)
        {
            for (int z = 0; z < Hut; z++)
            {
                bool shaft = x == 4 && z == 4;
                bool wall = x == 0 || z == 0 || x == Hut - 1 || z == Hut - 1;
                t.Cells.Add(shaft ? Cavity(x, depth, z) : Block(x, depth, z, "stone"));
                if (wall && !(x == 3 && z == 0))
                {
                    for (int y = depth + 1; y <= depth + 3; y++)
                    {
                        t.Cells.Add(Block(x, y, z, "stone"));
                    }
                }

                t.Cells.Add(Block(x, depth + 4, z, "stone"));
            }
        }

        // The shaft down to row 2 with a ladder on its wall, then a tunnel along X and a cavern at its end.
        for (int y = 2; y < depth; y++)
        {
            t.Cells.Add(Cavity(4, y, 4));
            t.Cells.Add(Block(4, y, 5, "ladder"));
        }

        for (int x = 4; x < 30; x++)
        {
            for (int y = 2; y <= 4; y++)
            {
                t.Cells.Add(Cavity(x, y, 4));
                t.Cells.Add(Cavity(x, y, 3));
            }
        }

        for (int x = 28; x < 38; x++)
        {
            for (int z = 2; z < 14; z++)
            {
                for (int y = 2; y <= 9; y++)
                {
                    t.Cells.Add(Cavity(x, y, z));
                }
            }
        }

        // The shaft, the tunnel and the cavern overlap: one cell per place, the last one written wins.
        var byPlace = new Dictionary<(int, int, int), TemplateCell>();
        foreach (var c in t.Cells)
        {
            byPlace[(c.X, c.Y, c.Z)] = c;
        }

        t.Cells = new List<TemplateCell>(byPlace.Values);
        t.Cells.Add(Marker(34, 2, 8, LandmarkRules.ChestMarker, "table=test_table"));
        return t;
    }

    /// <summary>The compact form of a template (a copy).</summary>
    public static StructureTemplate Compacted(StructureTemplate t)
    {
        TemplateBody.Compact(t);
        return t;
    }

    public static TemplateCell Block(int x, int y, int z, string id, bool keep = false)
        => new() { X = x, Y = y, Z = z, Kind = "block", Id = id, Keep = keep };

    public static TemplateCell Cavity(int x, int y, int z)
        => new() { X = x, Y = y, Z = z, Kind = LandmarkRules.CavityKind };

    public static TemplateCell Marker(int x, int y, int z, string id, string settings = "")
        => new() { X = x, Y = y, Z = z, Kind = "marker", Id = id, Config = settings };
}
