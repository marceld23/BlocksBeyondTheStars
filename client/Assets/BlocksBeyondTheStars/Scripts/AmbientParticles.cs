// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Slow-drifting ambient motes around the player, to make the air feel alive — the cheap-but-big "atmosphere" win
    /// that pairs with fog/god-rays. A code-driven Unity <see cref="ParticleSystem"/> using the additive
    /// <c>BlocksBeyondTheStars/Particle</c> shader; the emitter follows the camera's position (not its rotation, so motes
    /// don't swing with the view); particles simulate in world space and fade in/out softly. Two modes
    /// (<see cref="AmbientMotes.ModeFor"/>, #2808/#2818):
    /// <list type="bullet">
    /// <item><b>Outdoor</b> — the planet's own motes (<see cref="AmbientMotes.StyleFor"/>: dust, pollen, embers, ash,
    /// frost, rising violet spores on fungal worlds …), only in the open air: never in space, a station, an airless sky,
    /// aboard, in a cave or under a roof (a tree's canopy is no roof). Stepping home fades the outdoor ones out at once.</item>
    /// <item><b>Lamplight</b> — at home (aboard the own ship, or under a roof in the own base's air) a few very slow,
    /// warm amber-cream motes drift in the light of the fixtures nearby (<see cref="NearbyLights"/>: the world's light
    /// index plus the parked ships' lamps). Off with Reduced effects.</item>
    /// </list>
    /// Every rate follows the live effect density (<see cref="FxKit.Density"/>: quality preset × Reduced effects).
    /// </summary>
    public sealed class AmbientParticles : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const float RoofCheckInterval = 0.5f;
        private const float LampScanInterval = 1f;
        private const float LampReach = 9f; // fixtures within this many blocks of the player shed motes
        private const int MaxLamps = 6;
        private const float OutdoorAlpha = 0.5f;
        private const float LampAlpha = 0.55f;

        private ParticleSystem _ps;
        private Transform _dust;     // the particle object (a child — moved independently of the shared root)
        private Material _mat;

        private AmbientMoteMode _mode = AmbientMoteMode.None;
        private AmbientMoteStyle _style = AmbientMotes.Dust;
        private bool _styleValid;
        private AmbientMoteMode _styleMode;
        private string _styleBiome;
        private string _styleTheme;
        private Color _tintA, _tintB; // the style's two colours, linear, for the hand-emitted lamplight motes

        private bool _openAir = true;
        private float _roofTimer;

        private readonly NearbyLights _lights = new NearbyLights();
        private readonly List<NearbyLights.Hit> _lampHits = new List<NearbyLights.Hit>();
        private readonly List<Vector3> _lamps = new List<Vector3>();
        private float _lampTimer;
        private float _lampAcc;
        private ParticleSystem.Particle[] _fadeBuffer;

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Particle");
            if (shader == null)
            {
                enabled = false;
                return;
            }

            _mat = new Material(shader) { mainTexture = BuildDot() };

            var go = new GameObject("AmbientDust");
            go.transform.SetParent(transform, false); // child of the rig root; we move only THIS, never the root
            _dust = go.transform;
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.loop = true;
            main.startLifetime = 6f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // stay put as the camera turns
            main.gravityModifier = 0f;
            main.startColor = new Color(0.9f, 0.92f, 1f, OutdoorAlpha);

            var emission = _ps.emission;
            emission.rateOverTime = 0f; // gated each frame in LateUpdate

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(26f, 12f, 26f);

            // The style's steady drift: spores and embers rise, ash and frost settle (set per style in ApplyStyle).
            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);

            var noise = _ps.noise;
            noise.enabled = true;
            noise.strength = 0.07f;
            noise.frequency = 0.2f;
            noise.scrollSpeed = 0.1f;

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = _mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.None;

            _ps.Play();
        }

        private void LateUpdate()
        {
            if (_ps == null || Camera == null || Game == null)
            {
                return;
            }

            // Follow the camera's position only (world-sim particles keep their place as you look around).
            _dust.position = Camera.transform.position;

            bool airless = Game.SpaceViewActive || !string.IsNullOrEmpty(Game.StationName)
                           || (Game.Environment != null && Game.Environment.SpaceSky) || Game.OnFootInSpace;
            bool home = Game.AtHome && !Game.SpaceViewActive;
            var mode = AmbientMotes.ModeFor(airless, Game.Aboard, home, OpenAir(), FxKit.ReducedEffects);
            if (mode != _mode)
            {
                if (_mode == AmbientMoteMode.Outdoor && (home || Game.Aboard))
                {
                    FadeOutLive(1f); // stepping aboard or indoors at home: no outdoor pollen drifting through the room
                }

                _mode = mode;
                _lampTimer = 0f; // look for lamps at once
            }

            ApplyStyle();

            // Live density: the quality preset and Reduced effects (FxKit.Configure) apply without a restart.
            var em = _ps.emission;
            em.rateOverTime = _mode == AmbientMoteMode.Outdoor ? AmbientMotes.OutdoorRate * _style.Rate * FxKit.Density : 0f;

            if (_mode == AmbientMoteMode.Lamplight)
            {
                EmitLamplight(Time.deltaTime);
            }
        }

        /// <summary>Open air over the head: the sky scan's verdict, except that a tree's canopy is no roof — pollen
        /// still drifts under the leaves. Re-checked twice a second while the sky scan reports cover.</summary>
        private bool OpenAir()
        {
            if (Game.ExposedToSky)
            {
                _roofTimer = 0f;
                _openAir = true;
                return true;
            }

            _roofTimer -= Time.deltaTime;
            if (_roofTimer <= 0f)
            {
                _roofTimer = RoofCheckInterval;
                _openAir = UnderCanopyOnly();
            }

            return _openAir;
        }

        /// <summary>The column over the head holds flora (leaves, giant mushroom caps) and nothing else — the same
        /// column <see cref="GameBootstrap"/>'s sky scan walks. Nothing in the grid at all means a parked ship's hull
        /// covered the player, which is a roof.</summary>
        private bool UnderCanopyOnly()
        {
            var world = Game.World;
            var content = Game.Content;
            if (world == null || content == null || Game.Aboard)
            {
                return false;
            }

            int px = Mathf.FloorToInt(Game.PlayerPosition.x);
            int py = Mathf.FloorToInt(Game.PlayerPosition.y);
            int pz = Mathf.FloorToInt(Game.PlayerPosition.z);
            bool canopy = false;
            for (int y = py + 2; y <= py + 50; y++)
            {
                var id = world.GetBlock(px, y, pz);
                if (id.IsAir)
                {
                    continue;
                }

                var def = content.BlockById(id);
                if (def == null || def.Category != "flora")
                {
                    return false; // a real roof: rock, a building, a cave ceiling
                }

                canopy = true;
            }

            return canopy && !Game.LandedShipCovers(px, py + 2, pz);
        }

        /// <summary>Pushes the current mode's look into the particle system — on a mode or planet change only.</summary>
        private void ApplyStyle()
        {
            string biome = Game.Environment?.Biome ?? string.Empty;
            string theme = _mode == AmbientMoteMode.Lamplight ? string.Empty : Game.Content?.GetPlanet(biome)?.FloraTheme ?? string.Empty;
            theme += "|" + (FestivalMood.Effects(Game)?.Motes ?? string.Empty); // #3004: a festival's motes re-key the cache
            if (_styleValid && _styleMode == _mode && _styleBiome == biome && _styleTheme == theme)
            {
                return;
            }

            _styleValid = true;
            _styleMode = _mode;
            _styleBiome = biome;
            _styleTheme = theme;
            // #3004: a festival's motes (petals at Hanami, lamplight in the lights festivals …) replace the world's own outdoors.
            var festival = _mode == AmbientMoteMode.Lamplight ? null : AmbientMotes.ByName(FestivalMood.Effects(Game)?.Motes);
            _style = festival ?? (_mode == AmbientMoteMode.Lamplight ? AmbientMotes.Lamplight : AmbientMotes.StyleFor(biome, theme));

            float alpha = _mode == AmbientMoteMode.Lamplight ? LampAlpha : OutdoorAlpha;
            _tintA = FxKit.Lin(new Color(_style.R, _style.G, _style.B, alpha));
            _tintB = FxKit.Lin(new Color(_style.R2, _style.G2, _style.B2, alpha));

            var main = _ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(_tintA, _tintB); // each mote a random mix of the two
            main.startLifetime = new ParticleSystem.MinMaxCurve(_style.Life * 0.85f, _style.Life * 1.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f * _style.Size, 0.06f * _style.Size);

            var vel = _ps.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(_style.Rise);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
        }

        /// <summary>The lamplight motes (#2818): a few per second, density-scaled, each born in the air near one of the
        /// fixtures in reach (mostly below it — the lamps hang from ceilings and walls).</summary>
        private void EmitLamplight(float dt)
        {
            _lampTimer -= dt;
            if (_lampTimer <= 0f)
            {
                _lampTimer = LampScanInterval;
                FindLamps();
            }

            if (_lamps.Count == 0)
            {
                _lampAcc = 0f;
                return;
            }

            _lampAcc = Mathf.Min(_lampAcc + (AmbientMotes.LamplightRate * FxKit.Density * dt), 3f);
            while (_lampAcc >= 1f)
            {
                _lampAcc -= 1f;
                EmitLampMote(_lamps[Random.Range(0, _lamps.Count)]);
            }
        }

        /// <summary>The fixtures within <see cref="LampReach"/>: full-reach lights only (lamps, strips, lanterns, a
        /// campfire), not the faint natural emitters (glowing plants, lava, crystals).</summary>
        private void FindLamps()
        {
            _lamps.Clear();
            _lights.Collect(Game, LampReach, _lampHits, fixturesOnly: true);
            for (int i = 0; i < _lampHits.Count && _lamps.Count < MaxLamps; i++)
            {
                _lamps.Add(_lampHits[i].Scene);
            }
        }

        private void EmitLampMote(Vector3 lamp)
        {
            // A few tries for a spot in the room's air near the lamp; a wall or the lamp's own block rejects one.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var p = lamp + new Vector3(Random.Range(-1.3f, 1.3f), Random.Range(-2f, 0.6f), Random.Range(-1.3f, 1.3f));
                if (!NearbyLights.AirAt(Game, Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z)))
                {
                    continue;
                }

                var ep = new ParticleSystem.EmitParams
                {
                    position = p,
                    velocity = new Vector3(Random.Range(-0.03f, 0.03f), Random.Range(-0.015f, 0.03f), Random.Range(-0.03f, 0.03f)),
                    startSize = Random.Range(0.02f, 0.06f) * _style.Size,
                    startLifetime = Random.Range(_style.Life * 0.8f, _style.Life * 1.2f),
                    startColor = Color.Lerp(_tintA, _tintB, Random.value),
                    applyShapeToPosition = false,
                };
                _ps.Emit(ep, 1);
                return;
            }
        }

        /// <summary>Lets the live motes die within <paramref name="seconds"/> (the colour-over-lifetime fades them).</summary>
        private void FadeOutLive(float seconds)
        {
            if (_ps.particleCount == 0)
            {
                return;
            }

            int max = _ps.main.maxParticles;
            if (_fadeBuffer == null || _fadeBuffer.Length < max)
            {
                _fadeBuffer = new ParticleSystem.Particle[max];
            }

            int n = _ps.GetParticles(_fadeBuffer);
            for (int i = 0; i < n; i++)
            {
                if (_fadeBuffer[i].remainingLifetime > seconds)
                {
                    _fadeBuffer[i].remainingLifetime = seconds;
                }
            }

            _ps.SetParticles(_fadeBuffer, n);
        }

        /// <summary>A soft round dot (bright core → transparent rim) for each mote.</summary>
        private static Texture2D BuildDot()
        {
            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2f);
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
