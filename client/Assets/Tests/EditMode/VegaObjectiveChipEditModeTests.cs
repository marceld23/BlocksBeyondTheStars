// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The objective chip's text rules (#1859): the story objective names the fragment's world through a
    /// {0} argument, a bad translation must never blank the chip, the counter is never the part that gets
    /// truncated, and the chip grows a row only for a three-line objective.
    /// </summary>
    public sealed class VegaObjectiveChipEditModeTests
    {
        [Test]
        public void FormatSubstitutesTheArgument()
        {
            Assert.That(VegaObjectiveChip.Format("Story: a net fragment lies on {0} — follow VEGA’s signal", "Kepler-3 b"),
                Is.EqualTo("Story: a net fragment lies on Kepler-3 b — follow VEGA’s signal"));
        }

        [Test]
        public void FormatLeavesTheTemplateAlone_WithoutAnArgument()
        {
            Assert.That(VegaObjectiveChip.Format("Story: search ruins", string.Empty), Is.EqualTo("Story: search ruins"));
            Assert.That(VegaObjectiveChip.Format("Story: search ruins", null), Is.EqualTo("Story: search ruins"));
            Assert.That(VegaObjectiveChip.Format(null, "x"), Is.EqualTo(string.Empty));
        }

        [Test]
        public void FormatFallsBackOnABrokenTemplate()
        {
            // A stray brace in a translation must show the line rather than throw inside Refresh.
            Assert.That(VegaObjectiveChip.Format("Fragment on {0 — go", "Vega"), Is.EqualTo("Fragment on {0 — go"));
            Assert.That(VegaObjectiveChip.Format("Fragment on {1}", "Vega"), Is.EqualTo("Fragment on {1}"));
        }

        [Test]
        public void CounterRidesInlineOnAShortLine_AndGetsItsOwnLineOnALongOne()
        {
            Assert.That(VegaObjectiveChip.Compose("Objective: mine 3 blocks", "(1/3)", ownLine: false), Is.EqualTo("Objective: mine 3 blocks  (1/3)"));
            Assert.That(VegaObjectiveChip.Compose("Objective: a long story line", "(128/204)", ownLine: true), Is.EqualTo("Objective: a long story line\n(128/204)"));
            Assert.That(VegaObjectiveChip.Compose("Objective: no counter", string.Empty, ownLine: true), Is.EqualTo("Objective: no counter"));
        }

        [Test]
        public void ChipStaysOneRowForTwoSmallLines_AndGrowsForThree()
        {
            // Two 17 pt lines wrap to ~41 px — inside the 48 px chip; a third line (~61 px) needs the tall row.
            Assert.That(VegaObjectiveChip.Height(0f), Is.EqualTo(VegaObjectiveChip.HeightNormal));
            Assert.That(VegaObjectiveChip.Height(41f), Is.EqualTo(48f));
            Assert.That(VegaObjectiveChip.Height(48f), Is.EqualTo(48f));
            Assert.That(VegaObjectiveChip.Height(61f), Is.EqualTo(VegaObjectiveChip.HeightTall));
            Assert.That(VegaObjectiveChip.HeightTall, Is.EqualTo(72f));
        }

        [Test]
        public void FontsAreTheChipsTwoSizes()
        {
            Assert.That(VegaObjectiveChip.FontLarge, Is.EqualTo(20f));
            Assert.That(VegaObjectiveChip.FontSmall, Is.EqualTo(17f));
            Assert.That(VegaObjectiveChip.TextWidth, Is.EqualTo(614f), "the 640 chip minus its 14 px inset on the left and 12 on the right");
        }
    }
}
