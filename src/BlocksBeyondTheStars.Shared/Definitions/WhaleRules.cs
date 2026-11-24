// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The whale (#3975, <see cref="CreatureBodyPlan.Whale"/>): a big, calm swimmer of the deep sea — a long tapering body with a
/// big head, a blowhole, throat grooves, long pectoral fins and a horizontal fluke. Unlike the leviathan it is no one-per-world
/// giant: the spawner places it like any animal (one or two), it never hunts, and it can be hurt. The proportions are in the
/// whale's unit (<c>u = ½·clamp(size, 0.4, <see cref="MaxDrawnSize"/>)</c>): the client builds the body from them
/// (<c>CreatureBuilder.Cetacean.cs</c>), the server's footprint and body height read them. Pure and Unity-free.
/// </summary>
public static class WhaleRules
{
    /// <summary>The largest drawn size: a size-5 whale individual of the 1.3 size factor is drawn at 5.5 — 22 blocks long, a
    /// footprint radius of 11, inside <see cref="CreatureFootprint.MaxRadius"/>.</summary>
    public const float MaxDrawnSize = 5.5f;

    /// <summary>The body's length from the front of the head to the fluke's trailing edge, in units.</summary>
    public const float LengthUnits = 8f;

    /// <summary>The body's height at its thickest (the girth), in units.</summary>
    public const float GirthUnits = 1.5f;

    /// <summary>The whale's unit for a drawn size.</summary>
    public static float Unit(float size) => 0.5f * Math.Clamp(size, 0.4f, MaxDrawnSize);

    /// <summary>The body's length in blocks at drawn size <paramref name="size"/> (a size-5 whale: 20 blocks).</summary>
    public static float BodyLength(float size) => LengthUnits * Unit(size);

    /// <summary>The body's height in blocks at its thickest.</summary>
    public static float Girth(float size) => GirthUnits * Unit(size);

    /// <summary>The ground radius (<see cref="CreatureFootprint.Radius"/>): half the body's length — the body is drawn centred
    /// on the position.</summary>
    public static float FootprintRadius(float size) => 0.5f * BodyLength(size);

    /// <summary>How many cells tall the body is for the server's collision checks: its girth, rounded up, within the clamp.</summary>
    public static int BodyHeightCells(float size, int min, int max) => Math.Clamp((int)Math.Ceiling(Girth(size)), min, max);
}
