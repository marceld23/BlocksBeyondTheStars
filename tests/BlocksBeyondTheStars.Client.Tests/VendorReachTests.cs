// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2626 — the client's ONE vendor reach rule (<see cref="VendorReach"/>), mirroring the server's barter check: "Trade"
/// (the prompt, E, opening the screen) needs a vendor within <see cref="MarketStock.TradeReach"/> — the server accepts
/// every barter within <see cref="MarketStock.VendorReach"/> of a vendor, so it never refuses what the prompt offers —
/// and an open screen stays bound to the chosen vendor while it stands within <see cref="MarketStock.VendorReach"/>,
/// exactly the server's reach. Screelit's doctor drifted up to 1.4 blocks from his post; the old 3.6-block pick lost him
/// 42 % of the time at 3.2 blocks while the server would have traded. All measured the short way round the seam.
/// </summary>
public sealed class VendorReachTests
{
    private const int C = 3232;

    private static NetNpc Npc(int id, float x, float z, string role = "vendor", string theme = "medics", string name = "")
        => new() { Id = id, X = x, Y = 65f, Z = z, Role = role, Theme = theme, Name = name };

    [Fact]
    public void TheClientNeverPromisesMoreThanTheServerKeeps()
    {
        Assert.Equal(4f, MarketStock.TradeReach);   // the stall/post reach of the server
        Assert.Equal(6f, MarketStock.VendorReach);  // the server's vendor reach — the bound screen keeps exactly this
        Assert.True(MarketStock.TradeReach < MarketStock.VendorReach);
    }

    [Fact]
    public void TradeNeedsAVendor_WithinFourBlocks_TheNearestWins_AndPeopleWhoDontTradeDontCount()
    {
        var npcs = new[] { Npc(1, 13.5f, 10f, theme: "traders"), Npc(2, 12.0f, 10f), Npc(3, 10.5f, 10f, role: "settler") };

        Assert.Equal(2, VendorReach.NearestVendor(npcs, 10f, 65f, 10f, C)?.Id);       // 2 m beats 3.5 m; the settler at 0.5 m is no trader
        Assert.Equal("medics", VendorReach.NearestVendorTheme(npcs, 10f, 65f, 10f, C));
        Assert.Equal(1, VendorReach.NearestVendor(npcs, 15.0f, 65f, 10f, C)?.Id);
        Assert.Equal(2, VendorReach.NearestVendor(new[] { Npc(2, 14f, 10f) }, 10f, 65f, 10f, C)?.Id); // the edge itself counts (server: ≤)
        Assert.Null(VendorReach.NearestVendor(new[] { Npc(2, 14.1f, 10f) }, 10f, 65f, 10f, C));
        Assert.Equal(string.Empty, VendorReach.NearestVendorTheme(new[] { Npc(2, 30f, 10f) }, 10f, 65f, 10f, C));
        Assert.Null(VendorReach.NearestVendor(null, 0f, 0f, 0f, C));
    }

    [Fact]
    public void TheChosenVendor_StaysBound_WhileItStepsAboutWithinTheServersReach()
    {
        var doctor = Npc(7, 12f, 10f, name: "Mira");

        Assert.True(VendorReach.StillServes(doctor, 10f, 65f, 10f, C));
        doctor.X = 15.9f; // strolled off: 5.9 m — farther than the "Trade" prompt reaches, still the server's vendor reach
        Assert.Null(VendorReach.NearestVendor(new[] { doctor }, 10f, 65f, 10f, C));
        Assert.True(VendorReach.StillServes(doctor, 10f, 65f, 10f, C));
        doctor.X = 16.2f; // 6.2 m — out: the screen says "step closer"
        Assert.False(VendorReach.StillServes(doctor, 10f, 65f, 10f, C));

        Assert.False(VendorReach.StillServes(null, 10f, 65f, 10f, C));                  // gone from the NPC list
        Assert.False(VendorReach.StillServes(Npc(8, 11f, 10f, role: "settler"), 10f, 65f, 10f, C)); // no longer a trader
        Assert.Same(doctor, VendorReach.ById(new[] { Npc(1, 0f, 0f), doctor }, 7));
        Assert.Null(VendorReach.ById(new[] { doctor }, 0));
        Assert.Null(VendorReach.ById(null, 7));
    }

    [Fact]
    public void AVendorAcrossTheSeam_IsInReach_FromBothSides()
    {
        var npcs = new[] { Npc(5, C - 1.5f, 10f) };

        Assert.Equal(5, VendorReach.NearestVendor(npcs, -4.0f, 65f, 10f, C)?.Id); // scene X −4 (walked back over X = 0): 2.5 m east
        Assert.Equal(5, VendorReach.NearestVendor(npcs, 1.0f, 65f, 10f, C)?.Id);  // the canonical side: 2.5 m west across X = 0
        Assert.True(VendorReach.StillServes(npcs[0], 3.5f, 65f, 10f, C));          // 5 m across X = 0
        Assert.False(VendorReach.StillServes(npcs[0], 5.0f, 65f, 10f, C));         // 6.5 m — out
    }

    [Fact]
    public void TheScreenTitle_AndTheHeaderKey_NameTheVendorAndItsTrade()
    {
        Assert.Equal("Mira · Doctor", VendorReach.Label("Mira", "Doctor"));
        Assert.Equal("Mira", VendorReach.Label("Mira", ""));
        Assert.Equal("Doctor", VendorReach.Label("", "Doctor"));
        Assert.Equal(string.Empty, VendorReach.Label(null, null));

        Assert.Equal("ui.vendor.theme.medics", VendorReach.ThemeHeaderKey("medics"));
        Assert.Equal("ui.vendor.theme.mixed", VendorReach.ThemeHeaderKey(MarketStock.MixedTheme));
        Assert.Equal(string.Empty, VendorReach.ThemeHeaderKey(""));   // the ship's console: the generic barter line
    }
}
