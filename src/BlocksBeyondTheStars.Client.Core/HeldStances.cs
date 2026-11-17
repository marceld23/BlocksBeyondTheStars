// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>How a held item rests in the hand.</summary>
    public enum HeldStance
    {
        /// <summary>Pointing ahead, level: guns, drills, scanners, gadgets, blocks — everything before #3763.</summary>
        Level,

        /// <summary>A blade: tipped up, so its length shows instead of only its end.</summary>
        Raised,

        /// <summary>A pick: the handle tipped up further, the head on top; it chops instead of jabbing.</summary>
        Pick,
    }

    /// <summary>
    /// The rest pose and the use motion of a held item by its stance (#3763 — Justus, 2026-11-17: "sometimes you do not hold
    /// weapons and tools properly! The guardian sword, for example. The wooden pickaxe has a drill animation???").
    /// <para>Every held model is built along +Z, and the first-person view had one rest pose for all of them: a sword pointed
    /// straight away from the eye like a gun, so a narrow blade shrank to a short stick, and the pickaxe's handle hid its
    /// head. The wooden pickaxe is also a <c>drill</c> by its tool kind (that is what makes it mine), and the client gave
    /// it everything a drill has — the spin-up flourish and its sound, the forward jab, the drill loop while mining.</para>
    /// <para><b>A pick is recognised from the data, never from a key:</b> a drill-kind tool whose own held model has no
    /// bit on its axis (<see cref="HeldDrillBit.SpinParts"/>) — the rule that already kept the pickaxe's head from
    /// spinning. It is the held kind <see cref="PickKind"/>: no flourish, no drill sounds, an overhead chop.</para>
    /// </summary>
    public static class HeldStances
    {
        /// <summary>The held kind (<c>HeldItem.Kind</c> by name) of a pick.</summary>
        public const string PickKind = "Pick";

        /// <summary>Whether a drill-kind tool with this model of its own is a pick: the model has boxes and none of them
        /// is a bit on the drill's axis. A drill without a model of its own (the kind's default drill) is never one.</summary>
        public static bool IsPick(IReadOnlyList<HeldItemShapes.Part>? ownModel)
            => ownModel is { Count: > 0 } && HeldDrillBit.SpinParts(ownModel).Count == 0;

        /// <summary><see cref="IsPick"/> for an item's held model as the item data has it (null or empty: no model of its
        /// own, so never a pick).</summary>
        public static bool IsPickModel(IReadOnlyList<BlocksBeyondTheStars.Shared.Definitions.HeldModelPart>? heldModel)
            => heldModel is { Count: > 0 }
               && IsPick(HeldItemShapes.Parts("Drill", new HeldItemShapes.Rgb(0.6f, 0.6f, 0.6f), heldModel));

        /// <summary>The stance of a held kind (<c>HeldItem.Kind</c> by name).</summary>
        public static HeldStance For(string? heldKind) => heldKind switch
        {
            "Blade" => HeldStance.Raised,
            PickKind => HeldStance.Pick,
            _ => HeldStance.Level,
        };

        /// <summary>First person: the turn (degrees) added to the shared rest turn. A negative X tips the item's far end
        /// up; Y turns it toward the middle of the view; Z rolls it.</summary>
        public static (float X, float Y, float Z) RestTurn(HeldStance stance) => stance switch
        {
            HeldStance.Raised => (-34f, -8f, -10f),
            HeldStance.Pick => (-44f, -6f, 0f),
            _ => (0f, 0f, 0f),
        };

        /// <summary>First person: the shift (metres) added to the shared rest position — a tipped-up item sits a little
        /// lower and nearer, so its upper end stays inside the view.</summary>
        public static (float X, float Y, float Z) RestShift(HeldStance stance) => stance switch
        {
            HeldStance.Raised => (0f, -0.05f, -0.03f),
            HeldStance.Pick => (0f, -0.06f, -0.04f),
            _ => (0f, 0f, 0f),
        };

        /// <summary>Third person: how far (degrees) the carried item tips up from level in the hand.</summary>
        public static float CarryTip(HeldStance stance) => stance switch
        {
            HeldStance.Raised => 38f,
            HeldStance.Pick => 48f,
            _ => 0f,
        };

        /// <summary>Whether the stance's use motion is the overhead chop (first person) instead of the forward jab.</summary>
        public static bool Chops(HeldStance stance) => stance == HeldStance.Pick;

        /// <summary>The chop's pitch (degrees, added to the rest turn) at <paramref name="t"/> 0..1 of the swing: a short
        /// wind-up back over the shoulder, the strike down past level, and back to rest — 0 at both ends.</summary>
        public static float ChopPitch(float t)
        {
            t = Math.Min(1f, Math.Max(0f, t));
            if (t < ChopWindUp)
            {
                return -ChopBack * Ease(t / ChopWindUp);
            }

            if (t < ChopStrike)
            {
                return Lerp(-ChopBack, ChopDown, Ease((t - ChopWindUp) / (ChopStrike - ChopWindUp)));
            }

            return ChopDown * (1f - Ease((t - ChopStrike) / (1f - ChopStrike)));
        }

        /// <summary>How far the chop winds up back (degrees) …</summary>
        public const float ChopBack = 16f;

        /// <summary>… and strikes down past the rest pose.</summary>
        public const float ChopDown = 78f;

        /// <summary>The share of the swing spent winding up …</summary>
        public const float ChopWindUp = 0.22f;

        /// <summary>… and the moment the head lands.</summary>
        public const float ChopStrike = 0.55f;

        private static float Ease(float x) => x * x * (3f - (2f * x));

        private static float Lerp(float a, float b, float x) => a + ((b - a) * x);
    }
}
