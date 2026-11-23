// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Glowing haze: the haze and the valley mist take the colour of the natural lights around the player
    /// (lava, crystals, glowing plants), the more of them there are and the lower the haze lies — the orange fog over
    /// a lava field, the violet shimmer in a crystal hollow. One colour for the whole view (the mean of the lights
    /// within reach, read from the world's light index a few times a second), blended into the haze colour by
    /// <c>BbtsHazeMix</c>; placed lamps play no part.
    /// </summary>
    public sealed partial class Sky
    {
        private static readonly int GlowHazeId = Shader.PropertyToID("_Sc_LookGlowHaze");
        private static readonly int GlowHaze2Id = Shader.PropertyToID("_Sc_LookGlowHaze2");

        private const int GlowReach = 56;       // blocks around the player's chunk
        private const float GlowBand = 22f;     // metres above the lights over which the glow fades out of the haze

        private readonly List<(Vector3i Pos, int Rgb)> _glowSources = new List<(Vector3i Pos, int Rgb)>();
        private float _glowScan;
        private Vector4 _glowTarget, _glowNow;
        private float _glowHeightTarget, _glowHeightNow;

        /// <summary>Publishes the glow of the haze for this frame (called with the look's other globals).</summary>
        private void PublishGlowHaze()
        {
            float amount = GameLook.GlowHazeNow;
            if (amount <= 0f || Game == null || Game.World == null)
            {
                _glowTarget = Vector4.zero;
            }
            else
            {
                _glowScan -= Time.deltaTime;
                if (_glowScan <= 0f)
                {
                    _glowScan = 0.6f;
                    ScanGlow(amount);
                }
            }

            float blend = 1f - Mathf.Exp(-Time.deltaTime * 1.5f);
            _glowNow = Vector4.Lerp(_glowNow, _glowTarget, blend);
            _glowHeightNow = Mathf.Lerp(_glowHeightNow, _glowHeightTarget, blend);
            Shader.SetGlobalVector(GlowHazeId, _glowNow);
            Shader.SetGlobalVector(GlowHaze2Id, new Vector4(_glowHeightNow, 1f / GlowBand, 0f, 0f));
        }

        /// <summary>The mean colour, the amount and the mean height of the natural lights within reach, each weighted by
        /// its nearness. A huge lava field is sampled (every n-th source), so the scan stays cheap.</summary>
        private void ScanGlow(float amount)
        {
            Vector3 player = Game.PlayerPosition;
            var cell = WorldConstants.CanonicalBlock(
                new Vector3i(Mathf.FloorToInt(player.x), Mathf.FloorToInt(player.y), Mathf.FloorToInt(player.z)), Game.Circumference);
            _glowSources.Clear();
            Game.World.LightSourcesNear(WorldConstants.WorldToChunk(cell), GlowReach, _glowSources);
            int stride = Mathf.Max(1, _glowSources.Count / 2000);
            float weight = 0f, height = 0f;
            Vector3 colour = Vector3.zero;
            for (int i = 0; i < _glowSources.Count; i += stride)
            {
                int packed = _glowSources[i].Rgb;
                if (packed == 0 || BlockLight.RadiusFrom(packed) >= BlockLight.DefaultRadius)
                {
                    continue; // a source that casts nothing, or a placed lamp
                }

                var pos = _glowSources[i].Pos;
                Vector3 scene = Game.ScenePos(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
                float d = Vector3.Distance(scene, player) / 24f;
                float w = stride / (1f + (d * d));
                int rgb = BlockLight.ColorFrom(packed);
                colour += new Vector3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f) * w;
                height += scene.y * w;
                weight += w;
            }

            if (weight < 0.5f)
            {
                _glowTarget = Vector4.zero; // the height stays where it was while the glow fades
                return;
            }

            // The lights' hue at full brightness, a little over white in the shader's space: the haze glows.
            Vector3 mean = colour / weight;
            float peak = Mathf.Max(0.05f, Mathf.Max(mean.x, Mathf.Max(mean.y, mean.z)));
            Color hue = ShaderColor.Srgb(new Color(mean.x / peak, mean.y / peak, mean.z / peak));
            float strength = Mathf.Clamp01(weight / 40f) * Mathf.Clamp01(amount);
            _glowTarget = new Vector4(hue.r * 1.3f, hue.g * 1.3f, hue.b * 1.3f, strength);
            _glowHeightTarget = height / weight;
        }
    }
}
