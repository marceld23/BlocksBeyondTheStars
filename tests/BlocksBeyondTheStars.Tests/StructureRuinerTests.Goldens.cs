// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Tests.Helpers;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Goldens of the shared ruin pass itself (#3515), captured before it grew revisions and options: the pass run straight
/// over a voxel grid (the test landmarks and a synthetic grid with shapes, tints, glows, lights, glass, ladders, kept
/// cells and cavities), every profile, several seeds and strengths. Revision 1 with the default options must build these
/// byte for byte forever — a standing landmark's markers are re-derived through it on every load. The landmark goldens
/// of <see cref="StructureGeneratorGoldenTests"/> pin the same pass end to end.
/// </summary>
public sealed class StructureRuinerGoldenTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static readonly long[] Seeds = { 1, 20261112, 0x5DEECE66DL };

    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The pass the landmarks always ran (the old call), revision 1 asked for by name with a fresh set of
    /// options, and the same run through a grid over the caller's own arrays and through a copy written back (#3515) —
    /// all four build the pins.</summary>
    [Theory]
    [InlineData("default")]
    [InlineData("revision1")]
    [InlineData("view")]
    [InlineData("copy")]
    public void RevisionOne_WithTheDefaultOptions_MatchesThePins(string path)
    {
        var cases = new List<(string, ulong)>();
        ulong Run(RuinerTestGrids.Case c, long seed, string profile, double strength, string surface)
            => path switch
            {
                "revision1" => RunExplicit(c, seed, profile, strength, surface),
                "view" => RunOverView(c, seed, profile, strength, surface),
                "copy" => RunOnCopy(c, seed, profile, strength, surface),
                _ => RunDefault(c, seed, profile, strength, surface),
            };

        foreach (long seed in Seeds)
        {
            string s = seed.ToString(CultureInfo.InvariantCulture);
            foreach (double strength in new[] { 0.0, 0.15, 1.0 })
            {
                cases.Add(($"tower:{s}:{N(strength)}", Run(RuinerTestGrids.FromTemplate(Content, LandmarkShapes.Tower()), seed, LandmarkRules.ProfileMasonry, strength, "grass")));
            }

            cases.Add(($"tower:{s}:sand", Run(RuinerTestGrids.FromTemplate(Content, LandmarkShapes.Tower()), seed, LandmarkRules.ProfileMasonry, 0, "sand")));
            foreach (double strength in new[] { 0.0, 0.9 })
            {
                cases.Add(($"lattice:{s}:{N(strength)}", Run(RuinerTestGrids.FromTemplate(Content, LandmarkShapes.Lattice()), seed, LandmarkRules.ProfileLattice, strength, "grass")));
                cases.Add(($"mine:{s}:{N(strength)}", Run(RuinerTestGrids.FromTemplate(Content, LandmarkShapes.Mine()), seed, LandmarkRules.ProfileUnderground, strength, "ice")));
            }

            foreach (string profile in new[] { LandmarkRules.ProfileMasonry, LandmarkRules.ProfileLattice, LandmarkRules.ProfileUnderground })
            {
                foreach (double strength in new[] { 0.0, 0.6, 1.0 })
                {
                    cases.Add(($"soup:{profile}:{s}:{N(strength)}", Run(RuinerTestGrids.Soup(Content), seed, profile, strength, "basalt")));
                }
            }
        }

        StructureGoldens.AssertPinned("ruin pass (" + path + ")", Pins, cases);
    }

    private static ulong RunDefault(RuinerTestGrids.Case c, long seed, string profile, double strength, string surface)
    {
        StructureRuiner.Run(c.Grid, Content, seed, profile, strength, c.GroundRow, c.Markers, surface);
        return RuinerTestGrids.Hash(Content, c.Grid);
    }

    private static ulong RunExplicit(RuinerTestGrids.Case c, long seed, string profile, double strength, string surface)
    {
        StructureRuiner.Run(c.Grid, Content, seed, profile, strength, c.GroundRow, c.Markers, surface, revision: 1, new RuinOptions());
        return RuinerTestGrids.Hash(Content, c.Grid);
    }

    /// <summary>The pass in a grid over the case's own arrays: the result lands in them.</summary>
    private static ulong RunOverView(RuinerTestGrids.Case c, long seed, string profile, double strength, string surface)
    {
        var g = c.Grid;
        var view = VoxelGrid.Over(g.W, g.H, g.L, g.Blocks, g.Mods, g.Shapes);
        RuinerTestGrids.CopyBits(g, view);
        StructureRuiner.Run(view, Content, seed, profile, strength, c.GroundRow, c.Markers.Select(m => m.LocalPos).ToList(), surface,
            StructureRuiner.Revision, options: null);
        Assert.Same(g.Blocks, view.Blocks);
        return RuinerTestGrids.Hash(Content, view);
    }

    /// <summary>The pass on a copy, written back into fresh arrays — the case's own arrays stay as they were.</summary>
    private static ulong RunOnCopy(RuinerTestGrids.Case c, long seed, string profile, double strength, string surface)
    {
        var g = c.Grid;
        ulong before = RuinerTestGrids.Hash(Content, g);
        var copy = VoxelGrid.CopyOf(g.W, g.H, g.L, g.Blocks, g.Mods, g.Shapes);
        RuinerTestGrids.CopyBits(g, copy);
        StructureRuiner.Run(copy, Content, seed, profile, strength, c.GroundRow, c.Markers, surface);
        Assert.Equal(before, RuinerTestGrids.Hash(Content, g));

        var blocks = new ushort[g.Blocks.Length];
        var mods = new Dictionary<int, (int Tint, int Glow)> { [0] = (1, 1) }; // stale entries are cleared
        var shapes = new Dictionary<int, int> { [0] = 7 };
        copy.CopyTo(blocks, mods, shapes);
        var back = VoxelGrid.Over(g.W, g.H, g.L, blocks, mods, shapes);
        RuinerTestGrids.CopyBits(copy, back);
        return RuinerTestGrids.Hash(Content, back);
    }

    private static readonly Dictionary<string, string> Pins = new()
    {
        ["tower:1:0"] = "03a035712af80438",
        ["tower:1:0.15"] = "96f2062dff0ed5e8",
        ["tower:1:1"] = "67a4c4fd3bb6841f",
        ["tower:1:sand"] = "24da5a8619709ab6",
        ["lattice:1:0"] = "f99127d4738d680b",
        ["mine:1:0"] = "d425b76b55e753a9",
        ["lattice:1:0.9"] = "99bacbf2ffe13808",
        ["mine:1:0.9"] = "2041a4c5ad65f503",
        ["soup:masonry:1:0"] = "6c6333194dd34a4e",
        ["soup:masonry:1:0.6"] = "a45d2bb192b43574",
        ["soup:masonry:1:1"] = "e79e6cf3cedc26cd",
        ["soup:lattice:1:0"] = "6398a9cc70aa8545",
        ["soup:lattice:1:0.6"] = "e83cdb5606c6f67e",
        ["soup:lattice:1:1"] = "1f5dc56edf081c3b",
        ["soup:underground:1:0"] = "5c90f89e109f5f98",
        ["soup:underground:1:0.6"] = "0087e7da0caf2a22",
        ["soup:underground:1:1"] = "b918bb2f5486911b",
        ["tower:20261112:0"] = "d96b2e870dcb6cef",
        ["tower:20261112:0.15"] = "f633298d99734177",
        ["tower:20261112:1"] = "513c51380d84fe6d",
        ["tower:20261112:sand"] = "e52addadee83be91",
        ["lattice:20261112:0"] = "c7000b633f421e80",
        ["mine:20261112:0"] = "ecc77e44bc09cd3b",
        ["lattice:20261112:0.9"] = "38999d6f1a9c66db",
        ["mine:20261112:0.9"] = "8eb8250e2b1c7407",
        ["soup:masonry:20261112:0"] = "03bcb2721a2c293e",
        ["soup:masonry:20261112:0.6"] = "d0b5d5293070a75b",
        ["soup:masonry:20261112:1"] = "06699bbed49a0825",
        ["soup:lattice:20261112:0"] = "d8df818de0e5769d",
        ["soup:lattice:20261112:0.6"] = "d5fc51639cd18ddc",
        ["soup:lattice:20261112:1"] = "b0c5494ef25696c9",
        ["soup:underground:20261112:0"] = "37f482ba513070d8",
        ["soup:underground:20261112:0.6"] = "9d54774386fc0c21",
        ["soup:underground:20261112:1"] = "4e373068c74fd16d",
        ["tower:25214903917:0"] = "3756a093e27a9a5b",
        ["tower:25214903917:0.15"] = "6f7ac7f870c66290",
        ["tower:25214903917:1"] = "3615b04348217292",
        ["tower:25214903917:sand"] = "dee7a2a3a9f03831",
        ["lattice:25214903917:0"] = "5e87e10bb2d683e0",
        ["mine:25214903917:0"] = "d904fec32c50583f",
        ["lattice:25214903917:0.9"] = "b5ccf544edeb2071",
        ["mine:25214903917:0.9"] = "02fa07380379b8e3",
        ["soup:masonry:25214903917:0"] = "bb2f46720f1336b0",
        ["soup:masonry:25214903917:0.6"] = "0bf77484183e1235",
        ["soup:masonry:25214903917:1"] = "323db85f4b167ff3",
        ["soup:lattice:25214903917:0"] = "7951a397b30e1810",
        ["soup:lattice:25214903917:0.6"] = "d9415de4ccc42e24",
        ["soup:lattice:25214903917:1"] = "ad9d95a23d5997ee",
        ["soup:underground:25214903917:0"] = "900caf401af375b8",
        ["soup:underground:25214903917:0.6"] = "5c48bef93ed8bc47",
        ["soup:underground:25214903917:1"] = "916d6a28bdc1549b",
    };
}

/// <summary>Voxel grids for the ruin-pass tests (#3515): a landmark template read straight into a grid, a synthetic grid
/// with every kind of cell the pass treats differently, and a stable fingerprint of a grid.</summary>
internal static class RuinerTestGrids
{
    internal sealed record Case(VoxelGrid Grid, int GroundRow, List<SettlementMarker> Markers);

    /// <summary>The intact landmark (no turn) as a grid, with the template's keep cells and every marker.</summary>
    public static Case FromTemplate(GameContent content, StructureTemplate t)
    {
        var intact = LandmarkGenerator.Build(t, 1, 0, content, "grass", ruin: false);
        var s = intact.Structure;
        var g = new VoxelGrid(s.Width, s.Height, s.Length);
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < s.Height; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    var (tint, glow) = s.GetModifier(x, y, z);
                    g.Set(x, y, z, s.Get(x, y, z), tint, glow, s.GetShape(x, y, z));
                    g.Cavity[g.Index(x, y, z)] = intact.IsCavity(x, y, z);
                }
            }
        }

        foreach (var c in t.Cells.Where(c => c.Keep && g.In(c.X, c.Y, c.Z)))
        {
            g.Keep[g.Index(c.X, c.Y, c.Z)] = true;
        }

        return new Case(g, intact.GroundRow, s.Markers.ToList());
    }

    /// <summary>
    /// A synthetic block of rooms, 24 × 24 × 18 with the ground row at 8: an outer shell, a floor every five rows, a
    /// partition, a cellar with cavity cells below the ground row; the wall material cycles through stone, iron, glass,
    /// lights with and without a switched-off twin, ladders and rusted panels; some cells are shaped, tinted or glowing, a
    /// strip of the wall is kept, and three markers stand in it.
    /// </summary>
    public static Case Soup(GameContent content)
    {
        const int W = 24, H = 24, L = 18, Ground = 8;
        string[] palette = { "stone", "iron_wall", "glass", "torch", "stone", "light_white", "ladder", "rusted_panel", "lantern", "stone", "moss_stone" };
        ushort B(string key) => content.GetBlock(key)!.NumericId.Value;
        var g = new VoxelGrid(W, H, L);
        for (int x = 0; x < W; x++)
        {
            for (int y = 0; y < H; y++)
            {
                for (int z = 0; z < L; z++)
                {
                    bool shell = x == 0 || z == 0 || x == W - 1 || z == L - 1;
                    bool floor = y % 5 == 0 || y == Ground;
                    bool partition = x == 11 && y > Ground && (z < 6 || z > 9);
                    if (y < Ground && !shell && x > 2 && x < 9 && z > 2 && z < 12)
                    {
                        g.Cavity[g.Index(x, y, z)] = true; // the cellar
                        continue;
                    }

                    if (!(shell || floor || partition || y < Ground))
                    {
                        continue;
                    }

                    int pick = ((x * 7) + (y * 13) + (z * 3)) % palette.Length;
                    string key = floor || y < Ground ? (pick % 3 == 0 ? "iron_wall" : "stone") : palette[pick];
                    int shape = (x + y + z) % 9 == 0 ? 1 + ((x + z) % 4) : 0;
                    int tint = (x * y) % 13 == 1 ? 0x804020 + (x * 0x10101) : 0;
                    int glow = (y * z) % 17 == 2 ? 0x30FF60 : 0;
                    g.Set(x, y, z, B(key), tint, glow, shape);
                }
            }
        }

        for (int y = 14; y < H; y++)
        {
            g.Keep[g.Index(W - 1, y, 5)] = true; // a kept strip high on one wall
        }

        var markers = new List<SettlementMarker>
        {
            new(LandmarkRules.ChestMarker, new Vector3i(5, Ground + 1, 5), data: "items=iron_plate:1"),
            new(LandmarkRules.DenMarker, new Vector3i(15, 16, 8)),
            new("npc", new Vector3i(6, 2, 6)),
        };
        foreach (var m in markers)
        {
            g.Clear(m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z);
        }

        return new Case(g, Ground, markers);
    }

    /// <summary>Every cell of the grid.</summary>
    public static IEnumerable<(int X, int Y, int Z)> Cells(VoxelGrid g)
    {
        for (int x = 0; x < g.W; x++)
        {
            for (int y = 0; y < g.H; y++)
            {
                for (int z = 0; z < g.L; z++)
                {
                    yield return (x, y, z);
                }
            }
        }
    }

    /// <summary>The pieces of touching blocks (faces and edges, the settle step's rule), as lists of cells.</summary>
    public static List<List<(int X, int Y, int Z)>> Pieces(VoxelGrid g)
    {
        var seen = new HashSet<(int, int, int)>();
        var pieces = new List<List<(int X, int Y, int Z)>>();
        foreach (var start in Cells(g).Where(c => g.Solid(c.X, c.Y, c.Z)))
        {
            if (!seen.Add(start))
            {
                continue;
            }

            var piece = new List<(int X, int Y, int Z)>();
            var open = new Stack<(int X, int Y, int Z)>();
            open.Push(start);
            while (open.Count > 0)
            {
                var (x, y, z) = open.Pop();
                piece.Add((x, y, z));
                foreach (var n in Touching(x, y, z).Where(n => g.Solid(n.X, n.Y, n.Z) && seen.Add(n)))
                {
                    open.Push(n);
                }
            }

            pieces.Add(piece);
        }

        return pieces;
    }

    private static IEnumerable<(int X, int Y, int Z)> Touching(int x, int y, int z)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int axes = System.Math.Abs(dx) + System.Math.Abs(dy) + System.Math.Abs(dz);
                    if (axes == 1 || axes == 2)
                    {
                        yield return (x + dx, y + dy, z + dz);
                    }
                }
            }
        }
    }

    /// <summary>A deep copy of a grid: cells, modifiers, keep and cavity.</summary>
    public static VoxelGrid Clone(VoxelGrid g)
    {
        var copy = VoxelGrid.CopyOf(g.W, g.H, g.L, g.Blocks, g.Mods, g.Shapes);
        CopyBits(g, copy);
        return copy;
    }

    /// <summary>Copies the keep and cavity bits of one grid onto another of the same size.</summary>
    public static void CopyBits(VoxelGrid from, VoxelGrid to)
    {
        for (int i = 0; i < from.Blocks.Length; i++)
        {
            to.Keep[i] = from.Keep[i];
            to.Cavity[i] = from.Cavity[i];
        }
    }

    /// <summary>Every cell (block by key, tint, glow, shape), the cavity and the keep bits.</summary>
    public static ulong Hash(GameContent content, VoxelGrid g)
    {
        var h = new StructureGoldenHash(content);
        h.Grid(g.W, g.H, g.L, g.Get, (x, y, z) => g.Mods.TryGetValue(g.Index(x, y, z), out var m) ? m : (0, 0), g.ShapeAt);
        h.Grid(g.W, g.H, g.L, (x, y, z) => (ushort)((g.Cavity[g.Index(x, y, z)] ? 1 : 0) | (g.Keep[g.Index(x, y, z)] ? 2 : 0)));
        return h.Value;
    }
}
