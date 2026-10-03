// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;

namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>
/// Coins the names of substances ("Virexin") and compounds ("Kryovel"): language-neutral, so they need no
/// localization, and a pure function of a hash, so the server and every client read the same name without any
/// traffic. The syllable sets are small and soft on purpose — names a child can say, and no accidental words.
/// </summary>
public static class BioNames
{
    private static readonly string[] Starts =
    {
        "Vi", "Tho", "Ze", "Ky", "Lu", "Ma", "No", "Ra", "Sy", "Te", "Xe", "Ori", "Ali", "Bre", "Cal", "Dro",
        "Eli", "Fen", "Gal", "Hel", "Ixo", "Jun", "Kel", "Lir", "Mor", "Nel", "Pha", "Qui", "Ril", "Sol", "Tyr", "Vel",
    };

    private static readonly string[] Middles =
    {
        "re", "la", "vo", "ni", "xa", "to", "mi", "ro", "le", "na", "zo", "ri", "ve", "lu", "ta", "dra",
    };

    private static readonly string[] SubstanceEnds =
    {
        "xin", "lin", "zol", "ran", "tine", "lax", "dyl", "ron", "nase", "vix", "mium", "than", "sol", "ryl", "dine", "phen",
    };

    private static readonly string[] CompoundEnds =
    {
        "gel", "vel", "tan", "rit", "dor", "lex", "mar", "nox", "sil", "tum", "var", "zen", "lat", "ron", "mid", "kol",
    };

    /// <summary>The name of the substance a species carries.</summary>
    public static string Substance(uint seed) => Build(BioHash.Draw(seed, 40), SubstanceEnds);

    /// <summary>The name of a compound, from the hash of what it does.</summary>
    public static string Compound(ulong signature) => Build(BioHash.Mix(signature ^ 0xC0FFEEUL), CompoundEnds);

    private static string Build(ulong h, string[] ends)
    {
        var sb = new StringBuilder(12);
        sb.Append(Starts[(int)(h % (ulong)Starts.Length)]);
        h >>= 8;
        if ((h & 1) == 0)
        {
            sb.Append(Middles[(int)((h >> 1) % (ulong)Middles.Length)]);
        }

        h >>= 8;
        sb.Append(ends[(int)(h % (ulong)ends.Length)]);
        return sb.ToString();
    }
}
