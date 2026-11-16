// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Bio;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The bio lab's symbol language (#3717): every property has a symbol, every symbol a file, and the pairings the
/// symbols make visible are the ones the rules really have.
/// </summary>
public sealed class BioSymbolsTests
{
    private static string IconsDir() => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "icons");

    [Fact]
    public void EveryNamedIcon_IsAFileWithItsMeta()
    {
        var missing = BioSymbols.AllIcons()
            .Where(name => !File.Exists(Path.Combine(IconsDir(), name + ".png"))
                           || !File.Exists(Path.Combine(IconsDir(), name + ".png.meta")))
            .ToList();

        Assert.True(missing.Count == 0, "Bio symbols without a PNG + .meta in Resources/icons: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryEffect_HasItsOwnIcon()
    {
        var icons = BioRules.Effects.Select(BioSymbols.EffectIcon).ToList();

        Assert.DoesNotContain(null, icons);
        Assert.Equal(icons.Count, icons.Distinct().Count()); // the icon alone tells two effects apart
        Assert.Null(BioSymbols.EffectIcon(BioEffect.None));
    }

    [Fact]
    public void EveryTraitStatGroupAndReaction_HasAnIcon()
    {
        foreach (MatTrait trait in Enum.GetValues(typeof(MatTrait)))
        {
            Assert.Equal(trait == MatTrait.None, BioSymbols.TraitIcon(trait) is null);
        }

        foreach (ModStat stat in Enum.GetValues(typeof(ModStat)))
        {
            Assert.Equal(stat == ModStat.None, BioSymbols.StatIcon(stat) is null);
        }

        var traits = Enum.GetValues(typeof(MatTrait)).Cast<MatTrait>().Where(t => t != MatTrait.None).Select(BioSymbols.TraitIcon).ToList();
        Assert.Equal(traits.Count, traits.Distinct().Count());

        var groups = Enumerable.Range(0, BioRules.Groups).Select(BioSymbols.GroupIcon).ToList();
        Assert.Equal(BioRules.Groups, groups.Distinct().Count());
        Assert.Equal(BioSymbols.GroupIcon(1), BioSymbols.GroupIcon(1 + BioRules.Groups));

        var reactions = Enum.GetValues(typeof(BioReaction)).Cast<BioReaction>().Select(BioSymbols.ReactionIcon).ToList();
        Assert.Equal(reactions.Count, reactions.Distinct().Count());
    }

    /// <summary>A side effect wears the icon of the effect it undoes — so the pairing must be a real opposite: the two
    /// push the same number in opposite directions.</summary>
    [Theory]
    [InlineData(BioSideEffect.Hungry, BioEffect.Satiety)]
    [InlineData(BioSideEffect.Breathless, BioEffect.Breath)]
    [InlineData(BioSideEffect.Sluggish, BioEffect.Speed)]
    [InlineData(BioSideEffect.Leaden, BioEffect.Jump)]
    [InlineData(BioSideEffect.Tired, BioEffect.Regeneration)]
    [InlineData(BioSideEffect.SlowReflex, BioEffect.Reflex)]
    public void ASideEffect_WearsTheIconOfTheEffectItUndoes(BioSideEffect side, BioEffect effect)
    {
        Assert.Equal(effect, BioSymbols.Undoes(side));
        Assert.Equal(BioSymbols.EffectIcon(effect), BioSymbols.SideIcon(side));
        Assert.True(BioRules.SideMagnitude(side, 1) > 0f);
        Assert.True(BioRules.Magnitude(effect, 1) > 0f);
    }

    [Fact]
    public void EverySideEffect_UndoesADifferentEffect_AndNeverTheOneItComesWith()
    {
        var sides = Enum.GetValues(typeof(BioSideEffect)).Cast<BioSideEffect>().Where(s => s != BioSideEffect.None).ToList();

        Assert.Equal(sides.Count, sides.Select(BioSymbols.Undoes).Distinct().Count());
        Assert.Equal(BioEffect.None, BioSymbols.Undoes(BioSideEffect.None));

        // An effect never comes with its own opposite — the card would show the same icon twice, up and down.
        foreach (var effect in BioRules.Effects)
        {
            var (first, second) = BioRules.Opponents(effect);
            Assert.NotEqual(effect, BioSymbols.Undoes(first));
            Assert.NotEqual(effect, BioSymbols.Undoes(second));
        }
    }

    [Fact]
    public void AnEffectTakesItsFamilysColour_AndTheFiveFamiliesDiffer()
    {
        var colours = BioRules.Families.Select(BioSymbols.FamilyRgb).ToList();
        Assert.Equal(BioRules.Families.Length, colours.Distinct().Count());

        foreach (var effect in BioRules.Effects)
        {
            Assert.Equal(BioSymbols.FamilyRgb(BioRules.Family(effect)), BioSymbols.EffectRgb(effect));
        }

        // Clear of the warning amber (1.00, 0.72, 0.28) a side effect is drawn in: no family colour sits beside it.
        foreach (int rgb in colours)
        {
            int dr = ((rgb >> 16) & 0xFF) - 255, dg = ((rgb >> 8) & 0xFF) - 184, db = (rgb & 0xFF) - 71;
            Assert.True(dr * dr + dg * dg + db * db > 40 * 40, $"family colour {rgb:X6} is too close to the warning amber");
        }

        Assert.Equal(BioRules.MaxRarity + 1, Enumerable.Range(0, BioRules.MaxRarity + 1).Select(BioSymbols.RarityRgb).Distinct().Count());
    }

    [Fact]
    public void TheStrengthBar_HasOneSegmentPerRarityBand()
    {
        Assert.Equal(BioRules.MaxLevel, BioSymbols.BarSegments * BioSymbols.StepsPerSegment);

        for (int level = 0; level <= BioRules.MaxLevel; level++)
        {
            int filled = Enumerable.Range(0, BioSymbols.BarSegments).Sum(s => BioSymbols.SegmentFill(level, s));
            Assert.Equal(level, filled);

            if (level > 0)
            {
                // The segment the level reaches into is the rarity tier the level reads as.
                int reached = Enumerable.Range(0, BioSymbols.BarSegments).Last(s => BioSymbols.SegmentFill(level, s) > 0);
                Assert.Equal(BioRules.RarityOfLevel(level), reached);
            }
        }

        Assert.Equal(BioSymbols.StepsPerSegment, BioSymbols.SegmentFill(99, BioSymbols.BarSegments - 1));
    }

    [Fact]
    public void Reasons_NameTheTagsThatPullTheEffect_StrongestFirst()
    {
        var tags = BioTag.Lava | BioTag.Hot | BioTag.Dry | BioTag.ManyLegs | BioTag.Water;

        Assert.Equal(new[] { BioTag.Lava, BioTag.Hot, BioTag.Dry }, BioSymbols.Reasons(tags, BioEffect.HeatWard));
        Assert.Equal(new[] { BioTag.ManyLegs }, BioSymbols.Reasons(tags, BioEffect.Grip));
        Assert.Empty(BioSymbols.Reasons(tags, BioEffect.Stealth)); // the seed's surprise: nothing here asks for it
        Assert.Empty(BioSymbols.Reasons(BioTag.None, BioEffect.HeatWard));

        var many = BioTag.ManyLegs | BioTag.Arachnid | BioTag.Tentacles | BioTag.Hanging | BioTag.Rocky;
        var grip = BioSymbols.Reasons(many, BioEffect.Grip);
        Assert.Equal(BioSymbols.MaxReasons, grip.Count);
        Assert.Equal(BioTag.Arachnid, grip[0]);
    }

    [Fact]
    public void EveryTagTheAffinityTableKnows_HasAPhraseKey()
    {
        var named = BioSymbols.NamedTags();

        Assert.Equal(named.Count, named.Distinct().Count());
        Assert.All(BioRules.Affinities, a => Assert.Contains(a.Tag, named));
        Assert.Equal("bio.tag.manylegs", BioSymbols.TagKey(BioTag.ManyLegs));
    }

    [Fact]
    public void TheThermalHint_NamesTheStabiliserThatSteadiesIt()
    {
        Assert.Equal(MatTrait.HeatProof, BioSymbols.ThermalCounter(BioThermal.HeatSensitive));
        Assert.Equal(MatTrait.ColdProof, BioSymbols.ThermalCounter(BioThermal.ColdSensitive));
        Assert.Equal(MatTrait.None, BioSymbols.ThermalCounter(BioThermal.Stable));
        Assert.Null(BioSymbols.ThermalIcon(BioThermal.Stable));

        // The rule the hint shows: a heat-proof stabiliser makes a heat-sensitive substance stable.
        var active = new BioProfile { Effect = BioEffect.Speed, Level = 5, Thermal = BioThermal.HeatSensitive, DurationSeconds = 120 };
        var levels = new int[MaterialProfile.TraitCount];
        levels[(int)BioSymbols.ThermalCounter(active.Thermal)] = 1;
        var mixed = Synthesis.Compute(new SynthesisInput { Active = active, Stabiliser = MaterialProfiles.Synthetic(levels) });

        Assert.Equal(BioThermal.Stable, mixed.Thermal);
    }

    // ---------------- the reaction chart (#3721) ----------------

    private static Func<uint, BioProfile?> Book(params (uint Seed, int Group)[] species)
    {
        var map = species.ToDictionary(s => s.Seed, s => new BioProfile { Seed = s.Seed, Group = s.Group });
        return seed => map.TryGetValue(seed, out var profile) ? profile : null;
    }

    [Fact]
    public void TheChart_KnowsAPair_OnceTwoSamplesOfThoseGroupsWereMixed()
    {
        var book = Book((0x10, 0), (0x20, 1), (0x30, 4));
        var mixes = new[] { Synthesis.Signature(0x10, BioForm.Injector, 0, null, 0x20) };

        var chart = ReactionChart.From(mixes, book);

        Assert.True(chart.Knows(0, 1));
        Assert.True(chart.Knows(1, 0)); // the table is symmetric, so is what is known of it
        Assert.False(chart.Knows(0, 4));
        Assert.False(chart.Knows(0, 0));
        Assert.Equal(1, chart.Known);
        Assert.Equal(36, ReactionChart.Pairs);
    }

    [Fact]
    public void TheChart_CountsAPairOnce_HoweverOftenAndInWhateverFormItWasMixed()
    {
        var book = Book((0x10, 2), (0x20, 5), (0x30, 5), (0x40, 2));
        var mixes = new[]
        {
            Synthesis.Signature(0x10, BioForm.Injector, 0, null, 0x20),
            Synthesis.Signature(0x20, BioForm.Gel, 0, "iron_ingot", 0x10),           // the other way round, another form
            Synthesis.Signature(0x40, BioForm.Bar, 0x99, "copper_ore", 0x30),        // two other species of the same groups
            Synthesis.Signature(0x10, BioForm.Injector, 0, null, 0x20, washed: true), // the washed mix of the first
            Synthesis.Signature(0x10, BioForm.Injector, 0, null, 0x40),              // a group with itself
        };

        var chart = ReactionChart.From(mixes, book);

        Assert.Equal(2, chart.Known);
        Assert.True(chart.Knows(2, 5));
        Assert.True(chart.Knows(2, 2));
    }

    [Fact]
    public void TheChart_SkipsWhatTellsNothing()
    {
        var book = Book((0x10, 0), (0x20, 1));
        var mixes = new[]
        {
            Synthesis.Signature(0x10, BioForm.Injector, 0, null, 0),    // a plain extract: no second sample
            Synthesis.Signature(0x10, BioForm.Injector, 0, null, 0x77), // a species the book cannot read
            Synthesis.Signature(0x77, BioForm.Injector, 0, null, 0x20),
            "not a signature",
            string.Empty,
        };

        Assert.Equal(0, ReactionChart.From(mixes, book).Known);
    }

    [Theory]
    [InlineData(0x1a2b3c4dU, BioForm.Capsule, 0x00000042U, "tungsten_ore", 0x0badf00dU, true)]
    [InlineData(0x00000001U, BioForm.Injector, 0U, null, 0U, false)]
    [InlineData(0xffffffffU, BioForm.Coating, 0U, "salt", 0x00000002U, false)]
    public void ASignature_ReadsBackWhatWentIn(uint active, BioForm form, uint stabiliserSeed, string? stabiliserItem, uint modifier, bool washed)
    {
        string signature = Synthesis.Signature(active, form, stabiliserSeed, stabiliserItem, modifier, washed);

        Assert.True(Synthesis.TryParseSignature(signature, out uint a, out var f, out string item, out uint s, out uint m, out bool w));
        Assert.Equal(active, a);
        Assert.Equal(form, f);
        Assert.Equal(stabiliserItem ?? string.Empty, item);
        Assert.Equal(stabiliserSeed, s);
        Assert.Equal(modifier, m);
        Assert.Equal(washed, w);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000000a/0/:00000000")]            // a part missing
    [InlineData("0000000a/9/:00000000/00000000")]   // a form this version does not know
    [InlineData("zzzz/0/:00000000/00000000")]       // no hex seed
    [InlineData("0000000a/0/00000000/00000000")]    // the stabiliser without its colon
    [InlineData("0000000a/0/:00000000/00000000/x")] // a fifth part that is not the wash mark
    public void AStringThatIsNoSignature_IsRefused(string? text)
    {
        Assert.False(Synthesis.TryParseSignature(text, out _, out _, out _, out _, out _, out _));
    }

    /// <summary>The cards name tags, carriers and the rules in words — every one of them needs its text in the two
    /// mandatory languages (the community languages fall back to English).</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryTextTheSymbolsStandBeside_IsInTheLocale(string code)
    {
        string path = Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var keys = new List<string>
        {
            "ui.bio.tab.chart", "ui.bio.chart.hint", "ui.bio.chart.progress", "ui.bio.chart.untried", "ui.bio.made_of",
            "ui.bio.best_form", "ui.bio.stabiliser_against", "ui.bio.fits_life",
        };
        keys.AddRange(BioSymbols.NamedTags().Select(BioSymbols.TagKey));
        keys.AddRange(Enum.GetValues(typeof(BioCarrier)).Cast<BioCarrier>().Select(c => "bio.carrier." + c.ToString().ToLowerInvariant()));
        keys.AddRange(Enum.GetValues(typeof(MatTrait)).Cast<MatTrait>().Where(t => t != MatTrait.None).Select(t => "bio.trait." + t.ToString().ToLowerInvariant()));
        keys.AddRange(Enum.GetValues(typeof(BioReaction)).Cast<BioReaction>().Select(r => "bio.reaction." + r.ToString().ToLowerInvariant()));
        keys.AddRange(Enumerable.Range(0, BioRules.Groups).Select(g => "bio.group." + g));

        var missing = keys.Where(k => !doc.RootElement.TryGetProperty(k, out var v) || string.IsNullOrWhiteSpace(v.GetString())).ToList();

        Assert.True(missing.Count == 0, code + ".json lacks: " + string.Join(", ", missing));
        Assert.Contains("{0}", doc.RootElement.GetProperty("ui.bio.best_form").GetString());
        Assert.Contains("{0}", doc.RootElement.GetProperty("ui.bio.stabiliser_against").GetString());
        Assert.Contains("{0}", doc.RootElement.GetProperty("ui.bio.fits_life").GetString());
        Assert.Contains("{1}", doc.RootElement.GetProperty("ui.bio.chart.progress").GetString());
    }

    [Fact]
    public void EveryIconName_IsOneTheListCarries()
    {
        var all = new HashSet<string>(BioSymbols.AllIcons(), StringComparer.Ordinal);

        Assert.Contains(BioSymbols.HexPlate, all);
        Assert.Contains(BioSymbols.ToxicIcon, all);
        Assert.All(BioRules.Effects, e => Assert.Contains(BioSymbols.EffectIcon(e)!, all));
        Assert.Contains(BioSymbols.ThermalIcon(BioThermal.HeatSensitive)!, all);
        Assert.Contains(BioSymbols.ThermalIcon(BioThermal.ColdSensitive)!, all);
        Assert.Contains(BioSymbols.SideIcon(BioSideEffect.Tired)!, all);
    }
}
