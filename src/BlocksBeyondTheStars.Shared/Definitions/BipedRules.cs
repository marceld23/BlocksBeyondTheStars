// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The rules of the biped body plan (#2081, generation 16 — the school club idea of Paul and Ben): an upright two-legger
/// with two arms and a big head, rolled last into the Land pool. Pure and Unity-free so the generator, the server, the
/// client and the tests read the same numbers — the size band, the proportions (the server's body height and the client's
/// build come from the same shares), the group and the herd's flight when one of them is hurt.
/// <para>Bipeds are always peaceful (decision 2026-09-27): passive or skittish, never a hunter, and they bite for nothing.</para>
/// </summary>
public static class BipedRules
{
    /// <summary>Share of standard-plan Land species on a generation-16 world that become bipeds (about one in six).</summary>
    public const double BipedChance = 0.16;

    /// <summary>The size band: knee-high (0.4) to child-high (1.3). <see cref="HeightFor"/> turns it into blocks.</summary>
    public const float MinSize = 0.4f;
    public const float MaxSize = 1.3f;

    /// <summary>The head's size relative to the classic head — a biped's head is big.</summary>
    public const float MinHeadRatio = 1.3f;
    public const float MaxHeadRatio = 2.0f;

    /// <summary>Share of passive bipeds that beg (and then live in a begging herd of 8–12, <see cref="HerdRules"/>).</summary>
    public const double BeggingChance = 0.5;

    /// <summary>Every other biped lives in a group of this many (they are never solitary).</summary>
    public const int GroupMin = 4;
    public const int GroupMax = 8;

    /// <summary>A hurt biped startles its herd this long (decision 2026-09-27: the herd runs away)...</summary>
    public const double PanicSeconds = 10.0;

    /// <summary>...everyone of its species this close — a whole herd, where the classic startle reaches 12 blocks.</summary>
    public const float PanicRadius = 24f;

    // --- Proportions (shares of the size; the client builds the body from exactly these) ---

    /// <summary>The legs' share of the size (hip height).</summary>
    public const float LegShare = 0.30f;

    /// <summary>The torso's share of the size.</summary>
    public const float TorsoShare = 0.34f;

    /// <summary>The head's edge as a share of the size at head ratio 1.</summary>
    public const float HeadShare = 0.26f;

    /// <summary>The arm's length (shoulder to hand) as a share of the size.</summary>
    public const float ArmShare = 0.32f;

    /// <summary>Whether the species wears the biped body.</summary>
    public static bool IsBiped(CreatureSpecies sp) => sp.BodyPlan == CreatureBodyPlan.Biped;

    /// <summary>The head's edge in blocks for a body of <paramref name="size"/> with <paramref name="headRatio"/>.</summary>
    public static float HeadEdge(float size, float headRatio) => size * HeadShare * Math.Max(0.5f, headRatio);

    /// <summary>The standing height in blocks: legs + torso + head.</summary>
    public static float HeightFor(float size, float headRatio) => size * (LegShare + TorsoShare) + HeadEdge(size, headRatio);

    /// <summary>How many cells tall the collision body is — the real standing height, not the general <c>Size × 1.8</c>
    /// rule (which would give a knee-high biped a two-block body that no burrow or bush lets through).</summary>
    public static int BodyHeightCells(float size, float headRatio, int min, int max)
        => Math.Clamp((int)Math.Ceiling(HeightFor(size, headRatio) - 0.05f), min, max);
}
