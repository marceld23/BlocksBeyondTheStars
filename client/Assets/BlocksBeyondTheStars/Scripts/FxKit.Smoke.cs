// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The smoke look (#3274) of the <see cref="Kind.Smoke"/> and <see cref="Kind.Plume"/> emitters: the
    /// <c>ParticleSmoke</c> shader with the procedural puff atlas (<see cref="SmokePuffs"/>, baked once per process), fed
    /// by custom vertex streams — the particle's age (erosion, colour over life), a stable random (which puff, mirrored or
    /// not) and its centre (the fake sphere normal the sun lights). Each puff gets a random angle and a slow spin in
    /// <see cref="Emit"/>. Without the shader (a stripped build) both kinds fall back to the old soft dot.
    /// </summary>
    public sealed partial class FxKit
    {
        private const string SmokeShaderName = "BlocksBeyondTheStars/ParticleSmoke";

        private static Material _smokeMat;
        private static Texture2D _smokeAtlas;

        /// <summary>The vertex streams <c>ParticleSmoke</c> reads: POSITION, COLOR, TEXCOORD0 = (uv, age, random),
        /// TEXCOORD1.xyz = the centre. The order IS the layout (Unity packs the streams tightly).</summary>
        private static readonly List<ParticleSystemVertexStream> SmokeStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.AgePercent,
            ParticleSystemVertexStream.StableRandomX,
            ParticleSystemVertexStream.Center,
        };

        /// <summary>The 2×2 puff atlas: R erosion noise, G billows, A density (linear data, mipmapped, clamped).</summary>
        public static Texture2D SmokeAtlas
        {
            get
            {
                if (_smokeAtlas != null)
                {
                    return _smokeAtlas;
                }

                _smokeAtlas = new Texture2D(SmokePuffs.AtlasSize, SmokePuffs.AtlasSize, TextureFormat.RGBA32, true, true)
                {
                    name = "FxSmokeAtlas",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                };
                _smokeAtlas.SetPixelData(SmokePuffs.BuildAtlas(), 0);
                _smokeAtlas.Apply(true, true);
                return _smokeAtlas;
            }
        }

        /// <summary>The shared smoke material (<c>ParticleSmoke</c> + the puff atlas); the soft-dot alpha material when the
        /// shader is missing or unsupported.</summary>
        public static Material SmokeMaterial()
        {
            if (_smokeMat != null)
            {
                return _smokeMat;
            }

            var shader = Shader.Find("BlocksBeyondTheStars/ParticleSmoke");
            if (shader == null || !shader.isSupported)
            {
                return SoftAlphaMaterial();
            }

            _smokeMat = new Material(shader) { name = "FxSmoke", mainTexture = SmokeAtlas };
            return _smokeMat;
        }

        /// <summary>Billboards; with the smoke shader also its vertex streams and a back-to-front sort (big alpha puffs
        /// overlap a lot — a few hundred particles sort for next to nothing).</summary>
        private static void ConfigureSmokeRenderer(ParticleSystemRenderer r)
        {
            r.renderMode = ParticleSystemRenderMode.Billboard;
            var mat = r.sharedMaterial;
            if (mat != null && mat.shader != null && mat.shader.name == SmokeShaderName)
            {
                r.SetActiveVertexStreams(SmokeStreams);
                r.sortMode = ParticleSystemSortMode.Distance;
            }
        }

        /// <summary>#3277: the steady force on every <see cref="Kind.Plume"/> puff (world space, m/s²) — the wind bending a
        /// tall column more the higher it rises, and its lift fading as it cools. The crater smoke sets it every frame it
        /// draws; nothing else uses the Plume emitter.</summary>
        public static void SetPlumeForce(Vector3 force)
        {
            var ps = Ensure().Emitter(Kind.Plume);
            if (ps == null)
            {
                return;
            }

            var f = ps.forceOverLifetime;
            if (!f.enabled)
            {
                f.enabled = true;
                f.space = ParticleSystemSimulationSpace.World;
                f.randomized = false;
            }

            f.x = new ParticleSystem.MinMaxCurve(force.x);
            f.y = new ParticleSystem.MinMaxCurve(force.y);
            f.z = new ParticleSystem.MinMaxCurve(force.z);
        }
    }
}
