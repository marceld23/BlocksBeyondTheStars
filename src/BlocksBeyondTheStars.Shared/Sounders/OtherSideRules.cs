// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Sounders;

/// <summary>
/// The numbers of the other side (epic #3891) that server and client share: one place, so a limit is tuned once. The
/// other side is the same world in a player state — its rules live in the server's other-side core and the sound layer;
/// only what both sides must agree on is here.
/// </summary>
public static class OtherSideRules
{
    /// <summary>How many sound blocks one player may have standing in one world (#3908). Shaping one more is refused with
    /// a clear line until the player dissolves some of their own.</summary>
    public const int SoundBlockCap = 2000;

    /// <summary>Seconds between two shaping pulses of the Resonance Blaster over there (#3908) — far quicker than the
    /// 1.2 s of its sonic push on the real side, so a wall or a stair grows at a building pace.</summary>
    public const double ShapeCooldownSeconds = 0.2;

    /// <summary>How much earlier than a full <see cref="ShapeCooldownSeconds"/> the server still takes the next shaping
    /// pulse: one server tick (15 Hz). Intents are handled once per tick, so two pulses a held trigger sends 0.22 s apart
    /// arrive three or four ticks apart — and the three-tick ones would lose to the cooldown by rounding alone. The
    /// server's schedule moves on by whole cooldowns from the time the pulse was due (as the held beam's does), so being
    /// early never adds up to more than five blocks a second.</summary>
    public const double ShapeJitterToleranceSeconds = 0.07;

    /// <summary>The block key of a sound block — an alias of <see cref="SoundersIds.SoundBlock"/>, the one literal.</summary>
    public const string SoundBlockKey = SoundersIds.SoundBlock;

    /// <summary>
    /// The palette (#3908): the glow colour (0xRRGGBB) of a sound block shaped with tone 0–7 of the melody bar, a soft
    /// scale from deep to light. The look over there is white, grey and black, so the eight are eight clearly different
    /// BRIGHTNESS steps of one cool white — about 26 of 255 apart — and none is 0 (0 means "no glow" on the wire). The
    /// server stores the colour per cell; the client reads the tone back with <see cref="ToneOfGlow"/> to sound a block
    /// that is walked on.
    /// </summary>
    public static readonly int[] ToneGlow =
    {
        0x38404C, // 0  C   deep
        0x505A68, // 1  D
        0x687484, // 2  E
        0x808EA0, // 3  F
        0x9AA8BA, // 4  G
        0xB4C2D2, // 5  A
        0xD0DCE8, // 6  B
        0xF0F6FF, // 7  C'  light
    };

    /// <summary>The glow colour of a tone; a tone outside 0–7 is clamped into the palette.</summary>
    public static int GlowOfTone(int tone) => ToneGlow[tone < 0 ? 0 : tone >= ToneGlow.Length ? ToneGlow.Length - 1 : tone];

    /// <summary>The tone 0–7 a glow colour stands for: the palette entry itself, or — for a colour that is not in the
    /// palette — the entry nearest in brightness, so every sound block has a tone.</summary>
    public static int ToneOfGlow(int glow)
    {
        int best = 0;
        int bestDelta = int.MaxValue;
        int level = Brightness(glow);
        for (int tone = 0; tone < ToneGlow.Length; tone++)
        {
            if (ToneGlow[tone] == glow)
            {
                return tone;
            }

            int delta = System.Math.Abs(Brightness(ToneGlow[tone]) - level);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = tone;
            }
        }

        return best;
    }

    /// <summary>The pitch factor of a tone against tone 0 (C4): the C major scale up to C5 in equal temperament — so one
    /// sound file sings all eight tones (the <c>Pitch</c> of the <c>sound_shape</c> cue).</summary>
    public static float PitchOfTone(int tone)
    {
        int semitones = ToneSemitones[tone < 0 ? 0 : tone >= ToneSemitones.Length ? ToneSemitones.Length - 1 : tone];
        return (float)System.Math.Pow(2.0, semitones / 12.0);
    }

    private static readonly int[] ToneSemitones = { 0, 2, 4, 5, 7, 9, 11, 12 };

    /// <summary>How far around another player's body no stranger's sound block may stand, and how far around their own
    /// body a player may dissolve anybody's (cells): the body's two cells grown by this are a 3×4×3 box.</summary>
    public const int BodyMargin = 1;

    /// <summary>#3914: seconds of bare hands on one sound block — the mine key held, no Resonance Blaster in hand — until it
    /// gives way. Only a block in the player's own <see cref="BodyMargin"/> box gives way to bare hands (whoever shaped
    /// it), so nobody stays walled in without a blaster; the blaster reaches farther and is quick.</summary>
    public const double BareHandDissolveSeconds = 2.0;

    /// <summary>#3914: the longest pause between two bare-hand hits on the same sound block that still carries the work on
    /// (the mine key's hits come every quarter second; a pause longer than this starts the block over).</summary>
    public const double BareHandHitGapSeconds = 0.75;

    /// <summary>Whether <paramref name="cell"/> lies in a body box — the feet cell <paramref name="feet"/> and the head cell
    /// above it — grown by <paramref name="margin"/> cells on every side, the short way round the world's seam. The one
    /// rule the server's shaping and dissolving and the client's bare-hand aim share.</summary>
    public static bool InBodyBox(Vector3i feet, Vector3i cell, int margin, int circumference)
    {
        int dx = WorldConstants.WrapDeltaX(cell.X - feet.X, circumference);
        int dy = cell.Y - feet.Y;
        int dz = cell.Z - feet.Z;
        return dx >= -margin && dx <= margin && dz >= -margin && dz <= margin && dy >= -margin && dy <= 1 + margin;
    }

    /// <summary>Perceived brightness 0–255 of a 0xRRGGBB colour (integer Rec. 601 weights).</summary>
    private static int Brightness(int rgb)
        => ((((rgb >> 16) & 0xFF) * 299) + (((rgb >> 8) & 0xFF) * 587) + ((rgb & 0xFF) * 114)) / 1000;
}
