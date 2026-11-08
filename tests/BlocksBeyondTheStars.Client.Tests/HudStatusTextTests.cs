// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3411: the HUD status lines are localized — "Boarded Blumenhall." stood in English in a German game.</summary>
public sealed class HudStatusTextTests
{
    private static readonly Dictionary<string, string> De = new()
    {
        [HudStatusText.BoardedKey] = "An Bord von {0}.",
        [HudStatusText.RulesLineKey] = "Modus: {0} · PvP: {1}",
        ["ui.worldopt.pmode.1"] = "Überleben",
        ["ui.worldopt.pmode.2"] = "Kreativ",
        ["ui.worldopt.pvp.0"] = "Aus",
        ["ui.worldopt.pvp.1"] = "Nur Duelle",
        ["ui.worldopt.pvp.3"] = "An",
    };

    private static string Get(string key) => De.TryGetValue(key, out var v) ? v : key;

    [Fact]
    public void Boarded_NamesTheStationInTheClientsLanguage()
        => Assert.Equal("An Bord von Blumenhall.", HudStatusText.Format(Get(HudStatusText.BoardedKey), "Blumenhall"));

    [Theory]
    [InlineData("Survival", "Off", "Modus: Überleben · PvP: Aus")]
    [InlineData("Creative", "DuelsOnly", "Modus: Kreativ · PvP: Nur Duelle")]
    [InlineData("Survival", "On", "Modus: Überleben · PvP: An")]
    public void RulesLine_TranslatesTheServersRuleWords(string mode, string pvp, string expected)
        => Assert.Equal(expected, HudStatusText.RulesLine(Get, mode, pvp));

    [Fact]
    public void RulesLine_ShowsAnUnknownWordAsSent()
        => Assert.Equal("Modus: Hardcore · PvP: Chaos", HudStatusText.RulesLine(Get, "Hardcore", "Chaos"));

    [Fact]
    public void PvpKeys_FollowTheServersOrder()
    {
        Assert.Equal("ui.worldopt.pvp.0", HudStatusText.PvpKey("Off"));
        Assert.Equal("ui.worldopt.pvp.2", HudStatusText.PvpKey("GroupBased"));
        Assert.Equal("ui.worldopt.pvp.3", HudStatusText.PvpKey("On"));
        Assert.Equal(string.Empty, HudStatusText.PvpKey(null));
    }
}
