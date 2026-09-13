// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The HUD status toast's lifetime (#1860): it used to stay until the next server line replaced it, so
    /// "Life support lost — watch your oxygen!" sat over a player breathing station air for the rest of the
    /// session. The pure policy decides the hold (warnings longer), the fade and the expiry.
    /// </summary>
    public sealed class HudToastPolicyEditModeTests
    {
        [Test]
        public void OrdinaryMessagesHoldEightSeconds_WarningsFifteen()
        {
            Assert.That(HudToastPolicy.HoldSeconds("Mode: Survival · PvP: False"), Is.EqualTo(HudToastPolicy.HoldNormalSeconds));
            Assert.That(HudToastPolicy.HoldSeconds("Crafted: Iron plate"), Is.EqualTo(8f));
            // The resolved warning lines of ui.base.air_lost / ui.station.air_lost / ui.station.air_too_large
            // (EN + DE) and the base's ui.base.air_left.
            Assert.That(HudToastPolicy.HoldSeconds("Warning: the base is no longer airtight — air only reaches the core zone!"), Is.EqualTo(15f));
            Assert.That(HudToastPolicy.HoldSeconds("Warnung: Die Station ist nicht mehr luftdicht — Helm zu!"), Is.EqualTo(15f));
            Assert.That(HudToastPolicy.HoldSeconds("Life support lost — watch your oxygen!"), Is.EqualTo(15f));
            Assert.That(HudToastPolicy.HoldSeconds("Lebenserhaltung verloren — achte auf deinen Sauerstoff!"), Is.EqualTo(15f));
            Assert.That(HudToastPolicy.HoldSeconds("  ⚠ Hull breach"), Is.EqualTo(15f), "leading whitespace and the glyph prefix count");
        }

        [Test]
        public void WarningDetectionIsAPrefixMatch_NotASubstringOne()
        {
            Assert.That(HudToastPolicy.IsWarning("Warning: reactor"), Is.True);
            Assert.That(HudToastPolicy.IsWarning("No warning here"), Is.False, "'warning' mid-sentence is not a warning line");
            Assert.That(HudToastPolicy.IsWarning(string.Empty), Is.False);
            Assert.That(HudToastPolicy.IsWarning(null), Is.False);
        }

        [Test]
        public void AlphaHoldsAtOne_ThenFadesLinearlyToZero()
        {
            const float hold = 8f;
            Assert.That(HudToastPolicy.Alpha(0f, hold), Is.EqualTo(1f));
            Assert.That(HudToastPolicy.Alpha(7.99f, hold), Is.EqualTo(1f));
            Assert.That(HudToastPolicy.Alpha(hold, hold), Is.EqualTo(1f), "the fade starts AFTER the hold");
            Assert.That(HudToastPolicy.Alpha(hold + (HudToastPolicy.FadeSeconds / 2f), hold), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(HudToastPolicy.Alpha(hold + HudToastPolicy.FadeSeconds, hold), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(HudToastPolicy.Alpha(hold + 5f, hold), Is.EqualTo(0f), "never below zero once faded");
        }

        [Test]
        public void ExpiresOnceHoldAndFadeHaveRun()
        {
            const float hold = 15f;
            Assert.That(HudToastPolicy.Expired(14.9f, hold), Is.False);
            Assert.That(HudToastPolicy.Expired(hold, hold), Is.False, "still fading");
            Assert.That(HudToastPolicy.Expired(hold + HudToastPolicy.FadeSeconds - 0.01f, hold), Is.False);
            Assert.That(HudToastPolicy.Expired(hold + HudToastPolicy.FadeSeconds, hold), Is.True);
        }

        [Test]
        public void FadeIsHalfASecond()
        {
            // The ScreenshotDirector outwaits one HUD refresh interval (0.1 s) after blanking the message; the
            // natural fade must be short as well so a shot never catches a ghost of an old line.
            Assert.That(HudToastPolicy.FadeSeconds, Is.EqualTo(0.5f));
        }
    }
}
