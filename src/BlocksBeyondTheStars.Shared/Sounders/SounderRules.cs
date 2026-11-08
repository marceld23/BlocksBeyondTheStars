// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Sounders;

/// <summary>
/// The body of a Sounder (#3355, after the school club's sketches by Theo and a classmate): organic, tall and thin, three to four
/// blocks high, upright on two very long legs with toeless feet, <b>four</b> thin, slightly wavy arms in two pairs, a small
/// head with <b>no eyes</b>, long hare-like ears, a mouth that is often open (singing) and a rectangular slot on the chest where
/// the obsidian armour holds a Song Disc. A living Sounder wears light-blue or light-green skin (#3423, Justus: everything on
/// Soaniri light blue and light green) and a woven sash in a deeper blue or green; an <b>Echo</b> (#3356) is the same body,
/// translucent teal and softly glowing, floating a hand above the ground.
/// <para>Pure and Unity-free: the server measures an NPC's or a creature's height from the same shares the client builds the rig
/// from, so the drawn body and the body the server walks through doorways always agree. A Sounder's "size" is a scale factor
/// (1 = <see cref="BaseHeight"/> blocks to the crown) — the <c>NetNpc.Size</c> and the <c>CreatureSpecies.Size</c> alike.</para>
/// </summary>
public static class SounderRules
{
    /// <summary>The height in blocks from the soles to the crown (without the ears) at size 1.</summary>
    public const float BaseHeight = 3.3f;

    /// <summary>The smallest scale a Sounder is drawn at (≈ 3 blocks — the Young Voices).</summary>
    public const float MinSize = 0.9f;

    /// <summary>The largest scale a Sounder is drawn at (≈ 4 blocks — the Eldest).</summary>
    public const float MaxSize = 1.2f;

    /// <summary>The very long legs' share of the height (the hip height).</summary>
    public const float LegShare = 0.48f;

    /// <summary>The torso's share of the height (hips to shoulders).</summary>
    public const float TorsoShare = 0.30f;

    /// <summary>The thin neck's share of the height.</summary>
    public const float NeckShare = 0.08f;

    /// <summary>The small head's share of the height (the four shares add up to one).</summary>
    public const float HeadShare = 0.14f;

    /// <summary>Each long hare ear's length as a share of the height — they stand above the crown.</summary>
    public const float EarShare = 0.24f;

    /// <summary>The torso's width as a share of the height — thin.</summary>
    public const float TorsoWidthShare = 0.14f;

    /// <summary>The upper arm pair's reach (shoulder to fingertip) as a share of the height.</summary>
    public const float UpperArmShare = 0.40f;

    /// <summary>The lower arm pair's reach as a share of the height — a little shorter, from just under the upper pair.</summary>
    public const float LowerArmShare = 0.33f;

    /// <summary>How far an arm hangs out from the middle, as a share of the height — the body's footprint.</summary>
    public const float ReachShare = 0.14f;

    /// <summary>The arms: two pairs.</summary>
    public const int Arms = 4;

    /// <summary>How high an Echo floats over the ground, in blocks (a hand's breadth).</summary>
    public const float EchoHover = 0.2f;

    /// <summary>The living Sounders' skin (#3423): light blues and light greens — sky, ice blue, sea blue, mint, pale green
    /// and aqua.</summary>
    public static readonly uint[] SkinTones = { 0x8FC6EA, 0x9CD3F0, 0x86C5DC, 0x9FDDB8, 0xACE3C6, 0x93D4C0 };

    /// <summary>The woven sash's colours (#3423), deeper than the skin so the sash still reads on it: deep blue, sea green,
    /// teal, frost white, cobalt, leaf green.</summary>
    public static readonly uint[] SashTones = { 0x2F6FA8, 0x2E8A6A, 0x1F7F8C, 0xE6F4FF, 0x3F78B8, 0x5E9F4A };

    /// <summary>An Echo's translucent teal (the client draws it see-through and glowing).</summary>
    public const uint EchoTeal = 0x7FE0D0;

    /// <summary>An Echo's glow, a lighter teal.</summary>
    public const uint EchoGlow = 0xB8FFF2;

    /// <summary>A valid scale: <paramref name="size"/> within <see cref="MinSize"/>..<see cref="MaxSize"/>; 0, a negative or NaN
    /// value reads as 1.</summary>
    public static float ClampSize(float size)
        => float.IsNaN(size) || size <= 0f ? 1f : Math.Min(MaxSize, Math.Max(MinSize, size));

    /// <summary>The height in blocks from the soles to the crown at <paramref name="size"/>.</summary>
    public static float HeightFor(float size) => BaseHeight * ClampSize(size);

    /// <summary>The ears' tip height over the soles at <paramref name="size"/> (crown + one ear, upright).</summary>
    public static float EarTipFor(float size) => HeightFor(size) * (1f + EarShare);

    /// <summary>How many cells tall the collision body is: the real height, clamped to the caller's band.</summary>
    public static int BodyHeightCells(float size, int min, int max)
        => Math.Min(max, Math.Max(min, (int)Math.Ceiling(HeightFor(size) - 0.05f)));

    /// <summary>The ground radius the drawn body covers — the hanging arms.</summary>
    public static float FootprintRadius(float size) => HeightFor(size) * ReachShare;

    /// <summary>Whether an NPC look key draws the Sounder body (<see cref="SoundersIds.Sounder"/> or <see cref="SoundersIds.Echo"/>).</summary>
    public static bool IsSounderLook(string? look) => look == SoundersIds.Sounder || look == SoundersIds.Echo;

    /// <summary>Whether an NPC look key is an Echo.</summary>
    public static bool IsEcho(string? look) => look == SoundersIds.Echo;

    /// <summary>A living Sounder's skin and sash for <paramref name="seed"/> (deterministic, so the same NPC wears the same
    /// colours everywhere).</summary>
    public static (uint Skin, uint Sash) LookFor(int seed)
    {
        uint h = Mix(unchecked((uint)seed ^ 0x51D3E2u));
        return (SkinTones[h % (uint)SkinTones.Length], SashTones[(h >> 8) % (uint)SashTones.Length]);
    }

    /// <summary>A Sounder's scale for <paramref name="seed"/>: <see cref="MinSize"/>..<see cref="MaxSize"/>, the middle more
    /// likely than the ends.</summary>
    public static float SizeFor(int seed)
    {
        uint h = Mix(unchecked((uint)seed ^ 0x7A1C05u));
        float a = (h & 0xFFFF) / 65535f, b = (h >> 16) / 65535f;
        return MinSize + ((a + b) * 0.5f * (MaxSize - MinSize));
    }

    /// <summary>The MurmurHash3 finaliser: every input bit reaches every output bit.</summary>
    private static uint Mix(uint h)
    {
        unchecked
        {
            h ^= h >> 16;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0xC2B2AE35u;
            h ^= h >> 16;
            return h;
        }
    }
}
