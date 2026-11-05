// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What a creature's combat cue does with its own call (#3244).</summary>
    public enum CombatCue
    {
        Hurt,
        Alert,
        Attack,
        Die,
    }

    /// <summary>
    /// #3244: the four pitch/time ops (Glide, Vibrato, Stutter, Truncate) and the combat cues derived from a species' OWN
    /// call. Before this a creature's hurt/alert/attack/die cues came from 24 shared bank files (3 sizes × calm/hostile × 4
    /// cues) with only the species timbre on top, so two hostile species of one size screamed alike in exactly the moments a
    /// player notices most. Now the call itself is shaped into the cue (cut short and pitched up for a hurt, slowed and
    /// fading for a death) and the bank file only sits underneath as a quiet layer. Every value that reaches a cache key is
    /// quantised (#901), so species share bakes and the LRU cap holds.
    /// </summary>
    public static partial class SampleKit
    {
        private const float CombatBankGain = 0.38f;

        /// <summary>Renders a combat cue from the species' call: cue shaping → species op → the bank file underneath →
        /// reverb tail, in one pass under one key. Falls back to the bank file (or the dry call) when a clip cannot be read.</summary>
        public static AudioClip CombatVariant(AudioClip call, AudioClip bank, CombatCue cue, VoiceOp op, int amount, int tail)
        {
            if (call == null)
            {
                return bank;
            }

            string key = $"combat|{call.name}|{(bank != null ? bank.name : "-")}|{(int)cue}|{(int)op}|{amount}|{tail}";
            if (_cache.TryGetValue(key, out var hit) && hit.Clip != null)
            {
                hit.LastUsed = Time.realtimeSinceStartup;
                return hit.Clip;
            }

            var mono = ReadMono(call);
            if (mono == null)
            {
                return bank != null ? bank : call;
            }

            try
            {
                int rate = call.frequency;
                var data = Rent(mono.Length);
                mono.AsSpan().CopyTo(data);
                data = ShapeCue(data, rate, cue);
                if (op != VoiceOp.Glide && op != VoiceOp.Truncate)
                {
                    data = Apply(op, data, rate, amount); // the cue already glides / cuts; the species colour goes on top
                }

                var under = bank != null ? ReadMono(bank) : null;
                if (under != null)
                {
                    data = MixUnder(data, under, CombatBankGain);
                }

                if (tail > 0 || cue == CombatCue.Die)
                {
                    data = Reverb(data, rate, Mathf.Max(tail, 1));
                }

                Normalise(data);
                var clip = AudioClip.Create(key, data.Length, 1, rate, false);
                clip.SetData(data, 0);
                Insert(key, clip);
                return clip;
            }
            finally
            {
                ReturnRented();
            }
        }

        private static Span<float> ShapeCue(Span<float> d, int rate, CombatCue cue)
        {
            switch (cue)
            {
                case CombatCue.Hurt:
                    return Drive(GlideBetween(Cut(d, rate, 0.25f), 1f, 1.3f), 1);
                case CombatCue.Attack:
                    return Drive(Cut(d, rate, 0.18f), 1);
                case CombatCue.Alert:
                    return GlideBetween(d, 1f, 1.15f);
                default:
                    return FadeOut(GlideBetween(d, 1f, 0.7f), 0.6f);
            }
        }

        // ── the new voice ops (#3244) — each a plain float[] → float[] pass, all WebGL-safe ───────────────

        /// <summary>A pitch bend inside the sample: amount 0 rises, 1 falls, 2 whoops (up, then down).</summary>
        private static Span<float> Glide(Span<float> d, int amount)
        {
            switch (amount)
            {
                case 0: return GlideBetween(d, 0.92f, 1.18f);
                case 1: return GlideBetween(d, 1.12f, 0.82f);
                default: return GlideBetween(GlideBetween(d, 0.95f, 1.25f), 1.1f, 0.88f);
            }
        }

        /// <summary>Resamples with a playback rate moving linearly from <paramref name="from"/> to <paramref name="to"/> —
        /// pitch and speed together, like a sampler's pitch wheel.</summary>
        private static Span<float> GlideBetween(Span<float> d, float from, float to)
        {
            int n = d.Length;
            if (n < 2)
            {
                return d;
            }

            float slowest = Mathf.Min(from, to);
            var wet = Rent(Mathf.CeilToInt(n / Mathf.Max(0.3f, slowest)) + 2);
            float pos = 0f;
            int i = 0;
            while (pos < n - 1 && i < wet.Length)
            {
                int s0 = (int)pos;
                float frac = pos - s0;
                wet[i++] = d[s0] * (1f - frac) + d[s0 + 1] * frac;
                pos += Mathf.Lerp(from, to, pos / n);
            }

            return wet.Slice(0, i);
        }

        /// <summary>Real pitch vibrato: a short delay line swept by a sine (5–8 Hz).</summary>
        private static Span<float> Vibrato(Span<float> d, int rate, int amount)
        {
            float hz = new[] { 5f, 6.5f, 8f }[amount];
            float depth = new[] { 0.0012f, 0.0022f, 0.0035f }[amount] * rate;
            var wet = Rent(d.Length);
            float step = 2f * Mathf.PI * hz / rate;
            for (int i = 0; i < d.Length; i++)
            {
                float pos = i - depth * (1f + Mathf.Sin(step * i));
                if (pos < 0f)
                {
                    wet[i] = d[i];
                    continue;
                }

                int s0 = (int)pos;
                float frac = pos - s0;
                wet[i] = s0 + 1 < d.Length ? d[s0] * (1f - frac) + d[s0 + 1] * frac : d[s0];
            }

            return wet;
        }

        /// <summary>The onset (40/60/80 ms) repeated 2/3/4 times before the whole sample — a rattle, a cackle.</summary>
        private static Span<float> Stutter(Span<float> d, int rate, int amount)
        {
            int grain = Mathf.Min(d.Length, Mathf.RoundToInt(new[] { 0.04f, 0.06f, 0.08f }[amount] * rate));
            int repeats = amount + 2;
            var wet = Rent(grain * repeats + d.Length);
            int fade = Mathf.Max(1, grain / 6);
            for (int r = 0; r < repeats; r++)
            {
                for (int i = 0; i < grain; i++)
                {
                    float env = Mathf.Min(1f, Mathf.Min(i, grain - 1 - i) / (float)fade);
                    wet[r * grain + i] = d[i] * env * (0.75f + 0.25f * r / repeats);
                }
            }

            d.CopyTo(wet.Slice(grain * repeats));
            return wet;
        }

        /// <summary>Cut short (180/250/350 ms) with a fade — a clipped yip.</summary>
        private static Span<float> Truncate(Span<float> d, int rate, int amount) => Cut(d, rate, new[] { 0.18f, 0.25f, 0.35f }[amount]);

        private static Span<float> Cut(Span<float> d, int rate, float seconds)
        {
            int keep = Mathf.Min(d.Length, Mathf.Max(1, Mathf.RoundToInt(seconds * rate)));
            var cut = d.Slice(0, keep);
            int fade = Mathf.Min(keep, Mathf.RoundToInt(0.04f * rate));
            for (int i = 0; i < fade; i++)
            {
                cut[keep - 1 - i] *= i / (float)Mathf.Max(1, fade);
            }

            return cut;
        }

        private static Span<float> FadeOut(Span<float> d, float share)
        {
            int start = Mathf.Clamp((int)(d.Length * (1f - share)), 0, d.Length);
            int len = Mathf.Max(1, d.Length - start);
            for (int i = start; i < d.Length; i++)
            {
                d[i] *= 1f - (i - start) / (float)len;
            }

            return d;
        }

        /// <summary>Mixes a second sample in at a fixed gain from the start (no detune, no offset).</summary>
        private static Span<float> MixUnder(Span<float> top, float[] under, float gain)
        {
            var wet = Rent(Mathf.Max(top.Length, under.Length));
            top.CopyTo(wet);
            for (int i = 0; i < under.Length; i++)
            {
                wet[i] += under[i] * gain;
            }

            return wet;
        }
    }
}
