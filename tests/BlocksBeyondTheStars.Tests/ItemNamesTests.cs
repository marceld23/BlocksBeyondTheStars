// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The shared item display-name helper (#927). A modified stack carries its modifier inside the item
/// key, and three UI surfaces used to look up <c>item.{compositeKey}.name</c> raw — rendering
/// <c>[item.snow#t8fd030.name]</c>. These tests run against the REAL en/de locale tables, so a missing
/// suffix key (dyed/glowing/painted/shape labels) fails here instead of rendering as a bracketed key
/// in game.
/// </summary>
public sealed class ItemNamesTests
{
    private static Localizer Loc(string language = "en")
        => new(GameLocale.English, TestLocales.Load(language), TestLocales.Load("en"));

    [Theory]
    [InlineData("snow")]                        // plain
    [InlineData("snow#t8fd030")]                // dyed — the reported key
    [InlineData("stone#g00ffff")]               // glowing
    [InlineData("stone#s04")]                   // shaped (sphere)
    [InlineData("mud#p0001")]                   // painted
    [InlineData("mud#tff0000g00ff00s05p0007")]  // everything at once
    public void Display_NeverRendersABracketedKey(string itemKey)
    {
        foreach (string language in new[] { "en", "de" })
        {
            string name = ItemNames.Display(Loc(language), itemKey);
            Assert.False(name.Contains('['), $"{language}: '{name}' leaks a raw locale key for '{itemKey}'");
            Assert.False(name.Contains('#'), $"{language}: '{name}' leaks the raw modifier payload for '{itemKey}'");
            Assert.NotEqual(string.Empty, name);
        }
    }

    [Fact]
    public void Display_NamesTheModifiers_AsSuffixes()
    {
        var en = Loc();
        string plain = ItemNames.Display(en, "snow");

        // The dyed variant is the plain name plus a suffix — never a different base resolution.
        string dyed = ItemNames.Display(en, "snow#t8fd030");
        Assert.StartsWith(plain, dyed);
        Assert.Contains(en.Get("ui.color.dyed"), dyed);

        // Glow wins over dye in the suffix (a glowing block reads as glowing, like the crafting menu).
        string glowing = ItemNames.Display(en, "snow#t8fd030g00ffff");
        Assert.Contains(en.Get("ui.color.glowing"), glowing);
        Assert.DoesNotContain(en.Get("ui.color.dyed"), glowing);

        string shaped = ItemNames.Display(en, "stone#s04");
        Assert.Contains(en.Get("ui.shape.sphere"), shaped);

        string painted = ItemNames.Display(en, "mud#p0001");
        Assert.Contains(en.Get("ui.color.painted"), painted);
    }

    [Fact]
    public void Display_ResolvesCustomFormNames_AndFallsBackGenerically()
    {
        var en = Loc();
        string key = ItemKey.Compose("stone", 0, 0, ShapeCode.FirstCustom);

        // With a registry lookup, the player's own name for the form shows.
        string named = ItemNames.Display(en, key, idx => idx == ShapeCode.FirstCustom ? "Bogen" : null);
        Assert.Contains("Bogen", named);

        // Without one (or when the save no longer knows the id), the generic "own forms" label steps in —
        // never a raw key, never an empty suffix separator.
        string generic = ItemNames.Display(en, key);
        Assert.Contains(en.Get("ui.shape.custom.section"), generic);
        Assert.False(generic.Contains('['));
    }

    [Fact]
    public void ShapeLabel_CoversEveryBuiltInForm_InBothMandatoryLanguages()
    {
        foreach (string language in new[] { "en", "de" })
        {
            var loc = Loc(language);
            for (int shape = 1; shape < ShapeCode.Count; shape++)
            {
                string label = ItemNames.ShapeLabel(loc, shape);
                Assert.False(string.IsNullOrEmpty(label), $"{language}: shape {shape} has no label");
                Assert.False(label.Contains('['), $"{language}: shape {shape} label '{label}' is a raw key");
            }
        }
    }
}
