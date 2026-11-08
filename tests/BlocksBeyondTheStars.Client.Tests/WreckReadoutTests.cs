// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#3410 (Justus: "Was ist das?"): the wreck's scan card says what it is — kind in the title, the info line above
/// the contents.</summary>
public sealed class WreckReadoutTests
{
    [Fact]
    public void Title_NamesTheKindBeforeTheCoinedName()
    {
        Assert.Equal("Wrack · Lavoo", WreckReadout.Title("Wrack", "Lavoo"));
        Assert.Equal("Wrack", WreckReadout.Title("Wrack", string.Empty));
        Assert.Equal("Wrack", WreckReadout.Title("Wrack", null));
    }

    [Fact]
    public void Info_ShowsWhatItIs_AboveTheContents()
    {
        const string info = "Wrackhülle — mit dem Bergbaulaser zerlegen, um Schrott zu bergen.";
        const string contents = "Inhalt: Eisenplatte, Kabel";

        Assert.Equal(info + "\n" + contents, WreckReadout.Info(info, contents));
        Assert.Equal(contents, WreckReadout.Info(null, contents));
        Assert.Equal(info, WreckReadout.Info(info, string.Empty));
    }
}
