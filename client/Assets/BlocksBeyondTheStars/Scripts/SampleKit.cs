// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Offline sampler effects (#877/#903): renders an effect INTO a new <see cref="AudioClip"/> instead
    /// of attaching a filter component, because Unity Web silently ignores every audio filter component
    /// (AudioReverbFilter/AudioLowPassFilter/… — #878). The bake path (GetData → C# DSP → Create/SetData)
    /// is Web-supported for Decompress-On-Load clips — the same mechanism <see cref="ProceduralAudio"/>
    /// already uses.
    ///
    /// <para><b>Memory discipline (#901).</b> Unity never garbage-collects assets created in code, and
    /// <c>Resources.UnloadUnusedAssets</c> cannot free anything this cache still references — so the cache
    /// is capped (<see cref="MaxEntries"/>, least-recently-used evicted) and cleared outright on world
    /// teardown via <see cref="ClearCache"/>. Both are load-bearing: species voices bake per (call,
    /// op-chain), and `GameBootstrap` is not destroyed on interplanetary travel, so a planet-hopping
    /// session would otherwise never release a single clip.</para>
    ///
    /// <para>Everything that reaches a cache key is quantised on purpose (see <see cref="CreatureVoice"/>):
    /// a continuous op amount would give every species a bespoke bake and the cache could never hit.</para>
    /// </summary>
    public static class SampleKit
    {
        /// <summary>LRU ceiling. A baked ~1.2 s mono clip is ~200 KB, so this bounds the sampler at
        /// roughly 13 MB even if a player never returns to the menu.</summary>
        private const int MaxEntries = 64;

        /// <summary>An entry is only evictable once it has gone unused for longer than any clip can play,
        /// so eviction can never destroy an AudioClip that is still coming out of a speaker.</summary>
        private const float EvictAfterSeconds = 6f;

        private sealed class Entry
        {
            public AudioClip Clip;
            public float LastUsed;
        }

        private static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();
        private static readonly List<string> _evictScratch = new List<string>();

        /// <summary>A dripping-cavern tail for cave-dweller calls (replaces AudioReverbFilter.Cave):
        /// small Schroeder reverb — 4 parallel feedback combs into 2 series allpasses, ~0.9 s tail.</summary>
        public static AudioClip CaveReverb(AudioClip src) => Variant(src, null, VoiceOp.None, 0, tail: 2);

        /// <summary>An underwater-muffled variant (replaces the per-one-shot AudioLowPassFilter):
        /// one-pole low-pass at the same 680 Hz cutoff the desktop bus filter uses.</summary>
        public static AudioClip Muffle(AudioClip src) => Variant(src, null, VoiceOp.Dull, 2, tail: 0);

        /// <summary>
        /// Renders one species voice variant: optional layer mix → timbre op → reverb tail, composed in a
        /// SINGLE pass under ONE cache key. Chaining cached bakes instead would double the entry count for
        /// no audible gain.
        /// </summary>
        /// <param name="src">The base sample.</param>
        /// <param name="layer">A second sample mixed in behind it, or null.</param>
        /// <param name="op">The species timbre op.</param>
        /// <param name="amount">Quantised op intensity, 0–2.</param>
        /// <param name="tail">Quantised reverb amount, 0–2.</param>
        /// <param name="layerOffsetMs">How far behind the base sample the layer sits.</param>
        /// <param name="layerDetune">Quantised layer detune step, 0–2.</param>
        public static AudioClip Variant(AudioClip src, AudioClip layer, VoiceOp op, int amount, int tail,
            int layerOffsetMs = 0, int layerDetune = 0)
        {
            if (src == null)
            {
                return null;
            }

            if (op == VoiceOp.None && tail <= 0 && layer == null)
            {
                return src; // nothing to render — play the sample as it is, for free
            }

            string key = $"{src.name}|{(layer != null ? layer.name : "-")}|{layerOffsetMs}|{layerDetune}|{(int)op}|{amount}|{tail}";
            if (_cache.TryGetValue(key, out var hit) && hit.Clip != null)
            {
                hit.LastUsed = Time.realtimeSinceStartup;
                return hit.Clip;
            }

            // Mono throughout: these are 3D point sources, so a stereo bake doubles both the memory and the
            // DSP cost for something the spatialiser collapses anyway.
            var data = ReadMono(src);
            if (data == null)
            {
                return src; // unreadable (not Decompress-On-Load) — play the dry clip rather than nothing
            }

            int rate = src.frequency;
            if (layer != null)
            {
                var lay = ReadMono(layer);
                if (lay != null)
                {
                    data = MixLayer(data, lay, rate, layerOffsetMs, layerDetune);
                }
            }

            data = Apply(op, data, rate, amount);
            if (tail > 0)
            {
                data = Reverb(data, rate, tail);
            }

            Normalise(data);
            var clip = AudioClip.Create(key, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            Insert(key, clip);
            return clip;
        }

        /// <summary>Frees every baked clip. MUST be called on world teardown (GameBootstrap.OnDestroy):
        /// this is a static cache holding code-created assets, so nothing else can ever release them —
        /// the same trap the icon/atlas caches document (#423).</summary>
        public static void ClearCache()
        {
            foreach (var entry in _cache.Values)
            {
                if (entry.Clip != null)
                {
                    Object.Destroy(entry.Clip);
                }
            }

            _cache.Clear();
        }

        /// <summary>How many variants are currently resident — used by the client's audio diagnostics.</summary>
        public static int CachedCount => _cache.Count;

        private static void Insert(string key, AudioClip clip)
        {
            _cache[key] = new Entry { Clip = clip, LastUsed = Time.realtimeSinceStartup };
            if (_cache.Count <= MaxEntries)
            {
                return;
            }

            // Evict the least recently used entries that are old enough to be certainly silent. If nothing
            // qualifies the cache is briefly allowed over its cap — correctness beats the ceiling here.
            float now = Time.realtimeSinceStartup;
            _evictScratch.Clear();
            while (_cache.Count - _evictScratch.Count > MaxEntries)
            {
                string oldest = null;
                float oldestAt = float.MaxValue;
                foreach (var kv in _cache)
                {
                    if (_evictScratch.Contains(kv.Key) || now - kv.Value.LastUsed < EvictAfterSeconds)
                    {
                        continue;
                    }

                    if (kv.Value.LastUsed < oldestAt)
                    {
                        oldestAt = kv.Value.LastUsed;
                        oldest = kv.Key;
                    }
                }

                if (oldest == null)
                {
                    break;
                }

                _evictScratch.Add(oldest);
            }

            foreach (var key2 in _evictScratch)
            {
                if (_cache.TryGetValue(key2, out var entry))
                {
                    if (entry.Clip != null)
                    {
                        Object.Destroy(entry.Clip);
                    }

                    _cache.Remove(key2);
                }
            }
        }

        /// <summary>Decodes a clip to mono float PCM. Returns null when the clip is not readable — which on
        /// Web means it was not imported Decompress-On-Load.</summary>
        private static float[] ReadMono(AudioClip src)
        {
            var raw = new float[src.samples * src.channels];
            if (!src.GetData(raw, 0))
            {
                return null;
            }

            if (src.channels == 1)
            {
                return raw;
            }

            var mono = new float[src.samples];
            int ch = src.channels;
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0f;
                for (int c = 0; c < ch; c++)
                {
                    sum += raw[i * ch + c];
                }

                mono[i] = sum / ch;
            }

            return mono;
        }

        // ── species timbre ops (#903) — each a plain float[] → float[] pass, all WebGL-safe ────────────

        private static float[] Apply(VoiceOp op, float[] d, int rate, int amount)
        {
            int a = Mathf.Clamp(amount, 0, 2);
            switch (op)
            {
                case VoiceOp.Drive: return Drive(d, a);
                case VoiceOp.Tremolo: return Tremolo(d, rate, a);
                case VoiceOp.Comb: return Comb(d, rate, a);
                case VoiceOp.Dull: return LowPass(d, rate, new[] { 2600f, 1400f, 680f }[a]);
                case VoiceOp.Thin: return HighPass(d, rate, new[] { 500f, 900f, 1500f }[a]);
                case VoiceOp.Crush: return Crush(d, a);
                case VoiceOp.ReverseTail: return ReverseTail(d, a);
                case VoiceOp.Shape: return Shape(d, rate, a);
                default: return d;
            }
        }

        /// <summary>Algebraic soft-clip — a throaty, saturated edge for hostile fauna.</summary>
        private static float[] Drive(float[] d, int amount)
        {
            float g = new[] { 2.5f, 5f, 9f }[amount];
            for (int i = 0; i < d.Length; i++)
            {
                float x = d[i] * g;
                d[i] = x / (1f + Mathf.Abs(x));
            }

            return d;
        }

        /// <summary>Amplitude modulation — the alien warble, and what a limbless buzzing body reads as.</summary>
        private static float[] Tremolo(float[] d, int rate, int amount)
        {
            float hz = new[] { 7f, 12f, 19f }[amount];
            float depth = new[] { 0.35f, 0.55f, 0.75f }[amount];
            float step = 2f * Mathf.PI * hz / rate;
            for (int i = 0; i < d.Length; i++)
            {
                d[i] *= 1f - depth + depth * (0.5f + 0.5f * Mathf.Sin(step * i));
            }

            return d;
        }

        /// <summary>Feed-forward comb — evenly spaced notches read as metallic/insectoid resonance. Chosen
        /// over a feedback comb because it cannot ring away into instability.</summary>
        private static float[] Comb(float[] d, int rate, int amount)
        {
            int delay = Mathf.Max(1, Mathf.RoundToInt(new[] { 1.2f, 2.2f, 3.5f }[amount] * 0.001f * rate));
            float g = new[] { 0.6f, 0.75f, 0.9f }[amount];
            var wet = new float[d.Length];
            for (int i = 0; i < d.Length; i++)
            {
                wet[i] = d[i] + (i >= delay ? d[i - delay] * g : 0f);
            }

            return wet;
        }

        /// <summary>Bit + sample-rate reduction — chittery and slightly artificial, good on insect bodies.</summary>
        private static float[] Crush(float[] d, int amount)
        {
            float levels = new[] { 64f, 24f, 10f }[amount];
            int hold = new[] { 2, 3, 5 }[amount];
            float held = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                if (i % hold == 0)
                {
                    held = Mathf.Round(d[i] * levels) / levels;
                }

                d[i] = held;
            }

            return d;
        }

        /// <summary>Appends a reversed, quieter echo of the sample. Baked rather than played at a negative
        /// pitch because Unity Web forbids negative pitch outright.</summary>
        private static float[] ReverseTail(float[] d, int amount)
        {
            float gain = new[] { 0.35f, 0.5f, 0.7f }[amount];
            int gap = d.Length / 8;
            var wet = new float[d.Length + gap + d.Length];
            System.Array.Copy(d, wet, d.Length);
            for (int i = 0; i < d.Length; i++)
            {
                float fade = 1f - i / (float)d.Length;
                wet[d.Length + gap + i] += d[d.Length - 1 - i] * gain * fade;
            }

            return wet;
        }

        /// <summary>Envelope reshape: a clipped bark at amount 0, a slow drawn-out swell at amount 2 — the
        /// difference between an animal that snaps and one that sighs, from the same sample.</summary>
        private static float[] Shape(float[] d, int rate, int amount)
        {
            int n = d.Length;
            if (amount == 0)
            {
                int keep = Mathf.Max(rate / 8, n / 3); // a short bark, then a fast fade
                var bark = new float[Mathf.Min(n, keep)];
                System.Array.Copy(d, bark, bark.Length);
                int fade = bark.Length / 4;
                for (int i = 0; i < fade; i++)
                {
                    bark[bark.Length - 1 - i] *= i / (float)fade;
                }

                return bark;
            }

            float attack = amount == 1 ? 0.18f : 0.35f; // fraction of the clip spent swelling in
            int att = Mathf.Max(1, (int)(n * attack));
            for (int i = 0; i < n; i++)
            {
                float env = i < att ? i / (float)att : 1f - 0.55f * ((i - att) / (float)Mathf.Max(1, n - att));
                d[i] *= env;
            }

            return d;
        }

        // ── layering (#904) ────────────────────────────────────────────────────────────────────────────

        /// <summary>Mixes a second sample in behind the first, offset and detuned — the only op here that
        /// truly multiplies the palette, because it combines two samples rather than modulating one.</summary>
        private static float[] MixLayer(float[] baseData, float[] layerData, int rate, int offsetMs, int detune)
        {
            float ratio = new[] { 0.82f, 1f, 1.28f }[Mathf.Clamp(detune, 0, 2)];
            int offset = Mathf.Max(0, Mathf.RoundToInt(offsetMs * 0.001f * rate));
            int layerLen = Mathf.Max(1, (int)(layerData.Length / ratio));
            var wet = new float[Mathf.Max(baseData.Length, offset + layerLen)];
            System.Array.Copy(baseData, wet, baseData.Length);
            for (int i = 0; i < layerLen; i++)
            {
                // Linear resample of the layer — pitch and speed move together, exactly like a sampler.
                float srcPos = i * ratio;
                int s0 = (int)srcPos;
                if (s0 + 1 >= layerData.Length)
                {
                    break;
                }

                float frac = srcPos - s0;
                float v = layerData[s0] * (1f - frac) + layerData[s0 + 1] * frac;
                wet[offset + i] += v * 0.55f;
            }

            return wet;
        }

        // ── space / filters ───────────────────────────────────────────────────────────────────────────

        /// <summary>Small Schroeder cave reverb: 4 mutually-prime feedback combs into 2 series allpasses.
        /// The tail length and wet mix scale with <paramref name="amount"/> so a species can have "a little
        /// space" without sounding like it lives in a cavern.</summary>
        private static float[] Reverb(float[] dry, int rate, int amount)
        {
            float[] combMs = { 29.7f, 37.1f, 41.1f, 43.7f };
            float[] combGain = { 0.72f, 0.70f, 0.68f, 0.66f };
            float tailSec = new[] { 0f, 0.45f, 0.9f }[Mathf.Clamp(amount, 0, 2)];
            float wetMix = new[] { 0f, 0.22f, 0.45f }[Mathf.Clamp(amount, 0, 2)];
            int tail = Mathf.CeilToInt(tailSec * rate);
            var wet = new float[dry.Length + tail];

            var combBuf = new float[combMs.Length][];
            for (int c = 0; c < combMs.Length; c++)
            {
                combBuf[c] = new float[Mathf.Max(1, Mathf.RoundToInt(combMs[c] * 0.001f * rate))];
            }

            for (int i = 0; i < wet.Length; i++)
            {
                float x = i < dry.Length ? dry[i] : 0f;
                float sum = 0f;
                for (int c = 0; c < combBuf.Length; c++)
                {
                    var buf = combBuf[c];
                    int j = i % buf.Length;
                    float y = x + buf[j] * combGain[c]; // buf[j] still holds y from one delay ago
                    buf[j] = y;
                    sum += y;
                }

                wet[i] = sum * 0.25f;
            }

            Allpass(wet, Mathf.RoundToInt(0.0050f * rate), 0.7f);
            Allpass(wet, Mathf.RoundToInt(0.0017f * rate), 0.7f);

            for (int i = 0; i < wet.Length; i++)
            {
                float x = i < dry.Length ? dry[i] : 0f;
                wet[i] = Mathf.Clamp(x * 0.65f + wet[i] * wetMix, -1f, 1f);
            }

            return wet;
        }

        /// <summary>In-place series allpass (y = z − g·w, w = x + g·z) — smears the comb output into a dense
        /// tail without colouring the spectrum.</summary>
        private static void Allpass(float[] s, int delaySamples, float g)
        {
            var buf = new float[Mathf.Max(1, delaySamples)];
            for (int i = 0; i < s.Length; i++)
            {
                int j = i % buf.Length;
                float z = buf[j];
                float w = s[i] + g * z;
                buf[j] = w;
                s[i] = z - g * w;
            }
        }

        private static float[] LowPass(float[] d, int rate, float cutoff)
        {
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / rate);
            float state = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                state += a * (d[i] - state);
                d[i] = state;
            }

            return d;
        }

        private static float[] HighPass(float[] d, int rate, float cutoff)
        {
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / rate);
            float state = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                state += a * (d[i] - state);
                d[i] -= state;
            }

            return d;
        }

        /// <summary>Levels the bake back to the source's headroom. Drive and layering both add gain, and an
        /// un-levelled variant would make a species louder rather than different.</summary>
        private static void Normalise(float[] d)
        {
            float peak = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float v = Mathf.Abs(d[i]);
                if (v > peak)
                {
                    peak = v;
                }
            }

            if (peak <= 0.0001f || peak <= 0.95f)
            {
                return;
            }

            float scale = 0.95f / peak;
            for (int i = 0; i < d.Length; i++)
            {
                d[i] *= scale;
            }
        }
    }
}
