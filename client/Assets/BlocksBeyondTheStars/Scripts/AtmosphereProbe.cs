// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Reads the player's surroundings off the streamed client chunks for the atmosphere effects (#2408), a few
    /// times a second, so neither the height fog nor the eye adaptation needs a GPU pass or a readback:
    /// <list type="bullet">
    ///   <item><see cref="FogFloor"/> — the height the mist lies at: the low ground around the player (the 25th
    ///   percentile of the terrain heights on a 7×7 grid, 8 blocks apart), smoothed, so mist fills the valley you look
    ///   into and stays off the ridge you stand on (#2406).</item>
    ///   <item><see cref="CameraExposure"/> — 1 under the open sky, 0 underground, eased; the height fog and the cloud
    ///   shadows fade with it so a cave never mists up (#2406).</item>
    ///   <item><see cref="SceneBrightness"/> — how much light the player stands in (open sky × daylight, the interior
    ///   fill, nearby light fixtures); the eye adaptation (#2395) drives the post exposure from it: slow to adapt to
    ///   the dark, quick to squint in the light.</item>
    /// </list>
    /// Everything here is presentation; the server stays authoritative over the world.
    /// </summary>
    public sealed class AtmosphereProbe : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        /// <summary>Eye adaptation on/off (settings; also off under Reduced effects in a gentler form).</summary>
        public bool AdaptationEnabled = true;
        public bool ReducedEffects;

        /// <summary>Bubbles while the camera is under water (#2401), from the settings.</summary>
        public bool BubblesEnabled = true;
        private float _bubbleTimer;

        /// <summary>World Y the mist is densest at (see the class summary).</summary>
        public float FogFloor { get; private set; }

        /// <summary>1 under the open sky … 0 underground / indoors, eased over ~0.5 s.</summary>
        public float CameraExposure { get; private set; } = 1f;

        /// <summary>0 (pitch dark) … 1 (full daylight) estimate of the light at the player.</summary>
        public float SceneBrightness { get; private set; } = 1f;

        /// <summary>The adaptation's current exposure offset in EV (+ brightens), applied by <see cref="UrpScenePost"/>.</summary>
        public float AdaptationEv { get; private set; }

        private const float ProbeInterval = 0.4f;
        private const int GridHalf = 3;      // 7×7 columns
        private const int GridStep = 8;      // blocks between columns
        private const int ProbeUp = 48, ProbeDown = 48;
        private const float AdaptDarkRate = 0.35f;  // EV/s toward the dark (eyes adapt slowly)
        private const float AdaptLightRate = 2.5f;  // EV/s toward the light (a quick squint)
        private const float AdaptMaxEv = 0.9f;

        private float _probeTimer;
        private float _fogFloorTarget;
        private bool _fogFloorValid;
        private float _lampLight;            // 0..1 share of light from fixtures near the player
        private readonly List<float> _heights = new List<float>(64);
        private float _adaptTarget;
        private readonly System.Random _bubbleRng = new System.Random(2401);

        private void OnEnable() => GameBootstrap.CaptureSnap += Snap;

        private void OnDisable()
        {
            GameBootstrap.CaptureSnap -= Snap;
            UrpScenePost.Instance?.SetAdaptation(0f);
        }

        /// <summary>Capture tools: jump every eased value to its target so the first frame is settled (#2405).</summary>
        private void Snap()
        {
            Probe(force: true);
            FogFloor = _fogFloorTarget;
            CameraExposure = Game != null && Game.ExposedToSky ? 1f : 0f;
            AdaptationEv = _adaptTarget;
            UrpScenePost.Instance?.SetAdaptation(AdaptationEnabled ? AdaptationEv : 0f);
        }

        private void Update()
        {
            if (Game == null || Game.World == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            _probeTimer -= dt;
            if (_probeTimer <= 0f)
            {
                _probeTimer = ProbeInterval;
                Probe(force: false);
            }

            // Smooth toward the probe results.
            if (_fogFloorValid)
            {
                FogFloor = Mathf.Abs(FogFloor - _fogFloorTarget) > 24f
                    ? _fogFloorTarget // a world change / teleport: snap instead of sliding for half a minute
                    : Mathf.MoveTowards(FogFloor, _fogFloorTarget, dt * 3f);
            }

            bool exposed = Game.ExposedToSky && !Game.SpaceViewActive;
            CameraExposure = Mathf.MoveTowards(CameraExposure, exposed ? 1f : 0f, dt * 2f);

            // #2401: a slow stream of bubbles rises past the visor while submerged — a few a second, drifting up.
            if (BubblesEnabled && Camera != null && !Game.SpaceViewActive && Game.IsWaterAt(Camera.transform.position))
            {
                _bubbleTimer -= dt;
                if (_bubbleTimer <= 0f)
                {
                    _bubbleTimer = 0.18f + (float)_bubbleRng.NextDouble() * 0.25f;
                    var ct = Camera.transform;
                    Vector3 at = ct.position + ct.forward * (0.8f + (float)_bubbleRng.NextDouble() * 1.6f)
                                 + ct.right * ((float)_bubbleRng.NextDouble() * 1.6f - 0.8f)
                                 + ct.up * ((float)_bubbleRng.NextDouble() * 1.0f - 0.7f);
                    float size = 0.04f + (float)_bubbleRng.NextDouble() * 0.06f;
                    FxKit.Emit(FxKit.Kind.Motes, at, new Vector3(0f, 0.5f + (float)_bubbleRng.NextDouble() * 0.4f, 0f), size, 2.2f,
                        new Color(0.75f, 0.9f, 1f, 0.7f));
                }
            }

            // Eye adaptation (#2395): the exposure offset the dark earns. Adapting to the dark takes seconds, the
            // squint back to the light is quick — so stepping out of a cave blooms for a moment, stepping in starts
            // dark and resolves. Never a flash (WCAG 2.3.1): the fastest ramp is 2.5 EV/s, nothing oscillates.
            float darkRate = ReducedEffects ? AdaptDarkRate * 2f : AdaptDarkRate;
            float lightRate = ReducedEffects ? AdaptLightRate * 0.6f : AdaptLightRate;
            float maxEv = ReducedEffects ? AdaptMaxEv * 0.5f : AdaptMaxEv;
            float target = AdaptationEnabled && !Game.SpaceViewActive ? Mathf.Min(_adaptTarget, maxEv) : 0f;
            AdaptationEv = target > AdaptationEv
                ? Mathf.MoveTowards(AdaptationEv, target, dt * darkRate)
                : Mathf.MoveTowards(AdaptationEv, target, dt * lightRate);
            UrpScenePost.Instance?.SetAdaptation(AdaptationEv);
        }

        /// <summary>One read of the surroundings: terrain heights for the fog floor, fixtures for the lamp share, and
        /// the brightness estimate the adaptation follows.</summary>
        private void Probe(bool force)
        {
            var world = Game.World;
            Vector3 p = Game.PlayerPosition;
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y), pz = Mathf.FloorToInt(p.z);

            // --- Fog floor: the low ground around the player --------------------------------------------------
            _heights.Clear();
            for (int gx = -GridHalf; gx <= GridHalf; gx++)
            {
                for (int gz = -GridHalf; gz <= GridHalf; gz++)
                {
                    int x = px + gx * GridStep, z = pz + gz * GridStep;
                    int top = int.MinValue;
                    for (int y = py + ProbeUp; y >= py - ProbeDown; y--)
                    {
                        if (!world.TryGetBlock(x, y, z, out var id))
                        {
                            continue; // not streamed in (the far columns carry only a band around the surface)
                        }

                        if (!id.IsAir)
                        {
                            top = y + 1;
                            break;
                        }
                    }

                    if (top != int.MinValue)
                    {
                        _heights.Add(top);
                    }
                }
            }

            if (_heights.Count >= 4)
            {
                _heights.Sort();
                _fogFloorTarget = _heights[_heights.Count / 4]; // 25th percentile = the valleys, not the hilltops
                if (!_fogFloorValid || force)
                {
                    FogFloor = _fogFloorTarget;
                    _fogFloorValid = true;
                }
            }

            // --- Light at the player ----------------------------------------------------------------------------
            // Open sky: the 3×3 columns around the head, walked up like GameBootstrap.ComputeExposedToSky.
            int open = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    bool blocked = false;
                    for (int y = py + 2; y <= py + 40; y++)
                    {
                        if (world.TryGetBlock(px + dx, y, pz + dz, out var id) && !id.IsAir)
                        {
                            blocked = true;
                            break;
                        }
                    }

                    if (!blocked)
                    {
                        open++;
                    }
                }
            }

            float skyOpen = open / 9f;

            // Fixtures within 6 blocks: lamps, torches, campfires (BlockLight is the shared rule of #2036/#2407).
            var content = Game.Content;
            int lamps = 0;
            if (content != null)
            {
                for (int dx = -6; dx <= 6; dx += 2)
                {
                    for (int dy = -3; dy <= 4; dy++)
                    {
                        for (int dz = -6; dz <= 6; dz += 2)
                        {
                            if (!world.TryGetBlock(px + dx, py + dy, pz + dz, out var id) || id.IsAir)
                            {
                                continue;
                            }

                            if (BlockLight.NaturalColorOf(content.BlockById(id)) != 0)
                            {
                                lamps++;
                            }
                        }
                    }
                }
            }

            _lampLight = Mathf.Clamp01(lamps * 0.35f);

            // Daylight at the player: sun height (0 at night) × weather dim, read off the environment the Sky uses.
            var env = Game.Environment;
            float day = 0.5f;
            float weather = 0f;
            if (env != null)
            {
                float t = Game.LocalTimeOfDay;
                float sunHeight = Mathf.Sin((t - 0.25f) * Mathf.PI * 2f);
                float d = Mathf.Clamp01(sunHeight * 0.5f + 0.5f);
                day = d * (2f - d); // the same ease the Sky's light level uses
                weather = Mathf.Clamp01(env.Intensity);
            }

            float daylight = skyOpen * day * Mathf.Lerp(1f, 0.7f, weather);
            float interior = Game.Aboard || !string.IsNullOrEmpty(Game.StationName) ? 0.6f : 0f;
            float lamp = _lampLight * 0.55f;
            SceneBrightness = Mathf.Clamp01(Mathf.Max(daylight, interior, lamp, 0.03f));

            // Dark earns up to +0.9 EV; a lit room or daylight earns nothing. Night under the open sky counts as dark
            // too, which is what lets the eyes "get used to" a moonless night without re-tuning the night floor.
            float dark = 1f - Mathf.Clamp01(SceneBrightness / 0.55f);
            _adaptTarget = AdaptMaxEv * Mathf.Pow(dark, 1.5f);
        }
    }
}
