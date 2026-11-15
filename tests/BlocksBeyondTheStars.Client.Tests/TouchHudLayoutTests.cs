// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The touch layer and the HUD share the screen (#3668 follow-up): every HUD zone is laid out against the rectangles the
/// touch controls take (<see cref="TouchLayout"/>, <see cref="TouchReservation"/>), and none of them may lie on a control —
/// the radar and the compass (≡ and the flight VIEW used to cover them), the toast lane (VEGA's NEXT sat on it), VEGA's
/// speech panel (under UP / DOWN / RUN, the gear strip on her continue pill), the right column's cards (under the action
/// buttons), the gear strip itself and the opened chat window. Checked on a 16:9 tablet, 20:9 and 19.5:9 phones, a 4:3
/// iPad and a 16:10 tablet, at UI scale 0.8, 1 and 1.25, on foot, in flight and on a speeder.
/// </summary>
public sealed class TouchHudLayoutTests
{
    private const float Eps = 0.01f;

    // HUD constants at UI scale 1, mirrored from the Unity side (HudZones, HudRightColumn, VegaPanel, HudUi.Hotbar/GearStrip).
    private const float MapPartFootH = 298f;  // compass + captions (176), gap, clock with its chip row (74), playtime (44)
    private const float MapPartSpaceH = 212f; // radar face (160) + its two lines (RadarSize + 52)
    private const float ObjectiveChipH = 48f;
    private const float SpeechW = 440f, SpeechH = 190f, SpeechX = 16f, SpeechY = 546f;
    private static readonly HudRect GearStrip = new HudRect(524f, 670f, (3 * 58f) + 48f, 64f); // four cells above the bar's left end
    private static readonly HudRect Hotbar = new HudRect(400f, 742f, 736f, 92f);
    private static readonly HudRect ScanCard = new HudRect(0f, 0f, 330f, 182f), WreckCard = new HudRect(0f, 0f, 250f, 150f);

    /// <summary>Screens in pixels: a 16:9 tablet, two phones, a 4:3 iPad and a 16:10 tablet.</summary>
    private static readonly (float W, float H)[] Screens = { (1920f, 1080f), (2400f, 1080f), (2340f, 1080f), (2048f, 1536f), (2560f, 1600f) };

    private static readonly float[] Scales = { 0.8f, 1f, 1.25f };

    private static readonly TouchCluster[] Contexts = { TouchCluster.OnFoot, TouchCluster.Flight, TouchCluster.Speeder };

    public static TheoryData<float, float, float, TouchCluster> Everything()
    {
        var data = new TheoryData<float, float, float, TouchCluster>();
        foreach (var (w, h) in Screens)
        {
            foreach (float scale in Scales)
            {
                foreach (var cluster in Contexts)
                {
                    data.Add(w, h, scale, cluster);
                }
            }
        }

        return data;
    }

    [Fact]
    public void Table_HoldsEveryControlOnce()
    {
        foreach (TouchControl control in System.Enum.GetValues(typeof(TouchControl)))
        {
            Assert.Single(TouchLayout.All, s => s.Control == control);
        }
    }

    [Fact]
    public void Rects_At16x9_AreTheTouchLayoutsOwn()
    {
        // The menu button: anchored top right, centre 90 in from both edges, 96 square — HUD 1425.6…1502.4 × 33.6…110.4.
        var menu = TouchLayout.RectOnCanvas(TouchLayout.Of(TouchControl.Menu), 1920f, 1080f);
        AssertRect(new HudRect(1782f, 42f, 96f, 96f), menu);
        AssertRect(new HudRect(1425.6f, 33.6f, 76.8f, 76.8f), menu.Scaled(TouchLayout.HudPerTouch(1f)));

        // The move stick: 320 around (230, 230) from the bottom left.
        AssertRect(new HudRect(70f, 690f, 320f, 320f), TouchLayout.RectOnCanvas(TouchLayout.Of(TouchControl.Stick), 1920f, 1080f));
        Assert.Equal(0.64f, TouchLayout.HudPerTouch(1.25f), 3);
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public void Reservation_CanvasIsTheHudCanvas(float w, float h, float scale, TouchCluster cluster)
    {
        var touch = TouchReservation.ForScreen(cluster, w, h, scale);
        var (hudW, hudH) = TouchLayout.ExpandCanvas(w, h, TouchLayout.HudRefW / scale, TouchLayout.HudRefH / scale);
        Assert.Equal(hudW, touch.CanvasW, 2);
        Assert.Equal(hudH, touch.CanvasH, 2);
        Assert.DoesNotContain(touch.Controls, r => r.Equals(VegaNextRect(w, h, scale)));
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public void MapPart_StartsUnderTheTouchRow(float w, float h, float scale, TouchCluster cluster)
    {
        var touch = TouchReservation.ForScreen(cluster, w, h, scale);
        float top = TouchHudLayout.MapTop + TouchHudLayout.MapShift(touch);
        float partH = cluster == TouchCluster.Flight ? MapPartSpaceH : MapPartFootH;
        AssertClear(touch, new HudRect(TouchHudLayout.RightX, top, TouchHudLayout.RightEdge - TouchHudLayout.RightX, partH), "map part", w, h, scale);
    }

    [Fact]
    public void MapPart_MovesOnlyWhereTheTouchRowLiesOverIt()
    {
        // 16:9: ≡ (bottom 110.4) lies on the radar face → the map part starts at 118.4.
        Assert.Equal(108.4f, TouchHudLayout.MapShift(TouchReservation.ForScreen(TouchCluster.Flight, 1920f, 1080f, 1f)), 2);
        // A 20:9 phone: the right-anchored buttons sit past the HUD's 1536 frame — nothing moves.
        Assert.Equal(0f, TouchHudLayout.MapShift(TouchReservation.ForScreen(TouchCluster.OnFoot, 2400f, 1080f, 1f)));
        Assert.Equal(0f, TouchHudLayout.MapShift(TouchReservation.None));
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public void ToastLane_ClearsTheTouchRow(float w, float h, float scale, TouchCluster cluster)
    {
        var touch = TouchReservation.ForScreen(cluster, w, h, scale);
        var lane = TouchHudLayout.ToastLane(touch, 768f, 80f, 24f, 840f, 560f);
        Assert.True(lane.W >= 560f, $"the lane keeps most of its width: {lane}");
        Assert.True(lane.Y < 130f, $"and stays at the top: {lane}");
        AssertClear(touch, lane, "toast lane", w, h, scale);
    }

    [Fact]
    public void ToastLane_At16x9OnFoot_StopsLeftOfMap_AndDropsUnderTheRowAtALargeScale()
    {
        var touch = TouchReservation.ForScreen(TouchCluster.OnFoot, 1920f, 1080f, 1f);
        AssertRect(new HudRect(379.2f, 80f, 777.6f, 24f), TouchHudLayout.ToastLane(touch, 768f, 80f, 24f, 840f, 560f)); // MAP at 1164.8
        AssertRect(new HudRect(348f, 80f, 840f, 24f), TouchHudLayout.ToastLane(TouchReservation.None, 768f, 80f, 24f, 840f, 560f));

        // UI scale 1.25: MAP, VIEW, CHAT and ≡ end at 85.8…88.3 over the lane's right half — the lane moves under them.
        var large = TouchReservation.ForScreen(TouchCluster.OnFoot, 1920f, 1080f, 1.25f);
        AssertRect(new HudRect(348f, 96.32f, 840f, 24f), TouchHudLayout.ToastLane(large, 768f, 80f, 24f, 840f, 560f));
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public void ContextCards_StayOffTheActionButtons(float w, float h, float scale, TouchCluster cluster)
    {
        var touch = TouchReservation.ForScreen(cluster, w, h, scale);
        bool inSpace = cluster == TouchCluster.Flight;
        var stack = new ContextCardStack();
        foreach (bool planetCard in inSpace ? new[] { false, true } : new[] { false })
        {
            foreach (var cards in CardSets(inSpace))
            {
                stack.Begin(ContextTop(touch, inSpace), TouchHudLayout.RightEdge, TouchHudLayout.SecondColumnRight(inSpace),
                    TouchHudLayout.MapTop + TouchHudLayout.MapShift(touch), touch, planetCard);
                foreach (var card in cards)
                {
                    stack.Add(card.W, card.H);
                }

                AssertCards(stack, touch, $"{cards.Length} card(s), planet card {planetCard}", w, h, scale);
                for (int i = 0; planetCard && i < stack.Count; i++)
                {
                    Assert.True(stack[i].Right <= TouchHudLayout.RightEdge - TouchHudLayout.PlanetCardW + Eps, $"left of the planet card: {stack[i]}");
                }
            }
        }
    }

    [Fact]
    public void ContextCards_InFlightUnderThePlanetCard_KeepLeftOfIt()
    {
        var touch = TouchReservation.ForScreen(TouchCluster.Flight, 1920f, 1080f, 1f);
        var stack = new ContextCardStack();
        float top = ContextTop(touch, true); // 118.4 + 212 + 64 = 394.4
        stack.Begin(top, TouchHudLayout.RightEdge, TouchHudLayout.SecondColumnRight(true), 118.4f, touch);
        stack.Add(WreckCard.W, WreckCard.H);
        AssertRect(new HudRect(1276f, top, 250f, 150f), stack[0]); // the ship-repair card fits the first column (TARGET at 579.2)

        stack.Begin(top, TouchHudLayout.RightEdge, TouchHudLayout.SecondColumnRight(true), 118.4f, touch, firstColumnTaken: true);
        stack.Add(WreckCard.W, WreckCard.H);
        AssertRect(new HudRect(868f, top, 250f, 150f), stack[0]); // the planet card holds 1126…1526
        Assert.Equal(top, stack.FirstColumnBottom, 2);
    }

    private static void AssertCards(ContextCardStack stack, TouchReservation touch, string what, float w, float h, float scale)
    {
        for (int i = 0; i < stack.Count; i++)
        {
            if (!stack.Shown(i))
            {
                continue;
            }

            AssertClear(touch, stack[i], $"card {i} ({what})", w, h, scale);
            Assert.True(stack[i].X >= 768f + 60f, $"card {i} keeps clear of the crosshair: {stack[i]}");
            for (int j = 0; j < i; j++)
            {
                Assert.False(stack.Shown(j) && stack[j].Overlaps(stack[i]), $"cards {j} and {i} overlap: {stack[j]} {stack[i]}");
            }
        }

        Assert.True(stack.Shown(0), "the first card always shows");
    }

    [Fact]
    public void ContextCards_At16x9OnFoot_StepIntoTheSecondColumn()
    {
        var touch = TouchReservation.ForScreen(TouchCluster.OnFoot, 1920f, 1080f, 1f);
        var stack = new ContextCardStack();
        float top = ContextTop(touch, false); // 118.4 + 298 + 8 + 48 + 8 = 480.4
        stack.Begin(top, TouchHudLayout.RightEdge, TouchHudLayout.SecondColumnRight(false), 118.4f, touch);
        stack.Add(ScanCard.W, ScanCard.H);

        // PLACE's top is 576: the column ends at 560, so the scan card (182) moves left and up — it ends above FEED (537.6).
        Assert.Equal(560f, touch.StackLimit(TouchHudLayout.RightX, TouchHudLayout.RightEdge, 118.4f), 2);
        AssertRect(new HudRect(858f, 521.6f - 182f, 330f, 182f), stack[0]);
        Assert.Equal(top, stack.FirstColumnBottom, 2);

        // Without touch the same card stays in the column, at the context top.
        stack.Begin(top, TouchHudLayout.RightEdge, TouchHudLayout.SecondColumnRight(false), 10f, TouchReservation.None);
        stack.Add(ScanCard.W, ScanCard.H);
        AssertRect(new HudRect(1196f, top, 330f, 182f), stack[0]);
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public void PlanetCard_IsCappedAboveTheButtons(float w, float h, float scale, TouchCluster cluster)
    {
        var touch = TouchReservation.ForScreen(cluster, w, h, scale);
        float mapTop = TouchHudLayout.MapTop + TouchHudLayout.MapShift(touch);
        float x0 = TouchHudLayout.RightEdge - TouchHudLayout.PlanetCardW;
        float limit = System.Math.Min(touch.StackLimit(x0, TouchHudLayout.RightEdge, mapTop), touch.CanvasH - 8f);
        float minTop = mapTop + MapPartSpaceH + 8f;
        var (top, viewH) = TouchHudLayout.FitTallCard(ContextTop(touch, true), 520f, limit, minTop, 200f);
        Assert.True(viewH > 0f && viewH <= 520f);
        Assert.True(top >= minTop - Eps, "never over the radar");
        AssertClear(touch, new HudRect(x0, top, TouchHudLayout.PlanetCardW, viewH), "planet card", w, h, scale);
    }

    [Fact]
    public void PlanetCard_FitsWhereThereIsRoom_AndScrollsWhereNot()
    {
        Assert.Equal((300f, 400f), TouchHudLayout.FitTallCard(300f, 400f, 800f, 200f, 200f));   // fits
        Assert.Equal((300f, 250f), TouchHudLayout.FitTallCard(300f, 520f, 550f, 200f, 200f));   // capped, scrolls 270
        Assert.Equal((350f, 200f), TouchHudLayout.FitTallCard(420f, 520f, 550f, 300f, 200f));   // rises over the objectives
        Assert.Equal((300f, 150f), TouchHudLayout.FitTallCard(420f, 520f, 450f, 300f, 200f));   // not above the radar
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public void VegaPanel_AndNext_StayOffTheControls(float w, float h, float scale, TouchCluster cluster)
    {
        var touch = TouchReservation.ForScreen(cluster, w, h, scale);
        foreach (bool gear in new[] { false, true })
        {
            var obstacles = VegaObstacles(touch, cluster, gear && cluster != TouchCluster.Flight);
            var panel = TouchCommsLayout.SpeechSpot(SpeechW, SpeechH, SpeechX, SpeechY, 272f, obstacles, 1, touch.CanvasW, touch.CanvasH);
            var hard = obstacles.GetRange(0, obstacles.Count - 1); // the chat lane (last) is soft: dropped only on a small canvas
            AssertClearOf(scale <= 1f ? obstacles : hard, panel, "VEGA's panel", w, h, scale);
            Assert.True(panel.Right <= touch.CanvasW - 8f + Eps, $"VEGA's panel on the canvas: {panel}");

            float next = 96f * touch.HudPerTouch;
            var spot = TouchCommsLayout.NextSpot(panel, next, obstacles, touch.CanvasW, touch.CanvasH);
            AssertClearOf(obstacles, spot, "NEXT", w, h, scale);
            Assert.False(spot.Overlaps(panel), "NEXT beside the panel, not on it");
            Assert.False(spot.Overlaps(new HudRect(348f, 80f, 840f, 24f)), "NEXT off the toast lane");
            Assert.True(spot.InsideCanvas(touch.CanvasW, touch.CanvasH, TouchCommsLayout.EdgeMargin), $"NEXT on the canvas: {spot}");

            var tab = new HudRect(panel.X, panel.Bottom - 40f, 180f, 40f); // folded: NEXT beside the tab
            AssertClearOf(obstacles, TouchCommsLayout.NextSpot(tab, next, obstacles, touch.CanvasW, touch.CanvasH), "NEXT by the tab", w, h, scale);
        }
    }

    [Fact]
    public void VegaPanel_At16x9_SitsRightOfTheChatLaneAboveTheGearStrip()
    {
        var foot = TouchReservation.ForScreen(TouchCluster.OnFoot, 1920f, 1080f, 1f);
        var obstacles = VegaObstacles(foot, TouchCluster.OnFoot, gear: true);
        var panel = TouchCommsLayout.SpeechSpot(SpeechW, SpeechH, SpeechX, SpeechY, 272f, obstacles, 1, foot.CanvasW, foot.CanvasH);
        AssertRect(new HudRect(464f, 472f, SpeechW, SpeechH), panel); // the lane ends at 456; the gear strip starts at 670
        AssertRect(new HudRect(912f, 585.2f, 76.8f, 76.8f), TouchCommsLayout.NextSpot(panel, 76.8f, obstacles, foot.CanvasW, foot.CanvasH));

        var flight = TouchReservation.ForScreen(TouchCluster.Flight, 1920f, 1080f, 1f);
        var inFlight = TouchCommsLayout.SpeechSpot(SpeechW, SpeechH, SpeechX, SpeechY, 272f, VegaObstacles(flight, TouchCluster.Flight, false), 1,
            flight.CanvasW, flight.CanvasH);
        AssertRect(new HudRect(464f, 484f, SpeechW, SpeechH), inFlight); // above the instruments strip (682)

        // A 4:3 iPad has room under the chat lane: the desktop spot.
        var ipad = TouchReservation.ForScreen(TouchCluster.OnFoot, 2048f, 1536f, 1f);
        var desk = TouchCommsLayout.SpeechSpot(SpeechW, SpeechH, SpeechX, SpeechY, 272f, VegaObstacles(ipad, TouchCluster.OnFoot, true), 1,
            ipad.CanvasW, ipad.CanvasH);
        AssertRect(new HudRect(SpeechX, SpeechY, SpeechW, SpeechH), desk);
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public void GearStrip_RisesClearOfTheButtons(float w, float h, float scale, TouchCluster cluster)
    {
        var touch = TouchReservation.ForScreen(cluster, w, h, scale);
        AssertClear(touch, touch.LiftClear(GearStrip, 8f), "gear strip", w, h, scale);
    }

    [Fact]
    public void GearStrip_StaysPutWhereNothingIsInTheWay()
    {
        AssertRect(GearStrip, TouchReservation.ForScreen(TouchCluster.OnFoot, 1920f, 1080f, 1f).LiftClear(GearStrip, 8f));
        // A 20:9 phone: ◄ and ACT follow the screen's centre (HUD 636… and 540…, from 724) and reach under the strip.
        Assert.Equal(652f, TouchReservation.ForScreen(TouchCluster.OnFoot, 2400f, 1080f, 1f).LiftClear(GearStrip, 8f).Y, 2);
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public void ChatWindow_OnTouch_StaysOffTheControls(float w, float h, float scale, TouchCluster cluster)
    {
        var touch = TouchReservation.ForScreen(cluster, w, h, scale);
        foreach (bool vega in new[] { false, true })
        {
            var window = ChatWindowLayout.Resolve(vega, false, true, touch.CanvasW, touch.CanvasH, 0f);
            var r = TouchHudLayout.ClearOfTouch(window, touch);
            Assert.True(r.H >= 160f, $"the window keeps its room: {r.H}");
            AssertClear(touch, new HudRect(r.X, r.Y, r.W, r.H), "chat window", w, h, scale);
        }
    }

    [Fact]
    public void BrowserInset_ClearsTheFullscreenButton()
    {
        Assert.Equal(60f, TouchHudLayout.BrowserButtonInset(1.25f, 1f));           // 1920 px wide: 58 css px = 46 units → the floor
        Assert.Equal(69.6f, TouchHudLayout.BrowserButtonInset(0.8333f, 1f), 1);    // a 1280 px window
        Assert.Equal(61.9f, TouchHudLayout.BrowserButtonInset(1.875f, 2f), 1);     // a 2× display, 1440 css px wide
        Assert.Equal(120f, TouchHudLayout.BrowserButtonInset(0.3f, 3f));           // never more than the cap
        Assert.Equal(60f, TouchHudLayout.BrowserButtonInset(0f, 0f));              // nothing known: the floor
    }

    private static float ContextTop(TouchReservation touch, bool inSpace)
        => TouchHudLayout.MapTop + TouchHudLayout.MapShift(touch) + (inSpace ? MapPartSpaceH : MapPartFootH) + 8f + ObjectiveChipH + 8f;

    private static HudRect[][] CardSets(bool inSpace) => inSpace
        ? new[] { new[] { WreckCard } }
        : new[] { new[] { ScanCard }, new[] { WreckCard }, new[] { ScanCard, WreckCard }, new[] { WreckCard, WreckCard }, new[] { ScanCard, WreckCard, WreckCard } };

    /// <summary>What VEGA's touch spot keeps clear of (VegaPanel.Layout.cs): the controls, the gear strip and the quick-bar on
    /// foot, the instruments strip in flight, and last — soft — the chat lane above her desktop spot.</summary>
    private static List<HudRect> VegaObstacles(TouchReservation touch, TouchCluster cluster, bool gear)
    {
        var list = new List<HudRect>(touch.Controls);
        if (cluster == TouchCluster.Flight)
        {
            list.Add(new HudRect((touch.CanvasW / 2f) - 270f, touch.CanvasH - 182f, 540f, 182f));
        }
        else
        {
            list.Add(Hotbar);
            if (gear)
            {
                list.Add(touch.LiftClear(GearStrip, 8f));
            }
        }

        list.Add(new HudRect(16f, 272f, 440f, 274f));
        return list;
    }

    private static HudRect VegaNextRect(float w, float h, float scale)
    {
        var (tw, th) = TouchLayout.ExpandCanvas(w, h, TouchLayout.RefW, TouchLayout.RefH);
        return TouchLayout.RectOnCanvas(TouchLayout.Of(TouchControl.VegaNext), tw, th).Scaled(TouchLayout.HudPerTouch(scale));
    }

    private static void AssertClear(TouchReservation touch, HudRect rect, string what, float w, float h, float scale)
        => AssertClearOf(touch.Controls, rect, what, w, h, scale);

    private static void AssertClearOf(IReadOnlyList<HudRect> obstacles, HudRect rect, string what, float w, float h, float scale)
    {
        foreach (var o in obstacles)
        {
            Assert.False(o.Overlaps(rect), $"{what} {rect} lies on {o} ({w}×{h}, UI scale {scale})");
        }
    }

    private static void AssertRect(HudRect expected, HudRect actual)
    {
        Assert.Equal(expected.X, actual.X, 2);
        Assert.Equal(expected.Y, actual.Y, 2);
        Assert.Equal(expected.W, actual.W, 2);
        Assert.Equal(expected.H, actual.H, 2);
    }
}
