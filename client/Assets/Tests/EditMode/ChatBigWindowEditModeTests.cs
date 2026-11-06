// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// #3264: the opened chat is a big centred window that never covers its neighbours. The headless suite pins every
    /// rule of <see cref="ChatWindowLayout"/>; these pin it against the live client constants it mirrors — VEGA's panel,
    /// the HUD frame, the on-screen keyboard's real panel — through <see cref="ChatUi.ResolveBigWindow"/>, at 16:9,
    /// 16:10, 21:9 and 4:3 and every UI-scale step.
    /// </summary>
    public sealed class ChatBigWindowEditModeTests
    {
        private const float Eps = 0.01f;

        // The chat canvas in HUD units at UI scale 1 — (1536 × 864) expanded to the screen's aspect.
        private static readonly float[][] Aspects =
        {
            new[] { 1536f, 864f },  // 16:9
            new[] { 1536f, 960f },  // 16:10
            new[] { 2048f, 864f },  // 21:9
            new[] { 1536f, 1152f }, // 4:3
        };

        private static readonly ChatRect VegaSpeech = new ChatRect(24f, VegaPanel.SpeechY, 640f, VegaPanel.SpeechH);

        [Test]
        public void LayoutConstants_MatchTheClientsOwn()
        {
            Assert.That(ChatWindowLayout.VegaSpeechY, Is.EqualTo(VegaPanel.SpeechY));
            Assert.That(ChatWindowLayout.VegaSpeechH, Is.EqualTo(VegaPanel.SpeechH));
            Assert.That(ChatWindowLayout.MaxBottom, Is.LessThan(VegaPanel.ChipY), "the window ends above VEGA's objective chip");
            Assert.That(ChatWindowLayout.HudRefW, Is.EqualTo(UiKit.HudRefW));
            Assert.That(ChatWindowLayout.HudRefH, Is.EqualTo(UiKit.HudRefH));
        }

        [Test]
        public void OnScreenKeyboard_PanelTopIsItsCentredTallestPage()
        {
            // Four key rows plus the command row: 150 + 5 × (72 + 8) + 22 = 572 high, centred in 1080.
            Assert.That(OnScreenKeyboardUi.PanelTop, Is.EqualTo(254f).Within(Eps));
        }

        [Test]
        public void Default_IsCentredInTheHudFrame_AtEveryAspect()
        {
            foreach (var a in Aspects)
            {
                var r = ChatUi.ResolveBigWindow(false, false, false, a[0], a[1]);
                string tag = $"{a[0]}×{a[1]}";
                Assert.That(r.X, Is.EqualTo(384f).Within(Eps), tag);
                Assert.That(r.Y, Is.EqualTo(64f).Within(Eps), tag);
                Assert.That(r.W, Is.EqualTo(768f).Within(Eps), tag);
                Assert.That(r.H, Is.EqualTo(520f).Within(Eps), tag);
            }
        }

        [Test]
        public void VegaSpeaking_NeverDrawnOver_AtEveryAspectAndScale()
        {
            foreach (var a in Aspects)
            {
                for (int step = 8; step <= 16; step++)
                {
                    float scale = step / 10f;
                    var r = ChatUi.ResolveBigWindow(true, false, false, a[0] / scale, a[1] / scale);
                    string tag = $"{a[0]}×{a[1]} @ {scale}";
                    Assert.That(r.Overlaps(VegaSpeech), Is.False, tag);
                    Assert.That(r.W, Is.GreaterThan(0f), tag);
                    Assert.That(r.H, Is.GreaterThan(0f), tag);
                    if (scale <= 1f)
                    {
                        Assert.That(r.X, Is.GreaterThanOrEqualTo(680f), "slides right of the panel " + tag);
                        Assert.That(r.H, Is.EqualTo(520f).Within(Eps), "keeps its full height " + tag);
                    }
                }
            }
        }

        [Test]
        public void OnScreenKeyboard_WindowDocksAboveIt_AtEveryAspectAndScale()
        {
            foreach (var a in Aspects)
            {
                for (int step = 8; step <= 16; step++)
                {
                    float scale = step / 10f;
                    float w = a[0] / scale, h = a[1] / scale;
                    var r = ChatUi.ResolveBigWindow(false, true, false, w, h);
                    float oskTop = ChatWindowLayout.OskTopInHud(OnScreenKeyboardUi.PanelTop, w, h);
                    string tag = $"{a[0]}×{a[1]} @ {scale}";
                    Assert.That(r.Y, Is.EqualTo(ChatWindowLayout.DockTop).Within(Eps), tag);
                    Assert.That(r.Bottom, Is.LessThanOrEqualTo(oskTop), "above the keyboard " + tag);
                    Assert.That(r.H, Is.GreaterThan(80f), "room for a few lines " + tag);
                }
            }
        }

        [Test]
        public void TouchKeyboard_KeepsTheWindowInTheUpperHalf()
        {
            foreach (var a in Aspects)
            {
                var r = ChatUi.ResolveBigWindow(false, false, true, a[0], a[1]);
                Assert.That(r.Bottom, Is.LessThanOrEqualTo(a[1] / 2f), $"{a[0]}×{a[1]}");
                Assert.That(r.H, Is.GreaterThan(200f), $"{a[0]}×{a[1]}");
            }
        }

        [Test]
        public void EveryCombination_StaysOnTheCanvasAndAboveTheChip()
        {
            foreach (var a in Aspects)
            {
                for (int step = 8; step <= 16; step++)
                {
                    float scale = step / 10f;
                    float w = a[0] / scale, h = a[1] / scale;
                    for (int flags = 0; flags < 8; flags++)
                    {
                        var r = ChatUi.ResolveBigWindow((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, w, h);
                        string tag = $"{a[0]}×{a[1]} @ {scale}, flags {flags}";
                        Assert.That(r.X, Is.GreaterThanOrEqualTo(0f), tag);
                        Assert.That(r.Right, Is.LessThanOrEqualTo(w), tag);
                        Assert.That(r.Bottom, Is.LessThanOrEqualTo(h), tag);
                        Assert.That(r.Bottom, Is.LessThan(VegaPanel.ChipY), tag);
                    }
                }
            }
        }
    }
}
