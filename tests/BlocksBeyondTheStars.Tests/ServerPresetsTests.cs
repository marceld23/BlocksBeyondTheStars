// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using BlocksBeyondTheStars.Shared.Configuration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

public sealed class ServerPresetsTests
{
    [Fact]
    public void Names_EveryPresetResolves()
    {
        foreach (var name in ServerPresets.Names)
        {
            Assert.NotNull(ServerPresets.Get(name));
        }
    }

    [Theory]
    [InlineData("family")]
    [InlineData("FAMILY")]
    [InlineData("  family  ")]
    [InlineData("  FaMiLy  ")]
    public void Get_IsCaseInsensitiveAndTrimsWhitespace(string name)
    {
        Assert.NotNull(ServerPresets.Get(name));
    }

    [Fact]
    public void Get_ReturnsNullForUnknownName()
    {
        Assert.Null(ServerPresets.Get("does-not-exist"));
    }

    [Fact]
    public void ReturnToShip_IsOffOnlyOnTheDangerousPreset()
    {
        // #2286: the free way out of a cave is on everywhere except the one preset that wants the walk back.
        foreach (var name in ServerPresets.Names)
        {
            var rules = ServerPresets.Get(name)!;
            Assert.Equal(name != "dangerous", rules.ReturnToShip);
        }
    }

    [Fact]
    public void Get_ReturnsNullForNullName()
    {
        Assert.Null(ServerPresets.Get(null!));
    }
}
