// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How much of a creature's rig is worth animating at its current distance. Before this existed every
    /// creature ran its full rig every frame at any distance — up to the world's live cap (~25–45, hard cap
    /// 64) of them, including ones ninety metres away and four pixels tall.
    /// </summary>
    public enum CreatureLod : byte
    {
        Near,   // everything: foot planting, blink, gaze, jaw, flourishes
        Mid,    // the gait, wings, tail and body; no ground probes, no face detail
        Far,    // the same, at a third of the frame rate
        Frozen, // nothing — the body still moves, it just stops posing itself
    }

    /// <summary>
    /// One leg of a creature rig. Before this existed the animator received a flat <c>Transform[]</c> and
    /// inferred side and row from the array index — which the two body plans filled in opposite orders (the
    /// standard path numbered the REAR pair first, the titan path the FRONT pair), so any gait keyed off the
    /// index ran mirrored on titans. The builder now states side and row explicitly.
    /// </summary>
    public sealed class LegRig
    {
        public Transform Hip;      // pitch (X) + splay (Z); the whole leg before joints exist
        public Transform Knee;     // null until the jointed limbs are built
        public Transform Foot;     // null until the jointed limbs are built

        public int Side;           // 0 = left, 1 = right
        public int Row;            // 0 = the front-most pair
        public int Rows;           // how many pairs this body has

        public float UpperLen;     // hip → knee (the whole leg while Knee is null)
        public float LowerLen;     // knee → foot (0 while Knee is null)

        /// <summary>+1 folds the lower leg forward (hind-limb style), -1 folds it back (fore-limb style).
        /// A quadruped whose knees all bend the same way reads as a table, not an animal.</summary>
        public int KneeSign = 1;

        /// <summary>Rest pose captured at build time — every animator effect poses ADDITIVELY on top of this
        /// instead of overwriting the rotation, so splay, gait, tuck and rest pose can compose.</summary>
        public Quaternion HipRest = Quaternion.identity;
        public Vector3 HipRestPos;

        /// <summary>Hip-to-foot length — the lever the stride length is derived from.</summary>
        public float Length => UpperLen + LowerLen;
    }

    /// <summary>One wing: a shoulder that beats and (once jointed) a wrist that folds the outer panel away.
    /// <see cref="Row"/> / <see cref="Rows"/> (#1781): a multi-winged body carries its pairs along the torso, and
    /// the animator lags each row so the pairs beat as a wave instead of one copied flap.</summary>
    public sealed class WingRig
    {
        public Transform Shoulder;
        public Transform Wrist;    // null until the jointed limbs are built
        public int Side;           // 0 = left, 1 = right
        public int Row;            // 0 = the front-most pair
        public int Rows = 1;       // how many pairs this body has
        public Quaternion ShoulderRest = Quaternion.identity;
        public Quaternion WristRest = Quaternion.identity;
    }

    /// <summary>What a fin is, so the animator poses it by kind instead of by array index (#1782).</summary>
    public enum FinKind : byte
    {
        Pectoral, // a flank fin — sculls, mirrored per side, lagged per row
        Caudal,   // the vertical tail fin — sweeps with the body's undulation
        Dorsal,   // the back fin — barely moves
    }

    /// <summary>One fin. Pectorals carry a side and a row (a multi-finned body has two or three pairs along the
    /// flanks); the caudal and dorsal fins are single.</summary>
    public sealed class FinRig
    {
        public Transform Pivot;
        public FinKind Kind;
        public int Side;           // 0 = left, 1 = right (pectorals)
        public int Row;            // 0 = the front-most pair (pectorals)
        public int Rows = 1;
    }

    /// <summary>
    /// Everything <see cref="CreatureAnimator"/> needs to pose a body, handed over once at build time.
    /// Replaces the old positional parameter list, which had run out of room at eight arguments and could
    /// not express joints, chains or a jaw at all. Fields a later body part does not have stay null — the
    /// animator checks each one.
    /// </summary>
    public sealed class RigDescription
    {
        // --- limbs ---
        public LegRig[] Legs = System.Array.Empty<LegRig>();
        public WingRig[] Wings = System.Array.Empty<WingRig>();

        /// <summary>Tail segments from the base outward; one entry is the old rigid box.</summary>
        public Transform[] Tail = System.Array.Empty<Transform>();

        /// <summary>Neck segments from the shoulders up; the head hangs off the last one.</summary>
        public Transform[] Neck = System.Array.Empty<Transform>();

        /// <summary>Tentacle/arm chains — outer index is the arm, inner the segments from the root outward.</summary>
        public Transform[][] Tentacles = System.Array.Empty<Transform[]>();

        /// <summary>Trunk segments from the head down (elephant).</summary>
        public Transform[] Trunk = System.Array.Empty<Transform>();

        /// <summary>The fins, each with its kind, side and row (#1782) — a legless swimmer's only limbs.</summary>
        public FinRig[] Fins = System.Array.Empty<FinRig>();

        /// <summary>Ray plan (#1778): one panel chain per side, root panel first — the wave travels outward.</summary>
        public Transform[][] RayWings = System.Array.Empty<Transform[]>();

        // --- head ---
        /// <summary>The first (or only) head; <see cref="Heads"/> lists every head on a multi-headed body (#1780).</summary>
        public Transform Head;
        public Transform Jaw;
        public Transform[] Heads = System.Array.Empty<Transform>();
        public Transform[] Jaws = System.Array.Empty<Transform>();
        public Transform[] Eyelids = System.Array.Empty<Transform>();
        public Transform[] Ears = System.Array.Empty<Transform>();

        // --- body ---
        public Transform Body;     // the body rig — weave, bob, squash, rest pose

        // Medusa plan: the bell pulses and the rim arms trail it.
        public Transform Bell;

        // --- species facts the animator poses from ---
        public bool Hostile;
        public bool Asleep;
        public bool Aquatic;       // water or amphibian — undulates instead of striding
        public bool SkyGlider;     // #1778/#1779: a sky ray or air fish — a hoverer that glides; fins scull in the air
        public string Temperament = string.Empty;
        public string BodyPlan = "Standard";
        public float Size = 1f;
        public int LegCount;
        public bool Giant;         // titan plan or size past the giant threshold — plods, never bounds

        /// <summary>Hip height (also the leg's full length) — the lever every stride is measured against.</summary>
        public float LegLength = 1f;

        /// <summary>Body unit (half a block at size 1) — the scale every offset in the rig is expressed in.</summary>
        public float Unit = 0.5f;

        /// <summary>Stable per-species hash, so per-individual variation stays deterministic across clients.</summary>
        public int IdHash;

        /// <summary>How the feet find real ground (supplied by <see cref="CreatureView"/>, which owns the world
        /// handle). Null = no foot planting; the rig falls back to the body-relative gait.</summary>
        public CreatureFeet.GroundProbe Ground;
    }
}
