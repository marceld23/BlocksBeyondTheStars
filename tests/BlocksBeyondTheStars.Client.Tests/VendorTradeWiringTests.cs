// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2626 / #2628 / #2629 — the Unity half of the trade-screen fixes, guarded as source text (the Unity scripts cannot load
/// headless — the <see cref="SeamWiringTests"/> pattern): the "Trade" prompt, E's trade-or-talk and the crafting menu's
/// market page ask ONE picker (<see cref="VendorReach"/>), "Trade" binds the screen to the chosen vendor, the screen lists
/// that vendor's stock by the server's own rule (<c>MarketStock.ListFor</c>) and gives way to "step closer" when it walks
/// off, and the doctor's treatment plays the heal sound. No private 3.6-block vendor reach is left anywhere.
/// </summary>
public sealed class VendorTradeWiringTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    /// <summary>The body of the member that starts at <paramref name="signature"/> (up to the next doc comment).</summary>
    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{signature}' not found");
        int end = src.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        return end < 0 ? src.Substring(start) : src.Substring(start, end - start);
    }

    [Fact]
    public void ThePrompt_E_AndTheMarketPage_AskTheOneVendorPicker()
    {
        string boot = Script("GameBootstrap.cs");
        Assert.Contains("VendorReach.NearestVendor(Npcs, PlayerPosition.x, PlayerPosition.y, PlayerPosition.z, Circumference)", boot);
        Assert.Contains("public bool NearVendor => NearestTradingVendor() != null;", boot);
        Assert.Contains("NearestTradingVendor()?.Theme", Body(boot, "public string NearestVendorTheme"));
        Assert.Contains("MarketStock.Offers(", Body(boot, "public bool MarketOfferedBy("));

        string player = Script("PlayerController.cs");
        string offer = Body(player, "private bool OfferVendorChoice()");
        Assert.Contains("Game.NearestTradingVendor()", offer);
        Assert.Contains("Menu?.OpenMarket(id);", offer); // "Trade" binds the screen to THIS vendor

        foreach (string file in new[] { "GameBootstrap.cs", "PlayerController.cs", "VendorTradeUI.cs" })
        {
            Assert.DoesNotContain("3.6f", Script(file)); // the old private vendor reach
        }
    }

    [Fact]
    public void TheTradeScreen_IsBoundToItsVendor_ListsItsStock_AndAsksToStepCloser()
    {
        Assert.Contains("public void OpenMarket(int vendorId = 0)", Script("GameMenu.cs"));
        Assert.Contains("_vendorUi.Open(vendorId);", Script("GameMenu.cs"));

        string ui = Script("VendorTradeUI.cs");
        Assert.Contains("public void Open(int vendorId = 0)", ui);
        Assert.Contains("VendorReach.ById(Game.Npcs, _vendorId)", ui);
        Assert.Contains("VendorReach.StillServes(vendor, p.x, p.y, p.z, Game.Circumference)", ui);
        Assert.Contains("MarketStock.ListFor(Game.MarketRecipes(), theme, day)", ui);
        Assert.Contains("VendorReach.ThemeHeaderKey(vendor.Theme)", ui);
        Assert.Contains("L(\"ui.vendor.step_closer\")", ui);
        Assert.DoesNotContain("NearestVendorName", ui); // the title used to name the nearest vendor at any distance
        Assert.DoesNotContain("Game.MarketOfferedHere(", ui); // the screen no longer re-picks the vendor on every rebuild
    }

    [Fact]
    public void TheDoctorsTreatment_PlaysTheHealSound()
    {
        string dialog = Script("NpcDialogUi.cs");
        Assert.Contains("m.Action == \"heal\"", dialog);
        Assert.Contains("ClientAudio.Instance?.Cue(\"heal\")", dialog);
    }
}
