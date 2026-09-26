// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A code-synthesis sound library (M26 sound pass). Generates <see cref="AudioClip"/>s for every
    /// gameplay cue, ambience bed and looping texture entirely in code — so the game is fully audible
    /// with no recorded assets. <see cref="ClientAudio"/> fills any cue that has no bundled recording
    /// with the procedural version (recordings, when present, take priority). Loops are crossfaded at
    /// the seam so they tile without a click.
    /// </summary>
    public static class ProceduralAudio
    {
        private const int Rate = 44100;

        /// <summary>Every id this library can synthesize (used to pre-fill the cue table).</summary>
        public static readonly string[] KnownIds =
        {
            // mining / building
            "mine_stone", "mine_metal", "mine_crystal", "mine_dirt", "place_block",
            // combat / damage
            "asteroid_break", "ship_destroyed", "ship_shield_hit", "ship_hull_hit", "hurt_player",
            "ship_laser", "ship_mine",
            "thunder_1", "thunder_2", "thunder_3",
            // ui / vitals
            "ui_hover", "ui_click", "ui_confirm", "ui_back", "o2_warning", "tech_unlock", "hull_alarm",
            "vitals_warning", "research_available", "ui_success",
            // ship / space
            "hyperspace_jump", "station_board", "scan_ping", "lamp_toggle", "teleport",
            "ship_launch", "ship_landing",
            // loops (ambience / textures)
            "wind_light", "amb_forest", "amb_desert", "amb_ice", "amb_lava", "amb_swamp",
            "storm_loop", "rain_loop", "lava_bubble", "water_shore", "drill_loop", "engine_idle",
        };

        public static AudioClip Generate(string id) => id switch
        {
            "ai_blip" => Beep("ai_blip", 880f, 0.16f, 0.30f), // VEGA radio chirp fallback (real cue is bundled)
            "mine_stone" => NoiseHit("mine_stone", 0.16f, 0.45f, 1500f, 26f),
            "mine_dirt" => NoiseHit("mine_dirt", 0.16f, 0.40f, 700f, 30f),
            "mine_metal" => Clang("mine_metal", 380f, 0.22f, 0.40f),
            "mine_crystal" => Clang("mine_crystal", 1100f, 0.30f, 0.32f),
            "place_block" => Thud("place_block", 0.14f, 0.45f),
            "asteroid_break" => Explosion("asteroid_break", 0.55f, 0.5f),
            "ship_laser" => Zap("ship_laser", 0.16f, 0.5f),       // a quick combat laser "pew"
            "ship_mine" => Zap("ship_mine", 0.26f, 0.35f),        // a steadier mining beam zap
            "ship_destroyed" => Explosion("ship_destroyed", 1.3f, 0.7f),
            "ship_shield_hit" => Zap("ship_shield_hit", 0.28f, 0.5f),
            "ship_hull_hit" => Clang("ship_hull_hit", 200f, 0.3f, 0.5f),
            "hurt_player" => Hurt("hurt_player"),
            "thunder_1" => Thunder("thunder_1", 1),
            "thunder_2" => Thunder("thunder_2", 2),
            "thunder_3" => Thunder("thunder_3", 3),
            "ui_hover" => Beep("ui_hover", 1150f, 0.05f, 0.12f),
            "ui_click" => Beep("ui_click", 760f, 0.07f, 0.22f),
            "ui_confirm" => Arp("ui_confirm", 0.20f, 0.22f, up: true),
            "ui_back" => Arp("ui_back", 0.20f, 0.22f, up: false),
            "o2_warning" => Alarm("o2_warning", 950f, 0.5f),
            "tech_unlock" => Arp("tech_unlock", 0.45f, 0.30f, up: true), // longer rising fanfare than ui_confirm
            "research_available" => Chime("research_available"), // "you can research something new" ping (#763)
            "ui_success" => Arp("ui_success", 0.30f, 0.26f, up: true), // achievement toast — was a silent no-op (#763)
            "hull_alarm" => Alarm("hull_alarm", 640f, 0.45f), // deeper than the O₂ warning
            "vitals_warning" => Alarm("vitals_warning", 780f, 0.4f), // below-10 % vitals blink (#753), between the two
            "hyperspace_jump" => Warp("hyperspace_jump"),
            "station_board" => Airlock("station_board"),
            "scan_ping" => Ping("scan_ping"),
            "lamp_toggle" => Beep("lamp_toggle", 520f, 0.06f, 0.25f),
            "teleport" => Shimmer("teleport"),
            "ship_launch" => Roar("ship_launch", rising: true),
            "ship_landing" => Roar("ship_landing", rising: false),
            "wind_light" => WindLoop("wind_light", 4f, 0.18f, 0.35f),
            "amb_forest" => WindLoop("amb_forest", 4f, 0.16f, 0.5f, chirp: true),
            "amb_desert" => WindLoop("amb_desert", 4f, 0.20f, 0.25f),
            "amb_ice" => WindLoop("amb_ice", 4f, 0.22f, 0.7f),
            "amb_lava" => WindLoop("amb_lava", 4f, 0.18f, 0.18f, rumble: true),
            "amb_swamp" => WindLoop("amb_swamp", 4f, 0.14f, 0.4f, chirp: true),
            "storm_loop" => RainLoop("storm_loop", 0.45f),
            "rain_loop" => RainLoop("rain_loop", 0.28f),
            "lava_bubble" => BubbleLoop("lava_bubble"),
            "water_shore" => WindLoop("water_shore", 4f, 0.20f, 0.8f),
            "drill_loop" => DrillLoop("drill_loop"),
            "engine_idle" => EngineLoop("engine_idle"),
            // #2052 Crystal Net device cues — code-synthesized fallbacks; the bundled ElevenLabs clips take priority.
            "crystal_switch" => Thud("crystal_switch", 0.10f, 0.5f),
            "crystal_button" => Beep("crystal_button", 1320f, 0.08f, 0.3f),
            "alarm_siren_0" => Siren("alarm_siren_0", 620f, 880f, 1.2f),
            "alarm_siren_1" => Siren("alarm_siren_1", 440f, 520f, 0.5f),
            "alarm_siren_2" => Siren("alarm_siren_2", 700f, 1100f, 2.0f),
            "chime_0" => Chime("chime_0"),
            "chime_1" => Arp("chime_1", 0.5f, 0.35f, up: false),
            "chime_2" => Beep("chime_2", 1568f, 0.6f, 0.3f),
            "chime_3" => Clang("chime_3", 1046f, 0.5f, 0.35f),
            "horn_0" => Horn("horn_0", 110f, 1.6f),
            "horn_1" => Horn("horn_1", 220f, 1.0f),
            "horn_2" => Horn("horn_2", 82f, 1.2f),
            "caller_whistle" => Arp("caller_whistle", 0.5f, 0.35f, up: true),
            "fabricator_craft" => Clang("fabricator_craft", 520f, 0.35f, 0.35f),
            "clone_tank_bubble" => Loop("clone_tank_bubble", 2.0f, t => (Mathf.Sin(t * 37f) * Mathf.Sin(t * 5.3f) > 0.85f ? 0.5f : 0f) * Mathf.Sin(t * 2200f) * 0.25f),
            "auto_drill_loop" => Loop("auto_drill_loop", 1.5f, t => (Mathf.Sin(t * 620f) * 0.35f + Mathf.Sin(t * 47f) * 0.15f) * 0.5f),
            var note when note.StartsWith("note_", System.StringComparison.Ordinal) => Note(note),
            _ => null,
        };

        // ── primitives ───────────────────────────────────────────────────────────────────────


        // --- Crystal Net helpers (#2052) ---

        /// <summary>The Crystal Net's device cue ids the synthesizer can stand in for (the melody notes are
        /// generated on demand from their id, see <see cref="Note"/>).</summary>
        public static readonly string[] CrystalIds =
        {
            "crystal_switch", "crystal_button", "alarm_siren_0", "alarm_siren_1", "alarm_siren_2",
            "chime_0", "chime_1", "chime_2", "chime_3", "horn_0", "horn_1", "horn_2",
            "caller_whistle", "fabricator_craft", "clone_tank_bubble", "auto_drill_loop",
        };

        /// <summary>The eight-note scale of the melody block (C4 … C5) and its four instruments.</summary>
        private static readonly float[] NoteHz = { 261.63f, 293.66f, 329.63f, 349.23f, 392.00f, 440.00f, 493.88f, 523.25f };

        /// <summary>A melody-block note from its id <c>note_&lt;instrument&gt;_&lt;index&gt;</c>: 0 crystal (sine),
        /// 1 bell (clang), 2 bass (an octave down), 3 blip (short square-ish).</summary>
        private static AudioClip Note(string id)
        {
            var parts = id.Split('_');
            int inst = parts.Length > 1 && int.TryParse(parts[1], out int i0) ? Mathf.Clamp(i0, 0, 3) : 0;
            int idx = parts.Length > 2 && int.TryParse(parts[2], out int i1) ? Mathf.Clamp(i1, 0, NoteHz.Length - 1) : 0;
            float hz = NoteHz[idx];
            switch (inst)
            {
                case 1: return Clang(id, hz * 2f, 0.6f, 0.35f);
                case 2: return Beep(id, hz * 0.5f, 0.45f, 0.4f);
                case 3: return Buf(id, 0.18f, d =>
                {
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)Rate;
                        d[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * hz * t)) * Mathf.Exp(-t * 18f) * 0.22f;
                    }
                });
                default: return Beep(id, hz, 0.5f, 0.35f);
            }
        }

        /// <summary>A siren: the pitch sweeps between two frequencies over <paramref name="period"/> seconds, looped.</summary>
        private static AudioClip Siren(string name, float lo, float hi, float period) => Loop(name, period, t =>
        {
            float phase = Mathf.PingPong(t / period * 2f, 1f);
            float hz = Mathf.Lerp(lo, hi, phase);
            return Mathf.Sin(2f * Mathf.PI * hz * t) * 0.3f;
        });

        /// <summary>A horn: a fat low tone with a soft attack and a long tail.</summary>
        private static AudioClip Horn(string name, float hz, float dur) => Buf(name, dur, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t * 8f) * Mathf.Exp(-Mathf.Max(0f, t - dur * 0.6f) * 6f);
                d[i] = (Mathf.Sin(2f * Mathf.PI * hz * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * hz * 2f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * hz * 3f * t)) * env * 0.25f;
            }
        });

        /// <summary>A seamless loop of <paramref name="seconds"/> from a sample function of time.</summary>
        private static AudioClip Loop(string name, float seconds, System.Func<float, float> f) => Buf(name, seconds, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                d[i] = f(i / (float)Rate);
            }

            LoopFade(d, Rate / 20);
        });

        /// <summary>Every melody-block note id (four instruments × eight notes), pre-filled into the cue table.</summary>
        public static readonly string[] NoteIds =
        {
            "note_0_0",
            "note_0_1",
            "note_0_2",
            "note_0_3",
            "note_0_4",
            "note_0_5",
            "note_0_6",
            "note_0_7",
            "note_1_0",
            "note_1_1",
            "note_1_2",
            "note_1_3",
            "note_1_4",
            "note_1_5",
            "note_1_6",
            "note_1_7",
            "note_2_0",
            "note_2_1",
            "note_2_2",
            "note_2_3",
            "note_2_4",
            "note_2_5",
            "note_2_6",
            "note_2_7",
            "note_3_0",
            "note_3_1",
            "note_3_2",
            "note_3_3",
            "note_3_4",
            "note_3_5",
            "note_3_6",
            "note_3_7",
        };

        private static AudioClip Buf(string name, float seconds, System.Action<float[]> fill)
        {
            int n = Mathf.CeilToInt(Rate * seconds);
            var data = new float[n];
            fill(data);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Order-dependent string hash that is stable across sessions, runtimes and machines —
        /// .NET string hash codes are randomised per process, which would re-roll the noise character of
        /// every synthesized sound between runs (#720; same convention as NpcView/FloraTints).</summary>
        private static int StableHash(string s)
        {
            int h = 17;
            foreach (char c in s ?? string.Empty)
            {
                h = unchecked(h * 31 + c);
            }

            return h;
        }

        /// <summary>Crossfades the tail into the head so the clip loops without a click.</summary>
        private static void LoopFade(float[] d, int fade)
        {
            fade = Mathf.Min(fade, d.Length / 2);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                int j = d.Length - fade + i;
                d[j] = d[j] * (1f - k) + d[i] * k;
            }
        }

        private static AudioClip Beep(string name, float freq, float dur, float vol) => Buf(name, dur, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 18f) * vol;
            }
        });

        /// <summary>Two urgent beeps with a short gap — the low-oxygen warning cue.</summary>
        private static AudioClip Alarm(string name, float freq, float vol) => Buf(name, 0.46f, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float lt = t < 0.23f ? t : t - 0.23f;  // the envelope restarts for the second beep
                float gate = lt < 0.14f ? 1f : 0f;
                d[i] = Mathf.Sin(2f * Mathf.PI * freq * lt) * Mathf.Exp(-lt * 22f) * gate * vol;
            }
        });

        private static AudioClip Arp(string name, float dur, float vol, bool up) => Buf(name, dur, d =>
        {
            float[] notes = up ? new[] { 523f, 659f, 784f } : new[] { 784f, 659f, 523f };
            int seg = d.Length / notes.Length;
            for (int i = 0; i < d.Length; i++)
            {
                int n = Mathf.Min(notes.Length - 1, i / seg);
                float lt = (i - n * seg) / (float)Rate;
                d[i] = Mathf.Sin(2f * Mathf.PI * notes[n] * (i / (float)Rate)) * Mathf.Exp(-lt * 16f) * vol;
            }
        });

        /// <summary>Two soft bell notes a fifth apart plus a faint high shimmer — the "new research
        /// available" discovery ping (#763): brighter than ui_confirm, calmer than the tech_unlock fanfare.</summary>
        private static AudioClip Chime(string name) => Buf(name, 0.55f, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float a = Mathf.Sin(2f * Mathf.PI * 1046.5f * t) * Mathf.Exp(-t * 7f);              // C6
                float b = t > 0.16f
                    ? Mathf.Sin(2f * Mathf.PI * 1568f * (t - 0.16f)) * Mathf.Exp(-(t - 0.16f) * 6f) // G6
                    : 0f;
                float shimmer = Mathf.Sin(2f * Mathf.PI * 3135f * t) * Mathf.Exp(-t * 12f) * 0.15f;
                d[i] = (a * 0.55f + b * 0.6f + shimmer) * 0.3f;
            }
        });

        private static AudioClip NoiseHit(string name, float dur, float vol, float cutoff, float decay) => Buf(name, dur, d =>
        {
            var rng = new System.Random(StableHash(name));
            float lp = 0f, a = Mathf.Clamp01(cutoff / Rate * 6f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * a;
                d[i] = lp * Mathf.Exp(-t * decay) * vol;
            }
        });

        private static AudioClip Thud(string name, float dur, float vol) => Buf(name, dur, d =>
        {
            var rng = new System.Random(StableHash(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float body = Mathf.Sin(2f * Mathf.PI * (90f - t * 120f) * t);
                float n = (float)(rng.NextDouble() * 2 - 1) * 0.4f;
                d[i] = (body + n) * Mathf.Exp(-t * 22f) * vol;
            }
        });

        private static AudioClip Clang(string name, float freq, float dur, float vol) => Buf(name, dur, d =>
        {
            float[] partials = { 1f, 2.76f, 5.4f, 8.9f };
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int p = 0; p < partials.Length; p++)
                {
                    s += Mathf.Sin(2f * Mathf.PI * freq * partials[p] * t) * Mathf.Exp(-t * (8f + p * 6f));
                }

                d[i] = s * 0.3f * vol;
            }
        });

        private static AudioClip Explosion(string name, float dur, float vol) => Buf(name, dur, d =>
        {
            var rng = new System.Random(StableHash(name));
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.05f; // low rumble
                float rumble = Mathf.Sin(2f * Mathf.PI * (60f - t * 30f) * t) * 0.5f;
                d[i] = (lp * 1.4f + rumble) * Mathf.Exp(-t * 4.5f) * vol;
            }
        });

        private static AudioClip Zap(string name, float dur, float vol) => Buf(name, dur, d =>
        {
            var rng = new System.Random(7);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(1800f, 300f, t / dur);
                float n = (float)(rng.NextDouble() * 2 - 1) * 0.25f;
                d[i] = (Mathf.Sin(2f * Mathf.PI * f * t) + n) * Mathf.Exp(-t * 9f) * vol;
            }
        });

        private static AudioClip Hurt(string name) => Buf(name, 0.3f, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(420f, 180f, t / 0.3f);
                d[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 8f) * 0.5f;
            }
        });

        private static AudioClip Thunder(string name, int variant) => Buf(name, 1.6f, d =>
        {
            var rng = new System.Random(StableHash(name) + variant);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.02f;
                float env = Mathf.Exp(-t * 1.6f) * (0.6f + 0.4f * Mathf.Sin(t * 9f + variant));
                d[i] = lp * 2f * env * 0.6f;
            }
        });

        private static AudioClip Warp(string name) => Buf(name, 2.6f, d =>
        {
            var rng = new System.Random(11);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float p = t / 2.6f;
                float f = Mathf.Lerp(120f, 1400f, p * p);                 // accelerating rise
                float swell = Mathf.Sin(Mathf.PI * p);                    // in/out
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.2f;
                float boom = p > 0.8f ? Mathf.Sin(2f * Mathf.PI * 70f * t) * (p - 0.8f) * 5f : 0f;
                d[i] = (Mathf.Sin(2f * Mathf.PI * f * t) * 0.5f + lp * 0.4f + boom) * swell * 0.5f;
            }
        });

        private static AudioClip Airlock(string name) => Buf(name, 0.9f, d =>
        {
            var rng = new System.Random(3);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float hiss = (float)(rng.NextDouble() * 2 - 1);
                lp += (hiss - lp) * 0.08f;
                float hissEnv = Mathf.Exp(-Mathf.Abs(t - 0.2f) * 6f);     // pressure release
                float clunk = t > 0.6f ? Mathf.Sin(2f * Mathf.PI * 140f * t) * Mathf.Exp(-(t - 0.6f) * 20f) : 0f;
                d[i] = lp * hissEnv * 0.5f + clunk * 0.5f;
            }
        });

        private static AudioClip Ping(string name) => Buf(name, 0.35f, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float a = Mathf.Sin(2f * Mathf.PI * 1320f * t) * Mathf.Exp(-t * 10f);
                float b = t > 0.12f ? Mathf.Sin(2f * Mathf.PI * 1760f * t) * Mathf.Exp(-(t - 0.12f) * 10f) : 0f;
                d[i] = (a + b) * 0.3f;
            }
        });

        private static AudioClip Shimmer(string name) => Buf(name, 0.6f, d =>
        {
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(500f, 1800f, t / 0.6f);
                float sparkle = Mathf.Sin(2f * Mathf.PI * f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * f * 2f * t);
                d[i] = sparkle * Mathf.Sin(Mathf.PI * t / 0.6f) * 0.28f;
            }
        });

        private static AudioClip Roar(string name, bool rising) => Buf(name, 1.6f, d =>
        {
            var rng = new System.Random(StableHash(name));
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float p = t / 1.6f;
                float drive = rising ? p : 1f - p;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * (0.02f + drive * 0.08f);
                float low = Mathf.Sin(2f * Mathf.PI * (50f + drive * 60f) * t) * 0.4f;
                d[i] = (lp * 1.6f + low) * (0.3f + drive * 0.7f) * 0.5f;
            }
        });

        // ── loops ────────────────────────────────────────────────────────────────────────────

        private static AudioClip WindLoop(string name, float dur, float vol, float cutoff, bool chirp = false, bool rumble = false) => Buf(name, dur, d =>
        {
            var rng = new System.Random(StableHash(name));
            float lp = 0f, a = Mathf.Clamp01(cutoff * 0.02f + 0.01f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * a;
                float lfo = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.15f * t);
                float s = lp * lfo;
                if (rumble) s += Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.15f;
                if (chirp && rng.NextDouble() < 0.00012) s += Mathf.Sin(2f * Mathf.PI * 2400f * t) * 0.3f;
                d[i] = s * vol;
            }

            LoopFade(d, Rate / 8);
        });

        private static AudioClip RainLoop(string name, float vol) => Buf(name, 3f, d =>
        {
            var rng = new System.Random(StableHash(name));
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.4f; // brighter hiss = rain
                d[i] = lp * vol;
            }

            LoopFade(d, Rate / 8);
        });

        private static AudioClip BubbleLoop(string name) => Buf(name, 3f, d =>
        {
            var rng = new System.Random(StableHash(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float gurgle = Mathf.Sin(2f * Mathf.PI * (40f + 25f * Mathf.Sin(t * 2.3f)) * t) * 0.25f;
                float pop = rng.NextDouble() < 0.0008 ? 0.4f : 0f;
                d[i] = gurgle + pop * Mathf.Sin(2f * Mathf.PI * 200f * t);
            }

            LoopFade(d, Rate / 8);
        });

        private static AudioClip DrillLoop(string name) => Buf(name, 1f, d =>
        {
            var rng = new System.Random(StableHash(name));
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float saw = Mathf.Repeat(95f * t, 1f) * 2f - 1f;            // buzzy sawtooth
                float n = (float)(rng.NextDouble() * 2 - 1) * 0.2f;
                d[i] = (saw * 0.4f + n) * 0.4f;
            }

            LoopFade(d, Rate / 12);
        });

        private static AudioClip EngineLoop(string name) => Buf(name, 1.5f, d =>
        {
            var rng = new System.Random(StableHash(name));
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float low = Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 105f * t) * 0.2f;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.05f;
                d[i] = (low + lp * 0.5f) * 0.5f;
            }

            LoopFade(d, Rate / 10);
        });
    }
}
