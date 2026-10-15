// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Screelit's trader reports, 2026-10-15 (#2628, #2629, #2630): the doctor treats you (once per in-game day), the trade
/// screen names what a vendor deals in, and visitors bring goods of their own — a docked or landed trader pilot a trade drawn
/// per visit, the travelling merchant a bit of everything (<see cref="MarketStock.MixedTheme"/>), the SPS access code only
/// now and then. The stock rule is <see cref="MarketStock"/>, shared by the server's barter check and the client's screen.
/// </summary>
public sealed class VendorTradeTests : IDisposable
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static IEnumerable<RecipeDefinition> Recipes => Content.Recipes.Values;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_vendortrade_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    private static HashSet<string> LocaleKeys(string code)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", code + ".json")),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    private static string En(string key) => Content.CreateLocalizer(GameLocale.English).Get(key);

    // ---- the stock (#2629, #2630) ----------------------------------------------------------------------------

    [Fact]
    public void EveryMarketTheme_HasItsHeaderLine_InEnglishAndGerman()
    {
        var themes = MarketStock.Themes(Recipes);
        Assert.Contains("medics", themes);
        Assert.Contains("traders", themes);
        var en = LocaleKeys("en");
        var de = LocaleKeys("de");
        foreach (string theme in themes.Append(MarketStock.MixedTheme))
        {
            string key = "ui.vendor.theme." + theme;
            Assert.True(en.Contains(key), $"en.json lacks '{key}'");
            Assert.True(de.Contains(key), $"de.json lacks '{key}'");
        }

        Assert.Contains("{0}", En("ui.vendor.step_closer"));
    }

    [Fact]
    public void TheVendorScreen_ListsTheVendorsOwnGoodsFirst_ThenTheDealsEveryVendorHas()
    {
        foreach (string theme in MarketStock.Themes(Recipes))
        {
            for (long day = 0; day < 4; day++)
            {
                var rows = MarketStock.ListFor(Recipes, theme, day);
                int common = rows.FindIndex(r => r.MarketTheme.Length == 0);
                Assert.True(common > 0, $"{theme}, day {day}: no own goods before the common deals");
                Assert.All(rows.Take(common), r => Assert.Equal(theme, r.MarketTheme));
                Assert.All(rows.Skip(common), r => Assert.Equal(string.Empty, r.MarketTheme));

                // Exactly what the shared rule offers — the server's check and the client's screen read the same.
                var offered = Recipes.Where(r => MarketStock.IsTrade(r) && MarketStock.Offers(Recipes, theme, r, day)).ToHashSet();
                Assert.Equal(offered.OrderBy(r => r.Key), rows.OrderBy(r => r.Key));
            }
        }

        // The ship's own console (no vendor): only the deals every vendor has.
        Assert.All(MarketStock.ListFor(Recipes, string.Empty, 0), r => Assert.Equal(string.Empty, r.MarketTheme));
    }

    [Fact]
    public void TheMixedStock_SpansSeveralTrades_ChangesFromDayToDay_AndIsAlwaysInStock()
    {
        var mixes = new HashSet<string>(StringComparer.Ordinal);
        for (long day = 0; day < 12; day++)
        {
            var picks = MarketStock.MixedOffers(Recipes, day);
            Assert.Equal(MarketStock.MixedThemeCount, picks.Count);
            Assert.True(picks.Select(r => r.MarketTheme).Distinct().Count() >= 3, $"day {day}: one offer from each of several trades");
            Assert.All(picks, r => Assert.True(MarketStock.IsTrade(r) && r.MarketTheme.Length > 0 && r.OfferedOnDay(day), r.Key));
            Assert.Equal(picks.Select(r => r.Key), MarketStock.MixedOffers(Recipes, day).Select(r => r.Key)); // a pure function

            var rows = MarketStock.ListFor(Recipes, MarketStock.MixedTheme, day);
            Assert.Equal(picks.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal),
                rows.Where(r => r.MarketTheme.Length > 0).Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal));
            Assert.All(picks, r => Assert.True(MarketStock.Offers(Recipes, MarketStock.MixedTheme, r, day)));
            mixes.Add(string.Join(",", picks.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal)));
        }

        Assert.True(mixes.Count >= 6, $"the merchant's stock should rotate by day ({mixes.Count} different mixes in 12 days)");
    }

    [Fact]
    public void TheAccessCode_IsOnlyInStockSomeDays()
    {
        var code = Content.Recipes["market_buy_access_code"];
        Assert.Equal(3, code.MarketRotation);
        int inStock = Enumerable.Range(0, 30).Count(day => code.OfferedOnDay(day));
        Assert.Equal(10, inStock);
        Assert.Contains(Enumerable.Range(0, 3), day => !MarketStock.Offers(Recipes, "traders", code, day));
    }

    // ---- visitors bring their own goods (#2630) --------------------------------------------------------------

    [Fact]
    public void VisitingTraders_BringDifferentGoodsOnDifferentVisits_DockedOnesNeverTheShopsOwn()
    {
        foreach (string theme in SvGameServer.VisitingTraderThemes)
        {
            Assert.True(Recipes.Count(r => MarketStock.IsTrade(r) && r.MarketTheme == theme) >= 2, $"'{theme}' has goods to bring");
        }

        var server = NewServer("visits");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int visit = 0; visit < 12; visit++)
        {
            server.DockTraderForTest("station-under-test");
            string theme = server.VisitingTraderThemeForTest("station-under-test")!;
            Assert.NotEqual("traders", theme); // the station shop keeps "traders"
            Assert.Contains(theme, SvGameServer.VisitingTraderThemes);
            seen.Add(theme);
        }

        Assert.True(seen.Count >= 3, $"twelve visits brought only {string.Join(", ", seen)}");

        // A landed pilot (no shop beside the pad) may bring any of them, "traders" included — still one per visit.
        var landed = Enumerable.Range(0, 24).Select(i => SvGameServer.VisitingTraderTheme("landed:body:t" + i, null)).ToHashSet();
        Assert.True(landed.Count >= 4);
        Assert.Equal(SvGameServer.VisitingTraderTheme("landed:body:t7", null), SvGameServer.VisitingTraderTheme("landed:body:t7", null));
    }

    [Fact]
    public void ALandedTrader_SellsTheGoodsItBrought_AndNoOtherTradesThemedGoods()
    {
        var transport = new NpcLifeWorld.RecordingTransport();
        var server = NewServer("landed_goods", transport);
        var pilot = server.AddLocalPlayer("Pilot");
        pilot.State.AboardShip = false;
        string body = pilot.CurrentLocationId;
        Assert.True(server.LandTraderForTest(body));
        server.Tick(0.1); // the body's own tick sets it down
        var at = server.LandedTraderPilotPosForTest(body)!.Value;
        string theme = server.LandedTraderThemeForTest(body)!;
        Assert.Contains(server.NpcJobsForTest, n => n.Role == "vendor" && n.Theme == theme);

        pilot.State.Position = new Vector3f(at.X + 1.2f, at.Y, at.Z);
        foreach (var (item, n) in new[] { ("iron_ore", 40), ("titanium_ore", 10), ("copper_ore", 20), ("silicate", 20), ("carbon", 20), ("crystal", 10), ("gold_ingot", 10), ("diamond", 10), ("creature_meat", 10) })
        {
            pilot.State.Inventory.Add(item, n, 999);
        }

        long day = server.MarketDayForTest;
        var own = MarketStock.ListFor(Recipes, theme, day).First(r => r.MarketTheme == theme && r.Inputs.All(i => pilot.State.Inventory.CountOf(i.Item) >= i.Count));
        Assert.Equal("ok", Craft(server, transport, "Pilot", own.Key));
        var foreign = Recipes.First(r => MarketStock.IsTrade(r) && r.MarketTheme.Length > 0 && r.MarketTheme != theme && r.OfferedOnDay(day));
        Assert.Equal("@srv.craft.wrong_vendor", Craft(server, transport, "Pilot", foreign.Key));
    }

    // ---- the doctor treats (#2628) ---------------------------------------------------------------------------

    [Fact]
    public void TheDoctorsDialogue_OffersTreatment_AndTalksAboutHisGoods()
    {
        var doctor = Assert.Single(Content.Dialogs, d => d.Job == "doctor");
        Assert.Equal(SvGameServer.TreatConsequence, doctor.Nodes[0].Choices[0].Consequence);
        Assert.Equal("dlg.doctor.c_treat", doctor.Nodes[0].Choices[0].TextKey);
        Assert.True(doctor.Nodes[0].Choices[0].Next < 0); // the treatment ends the talk with its own line

        var en = LocaleKeys("en");
        var de = LocaleKeys("de");
        foreach (string key in new[] { "dlg.doctor.c_treat", "dlg.doctor.r_treat", "dlg.doctor.r_treat_again", "dlg.doctor.r_treat_healthy" })
        {
            Assert.True(en.Contains(key) && de.Contains(key), $"missing '{key}'");
        }

        Assert.Contains("medpack", En("dlg.doctor.prompt"), StringComparison.OrdinalIgnoreCase); // the greeting names his goods
    }

    [Fact]
    public void TheDoctor_TreatsYouToFullHealth_OncePerInGameDay()
    {
        using var w = new NpcLifeWorld(Content, "doctor_treats");
        int baseId = w.FoundBase();
        w.Set(NpcLifeWorld.Cx + 4, w.Feet, NpcLifeWorld.Cz + 4, "clinic_post");
        w.Server.ScanBaseLifeForTest();
        int doctor = Assert.Single(w.Server.BaseResidentsForTest(baseId), r => r.Job == "doctor").NpcId;
        var at = w.Server.NpcStateForTest(doctor)!.Value.Pos;
        w.Player.State.Position = new Vector3f(at.X + 1.5f, at.Y, at.Z);
        var p = w.Player.State;

        string Treat()
        {
            w.Server.TalkToNpcForTest(NpcLifeWorld.Owner, doctor);
            w.Server.ChooseDialogForTest(NpcLifeWorld.Owner, 0); // "Can you treat me?"
            var last = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcDialogState>().Last();
            Assert.True(last.End);
            return last.Text;
        }

        // A healthy patient hears so — and keeps today's visit.
        p.Health = 100f;
        Assert.Equal(En("dlg.doctor.r_treat_healthy"), Treat());

        p.Health = 35f;
        Assert.Equal(En("dlg.doctor.r_treat"), Treat());
        Assert.Equal(100f, p.Health);
        long today = w.Server.MarketDayForTest;
        Assert.Contains(SvGameServer.TreatedMilestonePrefix + today, p.Milestones); // saved with the player
        var healed = w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcDialogState>().Last();
        Assert.Equal("heal", healed.Action);

        // Hurt again the same day: "you're fine — come back tomorrow", and no free second heal.
        p.Health = 35f;
        Assert.Equal(En("dlg.doctor.r_treat_again"), Treat());
        Assert.Equal(35f, p.Health);

        // The next in-game day he treats you again; only the latest visit is remembered.
        w.Server.AdvanceSystemDaysForTest(1.0);
        Assert.Equal(En("dlg.doctor.r_treat"), Treat());
        Assert.Equal(100f, p.Health);
        Assert.Single(p.Milestones, m => m.StartsWith(SvGameServer.TreatedMilestonePrefix, StringComparison.Ordinal));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private SvGameServer NewServer(string name, NpcLifeWorld.RecordingTransport? transport = null)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var config = new ServerConfig { WorldName = name, Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        config.Rules.SpaceCombat = SpaceCombatMode.Off;
        var server = new SvGameServer(config, Content, transport ?? new NpcLifeWorld.RecordingTransport(), repo);
        server.Start();
        return server;
    }

    private static string Craft(SvGameServer server, NpcLifeWorld.RecordingTransport transport, string playerId, string recipe)
    {
        int mark = transport.Sent.Count;
        server.Craft(playerId, recipe);
        var res = transport.Sent.Skip(mark).Select(x => x.Msg).OfType<CraftResult>().LastOrDefault();
        return res is null ? "(no result)" : res.Success ? "ok" : res.Reason ?? string.Empty;
    }

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort
        }
        catch (UnauthorizedAccessException)
        {
            // best effort
        }
    }
}
