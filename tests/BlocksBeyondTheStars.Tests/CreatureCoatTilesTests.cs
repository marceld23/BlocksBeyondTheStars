// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The creature coat tiles (raw 64 × 64 RGBA32, rows bottom-up, in <c>client/Assets/Resources/textures</c>):
/// <list type="bullet">
/// <item>#2863: the 16 coat pattern masks <c>creature_pattern_&lt;name&gt;</c> are bundled, and each is a clean binary mask —
/// white (255) = the pattern colour, black (0) = the body, fully opaque — whose mark covers a sensible share of the tile.</item>
/// <item>#2867: the 5 horn tiles <c>creature_horn_&lt;name&gt;</c> are bundled as opaque grey tiles.</item>
/// <item>#2860: no creature tile — hide, pattern or horn — shows a seam where it repeats on a part. The seam ratio is the
/// mean luma step across the wrap (first vs last row, first vs last column) over the mean step between neighbours inside
/// the tile; a seamless tile sits around 1. Tools: <c>tools/ai-assets/gen_coat_tiles.py</c>, <c>heal_seams.py</c>.</item>
/// </list>
/// </summary>
public sealed class CreatureCoatTilesTests
{
    private const int Size = 64;
    private const int TileBytes = Size * Size * 4;
    private const double MaxSeamRatio = 1.3;
    private const double MinCoverage = 0.10;
    private const double MaxCoverage = 0.75;

    private static readonly string[] PatternNames =
    [
        "giraffe", "dalmatian", "pinto", "cheetah", "jaguar", "tabby", "brindle", "okapi",
        "ladybird", "poisonfrog", "salamander", "eyespots", "tortoise", "diamondback", "chevrons", "lengthwise",
    ];

    private static readonly string[] HornNames = ["ridged", "grain", "spiral", "crystal", "bark"];

    private static string TexturesDir() => Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "textures");

    public static TheoryData<string> Patterns() => ToTheoryData(PatternNames);

    public static TheoryData<string> Horns() => ToTheoryData(HornNames);

    private static TheoryData<string> ToTheoryData(string[] names)
    {
        var data = new TheoryData<string>();
        foreach (string name in names)
        {
            data.Add(name);
        }

        return data;
    }

    private static byte[] ReadTile(string key)
    {
        string path = Path.Combine(TexturesDir(), key + ".bytes");
        Assert.True(File.Exists(path), "missing tile " + key + ".bytes");
        byte[] data = File.ReadAllBytes(path);
        Assert.True(data.Length == TileBytes, key + ".bytes is not a raw 64x64 RGBA32 tile: " + data.Length.ToString(CultureInfo.InvariantCulture) + " bytes");
        return data;
    }

    private static string F(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    [Fact]
    public void ExactlyThePatternAndHornTilesAreBundled()
    {
        foreach (var (prefix, names) in new[] { ("creature_pattern_", PatternNames), ("creature_horn_", HornNames) })
        {
            var bundled = Directory.GetFiles(TexturesDir(), prefix + "*.bytes")
                .Select(f => Path.GetFileNameWithoutExtension(f)[prefix.Length..])
                .Order(StringComparer.Ordinal)
                .ToList();
            Assert.Equal(names.Order(StringComparer.Ordinal), bundled);
            foreach (string name in names)
            {
                ReadTile(prefix + name);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Patterns))]
    public void PatternTile_IsAnOpaqueBinaryMask_WithASensibleCoverage(string name)
    {
        byte[] data = ReadTile("creature_pattern_" + name);
        int marks = 0;
        for (int i = 0; i < data.Length; i += 4)
        {
            byte r = data[i], g = data[i + 1], b = data[i + 2], a = data[i + 3];
            Assert.True((r == 0 || r == 255) && g == r && b == r && a == 255,
                $"creature_pattern_{name}: texel {(i / 4).ToString(CultureInfo.InvariantCulture)} is not pure black or white and opaque");
            if (r == 255)
            {
                marks++;
            }
        }

        double coverage = marks / (double)(Size * Size);
        Assert.True(coverage is >= MinCoverage and <= MaxCoverage,
            $"creature_pattern_{name}: the mark covers {F(coverage)} of the tile, expected {F(MinCoverage)}..{F(MaxCoverage)}");
    }

    [Theory]
    [MemberData(nameof(Horns))]
    public void HornTile_IsOpaqueGrey(string name)
    {
        byte[] data = ReadTile("creature_horn_" + name);
        for (int i = 0; i < data.Length; i += 4)
        {
            Assert.True(data[i + 1] == data[i] && data[i + 2] == data[i] && data[i + 3] == 255,
                $"creature_horn_{name}: texel {(i / 4).ToString(CultureInfo.InvariantCulture)} is not an opaque grey");
        }
    }

    [Fact]
    public void EveryCreatureTile_RepeatsWithoutASeam()
    {
        var seams = new List<string>();
        foreach (string path in Directory.GetFiles(TexturesDir(), "creature_*.bytes").Order(StringComparer.Ordinal))
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length != TileBytes)
            {
                seams.Add(Path.GetFileNameWithoutExtension(path) + " (not a 64x64 RGBA32 tile)");
                continue;
            }

            double ratio = SeamRatio(data);
            if (ratio > MaxSeamRatio)
            {
                seams.Add(Path.GetFileNameWithoutExtension(path) + " " + F(ratio));
            }
        }

        Assert.True(seams.Count == 0,
            "creature tiles with a visible seam (ratio above " + F(MaxSeamRatio) + "; heal them with "
            + "tools/ai-assets/heal_seams.py): " + string.Join(", ", seams));
    }

    /// <summary>max(seam_v / inner_v, seam_h / inner_h) on luma 0..1 — see the class summary. On a binary mask this is the
    /// share of texel pairs across the wrap that differ over the share of neighbouring pairs inside that differ.</summary>
    private static double SeamRatio(byte[] data)
    {
        var luma = new double[Size, Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int i = ((y * Size) + x) * 4;
                luma[y, x] = ((0.299 * data[i]) + (0.587 * data[i + 1]) + (0.114 * data[i + 2])) / 255.0;
            }
        }

        double innerV = 0, innerH = 0, seamV = 0, seamH = 0;
        for (int a = 0; a < Size; a++)
        {
            for (int b = 0; b < Size - 1; b++)
            {
                innerV += Math.Abs(luma[b + 1, a] - luma[b, a]);
                innerH += Math.Abs(luma[a, b + 1] - luma[a, b]);
            }

            seamV += Math.Abs(luma[0, a] - luma[Size - 1, a]);
            seamH += Math.Abs(luma[a, 0] - luma[a, Size - 1]);
        }

        const double pairs = Size * (Size - 1);
        return Math.Max(Ratio(seamV / Size, innerV / pairs), Ratio(seamH / Size, innerH / pairs));
    }

    private static double Ratio(double seam, double inner) => inner > 0 ? seam / inner : seam > 0 ? double.PositiveInfinity : 0;
}
