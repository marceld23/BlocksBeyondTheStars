// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// #3175 — a Market base's trader landed a thousand blocks away and still said "nearby" (Lyxette, a 74-minute session without
/// a trader in sight): his pads were on the default "Me + allies", his own ship stood on one, and the communal fallback had no
/// distance cap. Now a Market base's free pads on "Me + allies" and "Everyone" take the trader (end to end: it touches down
/// there), the communal fallback stays within <see cref="SvGameServer.TraderCommunalPadReach"/>, the toasts and VEGA's relay
/// name how far and which way when the pad is not right by the player, and the base owner hears once per base and reason why
/// the base's pads took no trader. #3176 — the store never counts the sky's snow on a pad as built.
/// </summary>
public sealed class TraderPadChoiceTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static NpcLifeWorld World(string name)
    {
        var w = new NpcLifeWorld(Content.Value, name, configure: c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.FriendlyVisitors = false;
        });
        w.FoundBase();
        w.SetBaseFocus(BaseFocus.Market);
        w.Server.ScanBaseLifeForTest();
        return w;
    }

    /// <summary>A base pad east of the arena's core: its 17 × 17 square lies on the arena, clear of the core and the machines.</summary>
    private static Vector3i PadCell(NpcLifeWorld w) => new(NpcLifeWorld.Cx + 14, w.PadY, NpcLifeWorld.Cz);

    private static List<T> To<T>(NpcLifeWorld w, PlayerSession p)
        => w.Transport.Sent.Where(s => s.Conn == p.ConnectionId).Select(s => s.Msg).OfType<T>().ToList();

    private static IEnumerable<string> HintKeys(NpcLifeWorld w)
        => To<ShipAiLine>(w, w.Player).Select(l => l.LineKey).Where(k => k.StartsWith("vega.hint.market_pad", StringComparison.Ordinal));

    /// <summary>The public pad beside the arena (pad 0 on this seed): the free dry one nearest the core.</summary>
    private static int PadBesideTheArena(NpcLifeWorld w)
    {
        int best = -1;
        double bestSq = double.MaxValue;
        for (int i = 0; i < w.Server.LandingPadCount; i++)
        {
            var pad = w.Server.LandingPadInfoForTest(i);
            double dx = pad.X - NpcLifeWorld.Cx, dz = pad.Z - NpcLifeWorld.Cz;
            if (!pad.Wet && (dx * dx) + (dz * dz) < bestSq)
            {
                bestSq = (dx * dx) + (dz * dz);
                best = i;
            }
        }

        return best;
    }

    /// <summary>Distance (rounded to 10 m) and the English direction from a point to a pad centre, as the server words it.</summary>
    private static (int Distance, string Direction) Way(double fromX, double fromZ, double toX, double toZ)
    {
        double dx = toX - fromX, dz = toZ - fromZ;
        int distance = Math.Max(10, (int)(Math.Round(Math.Sqrt((dx * dx) + (dz * dz)) / 10.0) * 10));
        var en = Content.Value.CreateLocalizer(GameLocale.English);
        return (distance, en.Get(SvGameServer.DirectionKeyForTest(dx, dz)));
    }

    [Fact]
    public void AMarketBasePad_OnTheDefaultMeAndAllies_TakesTheTrader_AndItTouchesDownThere()
    {
        using var w = World("tpad_allies");
        string body = w.Server.ActiveLocationId;
        var cell = PadCell(w);
        Assert.Null(w.Server.PlaceBasePadForTest(w.Player, cell));
        Assert.Equal(BasePadAccess.Allies, Assert.Single(w.Server.BasePadSnapshots).Access); // the default, untouched

        Assert.True(w.Server.ForcePlanetTraderForTest());
        Assert.Equal(cell, w.Server.LandedTraderBasePadForTest(body));
        Assert.Equal(-1, w.Server.LandedTraderPadForTest(body));
        Assert.Contains("@srv.trader.approaching", w.MessagesToPlayer()); // right by the player: "look up!"

        w.Server.AdvanceUptimeForTest(21.0);
        w.Server.TickForTest(0.1);
        Assert.Contains(w.Server.PlacedHullOwnersForTest(), o => o.StartsWith("npc:", StringComparison.Ordinal));
        Assert.Single(To<ShipTransitFx>(w, w.Player), f => f.ApproachSeconds > 0f); // #3177: the approach's contrail
        var fx = Assert.Single(To<ShipTransitFx>(w, w.Player).Where(f => f.ApproachSeconds == 0f));
        Assert.True(fx.Landing);
        Assert.Equal((cell.X + 0.5f, cell.Z + 0.5f), (fx.X, fx.Z)); // the descent plays on the base pad
        Assert.Contains("@srv.trader.landed:" + w.Server.LandedTraderNameForTest(body), w.MessagesToPlayer());
        Assert.Empty(HintKeys(w));
    }

    [Fact]
    public void TheOwnShipOnTheOnlyPad_SendsTheTraderToThePublicPad_AndVegaSaysWhy_OncePerBase()
    {
        using var w = World("tpad_own_ship");
        string body = w.Server.ActiveLocationId;
        var cell = PadCell(w);
        Assert.Null(w.Server.PlaceBasePadForTest(w.Player, cell));
        w.Player.AssignedBasePad = cell; // his own ship is parked on it
        w.Player.AssignedPadIndex = -1;
        Assert.Null(w.Server.MarketBasePadForTest(w.Player));

        Assert.True(w.Server.ForcePlanetTraderForTest(), "the public pad beside the arena is in reach");
        Assert.Null(w.Server.LandedTraderBasePadForTest(body));
        Assert.Equal(PadBesideTheArena(w), w.Server.LandedTraderPadForTest(body));
        Assert.Equal(new[] { "vega.hint.market_pad_own_ship" }, HintKeys(w));
        Assert.Contains(w.Player.State.Milestones, m => m.StartsWith("vega:hint:market_pad:" + body + "|", StringComparison.Ordinal)
                                                         && m.EndsWith(":own_ship", StringComparison.Ordinal));

        Assert.True(w.Server.ReleaseLandedTraderRecordForTest(body));
        Assert.True(w.Server.ForcePlanetTraderForTest());
        Assert.Single(HintKeys(w)); // once per base and reason
    }

    [Fact]
    public void AnOnlyMePad_KeepsTheTradersOff_AndVegaSaysHowToOpenIt()
    {
        using var w = World("tpad_owner_only");
        string body = w.Server.ActiveLocationId;
        var cell = PadCell(w);
        Assert.Null(w.Server.PlaceBasePadForTest(w.Player, cell));
        w.Server.SetBasePadForTest(w.Player, cell, string.Empty, BasePadAccess.Owner);

        Assert.True(w.Server.ForcePlanetTraderForTest());
        Assert.Null(w.Server.LandedTraderBasePadForTest(body));
        Assert.Equal(new[] { "vega.hint.market_pad_owner_only" }, HintKeys(w));
    }

    [Fact]
    public void APublicPadBeyondTheReach_BringsNoTrader_AndVegaAsksForAPadAtTheBase()
    {
        using var w = World("tpad_far");
        w.Player.AssignedPadIndex = PadBesideTheArena(w); // his ship holds the only public pad in reach; the rest lie thousands of blocks off

        Assert.False(w.Server.ForcePlanetTraderForTest());
        Assert.Equal(0, w.Server.LandedTraderCountForTest());
        Assert.DoesNotContain(w.MessagesToPlayer(), m => m.Contains("trader", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(new[] { "vega.hint.market_pad_none" }, HintKeys(w));
    }

    [Fact]
    public void WithoutAMarketBase_AFarPublicPadStillGetsTheTrader_AndTheToastSaysHowFar()
    {
        // The reach cap is a Market base's promise ("parked near your base"). A player without one still gets the trader on the
        // nearest free public pad, however far — the communal pads lie far apart — and the toast names the way.
        using var w = new NpcLifeWorld(Content.Value, "tpad_far_nomarket", configure: c =>
        {
            c.Rules.PlanetEnemies = AlienActivity.Off;
            c.Rules.Bandits = AlienActivity.Off;
            c.Rules.FriendlyVisitors = false;
        });
        string body = w.Server.ActiveLocationId;
        int beside = PadBesideTheArena(w);
        w.Player.AssignedPadIndex = beside; // his ship holds the pad beside him; the next free one lies far off

        Assert.True(w.Server.ForcePlanetTraderForTest());
        int pad = w.Server.LandedTraderPadForTest(body);
        Assert.True(pad >= 0 && pad != beside, $"the trader takes another public pad (got {pad})");
        Assert.Contains(To<ServerMessage>(w, w.Player), m => m.Text.StartsWith("A trader ship is coming in to land about", StringComparison.Ordinal));
        Assert.Empty(HintKeys(w)); // no Market base, no VEGA hint
    }

    [Fact]
    public void APlayerAwayFromThePad_HearsHowFarAndWhichWay_TheOneBesideItHearsLookUp()
    {
        using var w = World("tpad_far_toast");
        string body = w.Server.ActiveLocationId;
        var visitor = w.Server.AddLocalPlayer("Visitor");
        visitor.State.AboardShip = false;
        visitor.State.Position = new Vector3f(NpcLifeWorld.Cx + 200.5f, w.Feet, NpcLifeWorld.Cz + 0.5f);
        var pad = w.Server.LandingPadInfoForTest(PadBesideTheArena(w));
        var (distance, direction) = Way(visitor.State.Position.X, visitor.State.Position.Z, pad.X + 0.5, pad.Z + 0.5);

        Assert.True(w.Server.ForcePlanetTraderForTest());
        Assert.Contains("@srv.trader.approaching", w.MessagesToPlayer());
        string toast = Assert.Single(To<ServerMessage>(w, visitor), m => m.Text.Contains("trader", StringComparison.OrdinalIgnoreCase)).Text;
        Assert.Equal($"A trader ship is coming in to land about {distance} m to the {direction} — look that way!", toast);
        Assert.True(distance > SvGameServer.TraderNearbyRadius);
        Assert.Equal(body, visitor.CurrentLocationId);
    }

    [Fact]
    public void VegasRelay_NamesTheWay_ForAMarketBaseAwayFromThePad_AndSaysNearForTheOneBesideIt()
    {
        using var w = World("tpad_far_news");
        string body = w.Server.ActiveLocationId;
        var visitor = w.Server.AddLocalPlayer("Visitor");
        visitor.State.AboardShip = false;
        var core = new Vector3i(NpcLifeWorld.Cx + 110, w.Feet, NpcLifeWorld.Cz);
        visitor.State.Position = new Vector3f(core.X + 2.5f, core.Y, core.Z + 0.5f);
        w.Server.PlaceBaseForTest(visitor, core);
        w.Server.World.SetBlock(core, Content.Value.GetBlock("base_core")!.NumericId);
        w.Server.YieldForTest(visitor, new YieldIntent
        {
            Action = YieldIntent.ActionFocus,
            Kind = YieldIntent.KindBase,
            BodyId = body,
            CellX = core.X,
            CellY = core.Y,
            CellZ = core.Z,
            Focus = BaseFocus.Market,
        });

        Assert.True(w.Server.ForcePlanetTraderForTest());
        int padIndex = w.Server.LandedTraderPadForTest(body);
        Assert.Equal(PadBesideTheArena(w), padIndex); // the owner's base beside the arena anchors it
        w.Server.AdvanceUptimeForTest(21.0);
        w.Server.TickForTest(0.1);

        var pad = w.Server.LandingPadInfoForTest(padIndex);
        var (distance, direction) = Way(core.X + 0.5, core.Z + 0.5, pad.X + 0.5, pad.Z + 0.5);
        var far = Assert.Single(To<ShipAiLine>(w, visitor), l => l.LineKey.StartsWith("vega.news.trader", StringComparison.Ordinal));
        Assert.Equal("vega.news.trader_far", far.LineKey);
        Assert.Equal(new[] { w.Server.Galaxy.FindBody(body)!.Name, distance.ToString(System.Globalization.CultureInfo.InvariantCulture), direction },
            LineText.SplitArgs(far.LineArg));
        Assert.Equal("vega.news.trader", Assert.Single(To<ShipAiLine>(w, w.Player), l => l.LineKey.StartsWith("vega.news.trader", StringComparison.Ordinal)).LineKey);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public void TheStore_NeverCountsTrackedWeatherSnow_AsBuilt_ButAPlayersBlockThere(string kind)
    {
        // #3176: a server write keeps the owner of a cell a player once mined or paved — the drift reads as the player's.
        string root = Path.Combine(Path.GetTempPath(), "bbts_tpad_store_" + Guid.NewGuid().ToString("N"));
        var paths = new SaveGamePaths(root, "store");
        IWorldRepository repo = kind == "memory" ? new MemoryWorldRepository(paths) : new SqliteWorldRepository(paths);
        try
        {
            repo.Initialize();
            ushort snow = Content.Value.GetBlock("snow")!.NumericId.Value, stone = Content.Value.GetBlock("stone")!.NumericId.Value;
            var cell = new Vector3i(5, 11, 5);
            var box = (Min: new Vector3i(0, 11, 0), Max: new Vector3i(10, 26, 10));
            repo.SetBlock("p", cell, 0, owner: "Homesteader"); // the player once mined here
            repo.SetBlock("p", cell, snow); // … and the sky laid a drift: no owner given, the old one is kept
            repo.SaveWeatherDeposit("p", cell, snow, 120.0);
            Assert.False(repo.HasPlayerBuiltBlocks("p", box.Min, box.Max, Array.Empty<ushort>()));

            repo.SetBlock("p", cell, stone, owner: "Homesteader"); // the player builds on the drift's cell: a block of his
            Assert.True(repo.HasPlayerBuiltBlocks("p", box.Min, box.Max, Array.Empty<ushort>()));
        }
        finally
        {
            (repo as IDisposable)?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }
}
