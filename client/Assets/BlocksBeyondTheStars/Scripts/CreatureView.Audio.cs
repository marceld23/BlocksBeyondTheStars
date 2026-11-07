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
    /// The creatures' sounds (#902–#907, #3242–#3246), apart from the body and motion code:
    /// <list type="bullet">
    /// <item>The call phrase of the generated voice — and since #3243 a small repertoire: an alarm call when startled (and
    /// echoed by up to three herd members), a quiet content sound when fed, tamed or near its owner, a higher begging
    /// phrase.</item>
    /// <item>The calling rate follows the hour and the weather (<see cref="CreatureCallRhythm"/>): a dawn chorus, a hushed
    /// storm and a burst when it ends. An answer comes from a real herd member.</item>
    /// <item>Movement foley (#3242) — footfalls, wing beats, slithering, splashes, chewing — strictly under
    /// <see cref="CreatureFoleyGate"/>: per species only the two nearest moving animals step, at most six foley events a
    /// second for everything, so a herd never turns into a drum roll.</item>
    /// <item>Distant fauna (#3245): the planet's own species call from far off now and then.</item>
    /// </list>
    /// </summary>
    public sealed partial class CreatureView
    {
        private readonly CreatureFoleyGate _foley = new CreatureFoleyGate();
        private readonly CreatureCallRhythm _rhythm = new CreatureCallRhythm();
        private readonly List<FoleyCandidate> _foleyScratch = new List<FoleyCandidate>();
        private readonly DistantFauna _distant = new DistantFauna(System.Environment.TickCount);
        private readonly HashSet<string> _nearbySpecies = new HashSet<string>(System.StringComparer.Ordinal);
        private System.Func<string, bool> _speciesNearbyFn;
        private bool _faunaSubscribed;
        private float _waterProbeAt;
        private bool _nearWater;
        private AudioSource _slither;

        private const float SlitherRange = 10f;
        private const float ChewRange = 7f;
        private const float AlarmRange = 25f;
        private const int AlarmEchoes = 3;

        // ── per frame, before the creature loop ─────────────────────────────────────────────────────────

        /// <summary>Ranks this frame's foley emitters, feeds the weather to the rhythm, plays a distant call when one is
        /// due. One pass over the ≤ 64 entries.</summary>
        private void TickCreatureAudio(float now)
        {
            _rhythm.Observe(IsStorm(), now);
            var player = Game.PlayerPosition;
            _foleyScratch.Clear();
            _nearbySpecies.Clear();
            foreach (var kv in _creatures)
            {
                var e = kv.Value;
                if (e.Root == null)
                {
                    continue;
                }

                _nearbySpecies.Add(e.SpeciesId ?? string.Empty);
                float d = Vector3.Distance(e.Root.transform.position, player);
                bool moving = e.Vel.sqrMagnitude > 0.25f;
                _foleyScratch.Add(new FoleyCandidate(kv.Key, e.SpeciesId, e.Size, d, moving, e.Flier && e.Airborne));
            }

            _foley.Rank(_foleyScratch);
            UpdateSlither(player);
            TickDistantFauna(now);
        }

        // ── the call phrase, repertoire and rhythm (#902, #3243) ─────────────────────────────────────────

        /// <summary>Periodic idle vocalisation: a PHRASE of the species, at its own rate times the hour/weather factor; a
        /// sleeper only snores; a beggar begs; a companion now and then just sounds content. Then the pending alarm echo,
        /// the phrase's next pulse and a scheduled answer.</summary>
        private void TickVoice(Entry entry, NetCreature c, float now, float begFastSq)
        {
            if (entry.PulsesLeft <= 0 && now >= entry.NextCall)
            {
                if (c.Asleep)
                {
                    // Sleep slows the species' own rate rather than replacing it with a global constant.
                    entry.NextCall = now + Random.Range(entry.Voice.CadenceMin, entry.Voice.CadenceMax) * 1.8f;
                    StartPhrase(entry, Mathf.Min(entry.Voice.Pulses, 2), 0.3f, Vector3.zero, now);
                }
                else
                {
                    StartAwakeCall(entry, c, now, begFastSq);
                }
            }

            if (entry.AlarmAt > 0f && now >= entry.AlarmAt && entry.PulsesLeft <= 0)
            {
                entry.AlarmAt = 0f;
                StartAlarm(entry, now);
            }

            FirePhrasePulse(entry, c, now);

            // The scheduled answer (#876): the same species phrase, now from a real herd member when one is near.
            if (entry.AnswerAt > 0f && now >= entry.AnswerAt)
            {
                entry.AnswerAt = 0f;
                if (!c.Asleep && entry.PulsesLeft <= 0)
                {
                    StartPhrase(entry, entry.Voice.Pulses, 0.55f, AnswerOffset(entry), now);
                }
            }
        }

        private void StartAwakeCall(Entry entry, NetCreature c, float now, float begFastSq)
        {
            // #2018: a begging animal calls at a quarter of its cadence — only the three nearest beggars do.
            float cadence = Random.Range(entry.Voice.CadenceMin, entry.Voice.CadenceMax) * RhythmFactor(c, now);
            bool excited = c.Begging && (Game.ScenePos(c.X, c.Y, c.Z) - Game.PlayerPosition).sqrMagnitude <= begFastSq;
            entry.NextCall = now + (excited ? cadence * 0.25f : cadence);
            if (c.Begging)
            {
                // #3243: begging is higher and bowl-shaped — the same animal, plainly asking.
                StartPhrase(entry, entry.Voice.Pulses, excited ? 1f : 0.8f, Vector3.zero, now, contour: VoiceContour.Bowl, pitch: 1.1f);
                return;
            }

            if (!string.IsNullOrEmpty(c.OwnerId) && Random.value < 0.3f)
            {
                StartContent(entry, now); // a companion near its owner mostly just sounds content
                return;
            }

            StartPhrase(entry, entry.Voice.Pulses, 0.8f, Vector3.zero, now);
            if (Random.value < CreatureCallRhythm.AnswerChance(Conspecifics(entry, AlarmRange, null)))
            {
                entry.AnswerAt = now + PhraseLength(entry.Voice) + Random.Range(0.4f, 0.9f);
            }
        }

        /// <summary>The factor on the calling interval: hour × weather × the after-storm burst.</summary>
        private float RhythmFactor(NetCreature c, float now)
        {
            var env = Game.Environment;
            bool rain = env != null && env.Precipitation is "rain" or "drizzle" or "acid" or "hail" or "sleet";
            float sun = CreatureCallRhythm.SunHeight(Game.LocalTimeOfDay);
            return CreatureCallRhythm.CadenceFactor(c.Activity, sun, rain, IsStorm()) * _rhythm.BurstFactor(now);
        }

        private bool IsStorm()
        {
            var env = Game?.Environment;
            return env != null && env.Weather is "storm" or "toxic_storm" or "blizzard" or "gale" or "ion_storm";
        }

        /// <summary>React to authoritative state (#3243): hurt → the hurt cue and the herd's alarm; turning hostile → the
        /// alert cue and the alarm; a companion's guard growl (#1210); fed (stops begging close by) or tamed → content.</summary>
        private void TickStateCalls(Entry entry, NetCreature c, ClientAudio audio, float now)
        {
            if (c.Hull < entry.PrevHull - 0.5f)
            {
                PlayCue(entry, "_hurt", 0.9f);
                RaiseAlarm(entry, now);
            }

            if (c.Hostile && !entry.PrevHostile)
            {
                PlayCue(entry, "_alert", 0.9f);
                RaiseAlarm(entry, now);
            }

            if (c.Alerting && !entry.PrevAlerting)
            {
                PlayClipCue(entry, audio.Has("creature_companion_growl_2") && Random.value < 0.5f
                    ? "creature_companion_growl_2" : "creature_companion_growl", 0.9f);
            }

            bool fed = entry.PrevBegging && !c.Begging && !c.Hostile
                && (entry.Root.transform.position - Game.PlayerPosition).sqrMagnitude < 36f;
            bool tamed = string.IsNullOrEmpty(entry.PrevOwner) && !string.IsNullOrEmpty(c.OwnerId);
            if ((fed || tamed) && entry.PulsesLeft <= 0)
            {
                StartContent(entry, now);
            }

            entry.PrevBegging = c.Begging;
            entry.PrevOwner = c.OwnerId;
        }

        /// <summary>Up to <see cref="AlarmEchoes"/> conspecifics within <see cref="AlarmRange"/> repeat the alarm call, a
        /// little apart — the herd startles together instead of each animal on its own.</summary>
        private void RaiseAlarm(Entry source, float now)
        {
            var near = _alarmScratch;
            near.Clear();
            Conspecifics(source, AlarmRange, near);
            near.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int i = 0; i < near.Count && i < AlarmEchoes; i++)
            {
                var e = near[i].Value;
                if (e.AlarmAt <= 0f)
                {
                    e.AlarmAt = now + 0.2f + 0.15f * i + Random.Range(0f, 0.2f);
                }
            }
        }

        private readonly List<KeyValuePair<float, Entry>> _alarmScratch = new List<KeyValuePair<float, Entry>>();

        /// <summary>Counts (and optionally lists, by distance) the other animals of this species within a range.</summary>
        private int Conspecifics(Entry source, float range, List<KeyValuePair<float, Entry>> into)
        {
            int count = 0;
            var at = source.Root.transform.position;
            foreach (var e in _creatures.Values)
            {
                if (ReferenceEquals(e, source) || e.Root == null || e.SpeciesId != source.SpeciesId)
                {
                    continue;
                }

                float d = Vector3.Distance(e.Root.transform.position, at);
                if (d <= range)
                {
                    count++;
                    into?.Add(new KeyValuePair<float, Entry>(d, e));
                }
            }

            return count;
        }

        /// <summary>Where an answer comes from: the nearest herd member (relative to the caller), else a spot nearby.</summary>
        private Vector3 AnswerOffset(Entry entry)
        {
            Entry best = null;
            float bestD = AlarmRange;
            var at = entry.Root.transform.position;
            foreach (var e in _creatures.Values)
            {
                if (ReferenceEquals(e, entry) || e.Root == null || e.SpeciesId != entry.SpeciesId)
                {
                    continue;
                }

                float d = Vector3.Distance(e.Root.transform.position, at);
                if (d < bestD)
                {
                    bestD = d;
                    best = e;
                }
            }

            return best != null ? best.Root.transform.position - at : new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
        }

        private static void StartAlarm(Entry e, float now)
            => StartPhrase(e, Mathf.Clamp(e.Voice.Pulses, 2, 3), 0.85f, Vector3.zero, now, e.Voice.AlarmCall, VoiceContour.Rising, 90, 1.1f);

        /// <summary>The content sound: the size's calm idle murmur through the species' own timbre (#3238, #3243).</summary>
        private static void StartContent(Entry e, float now)
        {
            string size = e.Size < 0.8f ? "small" : e.Size < 1.6f ? "medium" : "large";
            StartPhrase(e, 1, 0.5f, Vector3.zero, now, $"creature_{size}_calm_idle", VoiceContour.Flat, 0, 1f);
        }

        private static float PhraseLength(CreatureVoice voice)
            => Mathf.Max(0, voice.Pulses - 1) * voice.PulseGapMs * 0.001f;

        /// <summary>Per-utterance pitch jitter (#876): small enough to keep the species voice recognisable, large enough that
        /// two calls never sound bit-identical. Web only allows positive pitch.</summary>
        private static float PitchJitter() => Random.Range(0.93f, 1.07f);

        /// <summary>Per-utterance volume jitter (#876).</summary>
        private static float VolJitter() => Random.Range(0.85f, 1.15f);

        /// <summary>Picks a take of the species call (#879): the second take (<c>*_2</c>), when bundled, plays half the time.
        /// #3246: baked voices alternate too now — the special voices were exactly the ones repeating one file; the LRU
        /// sampler cache (#901) holds the extra variant.</summary>
        private static string TakeOf(Entry e)
        {
            var audio = ClientAudio.Instance;
            var repertoire = CreatureVoices.Repertoire(e.Voice.Call); // #3359: a singer of Soaniri sings a fresh recording each call
            if (repertoire != null && audio != null)
            {
                string pick = repertoire[Random.Range(0, repertoire.Count)];
                return audio.Has(pick) ? pick : e.Voice.Call;
            }

            string alt = e.Voice.Call + "_2";
            return audio != null && audio.Has(alt) && Random.value < 0.5f ? alt : e.Voice.Call;
        }

        /// <summary>Begins a phrase (#902): <paramref name="pulses"/> one-shots along a pitch contour. The species' call,
        /// gap and contour unless a repertoire call (#3243) overrides them.</summary>
        private static void StartPhrase(Entry e, int pulses, float volume, Vector3 offset, float now, string clip = null,
            VoiceContour? contour = null, int gapMs = 0, float pitch = 1f)
        {
            e.PulsesLeft = Mathf.Max(1, pulses);
            e.PhrasePulses = e.PulsesLeft;
            e.PulseIndex = 0;
            e.PhraseVol = volume;
            e.PhraseOffset = offset;
            e.PhraseClip = clip;
            e.PhraseContour = contour ?? e.Voice.Contour;
            e.PhraseGapMs = gapMs > 0 ? gapMs : e.Voice.PulseGapMs;
            e.PhrasePitch = pitch;
            e.NextPulseAt = now;
        }

        /// <summary>Fires the phrase's next pulse when it is due. Pulses fade slightly across the phrase so a click train
        /// reads as one utterance. Runs on world time (#908): pausing holds a phrase where it is.</summary>
        private static void FirePhrasePulse(Entry e, NetCreature c, float now)
        {
            if (e.PulsesLeft <= 0 || now < e.NextPulseAt)
            {
                return;
            }

            float contour = CreatureVoiceBank.ContourPitch(e.PhraseContour, e.PulseIndex, e.PhrasePulses);
            float decay = 1f - 0.1f * e.PulseIndex;
            float pitch = e.Pitch * e.PhrasePitch * contour * PitchJitter() * (c.Asleep ? 0.7f : 1f);
            var pos = e.Root.transform.position + e.PhraseOffset;

            string clipId = e.PhraseClip ?? TakeOf(e);
            var clip = CreatureVoiceBank.Resolve(clipId, e.Voice, e.Echo);
            if (clip != null)
            {
                ClientAudio.Instance?.AtClip(clip, pos, pitch, e.PhraseVol * decay * VolJitter(), clipId);
            }

            e.Animator?.Pulse(c.Asleep ? 0.2f : e.PhraseVol * decay); // the mouth opens on the pulse
            e.PulseIndex++;
            e.PulsesLeft--;
            e.NextPulseAt = now + e.PhraseGapMs * 0.001f;
        }

        /// <summary>Plays one of the species' combat/damage cues — since #3244 shaped from its own call (#903).</summary>
        private static void PlayCue(Entry e, string cue, float volume) => PlayClipCue(e, e.Bank + cue, volume);

        /// <summary>Plays an arbitrary clip id through this creature's own timbre + pitch (#1210 growl).</summary>
        private static void PlayClipCue(Entry e, string clipId, float volume)
        {
            var clip = CreatureVoiceBank.Resolve(clipId, e.Voice, e.Echo);
            if (clip != null)
            {
                ClientAudio.Instance?.AtClip(clip, e.Root.transform.position, e.Pitch * PitchJitter(), volume * VolJitter(), clipId);
            }

            e.Animator?.Pulse(volume); // every cue this creature makes also opens its mouth
        }

        // ── movement foley (#3242) ──────────────────────────────────────────────────────────────────────

        /// <summary>Hooks the rig's footfall event (exists since #1999 for the giants' dust) to a soft, surface-matched step —
        /// only for ordinary animals; the giants keep their own stomp.</summary>
        private void HookFoley(Entry entry, NetCreature c)
        {
            entry.SpeciesId = c.SpeciesId;
            entry.Size = c.Size;
            entry.Flier = c.HasWings;
            if (c.GiantHeight > 0f || entry.Animator == null || entry.Animator.Feet == null)
            {
                return;
            }

            string id = c.Id;
            entry.Animator.Feet.FootPlanted = p => OnFootfall(id, entry, p);
        }

        private void OnFootfall(string id, Entry entry, Vector3 at)
        {
            var audio = ClientAudio.Instance;
            if (audio == null || Game == null || !_foley.TryFootfall(id, entry.Size, Game.WorldTime))
            {
                return;
            }

            string key = Game.World != null && Game.Content != null
                ? Game.Content.BlockById(Game.World.GetBlock(Mathf.FloorToInt(at.x), Mathf.FloorToInt(at.y - 0.3f), Mathf.FloorToInt(at.z)))?.Key
                : null;
            string clip = FootstepClip(FootstepSurface.CueFor(key), audio);
            float pitch = CreatureFoleyGate.FootfallPitch(entry.Size) * Random.Range(0.94f, 1.06f);
            audio.AtTagged(clip, at, pitch, CreatureFoleyGate.FootfallVolume(entry.Size) * Random.Range(0.85f, 1.15f), AudioCategory.Foley);
        }

        /// <summary>One of the surface's takes (<c>step_x</c>, <c>step_x_2</c>, <c>step_x_3</c>) that exists.</summary>
        private static string FootstepClip(string cue, ClientAudio audio)
        {
            int take = Random.Range(1, 4);
            string id = take == 1 ? cue : cue + "_" + take;
            return audio.Has(id) ? id : cue;
        }

        /// <summary>Wing beats, splashes and grazing for one animal — all through the foley gate.</summary>
        private void TickFoley(Entry entry, NetCreature c, float now)
        {
            entry.Airborne = c.Airborne;
            var audio = ClientAudio.Instance;
            if (audio == null || c.GiantHeight > 0f || entry.Root == null)
            {
                return;
            }

            var pos = entry.Root.transform.position;
            float dist = Vector3.Distance(pos, Game.PlayerPosition);
            if (c.HasWings && c.Airborne && !c.Glides && _foley.TryWing(c.Id, now))
            {
                audio.AtTagged(Random.value < 0.5f ? "wing_flap" : "wing_flap_2", pos,
                    Mathf.Clamp(1.25f - 0.15f * c.Size, 0.7f, 1.3f) * Random.Range(0.94f, 1.06f), 0.35f, AudioCategory.Foley);
            }

            TickSplash(entry, c, pos, dist, now, audio);
            TickChew(entry, c, pos, dist, now, audio);
        }

        /// <summary>A swimmer breaking the surface splashes (checked a few times a second, near the player only).</summary>
        private void TickSplash(Entry entry, NetCreature c, Vector3 pos, float dist, float now, ClientAudio audio)
        {
            if ((c.Habitat != "Water" && c.Habitat != "Amphibian") || dist > 14f || now < entry.NextSplashProbe)
            {
                return;
            }

            entry.NextSplashProbe = now + 0.25f;
            bool inWater = Game.IsWaterAt(pos + Vector3.up * 0.3f);
            if (inWater != entry.WasInWater && _foley.TryOneOff(c.Id, 1.5f, now))
            {
                audio.AtTagged(inWater ? "water_splash_in" : "water_splash_out", pos,
                    Mathf.Clamp(1.3f - 0.2f * c.Size, 0.6f, 1.3f), Mathf.Clamp(0.25f + 0.1f * c.Size, 0.25f, 0.6f), AudioCategory.Foley);
            }

            entry.WasInWater = inWater;
        }

        /// <summary>A calm, awake animal standing still close by now and then chews (grazing) — quiet and rare.</summary>
        private void TickChew(Entry entry, NetCreature c, Vector3 pos, float dist, float now, ClientAudio audio)
        {
            bool still = entry.Vel.sqrMagnitude < 0.04f;
            entry.StillSince = still ? (entry.StillSince > 0f ? entry.StillSince : now) : 0f;
            if (!still || c.Hostile || c.Asleep || c.Size < CreatureFoleyGate.MinSize || dist > ChewRange
                || now - entry.StillSince < 2f || c.Habitat == "Water" || now < entry.NextChew)
            {
                return;
            }

            entry.NextChew = now + Random.Range(6f, 12f);
            if (_foley.TryOneOff(c.Id, 6f, now))
            {
                audio.AtTagged("eat", pos, Mathf.Clamp(1.2f - 0.15f * c.Size, 0.7f, 1.2f), 0.22f, AudioCategory.Foley);
            }
        }

        /// <summary>One shared slither loop at the nearest limbless crawler within range — never one per animal.</summary>
        private void UpdateSlither(Vector3 player)
        {
            Entry best = null;
            float bestD = SlitherRange;
            foreach (var kv in _creatures)
            {
                var e = kv.Value;
                if (e.Root == null || !e.Limbless || e.Airborne || e.Vel.sqrMagnitude < 0.25f)
                {
                    continue;
                }

                float d = Vector3.Distance(e.Root.transform.position, player);
                if (d < bestD)
                {
                    bestD = d;
                    best = e;
                }
            }

            var audio = ClientAudio.Instance;
            if (_slither == null && audio != null && audio.Clip("slither_loop") != null)
            {
                var go = new GameObject("creature_slither");
                go.transform.SetParent(transform, false);
                _slither = go.AddComponent<AudioSource>();
                _slither.clip = audio.Clip("slither_loop");
                _slither.loop = true;
                _slither.spatialBlend = 1f;
                _slither.minDistance = 1.5f;
                _slither.maxDistance = SlitherRange;
                _slither.rolloffMode = AudioRolloffMode.Linear;
                _slither.volume = 0f;
            }

            if (_slither == null)
            {
                return;
            }

            float target = best != null ? 0.35f * ClientAudio.EffectsVolume : 0f;
            if (best != null)
            {
                _slither.transform.position = best.Root.transform.position;
                _slither.pitch = Mathf.Clamp(1.2f - 0.15f * best.Size, 0.7f, 1.2f);
            }

            _slither.volume = Mathf.MoveTowards(_slither.volume, target, Time.deltaTime * 0.8f);
            if (_slither.volume > 0f && !_slither.isPlaying)
            {
                _slither.Play();
            }
            else if (_slither.volume <= 0f && _slither.isPlaying)
            {
                _slither.Stop();
            }
        }

        // ── distant fauna (#3245) ───────────────────────────────────────────────────────────────────────

        private void TickDistantFauna(float now)
        {
            if (!_faunaSubscribed && Game.Network != null)
            {
                Game.Network.FaunaRosterReceived += r => _distant.SetRoster(r?.Species);
                _faunaSubscribed = true;
            }

            var audio = ClientAudio.Instance;
            if (audio == null || Game.Environment == null)
            {
                return;
            }

            if (Time.time >= _waterProbeAt)
            {
                _waterProbeAt = Time.time + 3f;
                _nearWater = ProbeWater();
            }

            _speciesNearbyFn ??= id => _nearbySpecies.Contains(id);
            var env = Game.Environment;
            float sun = CreatureCallRhythm.SunHeight(Game.LocalTimeOfDay);
            bool roofed = Game.Aboard || !Game.ExposedToSky;
            var cond = new DistantFaunaConditions(Mathf.Clamp01(sun * 0.5f + 0.5f),
                env.Precipitation is "rain" or "drizzle" or "acid" or "hail" or "sleet" or "snow", IsStorm(), roofed,
                audio.InVacuum || audio.WorldAirless, Game.InSpace || Game.SpaceViewActive, GameBootstrap.CutsceneActive || NarratorVoice.AnySpeaking,
                _nearWater, !Game.ExposedToSky && !Game.Aboard, _speciesNearbyFn);
            var call = _distant.Tick(now, cond);
            if (call.HasValue)
            {
                PlayDistantCall(call.Value, audio);
            }
        }

        /// <summary>The species' own call, dull and with a long tail, from its bearing. It plays at a short virtual
        /// distance so the panning tells the direction while the level comes from the scheduler (40–120 blocks would be far
        /// past any one-shot's range).</summary>
        private void PlayDistantCall(DistantCall call, ClientAudio audio)
        {
            var sp = call.Species;
            var traits = new VoiceTraits(sp.Habitat, sp.Temperament, sp.BodyPlan, sp.Size, sp.Legs, sp.Eyes, sp.Horns,
                sp.BodySegments, sp.Tentacles, sp.HasGasSac, sp.Glows);
            var voice = CreatureVoiceBank.For(sp.VoiceSeed, traits);
            var src = audio.Clip(voice.Call);
            if (src == null)
            {
                return;
            }

            var far = SampleKit.Variant(src, null, VoiceOp.Dull, 1, tail: 2);
            float pitch = Mathf.Clamp(Mathf.Clamp(1.5f - 0.35f * sp.Size, 0.7f, 1.6f) * (0.82f + voice.PitchStep / 37f * 0.45f), 0.6f, 1.85f);
            var dir = new Vector3(Mathf.Cos(call.AngleRadians), 0.15f, Mathf.Sin(call.AngleRadians)).normalized;
            audio.AtClip(far, Game.PlayerPosition + dir * 12f, pitch * PitchJitter(), call.Volume01 * 0.55f, "creature_distant");
        }

        /// <summary>Water within ~28 blocks around the player (a coarse 9 × 9 × 3 grid, every 3 s) — shore and water
        /// species only call from afar near water.</summary>
        private bool ProbeWater()
        {
            if (Game.World == null || Game.Content == null)
            {
                return false;
            }

            var p = Game.PlayerPosition;
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y), pz = Mathf.FloorToInt(p.z);
            for (int dx = -28; dx <= 28; dx += 7)
            {
                for (int dz = -28; dz <= 28; dz += 7)
                {
                    for (int dy = -6; dy <= 2; dy += 4)
                    {
                        string k = Game.Content.BlockById(Game.World.GetBlock(px + dx, py + dy, pz + dz))?.Key;
                        if (k != null && k.Contains("water"))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
