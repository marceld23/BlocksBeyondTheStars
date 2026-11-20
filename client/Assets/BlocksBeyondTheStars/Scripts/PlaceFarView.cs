// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively places from afar: a place shows that somebody lives there before the player reaches it. By day a thin
    /// column of smoke rises over its fire place and its chimneys; at night its lanterns and its fire are warm points of
    /// light that carry far beyond the distance at which a lantern block itself can be made out. The points come from the
    /// server's place list (<see cref="NetPlacePoint"/>: fire, lamp, chimney); everything here is drawn on the shared
    /// effect emitters (<see cref="FxKit"/>) — no particle system, material or light of its own.
    /// <para>Few and capped per quality preset, nearest first, like the volcano smoke: Potato two columns and four
    /// lights, High six and fourteen. Near the player the real blocks do the work (a camp fire has its own smoke, a
    /// lantern its own glow), so a point closer than <see cref="NearCut"/> is left alone. Off in space and in the flight
    /// view. Render-only.</para>
    /// </summary>
    public sealed class PlaceFarView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float Reach = 260f;          // blocks: beyond it a point is not worth a particle
        private const float NearCut = 26f;         // closer than this the block itself is what one sees
        private const float RefreshSeconds = 2f;
        private const float PuffsPerSecond = 0.55f; // per column at density 1
        private const float GlowSeconds = 1.3f;     // a light point's life; re-emitted a little sooner, so it breathes
        private const float GlowEvery = 0.9f;

        private struct Spot
        {
            public Vector3 At;
            public float Distance;
            public float Acc;
        }

        private readonly List<Spot> _smoke = new List<Spot>();
        private readonly List<Spot> _lights = new List<Spot>();
        private float _refreshAt;
        private float _glowAt;

        private void Update()
        {
            if (Game == null || Game.World == null || Game.InSpace || Game.SpaceViewActive || !string.IsNullOrEmpty(Game.StationName))
            {
                _smoke.Clear();
                _lights.Clear();
                return;
            }

            if (Time.unscaledTime >= _refreshAt)
            {
                _refreshAt = Time.unscaledTime + RefreshSeconds;
                Refresh();
            }

            float dt = Game.WorldDeltaTime;
            if (dt <= 0f)
            {
                return;
            }

            float day = Game.LocalTimeOfDay;
            float night = NightAmbience.NightLevel(day);
            if (night < 0.95f && _smoke.Count > 0)
            {
                Smoke(dt, 1f - night);
            }

            if (night > 0.05f && _lights.Count > 0 && Time.time >= _glowAt)
            {
                _glowAt = Time.time + GlowEvery;
                foreach (var light in _lights)
                {
                    // Bigger the farther away, so a lantern stays a point of light instead of vanishing below a pixel.
                    float size = Mathf.Clamp(light.Distance * 0.016f, 0.7f, 3.6f);
                    FxKit.Emit(FxKit.Kind.Glow, light.At, Vector3.zero, size, GlowSeconds, new Color(1f, 0.74f, 0.36f, 0.55f * night));
                }
            }
        }

        private void OnDisable()
        {
            _smoke.Clear();
            _lights.Clear();
        }

        private void Smoke(float dt, float daylight)
        {
            Vector3 wind = Game.WindVector;
            float rate = PuffsPerSecond * FxKit.Density;
            for (int i = 0; i < _smoke.Count; i++)
            {
                var c = _smoke[i];
                c.Acc = Mathf.Min(c.Acc + (rate * dt), 2f);
                while (c.Acc >= 1f)
                {
                    c.Acc -= 1f;
                    var at = c.At + new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.2f, 0.2f));
                    var vel = new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(0.9f, 1.4f), Random.Range(-0.1f, 0.1f)) + (wind * 0.35f);
                    FxKit.Emit(FxKit.Kind.Smoke, at, vel, Random.Range(0.9f, 1.6f), Random.Range(5f, 8f),
                        new Color(0.78f, 0.78f, 0.8f, 0.32f * Mathf.Lerp(0.5f, 1f, daylight)));
                }

                _smoke[i] = c;
            }
        }

        /// <summary>The points in reach, nearest first, capped per preset.</summary>
        private void Refresh()
        {
            _smoke.Clear();
            _lights.Clear();
            var places = Game.Places;
            if (places == null || places.Length == 0)
            {
                return;
            }

            var player = Game.PlayerPosition;
            foreach (var place in places)
            {
                if (place.Points == null || place.Kind != PlaceKinds.Settlement
                    || !PlaceTracker.Contains(place, player.x, player.y, player.z, Game.Circumference, (int)Reach))
                {
                    continue;
                }

                foreach (var point in place.Points)
                {
                    bool smoke = point.Kind is PlacePointKinds.Chimney or PlacePointKinds.Fire;
                    bool light = point.Kind is PlacePointKinds.Lamp or PlacePointKinds.Fire;
                    if (!smoke && !light)
                    {
                        continue;
                    }

                    var at = Game.ScenePos(point.X + 0.5f, point.Y, point.Z + 0.5f); // the copy nearest the player across the seam
                    float distance = Vector3.Distance(at, player);
                    if (distance < NearCut || distance > Reach)
                    {
                        continue;
                    }

                    if (smoke)
                    {
                        _smoke.Add(new Spot { At = at + (Vector3.up * 1.2f), Distance = distance, Acc = Random.value });
                    }

                    if (light)
                    {
                        _lights.Add(new Spot { At = at + (Vector3.up * (point.Kind == PlacePointKinds.Lamp ? 1.6f : 0.6f)), Distance = distance });
                    }
                }
            }

            Cap(_smoke, FxKit.Preset switch { QualityPreset.Potato => 2, QualityPreset.Low => 3, QualityPreset.Medium => 4, _ => 6 });
            Cap(_lights, FxKit.Preset switch { QualityPreset.Potato => 4, QualityPreset.Low => 6, QualityPreset.Medium => 10, _ => 14 });
        }

        private static void Cap(List<Spot> spots, int cap)
        {
            if (spots.Count <= cap)
            {
                return;
            }

            spots.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            spots.RemoveRange(cap, spots.Count - cap);
        }
    }
}
