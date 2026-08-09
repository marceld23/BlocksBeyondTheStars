// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Offline sampler effects (#877): renders an effect INTO a new <see cref="AudioClip"/> instead of
    /// attaching a filter component, because Unity Web silently ignores every audio filter component
    /// (AudioReverbFilter/AudioLowPassFilter/… — #878). The bake path (GetData → C# DSP → Create/SetData)
    /// is Web-supported for Decompress-On-Load clips — the same mechanism <see cref="ProceduralAudio"/>
    /// already uses. Variants are cached per (clip, effect); a 1–2 s call costs a few ms to bake once.
    /// </summary>
    public static class SampleKit
    {
        private static readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();

        /// <summary>A dripping-cavern tail for cave-dweller calls (replaces AudioReverbFilter.Cave):
        /// small Schroeder reverb — 4 parallel feedback combs into 2 series allpasses, ~0.9 s tail.</summary>
        public static AudioClip CaveReverb(AudioClip src) => Bake(src, "cave", Reverb);

        /// <summary>An underwater-muffled variant (replaces the per-one-shot AudioLowPassFilter):
        /// one-pole low-pass at the same 680 Hz cutoff the desktop bus filter uses.</summary>
        public static AudioClip Muffle(AudioClip src) => Bake(src, "muffle", LowPass);

        private static AudioClip Bake(AudioClip src, string op, System.Func<float[], int, int, float[]> fx)
        {
            if (src == null)
            {
                return null;
            }

            string key = src.name + "|" + op;
            if (_cache.TryGetValue(key, out var hit) && hit != null)
            {
                return hit;
            }

            var dry = new float[src.samples * src.channels];
            if (!src.GetData(dry, 0))
            {
                return src; // unreadable (not Decompress-On-Load) — play the dry clip rather than nothing
            }

            var wet = fx(dry, src.channels, src.frequency);
            var clip = AudioClip.Create(key, wet.Length / src.channels, src.channels, src.frequency, false);
            clip.SetData(wet, 0);
            _cache[key] = clip;
            return clip;
        }

        /// <summary>Small Schroeder cave reverb. Interleaved-safe: every delay is measured per channel
        /// and scaled by the channel count, so each channel only ever feeds back into itself.</summary>
        private static float[] Reverb(float[] dry, int channels, int rate)
        {
            // Mutually-prime comb delays (ms) — the classic spread that avoids a metallic single-pitch ring.
            float[] combMs = { 29.7f, 37.1f, 41.1f, 43.7f };
            float[] combGain = { 0.72f, 0.70f, 0.68f, 0.66f };
            int tail = Mathf.CeilToInt(0.9f * rate) * channels;   // let the reverb ring past the dry end
            var wet = new float[dry.Length + tail];

            var combBuf = new float[combMs.Length][];
            for (int c = 0; c < combMs.Length; c++)
            {
                combBuf[c] = new float[Mathf.Max(1, Mathf.RoundToInt(combMs[c] * 0.001f * rate)) * channels];
            }

            for (int i = 0; i < wet.Length; i++)
            {
                float x = i < dry.Length ? dry[i] : 0f;
                float sum = 0f;
                for (int c = 0; c < combBuf.Length; c++)
                {
                    var buf = combBuf[c];
                    int j = i % buf.Length;
                    float y = x + buf[j] * combGain[c]; // buf[j] still holds this channel's y from one delay ago
                    buf[j] = y;
                    sum += y;
                }

                wet[i] = sum * 0.25f;
            }

            Allpass(wet, channels, Mathf.RoundToInt(0.0050f * rate), 0.7f);
            Allpass(wet, channels, Mathf.RoundToInt(0.0017f * rate), 0.7f);

            for (int i = 0; i < wet.Length; i++)
            {
                float x = i < dry.Length ? dry[i] : 0f;
                wet[i] = Mathf.Clamp(x * 0.65f + wet[i] * 0.45f, -1f, 1f);
            }

            return wet;
        }

        /// <summary>In-place series allpass (y = -g·x + z + g·y_delayed) — smears the comb output into a
        /// dense tail without colouring the spectrum.</summary>
        private static void Allpass(float[] s, int channels, int delaySamples, float g)
        {
            var buf = new float[Mathf.Max(1, delaySamples) * channels];
            for (int i = 0; i < s.Length; i++)
            {
                int j = i % buf.Length;
                float z = buf[j];          // w[n-D]
                float w = s[i] + g * z;    // w[n] = x + g·w[n-D]
                buf[j] = w;
                s[i] = z - g * w;          // y[n] = w[n-D] - g·w[n]
            }
        }

        /// <summary>One-pole low-pass at the underwater cutoff (matches ClientAudio's 680 Hz bus filter).</summary>
        private static float[] LowPass(float[] dry, int channels, int rate)
        {
            const float cutoff = 680f;
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / rate);
            var wet = new float[dry.Length];
            var state = new float[channels];
            for (int i = 0; i < dry.Length; i++)
            {
                int ch = i % channels;
                state[ch] += a * (dry[i] - state[ch]);
                wet[i] = state[ch];
            }

            return wet;
        }
    }
}
