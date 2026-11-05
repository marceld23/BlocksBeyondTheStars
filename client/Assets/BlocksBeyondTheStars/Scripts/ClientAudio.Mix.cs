// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The mixing layer every 3D one-shot goes through (#3249), the vacuum rules (#3241), rain on the hull (#3247) and
    /// the player's own body sounds (#3239/#3240): kept apart from the router's main file.
    /// <list type="bullet">
    /// <item>Distance profiles per cue id (<see cref="AudioMixRules.RangeFor"/>) instead of one 4–20 m curve for all.</item>
    /// <item>A voice budget per category (<see cref="AudioMixRules.BudgetFor"/>): a herd's footfalls or a crowd of remote
    /// players can never fill the pool; the nearest sounds win, the rest are dropped, never queued.</item>
    /// <item>Roof occlusion: a sound on the other side of a roof or a hull arrives muffled (the baked low-pass, so it also
    /// works in the browser) and at half level.</item>
    /// <item>Vacuum: the world heard through the suit — muffled and quieter, touched things clear, a soft suit tone, no wind,
    /// no sonic boom.</item>
    /// </list>
    /// </summary>
    public sealed partial class ClientAudio
    {
        private readonly Dictionary<AudioSource, AudioCategory> _sfxCategory = new Dictionary<AudioSource, AudioCategory>();
        private readonly Dictionary<AudioSource, float> _sfxDistance = new Dictionary<AudioSource, float>();
        private readonly List<AudioSource> _ownPool = new List<AudioSource>();
        private AudioListener _listener;
        private float _listenerSearchAt;
        private AudioSource _suitTone;   // #3241: the inside of the helmet in vacuum
        private AudioSource _hullRain;   // #3247: rain drumming on the ship's roof, heard aboard
        private const int OwnPoolMax = 6;
        private const int RoofScanCells = 20;

        // Rain on the hull replaces most of the muffled outdoor rain aboard (a little of it stays, for distance).
        private const float HullRainBedShare = 0.35f;
        private const float HullRainLevel = 0.5f;

        /// <summary>#3241: true while the player hears the world through the suit (spacewalk, on foot in space, an airless
        /// surface) — refreshed every frame.</summary>
        public bool InVacuum { get; private set; }

        /// <summary>#3241: the current world has no air (an asteroid, an airless moon) — a lift-off there makes no boom.</summary>
        public bool WorldAirless { get; private set; }

        /// <summary>Plays a 3D one-shot that counts against a voice budget the cue id alone cannot tell — another player's
        /// footsteps are the same clips as the player's own.</summary>
        public void AtTagged(string id, Vector3 pos, float pitch, float vol, AudioCategory category)
        {
            if (_clips.TryGetValue(id, out var clip) && clip != null)
            {
                PlayAt(clip, pos, pitch, vol, id, category);
            }
        }

        /// <summary>#3239/#3240: one of the player's own body sounds (a footstep, a landing, a breath, a stroke) — 2D, with a
        /// pitch (<see cref="Cue"/> has none: PlayOneShot cannot pitch). Aboard the ship it gets a small metal-room tail, in
        /// vacuum it is carried through the suit (a little duller and quieter). Both are bakes, so the browser hears them
        /// too.</summary>
        public void PlayOwn(string id, float vol, float pitch = 1f)
        {
            if (!_clips.TryGetValue(id, out var clip) || clip == null)
            {
                return;
            }

            float level = vol;
            if (InVacuum)
            {
                clip = SampleKit.Variant(clip, null, VoiceOp.Dull, 0, tail: 0);
                level *= VacuumAudio.ContactLevel;
            }
            else if (Game != null && Game.Aboard)
            {
                clip = SampleKit.Variant(clip, null, VoiceOp.None, 0, tail: 1); // the cabin's small metal room
            }

            var src = OwnSource();
            src.clip = clip;
            src.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            src.volume = Mathf.Clamp01(level * SfxVol());
            src.Play();
        }

        /// <summary>The single path of every 3D one-shot: budget → vacuum or roof occlusion → underwater muffle → the cue's
        /// distance profile.</summary>
        private void PlayAt(AudioClip clip, Vector3 pos, float pitch, float vol, string label, AudioCategory category)
        {
            if (clip == null)
            {
                return;
            }

            float distance = Vector3.Distance(ListenerPosition(), pos);
            if (!AdmitVoice(category, distance, out var steal))
            {
                return;
            }

            bool muffle = _submerged;
            float level = vol;
            if (InVacuum)
            {
                if (VacuumAudio.NeedsAir(label))
                {
                    return;
                }

                var (m, l) = VacuumAudio.Shape(distance);
                muffle |= m;
                level *= l;
            }
            else if (AudioMixRules.Occluded(ListenerRoofed(), SourceRoofed(pos), distance))
            {
                muffle = true;
                level *= AudioMixRules.OccludedLevel;
            }

            if (muffle)
            {
                clip = SampleKit.Muffle(clip);
            }

            var src = steal != null ? steal : PooledSfxSource();
            var (min, max) = AudioMixRules.Distances(AudioMixRules.RangeFor(label));
            src.transform.position = pos;
            src.clip = clip;
            src.spatialBlend = 1f;
            src.minDistance = min;
            src.maxDistance = max;
            src.rolloffMode = AudioRolloffMode.Linear; // linear → truly silent past maxDistance
            src.pitch = pitch;
            src.volume = Mathf.Clamp01(level * SfxVol());
            _sfxCategory[src] = category;
            _sfxDistance[src] = distance;
            src.Play();
        }

        /// <summary>Under the category's budget → yes. At it → only when nearer than the farthest of its kind still playing,
        /// which is then cut and its emitter reused (<paramref name="steal"/>).</summary>
        private bool AdmitVoice(AudioCategory category, float distance, out AudioSource steal)
        {
            steal = null;
            int budget = AudioMixRules.BudgetFor(category);
            if (budget == int.MaxValue)
            {
                return true;
            }

            int playing = 0;
            float farthest = -1f;
            foreach (var s in _sfxPool)
            {
                if (s == null || !s.isPlaying || !_sfxCategory.TryGetValue(s, out var c) || c != category)
                {
                    continue;
                }

                playing++;
                float d = _sfxDistance.TryGetValue(s, out var dd) ? dd : 0f;
                if (d > farthest)
                {
                    farthest = d;
                    steal = s;
                }
            }

            if (!AudioMixRules.Admit(playing, budget, distance, farthest))
            {
                steal = null;
                return false;
            }

            if (playing < budget)
            {
                steal = null; // a free emitter will do; nothing has to be cut
            }
            else
            {
                steal.Stop();
            }

            return true;
        }

        /// <summary>Where the ears are: the scene's AudioListener (the camera, or the player's head in third person).</summary>
        private Vector3 ListenerPosition()
        {
            if ((_listener == null || !_listener.isActiveAndEnabled) && Time.unscaledTime >= _listenerSearchAt)
            {
                // The ENABLED one: the shell's music listener stays in the scene, disabled, while a world is loaded.
                _listenerSearchAt = Time.unscaledTime + 1f;
                _listener = null;
                foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                {
                    if (l.isActiveAndEnabled)
                    {
                        _listener = l;
                        break;
                    }
                }
            }

            if (_listener != null && _listener.isActiveAndEnabled)
            {
                return _listener.transform.position;
            }

            return Game != null ? Game.PlayerPosition : Vector3.zero;
        }

        private bool ListenerRoofed() => Game != null && (Game.Aboard || !Game.ExposedToSky);

        /// <summary>A solid cell — the world's or a parked ship's — within <see cref="RoofScanCells"/> above the source.</summary>
        private bool SourceRoofed(Vector3 pos)
        {
            if (Game?.World == null)
            {
                return false;
            }

            int x = Mathf.FloorToInt(pos.x), y = Mathf.FloorToInt(pos.y), z = Mathf.FloorToInt(pos.z);
            bool ships = Game.LandedShips.Count > 0;
            for (int dy = 1; dy <= RoofScanCells; dy++)
            {
                if (!Game.World.GetBlock(x, y + dy, z).IsAir)
                {
                    return true;
                }

                if (ships && !Game.LandedShipBlockAt(x, y + dy, z, out _, out _).IsAir)
                {
                    return true;
                }
            }

            return false;
        }

        private AudioSource OwnSource()
        {
            foreach (var s in _ownPool)
            {
                if (s != null && !s.isPlaying)
                {
                    return s;
                }
            }

            if (_ownPool.Count < OwnPoolMax)
            {
                var created = gameObject.AddComponent<AudioSource>();
                created.playOnAwake = false;
                created.spatialBlend = 0f;
                _ownPool.Add(created);
                return created;
            }

            var oldest = _ownPool[0];
            _ownPool.RemoveAt(0);
            _ownPool.Add(oldest);
            oldest.Stop();
            return oldest;
        }

        /// <summary>Per frame: the vacuum state, the suit tone and the rain on the hull. Called from Update.</summary>
        private void TickMix(float bed, string weatherBedId)
        {
            WorldAirless = ComputeAirless();
            bool onStation = Game != null && !string.IsNullOrEmpty(Game.StationName);
            bool flying = Game != null && Game.SpaceViewActive && !Game.InEva;
            InVacuum = Game != null && !flying
                && VacuumAudio.InVacuum(Game.InEva, Game.OnFootInSpace, WorldAirless && !Game.InSpace, Game.Aboard, onStation);

            _suitTone = _suitTone != null ? _suitTone : LoopSource("suit_room_tone");
            FadeLoop(_suitTone, InVacuum ? bed * VacuumAudio.SuitToneLevel : 0f, 0.5f);

            bool hullRain = HullRainPlays(weatherBedId);
            _hullRain = _hullRain != null ? _hullRain : LoopSource("rain_on_hull");
            float intensity = Game != null ? Game.WeatherIntensity : 0.4f;
            FadeLoop(_hullRain, hullRain ? bed * HullRainLevel * Mathf.Clamp(0.45f + 0.7f * intensity, 0.4f, 1f) : 0f, 0.6f);
        }

        /// <summary>The weather/biome bed's share while special rules apply: none in vacuum or on an airless world, a little
        /// while the rain drums on the hull aboard (the hull loop carries it), full otherwise.</summary>
        private float BedShare(string weatherBedId)
        {
            if (InVacuum || (WorldAirless && !IsWeatherBed(weatherBedId)))
            {
                return 0f;
            }

            return HullRainPlays(weatherBedId) ? HullRainBedShare : 1f;
        }

        private bool HullRainPlays(string weatherBedId)
            => Game != null && Game.Aboard && OnPlanet() && weatherBedId is "rain_loop" or "storm_loop" or "drizzle_loop" or "toxic_storm_loop";

        private bool ComputeAirless()
        {
            var env = Game?.Environment;
            if (env == null || Game.Content == null)
            {
                return false;
            }

            var planet = Game.Content.GetPlanet(env.Biome ?? string.Empty);
            return VacuumAudio.IsAirless(planet != null && planet.IsAirless, (planet != null && planet.SpaceSky) || env.SpaceSky,
                env.AtmosphereDensity);
        }

        private AudioSource LoopSource(string id)
        {
            if (!_clips.TryGetValue(id, out var clip) || clip == null)
            {
                return null;
            }

            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 0f;
            src.clip = clip;
            return src;
        }

        private static void FadeLoop(AudioSource src, float target, float perSecond)
        {
            if (src == null)
            {
                return;
            }

            src.volume = Mathf.MoveTowards(src.volume, Mathf.Clamp01(target), Time.deltaTime * perSecond);
            if (src.volume > 0f && !src.isPlaying)
            {
                src.Play();
            }
            else if (src.volume <= 0f && src.isPlaying)
            {
                src.Stop();
            }
        }

        /// <summary>#3248: a hull or shield hit is as loud as it hurts, like the camera shake (a scratch is a tap, a big blow
        /// a bang), instead of one fixed level.</summary>
        private static float HitLevel(float drop) => Mathf.Clamp(0.45f + drop * 2.2f, 0.45f, 1f);

        /// <summary>The craft cue of a station with its own recorded sound (#3238: the decontaminator's rinse), else null.</summary>
        private string StationCraftCue(CraftingStation station) => station switch
        {
            CraftingStation.AlgaeTank when _clips.ContainsKey("algae_tank_craft") => "algae_tank_craft",
            CraftingStation.Decontaminator when _clips.ContainsKey("decontaminate") => "decontaminate",
            _ => null,
        };
    }
}
