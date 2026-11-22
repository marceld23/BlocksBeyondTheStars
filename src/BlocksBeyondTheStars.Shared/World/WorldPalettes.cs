// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// What a world of the colour harmony (terrain generation 48, <see cref="WorldHarmony"/>) rolls its colours FROM — one set
/// of fitting choices per kind of planet, so a world's colours follow from what the planet is and still differ from the
/// next world of its kind.
/// <para>
/// The kind is read from the type's data traits, never from a key list: the family <see cref="MistProfiles.FamilyOf"/>
/// already derives for the mist (cold or glacial → ice; hot, ashen or volcanic → volcanic; corrosive, contaminated or acid
/// → toxic; crystal, alien or rainbow → exotic; wet, flowering or densely green → lush; desert or savanna → arid; gas
/// giants; everything else the general set). Each family has its own list of sky colours (eight to fifteen anchors), its
/// own odds for the five moods and its own odds for the three plant schemes — an ice world is often pale under a blue,
/// silver or periwinkle sky, a volcanic one often gloomy or earthy under rust, orange or ash, an exotic one often vivid
/// and many-coloured under violet, rose or mint. Every family keeps every mood and every scheme possible, only rarer.
/// </para>
/// </summary>
public static class WorldPalettes
{
    private const int Blue = 0x8CBFF2, PaleCyan = 0x9FD0E8, DeepBlue = 0x7FA8E8, Teal = 0x8FD0B0, Mint = 0x9FE0D0,
        YellowGreen = 0xB8D89A, Amber = 0xE8D89A, PaleGold = 0xF0E0B0, Orange = 0xE8B488, Peach = 0xF0C0A8, Rust = 0xE0A0A0,
        Rose = 0xE8A8C0, Violet = 0xC8A8E0, Periwinkle = 0xB0A8E8, Silver = 0xD0D4DC;

    // The general set: the eight classic sky anchors and seven more, with flatter weights — the three blues together are
    // three worlds in ten (they were every second one). Pastel / mid-bright: the mood makes a sky stronger, paler or darker.
    private static readonly (int Rgb, int Weight)[] GeneralSkies =
    {
        (Blue, 16), (PaleCyan, 9), (DeepBlue, 7), (Teal, 8), (Mint, 6), (YellowGreen, 7), (Amber, 8), (PaleGold, 5),
        (Orange, 7), (Peach, 5), (Rust, 6), (Rose, 5), (Violet, 6), (Periwinkle, 5), (Silver, 5),
    };

    private static readonly (int Rgb, int Weight)[] IceSkies =
    {
        (Blue, 14), (PaleCyan, 14), (DeepBlue, 10), (Periwinkle, 10), (Silver, 12), (Mint, 8), (Rose, 6), (Violet, 8), (PaleGold, 4),
    };

    private static readonly (int Rgb, int Weight)[] AridSkies =
    {
        (Amber, 16), (PaleGold, 12), (Orange, 14), (Peach, 10), (Blue, 12), (PaleCyan, 8), (Rust, 8), (YellowGreen, 6), (Rose, 4),
    };

    private static readonly (int Rgb, int Weight)[] VolcanicSkies =
    {
        (Rust, 16), (Orange, 16), (Amber, 12), (Silver, 12), (Violet, 10), (Peach, 8), (Rose, 6), (DeepBlue, 6),
    };

    private static readonly (int Rgb, int Weight)[] ToxicSkies =
    {
        (YellowGreen, 18), (Teal, 14), (Amber, 12), (Violet, 12), (Mint, 10), (Silver, 8), (Orange, 8), (Periwinkle, 6),
    };

    private static readonly (int Rgb, int Weight)[] LushSkies =
    {
        (Blue, 18), (PaleCyan, 12), (Teal, 10), (Mint, 10), (DeepBlue, 8), (Rose, 8), (Violet, 8), (PaleGold, 6), (Peach, 6), (YellowGreen, 6),
    };

    private static readonly (int Rgb, int Weight)[] ExoticSkies =
    {
        (Violet, 16), (Periwinkle, 12), (Rose, 14), (Mint, 12), (Teal, 10), (Peach, 8), (PaleCyan, 8), (PaleGold, 6), (YellowGreen, 6),
    };

    // Mood odds per family, in the order of WorldMood: balanced, vivid, pastel, earthy, gloomy.
    private static readonly int[] GeneralMoods = { 32, 22, 14, 16, 16 };
    private static readonly int[] IceMoods = { 28, 12, 34, 8, 18 };
    private static readonly int[] AridMoods = { 28, 14, 16, 34, 8 };
    private static readonly int[] VolcanicMoods = { 18, 12, 4, 30, 36 };
    private static readonly int[] ToxicMoods = { 20, 26, 6, 18, 30 };
    private static readonly int[] LushMoods = { 34, 34, 14, 10, 8 };
    private static readonly int[] ExoticMoods = { 22, 42, 20, 4, 12 };

    // Scheme odds per family, in the order of HarmonyScheme: triad, monochrome, rainbow.
    private static readonly int[] GeneralSchemes = { 68, 12, 20 };
    private static readonly int[] IceSchemes = { 62, 26, 12 };
    private static readonly int[] AridSchemes = { 64, 24, 12 };
    private static readonly int[] VolcanicSchemes = { 60, 28, 12 };
    private static readonly int[] ToxicSchemes = { 64, 18, 18 };
    private static readonly int[] LushSchemes = { 62, 8, 30 };
    private static readonly int[] ExoticSchemes = { 48, 8, 44 };

    /// <summary>The sky colours (0xRRGGBB, with weights) a world of <paramref name="family"/> picks its sky from.</summary>
    public static IReadOnlyList<(int Rgb, int Weight)> SkiesOf(MistFamily family) => family switch
    {
        MistFamily.Ice => IceSkies,
        MistFamily.Arid => AridSkies,
        MistFamily.Volcanic => VolcanicSkies,
        MistFamily.Toxic => ToxicSkies,
        MistFamily.Lush => LushSkies,
        MistFamily.Exotic => ExoticSkies,
        _ => GeneralSkies,
    };

    /// <summary>The odds of the five moods for a world of <paramref name="family"/>, indexed by <see cref="WorldMood"/>.</summary>
    public static IReadOnlyList<int> MoodWeightsOf(MistFamily family) => family switch
    {
        MistFamily.Ice => IceMoods,
        MistFamily.Arid => AridMoods,
        MistFamily.Volcanic => VolcanicMoods,
        MistFamily.Toxic => ToxicMoods,
        MistFamily.Lush => LushMoods,
        MistFamily.Exotic => ExoticMoods,
        _ => GeneralMoods,
    };

    /// <summary>The odds of the three plant schemes for a world of <paramref name="family"/>, indexed by
    /// <see cref="HarmonyScheme"/>.</summary>
    public static IReadOnlyList<int> SchemeWeightsOf(MistFamily family) => family switch
    {
        MistFamily.Ice => IceSchemes,
        MistFamily.Arid => AridSchemes,
        MistFamily.Volcanic => VolcanicSchemes,
        MistFamily.Toxic => ToxicSchemes,
        MistFamily.Lush => LushSchemes,
        MistFamily.Exotic => ExoticSchemes,
        _ => GeneralSchemes,
    };

    /// <summary>A weighted pick: the index into <paramref name="weights"/> that <paramref name="word"/> lands on.</summary>
    public static int Pick(IReadOnlyList<int> weights, uint word)
    {
        int total = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            total += weights[i];
        }

        if (total <= 0)
        {
            return 0;
        }

        int roll = (int)(word % (uint)total);
        for (int i = 0; i < weights.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0)
            {
                return i;
            }
        }

        return weights.Count - 1;
    }
}
