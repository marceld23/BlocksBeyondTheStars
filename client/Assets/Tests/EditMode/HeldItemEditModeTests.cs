// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Pins the empty-hotbar-slot hand from issue #1033: selecting an empty slot must resolve to
    /// <see cref="HeldItem.Kind.Hand"/> (so the first-person viewmodel shows the gloved hand instead
    /// of nothing), tinted with the player's suit arm colour when the resolver is wired.
    /// </summary>
    public sealed class HeldItemEditModeTests
    {
        [TearDown]
        public void TearDown()
        {
            HeldItem.HandTintResolver = null;
            HeldItem.HandPaintResolver = null;
        }

        [Test]
        public void EmptySlotResolvesToHand()
        {
            Assert.That(HeldItem.For(null, null).kind, Is.EqualTo(HeldItem.Kind.Hand));
            Assert.That(HeldItem.For(null, "").kind, Is.EqualTo(HeldItem.Kind.Hand));
        }

        [Test]
        public void HandUsesResolvedArmColour()
        {
            var arm = new Color(0.9f, 0.1f, 0.2f);
            HeldItem.HandTintResolver = () => arm;
            Assert.That(HeldItem.For(null, "").tint, Is.EqualTo(arm));
        }

        [Test]
        public void HandFallsBackToDefaultGloveWithoutResolver()
        {
            HeldItem.HandTintResolver = null;
            Assert.That(HeldItem.For(null, "").tint, Is.EqualTo(new Color(0.20f, 0.45f, 0.80f)));
        }

        [Test]
        public void NonEmptyKeyWithoutContentStaysNone()
        {
            Assert.That(HeldItem.For(null, "iron_drill").kind, Is.EqualTo(HeldItem.Kind.None));
        }

        [Test]
        public void PaintResolverDoesNotChangeTheResolvedTint()
        {
            // The painting is applied at build time on top of the base tint (#1427) — For() keeps
            // resolving the flat arm colour so the cuff shade and the unpainted fallback stay stable.
            var arm = new Color(0.9f, 0.1f, 0.2f);
            HeldItem.HandTintResolver = () => arm;
            HeldItem.HandPaintResolver = () => new string('1', 8 * 32 * 32);
            Assert.That(HeldItem.For(null, "").tint, Is.EqualTo(arm));
        }
    }
}
