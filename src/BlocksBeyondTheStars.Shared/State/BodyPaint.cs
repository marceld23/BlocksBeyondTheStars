// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// The shared shape of the avatar body-paint payloads (#874) — the pixel paintings a player draws for
/// torso, arms, legs and the space-suit helmet, sibling to the custom pixel face. Like the face, a
/// painting is a string of palette indices (one hex char per pixel, <c>0</c> = transparent) that the
/// server treats as opaque: the client owns the palette and rendering, the server only bounds and
/// charset-checks it (persisted + rebroadcast, so an unvalidated blob would be a disk/bandwidth vector).
///
/// Per part the payload is a concatenation of 32×32 face chunks in a fixed order the client defines
/// (torso 4 side faces; arms/legs 2 limbs × 4 side faces, left limb first; helmet 5 shell faces).
/// Only the chunk COUNT lives here — it is what the server needs to validate an exact length.
/// An empty string means "not painted" and is always legal.
/// </summary>
public static class BodyPaint
{
    /// <summary>Part indices as used on the wire and in <see cref="PlayerState"/>.</summary>
    public const int Torso = 0;
    public const int Arms = 1;
    public const int Legs = 2;
    public const int Helmet = 3;

    /// <summary>Number of paintable parts (valid part indices are 0 .. PartCount-1).</summary>
    public const int PartCount = 4;

    /// <summary>Pixels per face chunk (32×32, the same resolution as the pixel face).</summary>
    public const int ChunkPixels = 32 * 32;

    /// <summary>Face chunks per part: torso front/right/back/left; arms + legs are per-limb
    /// (left row then right row) × front/outer/back/inner; helmet right/back/left/chin/top
    /// (no front — the helmet is open so the face stays visible).</summary>
    public static int ChunksOf(int part) => part switch
    {
        Torso => 4,
        Arms => 8,
        Legs => 8,
        Helmet => 5,
        _ => 0,
    };

    /// <summary>The exact hex-string length a non-empty payload for <paramref name="part"/> must have.</summary>
    public static int ExpectedLength(int part) => ChunksOf(part) * ChunkPixels;

    /// <summary>True when <paramref name="part"/> is a known paintable part.</summary>
    public static bool IsValidPart(int part) => part is >= 0 and < PartCount;
}
