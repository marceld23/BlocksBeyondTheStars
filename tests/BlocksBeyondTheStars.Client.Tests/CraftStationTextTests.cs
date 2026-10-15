// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2585: what the crafting menu says about a station that is out of reach. Aboard, a station is the ship's MODULE (the
/// server never looks at a block there; a forge built into a ship is decoration), so the texts name the module first —
/// a player inside his ship read "needs a forge nearby", built one beside the workbench and still got no titanium plates.
/// Off the ship the block comes first, as before. Checked against the real English and German tables.
/// </summary>
public sealed class CraftStationTextTests
{
    private static Func<string, string> Localizer(string code)
    {
        var en = VegaLineBudgetTests.Core("en");
        var table = VegaLineBudgetTests.Core(code);
        return key => table.TryGetValue(key, out var v) ? v : en.TryGetValue(key, out var e) ? e : "[" + key + "]";
    }

    [Fact]
    public void OffTheShip_TheBlockComesFirst_WithTheModuleAsTheAboardAlternative()
    {
        Assert.Equal("Needs a Forge nearby — or your ship's Refinery module while you're aboard.",
            CraftStationText.NeedStation("refinery", "Forge", aboard: false, Localizer("en")));
        Assert.Equal("Braucht in der Nähe: Schmiede — oder an Bord das Raffinerie-Modul deines Schiffs.",
            CraftStationText.NeedStation("refinery", "Schmiede", aboard: false, Localizer("de")));
        Assert.Equal("Needs a Campfire nearby.", CraftStationText.NeedStation("campfire", "Campfire", aboard: false, Localizer("en")));

        Assert.Equal("This recipe needs a Forge in reach.", CraftStationText.Hint("refinery", "Forge", aboard: false, Localizer("en")));
        Assert.Equal("Forge", CraftStationText.StationLineName("refinery", "Forge", aboard: false, Localizer("en")));
        Assert.True(CraftStationText.OffersStationBlock("refinery", aboard: false));
    }

    [Theory]
    [InlineData("en", "Forge", "Refinery")]
    [InlineData("de", "Schmiede", "Raffinerie")]
    public void Aboard_TheModuleComesFirst_AndTheBlockIsDecoration(string code, string block, string module)
    {
        var l = Localizer(code);
        string reason = CraftStationText.NeedStation("refinery", block, aboard: true, l);

        Assert.StartsWith(code == "en" ? "Aboard, this needs your ship's Refinery module" : "An Bord brauchst du dafür das Raffinerie-Modul", reason, StringComparison.Ordinal);
        Assert.Contains(l("ui.tab.ship"), reason, StringComparison.Ordinal); // where it is built: the Ship tab, by its own name
        Assert.Contains(block, reason, StringComparison.Ordinal);            // the block in the ship is only decoration
        Assert.DoesNotContain("{", reason, StringComparison.Ordinal);

        string hint = CraftStationText.Hint("refinery", block, aboard: true, l);
        Assert.Contains(module, hint, StringComparison.Ordinal);
        Assert.DoesNotContain(block, hint, StringComparison.Ordinal);

        Assert.Equal(code == "en" ? "Refinery module" : "Raffinerie-Modul", CraftStationText.StationLineName("refinery", block, aboard: true, l));
        Assert.False(CraftStationText.OffersStationBlock("refinery", aboard: true)); // no "craft a forge" offer in the ship
    }

    [Fact]
    public void Aboard_AStationNoModuleProvides_WorksOnAWorldOnly()
    {
        var l = Localizer("en");
        Assert.Equal("This works only on a world, next to this block: Campfire. In a ship it is only decoration.",
            CraftStationText.NeedStation("campfire", "Campfire", aboard: true, l));
        Assert.Equal("Only on a world — next to this block: Algae Tank.", CraftStationText.Hint("algaetank", "Algae Tank", aboard: true, l));
        Assert.Equal("Campfire", CraftStationText.StationLineName("campfire", "Campfire", aboard: true, l));

        // No "Craft one →" for a campfire or an algae tank aboard while the hint says "only on a world" — off the ship, yes.
        Assert.False(CraftStationText.OffersStationBlock("campfire", aboard: true));
        Assert.False(CraftStationText.OffersStationBlock("algaetank", aboard: true));
        Assert.True(CraftStationText.OffersStationBlock("campfire", aboard: false));
    }

    /// <summary>The factory is no "only on a world, next to this block" station: a player-placed terminal never produces
    /// (#1265). Aboard its text stays the off-ship one, like the server's <c>@need_station:factory</c>.</summary>
    [Fact]
    public void Aboard_TheFactory_KeepsItsOwnWording()
    {
        var l = Localizer("en");
        Assert.False(CraftStationText.IsWorldOnly("factory"));
        Assert.Equal(CraftStationText.NeedStation("factory", "Factory terminal", aboard: false, l),
            CraftStationText.NeedStation("factory", "Factory terminal", aboard: true, l));
        Assert.Equal(CraftStationText.Hint("factory", "Factory terminal", aboard: false, l),
            CraftStationText.Hint("factory", "Factory terminal", aboard: true, l));
    }

    /// <summary>No station at all in reach: off the ship "stand at a Workbench"; aboard (a ship without a workshop module,
    /// such as a commissioned self-built one) the workshop MODULE — a workbench in a ship is decoration.</summary>
    [Theory]
    [InlineData("en", "Workbench", "Workshop module")]
    [InlineData("de", "Werkbank", "Werkstatt-Modul")]
    public void HandOnly_AboardNamesTheWorkshopModule_NotAWorkbench(string code, string workbench, string module)
    {
        var l = Localizer(code);
        string onTheGround = CraftStationText.HandOnlyHint(workbench, aboard: false, l);
        string aboard = CraftStationText.HandOnlyHint(workbench, aboard: true, l);

        Assert.Equal(l("ui.craft.hint_hand_only").Replace("{block}", workbench), onTheGround);
        Assert.Contains(module, aboard, StringComparison.Ordinal);
        Assert.DoesNotContain(workbench, aboard, StringComparison.Ordinal);
        Assert.DoesNotContain("{", aboard, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("workshop", "workshop")]
    [InlineData("refinery", "refinery")]
    [InlineData("detoxifier", "detoxifier")]
    [InlineData("transmuter", "transmuter")]
    [InlineData("decontaminator", "decontaminator")]
    [InlineData("campfire", null)]
    [InlineData("algaetank", null)]
    [InlineData("factory", null)]
    [InlineData(null, null)]
    public void ModuleKey_MirrorsTheServersStationModules(string? station, string? module)
    {
        Assert.Equal(module, CraftStationText.ModuleKey(station));
        if (module != null)
        {
            Assert.True(VegaLineBudgetTests.Core("en").ContainsKey("module." + module + ".name"));
        }
    }

    /// <summary>German has no article that fits every station ("einen Schmiede" was the report's sentence): the four texts
    /// that name a station put no fixed article in front of it.</summary>
    [Theory]
    [InlineData("ui.craft.need_station")]
    [InlineData("ui.craft.need_block")]
    [InlineData("ui.craft.need_block_or_module")]
    [InlineData("ui.craft.hint_need_block")]
    public void German_PutsNoFixedArticleBeforeTheStationName(string key)
    {
        string text = VegaLineBudgetTests.Core("de")[key];
        Assert.DoesNotMatch(new Regex(@"\b(?:ein|eine|einen|einem|einer|der|die|das|den|dem)\s+\{(?:station|block)\}",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)), text);
    }
}
