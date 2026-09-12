// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using NUnit.Framework;
using static BlocksBeyondTheStars.Client.LoadingHandoffPolicy;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The loading screen's hand-off: the progress-bar screen used to launch the rig on a 2.5 s timer while
    /// the bundled server still needed 10–20 s to generate a fresh world, leaving the player on a nameless
    /// "Loading world…" curtain for the whole boot (#1800). The bar screen now holds until the server reports
    /// ready, with a ceiling, and the bar creeps instead of parking at 100 %.
    /// </summary>
    public sealed class LoadingHandoffPolicyEditModeTests
    {
        private const float MinShow = 2.5f;

        [Test]
        public void HoldsForMinShowThenLaunchesWhenNothingBoots()
        {
            Assert.That(Next(2.4f, MinShow, false, false), Is.EqualTo(Verdict.Hold));
            Assert.That(Next(2.5f, MinShow, false, false), Is.EqualTo(Verdict.Launch));
        }

        [Test]
        public void HoldsWhileTheBundledServerBoots()
        {
            // The 2026-09-12 case: the server needed 20 s; the old timer had launched at 2.5 s.
            Assert.That(Next(2.5f, MinShow, false, true), Is.EqualTo(Verdict.Hold));
            Assert.That(Next(20f, MinShow, false, true), Is.EqualTo(Verdict.Hold));
            Assert.That(Next(20.2f, MinShow, false, false), Is.EqualTo(Verdict.Launch), "ready: launch right away");
        }

        [Test]
        public void HoldsWhileTheBrowserHostBootsWithoutACeiling()
        {
            Assert.That(Next(5f, MinShow, true, false), Is.EqualTo(Verdict.Hold));
            Assert.That(Next(LocalBootCeilingSeconds + 30f, MinShow, true, false), Is.EqualTo(Verdict.Hold), "the #771 gate is unchanged");
        }

        [Test]
        public void GivesUpOnTheServerOnlyAtTheCeiling()
        {
            Assert.That(Next(LocalBootCeilingSeconds - 0.5f, MinShow, false, true), Is.EqualTo(Verdict.Hold));
            Assert.That(Next(LocalBootCeilingSeconds, MinShow, false, true), Is.EqualTo(Verdict.GiveUp));
        }

        [Test]
        public void MinShowStillAppliesWhenAServerBoots()
        {
            Assert.That(Next(1f, MinShow, false, true), Is.EqualTo(Verdict.Hold));
        }

        [Test]
        public void Progress_IsThePlainRampWhenNothingHolds()
        {
            Assert.That(Progress(0f, MinShow, false), Is.EqualTo(0f));
            Assert.That(Progress(1.25f, MinShow, false), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(Progress(10f, MinShow, false), Is.EqualTo(1f));
            Assert.That(Progress(3f, 0f, false), Is.EqualTo(1f), "a zero MinShow counts as done");
        }

        [Test]
        public void Progress_CreepsMonotonicallyBelowTheCapWhileTheServerBoots()
        {
            float last = -1f;
            for (float t = 0f; t <= 60f; t += 0.5f)
            {
                float p = Progress(t, MinShow, true);
                Assert.That(p, Is.GreaterThanOrEqualTo(last), $"t={t}");
                Assert.That(p, Is.LessThan(HoldCap), $"t={t}");
                last = p;
            }

            Assert.That(Progress(20f, MinShow, true), Is.GreaterThan(Progress(2.5f, MinShow, true)), "still visibly moving");
        }

        [Test]
        public void Progress_SnapsToFullTheMomentTheServerIsReady()
        {
            Assert.That(Progress(20f, MinShow, true), Is.LessThan(HoldCap));
            Assert.That(Progress(20f, MinShow, false), Is.EqualTo(1f));
        }
    }
}
