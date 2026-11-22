// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Numerics;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What a part of a held model is drawn as.</summary>
    public enum HeldPartForm
    {
        /// <summary>The box (with slightly rounded edges).</summary>
        Box,

        /// <summary>The box with its four edges along <see cref="HeldItemShapes.Part.Axis"/> cut flat at 45°: an octagon
        /// seen along that axis — a barrel, a shaft, a rod (bar), or a flange, a lens ring, a cap (plate).</summary>
        Chamfer,

        /// <summary>A cone along <see cref="HeldItemShapes.Part.Axis"/>, narrowing toward <see cref="HeldItemShapes.Part.Tip"/>
        /// (a drill's bit).</summary>
        Cone,
    }

    /// <summary>
    /// Which parts of a held tool get flattened corners and which drill bit becomes a cone. "Nicht alles
    /// rund": barrels and the like get chamfered edges, a cone is fine for the drill's bit — no cylinders. A part names its
    /// form in the data (<see cref="HeldModelPart.F"/>, <see cref="TryExplicit"/>); the official models leave it to the
    /// proportions (<see cref="Automatic"/>), a player's own look names <c>box</c> on every part. Unity-free so the headless
    /// suite pins it; <c>HeldItem.PartCube</c> picks the mesh (<see cref="HeldFormMeshes"/>).
    /// </summary>
    public static class HeldPartForms
    {
        /// <summary>How much the two equal sides of a bar (thin and mid) or a plate (mid and long) may differ, as a share
        /// of the larger.</summary>
        public const float SameSides = 0.03f;

        /// <summary>A bar is at least this many times as long as it is wide (long ≥ 1.7 × mid).</summary>
        public const float BarLength = 1.7f;

        /// <summary>A plate is at most this thick against its width (thin ≤ 0.4 × mid).</summary>
        public const float PlateThin = 0.4f;

        /// <summary>A part named <c>chamfer</c> is cut as a plate when it is at most this thick against its width (thin ≤
        /// 0.6 × mid), otherwise as a bar.</summary>
        public const float NamedPlateThin = 0.6f;

        /// <summary>Narrower parts (mid side, metres) are left as boxes: there is nothing to cut.</summary>
        public const float MinMid = 0.015f;

        /// <summary>Wider bars (mid side, metres) stay boxes — a tool's body, not a barrel — unless they are a drill's cone.</summary>
        public const float MaxBarMid = 0.08f;

        /// <summary>How far another part may reach beyond a drill's bit (metres) for the bit to still count as the tip.</summary>
        public const float ConeReach = 0.005f;

        /// <summary>
        /// The form a part names in the data, applied: <c>box</c> → the box; <c>chamfer</c> → a plate cut about its thinnest
        /// side when thin ≤ <see cref="NamedPlateThin"/> × mid, otherwise a bar along its longest side; <c>cone</c> → a cone
        /// along its longest side whose tip points to the side of that axis its centre lies on (+ at the centre). False (and
        /// the part as it is) for no form or a word the client does not know — the automatic rule decides then.
        /// </summary>
        public static bool TryExplicit(HeldItemShapes.Part part, string? form, out HeldItemShapes.Part formed)
        {
            formed = part;
            var (thin, mid, longest) = SortAxes(part.Size);
            switch (form)
            {
                case HeldModelPart.FormBox:
                    formed = part.WithForm(HeldPartForm.Box, 2, 1);
                    return true;
                case HeldModelPart.FormChamfer:
                    bool plate = Side(part.Size, thin) <= NamedPlateThin * Side(part.Size, mid);
                    formed = part.WithForm(HeldPartForm.Chamfer, plate ? thin : longest, 1);
                    return true;
                case HeldModelPart.FormCone:
                    formed = part.WithForm(HeldPartForm.Cone, longest, Side(part.Position, longest) < 0f ? -1 : 1);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The automatic forms of an official model (an item's model, a kind's standard model, an NPC's tool): every part
        /// whose <paramref name="decided"/> entry is not true (all of them without the list) is looked at with its sides
        /// sorted thin ≤ mid ≤ long —
        /// <list type="bullet">
        /// <item><b>bar</b>: mid − thin ≤ <see cref="SameSides"/> × mid, long ≥ <see cref="BarLength"/> × mid and mid ≥
        /// <see cref="MinMid"/> → chamfered along the long axis — but a bar wider than <see cref="MaxBarMid"/> (mid) is a
        /// tool's body and stays a box, unless it is the drill's cone;</item>
        /// <item><b>plate</b>: long − mid ≤ <see cref="SameSides"/> × long, thin ≤ <see cref="PlateThin"/> × mid and mid ≥
        /// <see cref="MinMid"/> → chamfered about the thin axis;</item>
        /// <item><b>cone</b>: on a <c>Drill</c> only, a bar along z whose front face (centre z + half its length) is the
        /// model's foremost point — no other part reaches more than <see cref="ConeReach"/> beyond it — narrowing toward
        /// +z;</item>
        /// <item>everything else stays a box (grips, housings, screens).</item>
        /// </list>
        /// Returns <paramref name="parts"/>, changed in place.
        /// </summary>
        public static List<HeldItemShapes.Part> Automatic(string kind, List<HeldItemShapes.Part> parts, IReadOnlyList<bool>? decided)
        {
            float front = float.MinValue;
            foreach (var p in parts)
            {
                front = Math.Max(front, p.Position.Z + (p.Size.Z * 0.5f));
            }

            for (int i = 0; i < parts.Count; i++)
            {
                if (decided != null && i < decided.Count && decided[i])
                {
                    continue;
                }

                var p = parts[i];
                if (IsBar(p.Size, out int along))
                {
                    bool tip = kind == "Drill" && along == 2 && p.Position.Z + (p.Size.Z * 0.5f) + ConeReach >= front;
                    if (tip || Side(p.Size, SortAxes(p.Size).Mid) <= MaxBarMid)
                    {
                        parts[i] = p.WithForm(tip ? HeldPartForm.Cone : HeldPartForm.Chamfer, along, 1);
                    }
                }
                else if (IsPlate(p.Size, out int about))
                {
                    parts[i] = p.WithForm(HeldPartForm.Chamfer, about, 1);
                }
            }

            return parts;
        }

        /// <summary>True for a bar's proportions (see <see cref="Automatic"/>); <paramref name="axis"/> is its long axis.</summary>
        public static bool IsBar(Vector3 size, out int axis)
        {
            var (thin, mid, longest) = SortAxes(size);
            float t = Side(size, thin), m = Side(size, mid), l = Side(size, longest);
            axis = longest;
            return m >= MinMid && m - t <= SameSides * m && l >= BarLength * m;
        }

        /// <summary>True for a plate's proportions (see <see cref="Automatic"/>); <paramref name="axis"/> is its thin axis.</summary>
        public static bool IsPlate(Vector3 size, out int axis)
        {
            var (thin, mid, longest) = SortAxes(size);
            float t = Side(size, thin), m = Side(size, mid), l = Side(size, longest);
            axis = thin;
            return m >= MinMid && l - m <= SameSides * l && t <= PlateThin * m;
        }

        /// <summary>True for a chamfered part cut about an axis that is not its longest side — a plate, not a bar.</summary>
        public static bool IsPlate(HeldItemShapes.Part part)
            => part.Form == HeldPartForm.Chamfer && part.Axis != SortAxes(part.Size).Long;

        /// <summary>
        /// The form a part is drawn as for the given <paramref name="mode"/>: 0 (or less) = every part
        /// the box, 2 = bars and cones only (plates stay boxes), anything else = the part's own form.
        /// </summary>
        public static HeldPartForm Shown(HeldItemShapes.Part part, int mode)
        {
            if (mode <= 0 || (mode == 2 && IsPlate(part)))
            {
                return HeldPartForm.Box;
            }

            return part.Form;
        }

        /// <summary>
        /// The far stage's share for a part drawn as <paramref name="form"/> (<c>RoundLod</c>): the rounding radius, as a share
        /// of the part's thinnest side, whose corner gap (0.2929 × radius) equals this form's. A chamfer cut by
        /// <paramref name="cut"/> of its smaller cross side moves the corner by 0.7071 × the cut, so the share is
        /// cut × 2.41 × (smaller cross side / thinnest side) — the last factor is 1 on a bar, larger on a plate. A cone counts
        /// as 1. A box keeps <paramref name="boxShare"/>.
        /// </summary>
        public static float LodShare(HeldItemShapes.Part part, HeldPartForm form, float cut, float boxShare)
        {
            switch (form)
            {
                case HeldPartForm.Chamfer:
                    var size = Abs(part.Size);
                    float thinnest = Math.Min(size.X, Math.Min(size.Y, size.Z));
                    float cross = Math.Min(Side(size, (part.Axis + 1) % 3), Side(size, (part.Axis + 2) % 3));
                    return thinnest > 0f ? cut * (0.7071f / 0.2929f) * cross / thinnest : 0f;
                case HeldPartForm.Cone:
                    return 1f;
                default:
                    return boxShare;
            }
        }

        /// <summary>The axes ordered by side length (ties: the lower axis first, so the long one of a tie is z).</summary>
        public static (int Thin, int Mid, int Long) SortAxes(Vector3 size)
        {
            var abs = Abs(size);
            int a = 0, b = 1, c = 2;
            if (Side(abs, b) < Side(abs, a))
            {
                (a, b) = (b, a);
            }

            if (Side(abs, c) < Side(abs, b))
            {
                (b, c) = (c, b);
                if (Side(abs, b) < Side(abs, a))
                {
                    (a, b) = (b, a);
                }
            }

            return (a, b, c);
        }

        /// <summary>The component <paramref name="axis"/> (0 = x, 1 = y, 2 = z) of <paramref name="v"/>.</summary>
        public static float Side(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        private static Vector3 Abs(Vector3 v) => new Vector3(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z));
    }
}
