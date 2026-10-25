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

    /// <summary>Every <c>GameBootstrap*.cs</c> part (#2912), by file name — checked against the exact expected set below,
    /// so a renamed or added part cannot slip past the "no private vendor reach" scan unnoticed.</summary>
    private static readonly string[] GameBootstrapParts =
    {
        "GameBootstrap.ChunkPipeline.cs", "GameBootstrap.ChunkStreaming.cs", "GameBootstrap.cs", "GameBootstrap.Entities.cs",
        "GameBootstrap.Environment.cs", "GameBootstrap.Knowledge.cs", "GameBootstrap.Menus.cs", "GameBootstrap.Messages.cs",
        "GameBootstrap.PlayerState.cs", "GameBootstrap.Pvp.cs", "GameBootstrap.Space.cs", "GameBootstrap.Stations.cs", "GameBootstrap.Story.cs",
        "GameBootstrap.Structures.cs",
    };

    /// <summary>The <c>GameBootstrap*.cs</c> file names actually present in the client's Scripts folder, sorted.</summary>
    private static string[] GameBootstrapPartsOnDisk()
        => Directory.EnumerateFiles(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts"), "GameBootstrap*.cs")
            .Select(f => Path.GetFileName(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

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
        string boot = Script("GameBootstrap.Entities.cs"); // the vendor pickers' GameBootstrap part (#2912)
        Assert.Contains("VendorReach.NearestVendor(Npcs, PlayerPosition.x, PlayerPosition.y, PlayerPosition.z, Circumference)", boot);
        Assert.Contains("public bool NearVendor => NearestTradingVendor() != null;", boot);
        Assert.Contains("NearestTradingVendor()?.Theme", Body(boot, "public string NearestVendorTheme"));
        Assert.Contains("MarketStock.Offers(", Body(boot, "public bool MarketOfferedBy("));

        string player = ClientTestPaths.PlayerControllerSources();
        string offer = Body(player, "private bool OfferVendorChoice()");
        Assert.Contains("Game.NearestTradingVendor()", offer);
        Assert.Contains("Menu?.OpenMarket(id);", offer); // "Trade" binds the screen to THIS vendor

        Assert.Equal(GameBootstrapParts.OrderBy(f => f, StringComparer.Ordinal), GameBootstrapPartsOnDisk());
        foreach (string src in GameBootstrapParts.Select(Script).Concat(new[] { ClientTestPaths.PlayerControllerSources(), Script("VendorTradeUI.cs") }))
        {
            Assert.DoesNotContain("3.6f", src); // the old private vendor reach
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
