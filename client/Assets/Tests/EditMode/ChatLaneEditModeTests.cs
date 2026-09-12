// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The chat overlay shares the HUD's left column with VEGA (speech panel y 396…586, objective chip
    /// y 594…642, HUD reference 1536×864). Before the lane arbitration the chat's scrollback (y 280…590)
    /// drew straight across a story line and its input row (y 596…640) sat exactly on the chip. These pin
    /// the geometry <see cref="ChatUi.ResolveLane"/> hands out for every VEGA / typing combination.
    /// </summary>
    public sealed class ChatLaneEditModeTests
    {
        private const float LaneTop = 280f, LaneBottom = 590f, InputH = 44f, Gap = 6f;

        private static float Bottom(ChatUi.ChatLane lane) => lane.LogY + lane.LogH;

        [Test]
        public void VegaQuiet_KeepsTheOriginalLane()
        {
            var lane = ChatUi.ResolveLane(typing: false, speechVisible: false, chipVisible: false);
            Assert.That(lane.LogY, Is.EqualTo(LaneTop));
            Assert.That(Bottom(lane), Is.EqualTo(LaneBottom));
            Assert.That(lane.InputY, Is.EqualTo(596f));

            var typing = ChatUi.ResolveLane(typing: true, speechVisible: false, chipVisible: false);
            Assert.That(typing.LogH, Is.EqualTo(lane.LogH), "typing alone never shortens the scrollback");
            Assert.That(typing.InputY, Is.EqualTo(596f));
        }

        [Test]
        public void SpeechUp_ScrollbackEndsAboveThePanel()
        {
            var lane = ChatUi.ResolveLane(typing: false, speechVisible: true, chipVisible: false);
            Assert.That(lane.LogY, Is.EqualTo(LaneTop));
            Assert.That(Bottom(lane), Is.LessThan(VegaPanel.SpeechY));
            Assert.That(Bottom(lane), Is.EqualTo(VegaPanel.SpeechY - Gap));
            Assert.That(lane.LogH, Is.GreaterThan(60f), "room for a few lines above the speech panel");
        }

        [Test]
        public void ChipOnly_NotTyping_ScrollbackUnchanged()
        {
            // The chip (y 594+) never overlapped the scrollback — only the input row — so the log keeps its lane.
            var lane = ChatUi.ResolveLane(typing: false, speechVisible: false, chipVisible: true);
            Assert.That(Bottom(lane), Is.EqualTo(LaneBottom));
            Assert.That(Bottom(lane), Is.LessThan(VegaPanel.ChipY));
        }

        [Test]
        public void ChipUp_Typing_InputRowStacksUnderTheScrollback()
        {
            var lane = ChatUi.ResolveLane(typing: true, speechVisible: false, chipVisible: true);
            Assert.That(lane.InputY + InputH, Is.LessThanOrEqualTo(VegaPanel.ChipY), "input row clear of the chip");
            Assert.That(Bottom(lane) + Gap, Is.EqualTo(lane.InputY), "input row sits directly under the log");
            Assert.That(lane.LogH, Is.GreaterThan(200f), "the log still shows most of its rows");
        }

        [Test]
        public void SpeechAndChipUp_Typing_EverythingStaysAboveVega()
        {
            var lane = ChatUi.ResolveLane(typing: true, speechVisible: true, chipVisible: true);
            Assert.That(lane.InputY + InputH, Is.LessThanOrEqualTo(VegaPanel.SpeechY - Gap));
            Assert.That(Bottom(lane) + Gap, Is.EqualTo(lane.InputY));
            Assert.That(lane.LogY, Is.EqualTo(LaneTop));
            Assert.That(lane.LogH, Is.GreaterThan(0f));
        }

        [Test]
        public void EveryCombination_StaysInsideTheColumnAndNeverGoesNegative()
        {
            foreach (bool typing in new[] { false, true })
            {
                foreach (bool speech in new[] { false, true })
                {
                    foreach (bool chip in new[] { false, true })
                    {
                        var lane = ChatUi.ResolveLane(typing, speech, chip);
                        Assert.That(lane.LogY, Is.EqualTo(LaneTop), $"top fixed ({typing},{speech},{chip})");
                        Assert.That(lane.LogH, Is.GreaterThanOrEqualTo(0f));
                        Assert.That(Bottom(lane), Is.LessThanOrEqualTo(LaneBottom));
                        Assert.That(lane.InputY, Is.LessThanOrEqualTo(596f));
                        if (speech)
                        {
                            Assert.That(Bottom(lane), Is.LessThan(VegaPanel.SpeechY), $"log clear of speech ({typing},{chip})");
                            if (typing)
                            {
                                Assert.That(lane.InputY + InputH, Is.LessThan(VegaPanel.SpeechY), $"input clear of speech ({chip})");
                            }
                        }

                        if (chip && typing)
                        {
                            Assert.That(lane.InputY + InputH, Is.LessThanOrEqualTo(VegaPanel.ChipY), $"input clear of chip ({speech})");
                        }
                    }
                }
            }
        }
    }
}
