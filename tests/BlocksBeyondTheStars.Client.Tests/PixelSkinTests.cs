// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2682 — the pioneers' pixel skins: the pool file (<c>data/pioneer_skins.json</c>) is valid — every entry decodes to
/// body paint of exact lengths in the palette — and the seeded pick and recolour (<see cref="PixelSkinPool.SkinFor"/>) is
/// deterministic, varied, and never breaks a painting.
/// </summary>
public sealed class PixelSkinTests
{
    private static string PoolText() => File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "pioneer_skins.json"));

    private static readonly int[] Parts = { BodyPaint.Torso, BodyPaint.Arms, BodyPaint.Legs, BodyPaint.Helmet };

    [Fact]
    public void ThePoolFile_IsValid_AndEveryEntryDecodesToBodyPaintOfExactLengths()
    {
        string json = PoolText();
        Assert.Empty(PixelSkinPool.Validate(json));
        var pool = PixelSkinPool.Parse(json);
        Assert.InRange(pool.Skins.Count, 6, 64);
        Assert.Equal(pool.Skins.Count, pool.Skins.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count());
        foreach (var s in pool.Skins)
        {
            Assert.True(s.FacePixels.Length == PixelSkinPool.FacePixelCount, s.Key + " has a face");
            Assert.True(PaintPayload.IsValidSymbols(s.FacePixels), s.Key);
            foreach (int part in Parts)
            {
                string pixels = s.PartPixels(part);
                Assert.True(pixels.Length == 0 || pixels.Length == BodyPaint.ExpectedLength(part), $"{s.Key}: part {part} has {pixels.Length} pixels");
                Assert.True(PaintPayload.IsValidSymbols(pixels), $"{s.Key}: part {part}");
            }

            Assert.True(s.TorsoPixels.Length > 0 && s.ArmPixels.Length > 0 && s.LegPixels.Length > 0, s.Key + " is painted head to toe");
            Assert.InRange(s.Skin, 1, PaintPayload.PaletteSize - 1);
            Assert.InRange(s.Hair, 0, PaintPayload.PaletteSize - 1);
        }
    }

    [Fact]
    public void TheValidation_NamesWrongLengths_ForeignSymbols_TwinKeys_AndColoursOutsideThePalette()
    {
        string face = new('0', PixelSkinPool.FacePixelCount);
        string Entry(string key, string facePixels, int skin = 7, string torso = "")
            => $$"""{"key": "{{key}}", "skin": {{skin}}, "hair": 5, "torso": 11, "arms": 11, "legs": 2, "facePixels": "{{facePixels}}", "torsoPixels": "{{torso}}"}""";

        var problems = PixelSkinPool.Validate($$"""{"skins": [{{Entry("a", face)}}, {{Entry("a", face)}}, {{Entry("b", "123")}}, {{Entry("c", face, skin: 40)}}, {{Entry("d", face, torso: new string('z', 4096))}}]}""");
        string all = string.Join("\n", problems);
        Assert.Contains("'a' appears twice", all);
        Assert.Contains("'facePixels' has 3 pixels", all);
        Assert.Contains("'skin' must be a palette index", all);
        Assert.Contains("'torsoPixels' holds a symbol outside the palette", all);
        Assert.NotEmpty(PixelSkinPool.Validate("not json"));
        Assert.NotEmpty(PixelSkinPool.Validate("""{"skins": []}"""));

        // Parse keeps the good entries only.
        var pool = PixelSkinPool.Parse($$"""{"skins": [{{Entry("a", face)}}, {{Entry("b", "123")}}]}""");
        Assert.Equal(new[] { "a" }, pool.Skins.Select(s => s.Key));
        Assert.Empty(PixelSkinPool.Parse("garbage").Skins);
        Assert.Null(PixelSkinPool.Parse("garbage").SkinFor(5)); // an empty pool: the plain NPC look
    }

    [Fact]
    public void ASeed_AlwaysWearsTheSameSkin_AndTheSkinsVary()
    {
        var pool = PixelSkinPool.Parse(PoolText());
        var again = PixelSkinPool.Parse(PoolText());
        var looks = new HashSet<string>(StringComparer.Ordinal);
        var picks = new HashSet<string>(StringComparer.Ordinal);
        for (int seed = 1; seed < 400; seed += 7)
        {
            var a = pool.SkinFor(seed)!;
            var b = again.SkinFor(seed)!;
            Assert.Same(a, pool.SkinFor(seed)); // cached per seed
            Assert.Equal((a.Key, a.Skin, a.Hair, a.Torso, a.Arms, a.Legs), (b.Key, b.Skin, b.Hair, b.Torso, b.Arms, b.Legs));
            Assert.Equal(a.FacePixels, b.FacePixels);
            Assert.Equal(a.TorsoPixels, b.TorsoPixels);
            Assert.Equal(a.ArmPixels, b.ArmPixels);
            Assert.Equal(a.LegPixels, b.LegPixels);
            looks.Add(a.FacePixels + a.TorsoPixels);
            picks.Add(a.Key);
        }

        Assert.True(picks.Count >= 5, $"only {picks.Count} of the pool's skins were picked");
        Assert.True(looks.Count >= 40, $"only {looks.Count} different looks among 57 pioneers");
    }

    [Fact]
    public void TheRecolour_KeepsEveryPaintingWhole_AndTransparentPixelsTransparent()
    {
        var pool = PixelSkinPool.Parse(PoolText());
        foreach (var source in pool.Skins)
        {
            for (int seed = -50; seed <= 50; seed += 5)
            {
                var r = PixelSkins.Recolour(source, seed);
                Assert.Equal(source.Key, r.Key);
                foreach (var (from, to) in new[] { (source.FacePixels, r.FacePixels), (source.TorsoPixels, r.TorsoPixels), (source.ArmPixels, r.ArmPixels), (source.LegPixels, r.LegPixels) })
                {
                    Assert.Equal(from.Length, to.Length);
                    Assert.True(PaintPayload.IsValidSymbols(to));
                    for (int i = 0; i < from.Length; i++)
                    {
                        Assert.Equal(from[i] == '0', to[i] == '0'); // what was see-through stays see-through, and nothing else becomes it
                    }
                }

                Assert.Contains(r.Skin, new[] { 7, 6, 21, 19 }); // a skin tone stays a skin tone (the ladder light → tan → deep → its shade)
                Assert.InRange(r.Torso, 1, PaintPayload.PaletteSize - 1);
            }
        }

        // The garments really change colour with the seed (the four hue families are permuted).
        var trail = pool.Skins.First(s => s.Key == "trailblazer");
        var torsos = Enumerable.Range(0, 40).Select(seed => PixelSkins.Recolour(trail, seed).Torso).Distinct().ToList();
        Assert.True(torsos.Count >= 3, "the jacket wears several colours across pioneers");
    }

    [Fact]
    public void TheMix_IsAStableHash()
    {
        Assert.Equal(PixelSkins.Mix(12345u, 1u), PixelSkins.Mix(12345u, 1u));
        Assert.NotEqual(PixelSkins.Mix(12345u, 1u), PixelSkins.Mix(12345u, 2u));
        Assert.NotEqual(PixelSkins.Mix(12345u, 1u), PixelSkins.Mix(12346u, 1u));
        var buckets = new int[3];
        for (uint s = 0; s < 3000; s++)
        {
            buckets[PixelSkins.Mix(s, 3u) % 3]++;
        }

        Assert.All(buckets, n => Assert.InRange(n, 850, 1150)); // spread evenly
    }
}
