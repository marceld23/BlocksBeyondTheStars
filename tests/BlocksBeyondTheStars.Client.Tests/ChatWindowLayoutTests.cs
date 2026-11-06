// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3264: the opened chat is a big centred window that never covers its neighbours — VEGA's speech panel, the objective
/// chip, the HUD's right column, the pad's on-screen keyboard and a touch device's system keyboard — at every aspect
/// ratio and UI-scale step. These pin <see cref="ChatWindowLayout.Resolve"/>.
/// </summary>
public sealed class ChatWindowLayoutTests
{
    /// <summary>The on-screen keyboard's panel top in its 1920 × 1080 reference: four key rows + the command row.</summary>
    private const float OskTop = 254f;

    private const float Eps = 0.01f;

    /// <summary>The chat canvas in HUD units for a screen aspect (16:9, 16:10, 21:9, 4:3) at UI scale 1.</summary>
    public static TheoryData<float, float> Aspects => new()
    {
        { 1536f, 864f },  // 16:9
        { 1536f, 960f },  // 16:10 (Steam Deck)
        { 2048f, 864f },  // 21:9 (2560 × 1080)
        { 1536f, 1152f }, // 4:3
    };

    /// <summary>Every aspect at every UI-scale step (0.8 … 1.6 in 0.1 steps): the canvas reference shrinks by the scale.</summary>
    public static TheoryData<float, float> AspectsAndScales()
    {
        var data = new TheoryData<float, float>();
        foreach (var (w, h) in new[] { (1536f, 864f), (1536f, 960f), (2048f, 864f), (1536f, 1152f) })
        {
            for (int step = 8; step <= 16; step++)
            {
                float scale = step / 10f;
                data.Add(w / scale, h / scale);
            }
        }

        return data;
    }

    [Fact]
    public void Default_IsTheCentred768By520Window()
    {
        var r = ChatWindowLayout.Resolve(vegaSpeech: false, osk: false, touchKeyboard: false, 1536f, 864f, OskTop);
        Assert.Equal(384f, r.X, Eps);
        Assert.Equal(64f, r.Y, Eps);
        Assert.Equal(768f, r.W, Eps);
        Assert.Equal(520f, r.H, Eps);
        Assert.Equal(1152f, r.Right, Eps);
        Assert.Equal(584f, r.Bottom, Eps);
    }

    [Theory]
    [MemberData(nameof(Aspects))]
    public void EveryAspect_KeepsTheWindowInTheHudFramesCentreLane(float canvasW, float canvasH)
    {
        // The HUD's columns are anchored top-left in its 1536-wide frame, so the window centres in that frame: on a
        // 21:9 screen it stays between the left lane and the right column instead of drifting under the compass.
        var r = ChatWindowLayout.Resolve(false, false, false, canvasW, canvasH, OskTop);
        Assert.Equal(384f, r.X, Eps);
        Assert.Equal(64f, r.Y, Eps);
        Assert.Equal(768f, r.W, Eps);
        Assert.Equal(520f, r.H, Eps);
        Assert.True(r.Right <= ChatWindowLayout.RightColumnX, "clear of the right column");
    }

    [Fact]
    public void VegaSpeaking_WindowSlidesRightOfThePanel_AndStopsBeforeTheRightColumn()
    {
        var r = ChatWindowLayout.Resolve(vegaSpeech: true, osk: false, touchKeyboard: false, 1536f, 864f, OskTop);
        Assert.Equal(ChatWindowLayout.VegaClearX, r.X, Eps);
        Assert.Equal(1310f, r.Right, Eps);
        Assert.Equal(64f, r.Y, Eps);
        Assert.Equal(520f, r.H, Eps);
        Assert.False(r.Overlaps(ChatWindowLayout.VegaSpeech));
    }

    [Fact]
    public void VegaSpeaking_AtABigUiScale_TheWindowRisesAboveThePanelInstead()
    {
        // UI scale 160 %: the canvas is 960 × 540 HUD units, so a window right of VEGA would be a 264-wide strip.
        var r = ChatWindowLayout.Resolve(vegaSpeech: true, osk: false, touchKeyboard: false, 960f, 540f, OskTop);
        Assert.Equal(96f, r.X, Eps);
        Assert.Equal(768f, r.W, Eps);
        Assert.Equal(ChatWindowLayout.VegaSpeechY - ChatWindowLayout.Gap, r.Bottom, Eps);
        Assert.False(r.Overlaps(ChatWindowLayout.VegaSpeech));
    }

    [Fact]
    public void OnScreenKeyboard_DocksTheWindowToTheTop_AboveTheKeyboard()
    {
        var r = ChatWindowLayout.Resolve(vegaSpeech: false, osk: true, touchKeyboard: false, 1536f, 864f, OskTop);
        Assert.Equal(ChatWindowLayout.DockTop, r.Y, Eps);
        Assert.Equal(254f * 0.8f - ChatWindowLayout.Gap, r.Bottom, Eps); // one keyboard unit is 0.8 HUD units at 16:9
        Assert.Equal(384f, r.X, Eps);
        Assert.True(r.H > 150f, "room for a title and several lines");
    }

    [Theory]
    [MemberData(nameof(Aspects))]
    public void OnScreenKeyboardTop_ConvertsBetweenTheTwoExpandCanvases(float canvasW, float canvasH)
    {
        // At UI scale 1 the HUD reference is 0.8 of the keyboard's on every aspect: both expand to the same screen.
        Assert.Equal(254f * 0.8f, ChatWindowLayout.OskTopInHud(254f, canvasW, canvasH), Eps);

        // A bigger UI scale shrinks the HUD canvas, so the same keyboard edge is fewer HUD units down.
        Assert.Equal(254f * 0.8f / 1.25f, ChatWindowLayout.OskTopInHud(254f, canvasW / 1.25f, canvasH / 1.25f), Eps);
    }

    [Fact]
    public void TouchKeyboard_KeepsTheWindowInTheUpperHalf()
    {
        var r = ChatWindowLayout.Resolve(vegaSpeech: false, osk: false, touchKeyboard: true, 1536f, 864f, OskTop);
        Assert.Equal(864f / 2f - ChatWindowLayout.Gap, r.Bottom, Eps);
        Assert.Equal(ChatWindowLayout.DefaultTop, r.Y, Eps);
        Assert.Equal(768f, r.W, Eps);
    }

    [Fact]
    public void TouchKeyboard_AndVega_TheUpperHalfWindowStillSlidesRight()
    {
        // The upper-half window ends at y 424, inside the speech panel's band (396…586), so it still yields to VEGA.
        var r = ChatWindowLayout.Resolve(vegaSpeech: true, osk: false, touchKeyboard: true, 1536f, 864f, OskTop);
        Assert.Equal(ChatWindowLayout.VegaClearX, r.X, Eps);
        Assert.False(r.Overlaps(ChatWindowLayout.VegaSpeech));
        Assert.True(r.Bottom <= 864f / 2f - ChatWindowLayout.Gap + Eps);
    }

    [Fact]
    public void OnScreenKeyboard_AndVega_TheDockedWindowIsAlreadyClear()
    {
        var docked = ChatWindowLayout.Resolve(vegaSpeech: true, osk: true, touchKeyboard: false, 1536f, 864f, OskTop);
        Assert.Equal(384f, docked.X, Eps); // ends far above the speech panel, so it stays centred
        Assert.False(docked.Overlaps(ChatWindowLayout.VegaSpeech));
    }

    [Theory]
    [MemberData(nameof(AspectsAndScales))]
    public void EveryCombination_StaysOnTheCanvas_AndClearOfItsNeighbours(float canvasW, float canvasH)
    {
        foreach (bool vega in new[] { false, true })
        {
            foreach (bool osk in new[] { false, true })
            {
                foreach (bool touch in new[] { false, true })
                {
                    var r = ChatWindowLayout.Resolve(vega, osk, touch, canvasW, canvasH, OskTop);
                    string tag = $"({canvasW}×{canvasH}, vega {vega}, osk {osk}, touch {touch})";
                    Assert.True(r.W > 0f && r.H > 0f, "a window to type into " + tag);
                    Assert.True(r.X >= 0f && r.Y >= 0f, "inside the canvas " + tag);
                    Assert.True(r.Right <= canvasW - ChatWindowLayout.EdgeMargin + Eps, "clear of the right edge " + tag);
                    Assert.True(r.Bottom <= canvasH - ChatWindowLayout.EdgeMargin + Eps, "clear of the bottom edge " + tag);
                    Assert.True(r.Bottom <= ChatWindowLayout.MaxBottom + Eps, "above VEGA's objective chip " + tag);
                    Assert.True(r.Right <= ChatWindowLayout.RightColumnX, "clear of the HUD's right column " + tag);
                    if (vega)
                    {
                        Assert.False(r.Overlaps(ChatWindowLayout.VegaSpeech), "never over VEGA's speech " + tag);
                    }

                    if (osk)
                    {
                        Assert.True(r.Bottom <= ChatWindowLayout.OskTopInHud(OskTop, canvasW, canvasH) + Eps, "above the keyboard " + tag);
                    }

                    if (touch)
                    {
                        Assert.True(r.Bottom <= canvasH / 2f + Eps, "in the upper half " + tag);
                    }
                }
            }
        }
    }

    [Fact]
    public void Overlaps_IgnoresTouchingEdges()
    {
        var a = new ChatRect(0f, 0f, 10f, 10f);
        Assert.True(a.Overlaps(new ChatRect(9f, 9f, 5f, 5f)));
        Assert.False(a.Overlaps(new ChatRect(10f, 0f, 5f, 5f)));
        Assert.False(a.Overlaps(new ChatRect(0f, 10f, 5f, 5f)));
    }
}
