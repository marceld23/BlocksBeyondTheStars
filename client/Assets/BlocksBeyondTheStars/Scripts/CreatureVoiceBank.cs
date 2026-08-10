// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Turns a species' generated <see cref="CreatureVoice"/> into playable clips (#902–#907).
    ///
    /// <para>Two jobs. First, resolving the voice: the server sends a per-world
    /// <see cref="NetCreature.VoiceSeed"/>, and when it is absent (legacy server) we hash the trait tuple
    /// instead — never the species id, which is "sp0".."sp8" and repeats on every planet, so hashing it
    /// gave the entire game nine voices per habitat.</para>
    ///
    /// <para>Second, <b>when</b> the sampler runs. <c>GetData</c> + DSP is synchronous on the main thread,
    /// so baking on the audio path would stutter the moment two species spawned in one frame — worst on
    /// WebGL, which has no second thread to hide it on. Voices are queued at spawn and rendered at most
    /// ONE PER FRAME (#901).</para>
    /// </summary>
    public static class CreatureVoiceBank
    {
        private static readonly Dictionary<int, CreatureVoice> _voices = new Dictionary<int, CreatureVoice>();
        private static readonly Queue<PendingBake> _pending = new Queue<PendingBake>();
        private static readonly HashSet<string> _queued = new HashSet<string>();

        private readonly struct PendingBake
        {
            public PendingBake(string clipId, CreatureVoice voice, bool echo)
            {
                ClipId = clipId;
                Voice = voice;
                Echo = echo;
            }

            public string ClipId { get; }

            public CreatureVoice Voice { get; }

            public bool Echo { get; }
        }

        /// <summary>The species' voice, derived once per seed and reused for every individual — two animals
        /// of one species must sound like one species, so nothing here is ever drawn per creature.</summary>
        public static CreatureVoice For(NetCreature c)
        {
            int seed = c.VoiceSeed != 0 ? c.VoiceSeed : TraitHash(c);
            if (_voices.TryGetValue(seed, out var cached))
            {
                return cached;
            }

            var audio = ClientAudio.Instance;
            var traits = new VoiceTraits(c.Habitat, c.Temperament, c.BodyPlan, c.Size, c.Legs, c.Eyes,
                c.Horns, c.BodySegments, c.Tentacles, c.HasGasSac, c.Glows);
            var voice = CreatureVoices.Derive(seed, traits, audio != null ? audio.Has : (System.Func<string, bool>)null);
            _voices[seed] = voice;
            return voice;
        }

        /// <summary>Queues the bakes this voice needs (its call, and its combat bank cues). Called when a
        /// creature first appears, so the sampler work happens before the first utterance rather than
        /// during it.</summary>
        public static void Prewarm(CreatureVoice voice, string bank, bool echo)
        {
            Enqueue(voice.Call, voice, echo);
            foreach (var cue in BankCues)
            {
                Enqueue(bank + cue, voice, echo);
            }
        }

        /// <summary>Renders at most one queued variant. Call once per frame.</summary>
        public static void Pump()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            var job = _pending.Dequeue();
            _queued.Remove(Key(job.ClipId, job.Voice, job.Echo));
            Resolve(job.ClipId, job.Voice, job.Echo);
        }

        /// <summary>The playable clip for a cue under this species' voice — the baked variant when one
        /// exists, the dry sample otherwise. Baking here is a fallback: the prewarm queue normally got
        /// there first.</summary>
        public static AudioClip Resolve(string clipId, CreatureVoice voice, bool echo)
        {
            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return null;
            }

            var src = audio.Clip(clipId);
            if (src == null)
            {
                return null;
            }

            // The layer only ever joins the species' own signature call — layering a death scream with an
            // idle chirp would read as two animals, and it would double the sampler's key space for nothing.
            bool isCall = clipId == voice.Call;
            var layer = isCall && voice.LayerCall.Length > 0 ? audio.Clip(voice.LayerCall) : null;
            int tail = Mathf.Max(voice.Tail, echo ? 2 : 0); // a cave dweller keeps its cavern regardless
            return SampleKit.Variant(src, layer, voice.Op, voice.OpAmount, tail,
                voice.LayerOffsetMs, voice.LayerDetune);
        }

        /// <summary>Pitch multiplier for one pulse of a phrase — the species' melodic shape (#902). A
        /// falling three-pulse snarl and a rising click train are unmistakably different animals even when
        /// they come off the same sample.</summary>
        public static float ContourPitch(VoiceContour contour, int index, int pulses)
        {
            if (pulses <= 1)
            {
                return 1f;
            }

            float t = index / (float)(pulses - 1); // 0..1 across the phrase
            switch (contour)
            {
                case VoiceContour.Rising: return Mathf.Lerp(0.92f, 1.10f, t);
                case VoiceContour.Falling: return Mathf.Lerp(1.10f, 0.92f, t);
                case VoiceContour.Bowl: return Mathf.Lerp(1.06f, 0.92f, 1f - Mathf.Abs(2f * t - 1f));
                case VoiceContour.Arch: return Mathf.Lerp(0.94f, 1.08f, 1f - Mathf.Abs(2f * t - 1f));
                default: return 1f;
            }
        }

        /// <summary>Drops every derived voice. Called on world teardown alongside the sampler cache so a
        /// new world does not inherit the last one's species table.</summary>
        public static void Clear()
        {
            _voices.Clear();
            _pending.Clear();
            _queued.Clear();
        }

        /// <summary>The combat/damage cues that inherit the species timbre. Without these, every large
        /// hostile creature in every world screams out of the same five files (#903).</summary>
        private static readonly string[] BankCues = { "_hurt", "_alert", "_attack", "_die" };

        private static void Enqueue(string clipId, CreatureVoice voice, bool echo)
        {
            if (!voice.NeedsBake && !echo)
            {
                return; // dry sample — nothing to render
            }

            string key = Key(clipId, voice, echo);
            if (_queued.Add(key))
            {
                _pending.Enqueue(new PendingBake(clipId, voice, echo));
            }
        }

        private static string Key(string clipId, CreatureVoice voice, bool echo)
            => clipId + "|" + voice.BakeKey + (echo ? "|e" : string.Empty);

        /// <summary>Fallback seed for a server that predates <see cref="NetCreature.VoiceSeed"/>: hash the
        /// generated trait tuple. Colour alone spans 16.7 M values, so this is effectively unique per
        /// species per world — unlike the species id, which is not unique at all.</summary>
        private static int TraitHash(NetCreature c)
        {
            unchecked
            {
                uint h = 2166136261u;
                void Feed(int v)
                {
                    h ^= (uint)v;
                    h *= 16777619u;
                }

                Feed(c.ColorRgb);
                Feed(c.BellyRgb);
                Feed(Mathf.RoundToInt(c.Size * 1000f));
                Feed(c.Legs);
                Feed(c.Eyes);
                Feed(c.Horns);
                Feed(c.BodySegments);
                Feed(c.Tentacles);
                Feed(c.HasGasSac ? 1 : 0);
                Feed(c.Glows ? 1 : 0);
                Feed(StableHash(c.Habitat));
                Feed(StableHash(c.Temperament));
                Feed(StableHash(c.BodyPlan));
                return (int)h;
            }
        }

        /// <summary>Order-dependent string hash that is stable across sessions, runtimes and machines —
        /// .NET string hash codes are randomised per process, which would re-roll every species voice
        /// between runs (#720).</summary>
        private static int StableHash(string s)
        {
            int h = 17;
            foreach (char c in s ?? string.Empty)
            {
                h = unchecked(h * 31 + c);
            }

            return h;
        }
    }
}
