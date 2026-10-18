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

        /// <summary>#2777: a held tool comes with the suit hand (forearm, cuff, fist, thumb) in the shared hand materials;
        /// the bare hand and the gloves bring their own and get none. The muzzle stays the tool's tip, not the hand's.</summary>
        [Test]
        public void AHeldTool_IsHeldByTheSuitHand_AndKeepsItsMuzzle()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; // Object.Destroy logs outside play mode
            var root = new GameObject("HeldHandTestRoot");
            try
            {
                var drill = HeldItem.Build(root.transform, HeldItem.Kind.Drill, Color.gray);
                Assert.That(HeldItem.MuzzleOf(drill, out var before), Is.True);
                HeldItem.AttachHand(drill, HeldItem.Kind.Drill, null, null);
                var hand = drill.transform.Find("Hand");
                Assert.That(hand, Is.Not.Null, "The drill is held by a hand.");
                Assert.That(hand.childCount, Is.EqualTo(4), "Forearm, cuff, fist and thumb.");
                var a = hand.GetChild(0).GetComponent<Renderer>().sharedMaterial;
                var b = HeldItem.Build(root.transform, HeldItem.Kind.Gun, Color.gray);
                HeldItem.AttachHand(b, HeldItem.Kind.Gun, null, null);
                Assert.That(b.transform.Find("Hand").GetChild(0).GetComponent<Renderer>().sharedMaterial, Is.SameAs(a),
                    "Every hand shares its materials — no new ones per quick-bar switch.");

                Assert.That(HeldItem.MuzzleOf(drill, out var after), Is.True);
                Assert.That(Vector3.Distance(before, after), Is.LessThan(1e-4f), "The hand is no part of the tool's tip.");

                var bare = HeldItem.Build(root.transform, HeldItem.Kind.Hand, Color.blue);
                HeldItem.AttachHand(bare, HeldItem.Kind.Hand, null, null);
                Assert.That(bare.transform.Find("Hand"), Is.Null, "The bare hand is its own hand.");
                Assert.That(HeldItem.MuzzleOf(bare, out var fist), Is.True, "A punch still starts at the fist.");
                Assert.That(bare.transform.InverseTransformPoint(fist).z, Is.GreaterThan(0.13f), "… at its front.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                HeldItem.ReleasePartMaterials();
                HeldItem.ReleaseHandAtlas();
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
