// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The HUD's right-hand column (#3217–#3219, Lyxette on v2026.11.5): the compass's caption rows end above the clock panel
    /// (the third row used to show through the clock line), the playtime panel stays under the clock panel with and without
    /// its chip row, and the fixed readout lines shrink to fit their rects instead of running out of their panels
    /// ("Gesamt 93:59:1…" cut by the screen edge, "…Sturm 1" off-screen).
    /// </summary>
    public sealed class HudRightColumnEditModeTests
    {
        private const float ClockLineW = 184f;
        private GameObject _canvas;

        [SetUp]
        public void SetUp() => _canvas = new GameObject("HudColumnTestCanvas", typeof(RectTransform), typeof(Canvas));

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_canvas);

        [Test]
        public void CompassCaptions_EndAboveTheClockPanel()
        {
            float lastRowBottom = HudRightColumn.CompassY + HudRightColumn.CaptionY(HudRightColumn.CaptionRows - 1) + HudRightColumn.CaptionRowH;
            Assert.That(lastRowBottom, Is.LessThanOrEqualTo(HudRightColumn.ClockY),
                "the third caption row (Guardian core / VEGA's goal / fragment signal) must not reach the clock panel");
            Assert.That(HudRightColumn.CompassY + HudRightColumn.CompassSize, Is.LessThanOrEqualTo(HudRightColumn.ClockY));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlaytimePanel_StaysBelowTheClockPanel(bool chipRow)
        {
            Assert.That(HudRightColumn.PlaytimeY(chipRow),
                Is.GreaterThanOrEqualTo(HudRightColumn.ClockY + HudRightColumn.ClockHeight(chipRow)),
                "the playtime panel follows the clock panel down when the chip row opens");
        }

        [Test]
        public void ChipRow_ShrinksALongChipToFitItsRect()
        {
            // Too wide for 184 px at 14 pt bold (the old clock line overflowed at about 26 characters), narrow enough for 10 pt.
            var t = Line(ClockLineW, HudRightColumn.ClockChipRowH, FontStyle.Bold);
            t.text = "Sandsturm  Erntefest der Siedler";
            t.ForceMeshUpdate();

            Assert.That(t.fontSize, Is.LessThan(14f), "the line shrank");
            Assert.That(t.textBounds.size.x, Is.LessThanOrEqualTo(ClockLineW + 0.5f), "and now fits its rect");
        }

        [Test]
        public void ClockLine_KeepsItsSizeWhenItFits()
        {
            var t = Line(ClockLineW, 18f, FontStyle.Bold);
            t.text = "NACHT  12:34  -35°C  1,5 g";
            t.ForceMeshUpdate();

            Assert.That(t.fontSize, Is.EqualTo(14f), "the readings alone fit at full size");
            Assert.That(t.textBounds.size.x, Is.LessThanOrEqualTo(ClockLineW + 0.5f));
        }

        [Test]
        public void Playtime_TwoLinesFitThePanel()
        {
            float h = HudRightColumn.PlaytimeH - 8f;
            var t = Line(ClockLineW, h, FontStyle.Normal);
            t.text = "Sitzung  12:34:56\nGesamt  1234:56:07";
            t.ForceMeshUpdate();

            Assert.That(t.textInfo.lineCount, Is.EqualTo(2));
            Assert.That(t.textBounds.size.x, Is.LessThanOrEqualTo(ClockLineW + 0.5f));
            Assert.That(t.textBounds.size.y, Is.LessThanOrEqualTo(h + 0.5f));
        }

        private TMP_Text Line(float w, float h, FontStyle style)
        {
            var t = UiText.Add(_canvas.transform, 0, 0, w, h, string.Empty, 14, Color.white, TextAnchor.MiddleLeft, style);
            UiText.Fit(t);
            return t;
        }
    }
}
