// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Feel
{
    /// <summary>
    /// The timing of the emotes and body reactions a figure plays (the poses themselves are the avatar's): how long
    /// each lasts, the envelope every one of them rides on — in from rest, hold, back to rest, so nothing snaps — and
    /// the blink of the eyes. Pure numbers, shared by the own figure, the other players' and the villagers'.
    /// </summary>
    public static class EmoteCurves
    {
        /// <summary>The arm out straight.</summary>
        public const float PointSeconds = 1.4f;

        /// <summary>The head from side to side.</summary>
        public const float HeadShakeSeconds = 1.0f;

        /// <summary>A little dance.</summary>
        public const float DanceSeconds = 3.2f;

        /// <summary>Clapping.</summary>
        public const float ClapSeconds = 1.6f;

        /// <summary>A bow.</summary>
        public const float BowSeconds = 1.5f;

        /// <summary>The flinch of a hit.</summary>
        public const float FlinchSeconds = 0.35f;

        /// <summary>The give in the knees after a landing.</summary>
        public const float LandSeconds = 0.22f;

        /// <summary>A defeated figure stays down at most this long without news (the rise is told by its moving again).</summary>
        public const float DownSeconds = 12f;

        /// <summary>How long the eyes stay shut in a blink.</summary>
        public const float BlinkSeconds = 0.12f;

        /// <summary>How long a timed emote lasts; 0 for one that is not timed (sit) or not an emote.</summary>
        public static float Seconds(byte emote) => emote switch
        {
            PlayerEmotes.Point => PointSeconds,
            PlayerEmotes.HeadShake => HeadShakeSeconds,
            PlayerEmotes.Dance => DanceSeconds,
            PlayerEmotes.Clap => ClapSeconds,
            PlayerEmotes.Bow => BowSeconds,
            PlayerEmotes.Hurt => FlinchSeconds,
            _ => 0f,
        };

        /// <summary>
        /// The envelope of a gesture at <paramref name="progress"/> (0 = it starts, 1 = it is over): eases in over the
        /// first <paramref name="attack"/> share, holds, eases out over the last <paramref name="release"/> share.
        /// 0 at both ends, 1 in the hold.
        /// </summary>
        public static float Envelope(float progress, float attack = 0.2f, float release = 0.25f)
        {
            if (progress <= 0f || progress >= 1f)
            {
                return 0f;
            }

            float k = 1f;
            if (attack > 0f && progress < attack)
            {
                k = progress / attack;
            }
            else if (release > 0f && progress > 1f - release)
            {
                k = (1f - progress) / release;
            }

            return k * k * (3f - (2f * k)); // smoothstep: no kink where it meets the hold
        }

        /// <summary>Seconds until a figure's next blink: between three and six and a half, different for every figure
        /// (<paramref name="seed"/>) and every blink (<paramref name="count"/>), so a crowd never blinks in step.</summary>
        public static float BlinkGap(int seed, int count)
        {
            uint h = unchecked(((uint)seed * 2654435761u) ^ ((uint)count * 40503u));
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return 3f + ((h & 0xFFFF) / 65535f * 3.5f);
        }

        /// <summary>How open a talking mouth is (0…1) at <paramref name="time"/> seconds: an uneven chatter, not a metronome.</summary>
        public static float MouthOpen(float time)
        {
            double a = Math.Sin(time * 17.0);
            double b = Math.Sin((time * 11.3) + 1.7);
            double v = 0.5 + (0.3 * a) + (0.2 * b);
            return v < 0.0 ? 0f : v > 1.0 ? 1f : (float)v;
        }
    }
}
