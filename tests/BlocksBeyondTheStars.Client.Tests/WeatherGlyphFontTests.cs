// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text;
using BlocksBeyondTheStars.Shared.Weather;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2571: the weather glyphs (<see cref="WeatherLook.Glyph"/>) are drawn by the HUD weather chip (TextMeshPro on the
/// runtime Rajdhani SDF font) and by the maps and the flight view (legacy Text on Rajdhani). Rajdhani and the Noto text
/// fonts carry only ≈ and • of them, so rain, storms and most other states showed a missing-glyph box □ and flooded the
/// player log. A tiny subset of Noto Sans Symbols 2 now sits in both fallback chains. These tests pin the glyph set and
/// read the real font files, so a new weather state with a glyph no bundled font carries fails here, not on screen.
/// </summary>
public sealed class WeatherGlyphFontTests
{
    /// <summary>Every glyph a weather state may use — each one is in a bundled UI font (see the font test below).
    /// A new glyph must first be added to <c>NotoSansSymbols2-Subset.ttf</c> (recipe in NOTICES.md).</summary>
    private const string AllowedGlyphs = "☀☁☂⚡❄≈☣★▲❋•";

    /// <summary>The arrows the subset also carries: the drifting weather fronts on the landing map and the planet map
    /// ("◀ front", "front ▶"), the touch "NEXT ▶", the chat's "▼ newer lines", the editors' ◀ ▶ ▼ and ► ◄.</summary>
    private const string Arrows = "◀▶▼►◄";

    /// <summary>The radio-call glyph the subset also carries: the NPC radio calls' chat sender ("☎ Name (Place)") and
    /// the Missions tab's "☎ Recent radio calls" — it replaced the 📻 emoji, which no bundled font has.</summary>
    private const string RadioCall = "☎";

    /// <summary>The fonts both text paths fall back through, in order (UiText.Font and Rajdhani's importer list).</summary>
    private static readonly string[] FallbackChain =
    {
        "Rajdhani-Medium.ttf", "NotoSans-Medium.ttf", "NotoSansSymbols2-Subset.ttf",
        "NotoSansJP-Medium.otf", "NotoSansKR-Medium.otf", "NotoSansSC-Medium.otf",
    };

    private static string FontDir => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "fonts");

    [Fact]
    public void EveryWeatherStateUsesAPinnedGlyph()
    {
        var allowed = AllowedGlyphs.EnumerateRunes().ToHashSet();
        var keys = WeatherCatalog.AllKeys.Concat(new[] { "clear", "unknown_state", string.Empty }).ToList();
        var offenders = new List<string>();
        foreach (var key in keys)
        {
            string glyph = WeatherLook.Glyph(key);
            if (string.IsNullOrEmpty(glyph))
            {
                offenders.Add($"'{key}' has no glyph");
            }

            // A glyph outside the pinned set: add it to the symbol subset font first (recipe in NOTICES.md).
            offenders.AddRange(glyph.EnumerateRunes().Where(r => !allowed.Contains(r))
                .Select(r => $"'{key}' uses {r} (U+{r.Value:X4})"));
        }

        Assert.Empty(offenders);
        Assert.Equal("•", WeatherLook.Glyph(null));
    }

    [Fact]
    public void EveryPinnedGlyphIsInTheUiFontChain()
    {
        var covered = new HashSet<int>();
        foreach (var file in FallbackChain)
        {
            covered.UnionWith(CodePoints(Path.Combine(FontDir, file)));
        }

        // A glyph in no bundled UI font renders as □.
        var missing = (AllowedGlyphs + Arrows + RadioCall).EnumerateRunes().Where(r => !covered.Contains(r.Value))
            .Select(r => $"{r} (U+{r.Value:X4})").ToList();
        Assert.Empty(missing);

        // The point of the subset: Rajdhani alone lacks these (if it ever gains them, the subset may go).
        var rajdhani = CodePoints(Path.Combine(FontDir, "Rajdhani-Medium.ttf"));
        Assert.Contains(0x2248, rajdhani);       // ≈
        Assert.DoesNotContain(0x26A1, rajdhani); // ⚡ — the glyph from the player's log
    }

    [Fact]
    public void TheSymbolSubsetStaysSmallAndIsWiredIntoBothFallbackChains()
    {
        string subset = Path.Combine(FontDir, "NotoSansSymbols2-Subset.ttf");
        Assert.True(new FileInfo(subset).Length < 32 * 1024, "everything under Resources/ lands in the WebGL download — keep the subset tiny");
        Assert.True(File.Exists(Path.Combine(FontDir, "NotoSansSymbols2-OFL.txt")), "the OFL licence ships next to the font");

        // Legacy Text (menus, maps, flight view): Rajdhani's importer fallback list references the subset by GUID.
        string guid = File.ReadAllLines(subset + ".meta").Single(l => l.StartsWith("guid: ", StringComparison.Ordinal))["guid: ".Length..];
        string rajdhaniMeta = File.ReadAllText(Path.Combine(FontDir, "Rajdhani-Medium.ttf.meta"));
        Assert.Contains($"guid: {guid}, type: 3", rajdhaniMeta);

        // TextMeshPro (HUD): the runtime font asset adds it as a fallback.
        string uiText = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "UiText.cs"));
        Assert.Contains("\"fonts/NotoSansSymbols2-Subset\"", uiText);
    }

    /// <summary>The Unicode code points a TrueType/OpenType font maps to a real glyph (its Unicode cmap subtables,
    /// formats 4 and 12).</summary>
    private static HashSet<int> CodePoints(string path)
    {
        byte[] f = File.ReadAllBytes(path);
        int numTables = U16(f, 4);
        int cmap = -1;
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + (i * 16);
            if (Encoding.ASCII.GetString(f, rec, 4) == "cmap")
            {
                cmap = (int)U32(f, rec + 8);
                break;
            }
        }

        Assert.True(cmap >= 0, $"{Path.GetFileName(path)} has no cmap table");
        var set = new HashSet<int>();
        int subtables = U16(f, cmap + 2);
        for (int i = 0; i < subtables; i++)
        {
            int rec = cmap + 4 + (i * 8);
            int platform = U16(f, rec);
            int encoding = U16(f, rec + 2);
            if (platform != 0 && !(platform == 3 && (encoding == 1 || encoding == 10)))
            {
                continue; // not a Unicode subtable
            }

            int sub = cmap + (int)U32(f, rec + 4);
            switch (U16(f, sub))
            {
                case 4:
                    ReadFormat4(f, sub, set);
                    break;
                case 12:
                    ReadFormat12(f, sub, set);
                    break;
            }
        }

        return set;
    }

    private static void ReadFormat4(byte[] f, int sub, HashSet<int> set)
    {
        int segX2 = U16(f, sub + 6);
        int ends = sub + 14;
        int starts = ends + segX2 + 2;
        int deltas = starts + segX2;
        int rangeOffsets = deltas + segX2;
        for (int s = 0; s < segX2 / 2; s++)
        {
            int end = U16(f, ends + (2 * s));
            int start = U16(f, starts + (2 * s));
            int delta = U16(f, deltas + (2 * s));
            int rangeAt = rangeOffsets + (2 * s);
            int range = U16(f, rangeAt);
            for (int c = start; c <= end && c != 0xFFFF; c++)
            {
                int glyph = range == 0 ? (c + delta) & 0xFFFF : U16(f, rangeAt + range + (2 * (c - start)));
                if (range != 0 && glyph != 0)
                {
                    glyph = (glyph + delta) & 0xFFFF;
                }

                if (glyph != 0)
                {
                    set.Add(c);
                }
            }
        }
    }

    private static void ReadFormat12(byte[] f, int sub, HashSet<int> set)
    {
        long groups = U32(f, sub + 12);
        for (long g = 0; g < groups; g++)
        {
            int at = sub + 16 + (int)(g * 12);
            long start = U32(f, at);
            long end = U32(f, at + 4);
            long firstGlyph = U32(f, at + 8);
            for (long c = start; c <= end; c++)
            {
                if (firstGlyph + (c - start) != 0)
                {
                    set.Add((int)c);
                }
            }
        }
    }

    private static int U16(byte[] f, int at) => (f[at] << 8) | f[at + 1];

    private static long U32(byte[] f, int at) => ((long)f[at] << 24) | ((long)f[at + 1] << 16) | ((long)f[at + 2] << 8) | f[at + 3];
}
