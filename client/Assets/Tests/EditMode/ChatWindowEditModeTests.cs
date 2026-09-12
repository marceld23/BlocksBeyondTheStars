// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The chat's holo window (#1799) hugs its lines: it ends where the lane ends, grows upward by the
    /// measured text block plus padding, takes the input row in as its bottom row while typing, never rises
    /// above the lane top (the toast sits there), and is gone — height 0 — when there is nothing to frame.
    /// These pin <see cref="ChatUi.ResolveWindow"/> against the lane geometry of <see cref="ChatUi.ResolveLane"/>.
    /// </summary>
    public sealed class ChatWindowEditModeTests
    {
        private const float LaneTop = 280f, LaneBottom = 590f, InputH = 44f, Pad = 10f, Gap = 6f;

        private static float Bottom(ChatUi.ChatWindow w) => w.Y + w.H;

        [Test]
        public void NoLines_NotTyping_NoWindow()
        {
            var w = ChatUi.ResolveWindow(typing: false, speechVisible: false, chipVisible: false, contentH: 0f);
            Assert.That(w.H, Is.EqualTo(0f));
            Assert.That(w.TextH, Is.EqualTo(0f));
        }

        [Test]
        public void SomeLines_WindowHugsTheBlockAtTheLaneBottom()
        {
            var w = ChatUi.ResolveWindow(typing: false, speechVisible: false, chipVisible: false, contentH: 66f);
            Assert.That(w.TextH, Is.EqualTo(66f));
            Assert.That(w.H, Is.EqualTo(66f + 2f * Pad), "text block plus padding, nothing else");
            Assert.That(Bottom(w), Is.EqualTo(LaneBottom), "anchored at the lane bottom");
            Assert.That(w.Y, Is.GreaterThan(LaneTop), "a short block leaves the top of the lane free");
        }

        [Test]
        public void Typing_InputRowIsTheWindowsBottomRow()
        {
            var w = ChatUi.ResolveWindow(typing: true, speechVisible: false, chipVisible: false, contentH: 66f);
            Assert.That(Bottom(w), Is.EqualTo(596f + InputH), "window ends under the input row");
            Assert.That(w.H, Is.EqualTo(66f + 2f * Pad + InputH));
            Assert.That(w.InputY, Is.EqualTo(66f + 2f * Pad), "input row sits directly under the text block");
            Assert.That(w.InputY + InputH, Is.EqualTo(w.H), "…and is flush with the window's bottom edge");
        }

        [Test]
        public void Typing_NoLines_CompactInputFrame()
        {
            var w = ChatUi.ResolveWindow(typing: true, speechVisible: false, chipVisible: false, contentH: 0f);
            Assert.That(w.H, Is.EqualTo(InputH), "just the input row, no empty text block above it");
            Assert.That(w.TextH, Is.EqualTo(0f));
            Assert.That(w.InputY, Is.EqualTo(0f));
        }

        [Test]
        public void TallBlock_IsCappedSoTheWindowNeverRisesAboveTheLaneTop()
        {
            var w = ChatUi.ResolveWindow(typing: false, speechVisible: false, chipVisible: false, contentH: 5000f);
            Assert.That(w.Y, Is.EqualTo(LaneTop));
            Assert.That(w.TextH, Is.EqualTo(w.Capacity));
            Assert.That(w.Capacity, Is.EqualTo(LaneBottom - LaneTop - 2f * Pad));

            var typing = ChatUi.ResolveWindow(typing: true, speechVisible: false, chipVisible: false, contentH: 5000f);
            Assert.That(typing.Y, Is.EqualTo(LaneTop));
            Assert.That(typing.Capacity, Is.LessThan(w.Capacity), "the input row costs text rows, not lane");
        }

        [Test]
        public void SpeechUp_WindowEndsAboveVega()
        {
            var w = ChatUi.ResolveWindow(typing: false, speechVisible: true, chipVisible: false, contentH: 5000f);
            Assert.That(Bottom(w), Is.EqualTo(VegaPanel.SpeechY - Gap));
            Assert.That(w.Y, Is.EqualTo(LaneTop));
            Assert.That(w.Capacity, Is.GreaterThan(40f), "room for a couple of rows above the speech panel");

            var typing = ChatUi.ResolveWindow(typing: true, speechVisible: true, chipVisible: true, contentH: 5000f);
            Assert.That(Bottom(typing), Is.LessThanOrEqualTo(VegaPanel.SpeechY - Gap), "input row clear of the speech panel");
            Assert.That(typing.InputY + InputH, Is.EqualTo(typing.H));
        }

        [Test]
        public void EveryCombination_StaysInsideTheColumn()
        {
            foreach (bool typing in new[] { false, true })
            {
                foreach (bool speech in new[] { false, true })
                {
                    foreach (bool chip in new[] { false, true })
                    {
                        foreach (float content in new[] { 0f, 22f, 200f, 5000f })
                        {
                            var w = ChatUi.ResolveWindow(typing, speech, chip, content);
                            string tag = $"({typing},{speech},{chip},{content})";
                            Assert.That(w.H, Is.GreaterThanOrEqualTo(0f), tag);
                            Assert.That(w.TextH, Is.LessThanOrEqualTo(w.Capacity), tag);
                            if (w.H > 0f)
                            {
                                Assert.That(w.Y, Is.GreaterThanOrEqualTo(LaneTop), "never over the toast " + tag);
                                Assert.That(Bottom(w), Is.LessThanOrEqualTo(596f + InputH), "never over the scan panel " + tag);
                            }

                            if (speech)
                            {
                                Assert.That(Bottom(w), Is.LessThanOrEqualTo(VegaPanel.SpeechY - Gap), "clear of VEGA " + tag);
                            }

                            if (chip && typing)
                            {
                                Assert.That(Bottom(w), Is.LessThanOrEqualTo(VegaPanel.ChipY), "input clear of the chip " + tag);
                            }
                        }
                    }
                }
            }
        }
    }
}
