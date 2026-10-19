// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Campfire sparks and smoke (#2821): every lit <c>campfire</c> (never <c>campfire_off</c>) within <see cref="Reach"/>
    /// blocks sends up a few slow sparks and a thin, soft column of smoke. The fires come from the light index
    /// (<see cref="NearbyLights"/>: a lit campfire is a light, a cold one is not), rescanned once a second, so nothing
    /// walks the block grid. Everything goes through the shared <see cref="FxKit"/> emitters — no particle system or
    /// material of its own — scaled by the effect density (quality preset × Reduced effects), thinned with distance, and
    /// fewer fires on Low (3) and Potato (2). Smoke and sparks never rise through a roof: their lifetime follows the free
    /// headroom over the fire, and a block right on top smothers the show. The smoke drifts with the wind under open
    /// sky and darkens at night. Render-only; the crackle is the audio's.
    /// </summary>
    public sealed class CampfireFx : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float Reach = 24f;
        private const float ScanInterval = 1f;
        private const float SparksPerSecond = 2.4f;
        private const float SmokePerSecond = 1.6f;
        private const int MaxHeadroom = 5;      // free cells counted over the fire; this many = open air (wind)
        private const float SparkSpeed = 0.8f;  // mean rise of a spark, m/s
        private const float SmokeSpeed = 0.6f;  // the Smoke emitter's speed limit, m/s

        private struct Fire
        {
            public Vector3 Base;     // the bottom centre of the campfire's cell, in the scene
            public float Headroom;   // free cells over it (1..MaxHeadroom)
            public bool Open;        // nothing over it within MaxHeadroom: the wind takes the smoke
            public float Near;       // 1 close by … 0.4 at the edge of reach
            public float SparkAcc;
            public float SmokeAcc;
        }

        private readonly NearbyLights _lights = new NearbyLights();
        private readonly List<NearbyLights.Hit> _hits = new List<NearbyLights.Hit>();
        private readonly List<Fire> _fires = new List<Fire>();
        private float _scanTimer;
        private BlockId _campfire;
        private bool _haveCampfire;

        private void Update()
        {
            if (Game == null || Game.World == null || Game.Content == null || Game.SpaceViewActive)
            {
                _fires.Clear();
                return;
            }

            if (!_haveCampfire)
            {
                var def = Game.Content.GetBlock("campfire");
                if (def == null)
                {
                    return;
                }

                _campfire = def.NumericId;
                _haveCampfire = true;
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                Rescan();
            }

            // World time (#908): the fire holds with the rest of the world behind the pause menu.
            float dt = Game.WorldDeltaTime;
            if (dt <= 0f || _fires.Count == 0)
            {
                return;
            }

            float density = FxKit.Density;
            float day = Daylight();
            var smoke = Color.Lerp(new Color(0.30f, 0.26f, 0.24f), new Color(0.62f, 0.62f, 0.64f), day);
            smoke.a = 0.28f; // thin and soft: the Smoke emitter's own fade peaks at 0.75 of this
            Vector3 wind = Game.WindVector * 0.35f;

            for (int i = 0; i < _fires.Count; i++)
            {
                var f = _fires[i];
                f.SparkAcc = Mathf.Min(f.SparkAcc + (SparksPerSecond * density * f.Near * dt), 3f);
                while (f.SparkAcc >= 1f)
                {
                    f.SparkAcc -= 1f;
                    Spark(f);
                }

                f.SmokeAcc = Mathf.Min(f.SmokeAcc + (SmokePerSecond * density * f.Near * dt), 3f);
                while (f.SmokeAcc >= 1f)
                {
                    f.SmokeAcc -= 1f;
                    Smoke(f, smoke, f.Open ? wind : Vector3.zero);
                }

                _fires[i] = f;
            }
        }

        /// <summary>The lit campfires in reach, nearest first, up to the preset's cap; each with its headroom.</summary>
        private void Rescan()
        {
            _fires.Clear();
            _lights.Collect(Game, Reach, _hits);
            int cap = FxKit.Preset >= QualityPreset.Medium ? 6 : FxKit.Preset == QualityPreset.Low ? 3 : 2;
            for (int i = 0; i < _hits.Count && _fires.Count < cap; i++)
            {
                var h = _hits[i];
                if (h.Block != _campfire)
                {
                    continue;
                }

                int x = Mathf.FloorToInt(h.Scene.x), y = Mathf.FloorToInt(h.Scene.y), z = Mathf.FloorToInt(h.Scene.z);
                int free = 0;
                while (free < MaxHeadroom && NearbyLights.AirAt(Game, x, y + 1 + free, z))
                {
                    free++;
                }

                if (free == 0)
                {
                    continue; // a block right on top: the fire still burns, but nothing can rise out of it
                }

                _fires.Add(new Fire
                {
                    Base = new Vector3(h.Scene.x, y, h.Scene.z),
                    Headroom = free,
                    Open = free >= MaxHeadroom,
                    Near = Mathf.Lerp(1f, 0.4f, h.Distance / Reach),
                    // A random start keeps the mean rate exact across rescans: a rate below one per second (Potato, a
                    // far fire) would never fill a zeroed accumulator within the second between two scans.
                    SparkAcc = Random.value,
                    SmokeAcc = Random.value,
                });
            }
        }

        /// <summary>One slow ember: an additive gold-to-orange mote rising out of the flames, shrinking and fading as it goes.</summary>
        private static void Spark(Fire f)
        {
            var at = f.Base + new Vector3(Random.Range(-0.18f, 0.18f), Random.Range(0.35f, 0.55f), Random.Range(-0.18f, 0.18f));
            float up = SparkSpeed * Random.Range(0.75f, 1.25f);
            var v = new Vector3(Random.Range(-0.15f, 0.15f), up, Random.Range(-0.15f, 0.15f));
            float life = Mathf.Min(Random.Range(1.1f, 2f), (f.Headroom + 0.4f) / up); // gone before the ceiling
            var c = Color.Lerp(new Color(1f, 0.55f, 0.18f), new Color(1f, 0.82f, 0.4f), Random.value);
            FxKit.Emit(FxKit.Kind.Motes, at, v, Random.Range(0.03f, 0.055f), life, c);
        }

        /// <summary>One soft puff of the smoke column: born over the flames, rising and widening, leaning with the wind.</summary>
        private static void Smoke(Fire f, Color color, Vector3 wind)
        {
            var at = f.Base + new Vector3(Random.Range(-0.08f, 0.08f), 0.75f, Random.Range(-0.08f, 0.08f));
            var v = new Vector3(Random.Range(-0.04f, 0.04f), Random.Range(0.25f, 0.4f), Random.Range(-0.04f, 0.04f)) + wind;
            float life = Mathf.Clamp((f.Headroom - 0.2f) / SmokeSpeed, 0.8f, Random.Range(3.2f, 4.6f));
            FxKit.Emit(FxKit.Kind.Smoke, at, v, Random.Range(0.32f, 0.46f), life, color);
        }

        /// <summary>0 at night … 1 at noon — the same ease the Sky's light level uses; dims the smoke after dark.</summary>
        private float Daylight()
        {
            float t = Game.LocalTimeOfDay;
            float sun = Mathf.Sin((t - 0.25f) * Mathf.PI * 2f);
            float d = Mathf.Clamp01((sun * 0.5f) + 0.5f);
            return d * (2f - d);
        }
    }
}
