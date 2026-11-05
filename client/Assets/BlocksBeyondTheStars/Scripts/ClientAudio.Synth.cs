// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The router's code-synth fallbacks (the jetpack rumble, the confirm/reject tones, the old hull hum).</summary>
    public sealed partial class ClientAudio
    {
        /// <summary>Synthesized jetpack thrust: a low rumble under filtered hiss (used when no recording exists).</summary>
        private static AudioClip JetClip()
        {
            const int rate = 44100;
            const float duration = 1f; // loops seamlessly enough for a continuous hiss
            int samples = Mathf.CeilToInt(rate * duration);
            var clip = AudioClip.Create("jetpack_loop", samples, 1, rate, false);
            var data = new float[samples];
            var rng = new System.Random(91);
            float lp = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float white = (float)(rng.NextDouble() * 2 - 1);
                lp += (white - lp) * 0.25f; // low-pass the noise into an airy hiss
                float rumble = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.25f + Mathf.Sin(2f * Mathf.PI * 90f * t) * 0.12f;
                data[i] = (lp * 0.6f + rumble) * 0.5f;
            }

            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Generates a short decaying sine tone as a fallback <see cref="AudioClip"/>.</summary>
        private static AudioClip Tone(float frequency, float duration, float volume)
        {
            const int rate = 44100;
            int samples = Mathf.CeilToInt(rate * duration);
            var clip = AudioClip.Create("tone", samples, 1, rate, false);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-t * 12f);
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * volume;
            }

            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Generates a 2 s seamless low hull-hum loop (two low sines + faint air hiss).</summary>
        private static AudioClip Hum()
        {
            const int rate = 44100;
            const float duration = 2f;
            int samples = Mathf.CeilToInt(rate * duration);
            var clip = AudioClip.Create("hum", samples, 1, rate, false);
            var data = new float[samples];
            var rng = new System.Random(7);
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float s = Mathf.Sin(2f * Mathf.PI * 80f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.22f;
                s += (float)(rng.NextDouble() * 2 - 1) * 0.03f;
                data[i] = s * 0.5f;
            }

            clip.SetData(data, 0);
            return clip;
        }
    }
}
