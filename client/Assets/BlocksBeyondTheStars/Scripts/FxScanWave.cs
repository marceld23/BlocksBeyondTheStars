// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The scan wave of the VFX overhaul (#2153): an expanding shell around a scanner that every block, model and
    /// creature draws from its own world position (the <c>_Sc_ScanWave*</c> globals read by <c>FxCommon.hlsl</c>) — a
    /// razor-thin bright front, a soft glow trailing behind it and faint horizontal lines, the No Man's Sky scanner
    /// look. One wave at a time (a new scan restarts it). No full-screen pass and no depth texture, so it runs on
    /// every preset and in the browser. <see cref="Tick"/> is driven by <see cref="FxKit"/>.
    /// </summary>
    public static class FxScanWave
    {
        private static readonly int WaveId = Shader.PropertyToID("_Sc_ScanWave");
        private static readonly int ColId = Shader.PropertyToID("_Sc_ScanWaveCol");
        private static readonly int ParamsId = Shader.PropertyToID("_Sc_ScanWaveParams");

        private static bool _running;
        private static Vector3 _origin;
        private static float _radius;
        private static float _maxRadius;
        private static float _speed;
        private static float _width;
        private static float _lines;
        private static Color _color;
        private static float _strength;

        /// <summary>True while a wave rolls.</summary>
        public static bool Running => _running;

        /// <summary>The wave's current radius (blocks) — lets callers reveal things exactly as the front reaches them.</summary>
        public static float Radius => _radius;

        /// <summary>Seconds until the front reaches <paramref name="distance"/> blocks from the origin of a wave
        /// started with <paramref name="speed"/> blocks/s.</summary>
        public static float ArrivalDelay(float distance, float speed) => speed <= 0f ? 0f : Mathf.Max(0f, distance) / speed;

        /// <summary>Starts a wave at <paramref name="origin"/> (scene position) that grows at <paramref name="speed"/>
        /// blocks/s out to <paramref name="maxRadius"/>. Colour is sRGB-authored.</summary>
        public static void Start(Vector3 origin, float maxRadius, float speed, Color color, float width = 3.5f, float strength = 1f, float lineDensity = 1.2f)
        {
            FxKit.Ensure();
            _origin = origin;
            _radius = 0f;
            _maxRadius = Mathf.Max(1f, maxRadius);
            _speed = Mathf.Max(1f, speed);
            _width = width;
            _lines = lineDensity;
            _color = color;
            _strength = strength * (FxKit.ReducedEffects ? 0.6f : 1f);
            _running = true;
            Upload(1f);
        }

        public static void Stop()
        {
            _running = false;
            Shader.SetGlobalVector(ColId, Vector4.zero);
        }

        internal static void Tick(float dt)
        {
            if (!_running)
            {
                return;
            }

            _radius += _speed * dt;
            // The wave runs a little past its max radius while it fades, so the edge of the scan range glows out.
            float over = _radius - _maxRadius;
            float fade = over <= 0f ? 1f : 1f - over / Mathf.Max(1f, _width * 2f);
            if (fade <= 0f)
            {
                Stop();
                return;
            }

            Upload(fade);
        }

        private static void Upload(float fade)
        {
            var lin = ShaderColor.Srgb(_color);
            Shader.SetGlobalVector(WaveId, new Vector4(_origin.x, _origin.y, _origin.z, _radius));
            Shader.SetGlobalVector(ColId, new Vector4(lin.r, lin.g, lin.b, _strength * fade));
            Shader.SetGlobalVector(ParamsId, new Vector4(_width, _maxRadius, _lines, 0f));
        }
    }
}
