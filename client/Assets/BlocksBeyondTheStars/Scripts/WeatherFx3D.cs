// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// In-world weather (M27 polish, P7 weather rest): actual 3D rain falling around the player during
    /// rain/storm, plus storm fog that cuts view distance. The drops are a recycled pool whose positions this
    /// class integrates itself; since #2399 they are DRAWN by one particle system (soft streaks stretched along
    /// their fall, soft flakes for snow and ash) fed through <c>SetParticles</c> each frame — the simulation has no
    /// emission and no gravity, so the world-aware placement below stays the only thing that moves a drop. Drops are gated per
    /// *column*, not by the player's own sky exposure: each drop only spawns where the sky is open above it and
    /// dies on hitting a solid block, so rain stays visible outside a cave mouth while the player stands inside,
    /// yet never falls through roofs or ceilings. A drop that reaches the ground near the camera leaves a small
    /// splash. Storm fog keys on the player's exposure (global fog would fill the cave/room). Both are off in the
    /// space view and while a menu is up. Density/speed/slant scale with the authoritative
    /// <c>WorldEnvironment.Intensity</c>. The looping rain/storm bed + thunder live in
    /// <see cref="ClientAudio"/>; the screen wash + lightning flash in <see cref="WeatherFx"/>.
    /// </summary>
    public sealed class WeatherFx3D : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Cam;

        /// <summary>Ground splashes and the full pool (#2399); off = half the drops, no splashes.</summary>
        public bool Particles = true;

        private const int Pool = 280;
        private const float SpawnRadius = 18f; // box half-extent around the camera the rain spawns in
        private const float SpawnUp = 12f;     // height above the camera it spawns at
        private const float SplashRadius = 14f; // splashes only where they can be seen
        private Vector3[] _pos;
        private bool[] _alive;
        private float[] _speed;
        private ParticleSystem _ps;
        private ParticleSystemRenderer _psr;
        private ParticleSystem.Particle[] _particles;
        private string _styledPrecip;         // the precipitation kind the renderer is currently set up for (mode, material, stretch)
        private readonly System.Random _rng = new System.Random(13);

        // Per-column "open sky above?" cache so per-drop spawn checks stay cheap; cleared once a
        // second so block edits and freshly streamed chunks are picked up.
        private readonly Dictionary<long, bool> _skyOpen = new Dictionary<long, bool>(256);
        private float _skyCacheTimer;
        private string _loggedPrecip = "none"; // diagnostics: last precipitation kind written to the log

        // Saved global fog state so storm fog restores cleanly when the weather clears / we leave the world.
        private bool _fogSaved;
        private bool _prevFog;
        private Color _prevFogColor;
        private float _prevFogDensity;
        private FogMode _prevFogMode;

        private void Start()
        {
            _pos = new Vector3[Pool];
            _alive = new bool[Pool];
            _speed = new float[Pool];
            _particles = new ParticleSystem.Particle[Pool];
            for (int i = 0; i < Pool; i++)
            {
                _speed[i] = 26f + (float)_rng.NextDouble() * 16f;
            }

            // One particle system, driven by SetParticles. The simulation keeps running (a paused system never refreshes
            // its renderer bounds, so the camera culled every drop), but it has nothing of its own to do: no emission,
            // no gravity, and every position is rewritten here each frame — the velocity only sets the streak's stretch
            // and moves the drop by one frame's fall before the next rewrite. Never culled: the drops follow the camera.
            var go = new GameObject("WeatherDrops");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            var main = _ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.simulationSpeed = 1f;
            main.gravityModifier = 0f;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.maxParticles = Pool;
            main.startLifetime = 1000f;
            var emission = _ps.emission;
            emission.enabled = false;
            _psr = go.GetComponent<ParticleSystemRenderer>();
            _psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _psr.receiveShadows = false;
            _psr.renderMode = ParticleSystemRenderMode.Stretch;
            _psr.velocityScale = 0f;
            _psr.lengthScale = 1f;
            _psr.sharedMaterial = FxKit.StreakMaterial();
            _ps.Play();
        }

        private struct Style
        {
            public Color Color;
            public float Fall, Slant, Drift, Density;
            public Vector3 Scale;
            public Quaternion Tilt;

            /// <summary>Rises instead of falling — spores drifting up off the canopy.</summary>
            public bool Rises;
        }

        /// <summary>Per-precipitation look: colour, fall speed, slant, sideways drift, density + drop shape.</summary>
        private static Style StyleFor(string precip, bool storm) => precip switch
        {
            "snow" => new Style { Color = new Color(0.96f, 0.97f, 1f), Fall = 0.22f, Slant = 0.8f, Drift = 1.5f, Density = 1.1f, Scale = new Vector3(0.09f, 0.09f, 0.09f), Tilt = Quaternion.identity },
            "sleet" => new Style { Color = new Color(0.88f, 0.92f, 0.98f), Fall = 0.7f, Slant = 2.5f, Drift = 0.9f, Density = 1.0f, Scale = new Vector3(0.05f, 0.22f, 0.05f), Tilt = Quaternion.Euler(9f, 0f, 0f) },
            "hail" => new Style { Color = new Color(0.86f, 0.90f, 0.96f), Fall = 1.9f, Slant = 1.5f, Drift = 0.3f, Density = 0.9f, Scale = new Vector3(0.11f, 0.13f, 0.11f), Tilt = Quaternion.identity },
            "ash" => new Style { Color = new Color(1f, 0.5f, 0.2f), Fall = 0.30f, Slant = 0.8f, Drift = 1.8f, Density = 1.0f, Scale = new Vector3(0.07f, 0.07f, 0.07f), Tilt = Quaternion.identity },
            "sandstorm" => new Style { Color = new Color(0.82f, 0.70f, 0.46f), Fall = 0.35f, Slant = 16f, Drift = 3f, Density = 1.3f, Scale = new Vector3(0.05f, 0.05f, 0.30f), Tilt = Quaternion.identity },
            // A gale carries grit, not water: long, near-horizontal streaks.
            "dust" => new Style { Color = new Color(0.74f, 0.68f, 0.55f), Fall = 0.18f, Slant = 20f, Drift = 4f, Density = 1.1f, Scale = new Vector3(0.04f, 0.04f, 0.36f), Tilt = Quaternion.identity },
            "drizzle" => new Style { Color = new Color(0.72f, 0.82f, 1f), Fall = 0.55f, Slant = 1.2f, Drift = 0.5f, Density = 0.55f, Scale = new Vector3(0.02f, 0.22f, 0.02f), Tilt = Quaternion.Euler(3f, 0f, 0f) },
            // Corrosive rain: sickly green, heavier and faster than water.
            "acid" => new Style { Color = new Color(0.62f, 0.95f, 0.42f), Fall = 1.15f, Slant = 3f, Drift = 0.4f, Density = 1.0f, Scale = new Vector3(0.04f, 0.55f, 0.04f), Tilt = Quaternion.Euler(8f, 0f, 0f) },
            // Meteorite grit: very fast, long, burning-bright streaks.
            "meteor" => new Style { Color = new Color(1f, 0.82f, 0.45f), Fall = 3.4f, Slant = 9f, Drift = 0.2f, Density = 0.5f, Scale = new Vector3(0.04f, 1.4f, 0.04f), Tilt = Quaternion.Euler(22f, 0f, 0f) },
            // Spores drift UP off the canopy — the one weather that rises.
            "spores" => new Style { Color = new Color(0.72f, 1f, 0.78f), Fall = 0.12f, Slant = 0.6f, Drift = 2.2f, Density = 0.8f, Scale = new Vector3(0.06f, 0.06f, 0.06f), Tilt = Quaternion.identity, Rises = true },
            _ => new Style { Color = new Color(0.68f, 0.80f, 1f), Fall = storm ? 1.45f : 1f, Slant = storm ? 7f : 2f, Drift = 0.3f, Density = 1.0f, Scale = new Vector3(0.03f, storm ? 0.75f : 0.5f, 0.03f), Tilt = Quaternion.Euler(storm ? 14f : 4f, 0f, 0f) },
        };

        private void LateUpdate()
        {
            var env = Game?.Environment;
            string precip = env?.Precipitation ?? "none";
            // Deliberately NOT gated on Game.ExposedToSky: per-column checks below decide where drops
            // may fall, so rain stays visible outside the cave mouth while the player stands inside.
            bool inWorld = env != null && !Game.SpaceViewActive && !Game.MenuOpen;
            bool active = inWorld && precip != "none";

            // Fog is keyed on the WEATHER, not on the precipitation: fog, ground fog and an ion storm
            // drop nothing at all yet are exactly the states that close the view in (#900). It still
            // keys on the player's own exposure — otherwise a sheltered cave/room would fill with fog.
            ApplyFog(env, precip, inWorld && Game.ExposedToSky);

            if (Cam == null || !active)
            {
                HideAll();
                return;
            }

            _skyCacheTimer -= Time.deltaTime;
            if (_skyCacheTimer <= 0f)
            {
                _skyCacheTimer = 1f;
                _skyOpen.Clear();
            }

            var s = StyleFor(precip, env.Weather == "storm");
            if (!ReferenceEquals(precip, _loggedPrecip) && precip != _loggedPrecip)
            {
                // Playtest 2026-09-05 ("no rain in a thunderstorm"): one line per precipitation change so the
                // Player.log says what the client was told and how many drops it tried to show.
                _loggedPrecip = precip;
                Debug.Log($"[Weather3D] precipitation '{precip}' (weather {env.Weather}, intensity {env.Intensity:0.00}, exposed {Game?.ExposedToSky})");
            }

            // #1758: WATER precipitation takes the world's water colour (rain, drizzle, sleet); snow, hail, ash, sand,
            // acid, meteors and spores keep their own look. A rainbow world's rain cycles through the colours.
            Color styled = precip is "rain" or "drizzle" or "sleet" ? WaterColours.Blend(s.Color, env, 0.75f, Time.time) : s.Color;
            Color drop = ShaderColor.Srgb(styled); // particle vertex colours go to the shader as-is → hand over linear
            drop.a = precip is "snow" or "ash" or "spores" ? 0.85f : 0.9f;
            // #2399: flakes (snow, hail, ash, spores) are round soft-dot billboards; everything else a streak (its own
            // texture: firm core, soft ends) stretched along its fall — the particle's velocity sets the stretch only.
            bool flakes = precip is "snow" or "hail" or "ash" or "spores";
            if (!ReferenceEquals(precip, _styledPrecip) && precip != _styledPrecip)
            {
                // Per precipitation kind (the first rain of a session included — this used to key on the flake/streak
                // flip alone, so the first rain kept the stretch length of 1 and fell as dots): render mode, material
                // and the streak length, which the style's own proportions set.
                _styledPrecip = precip;
                _psr.renderMode = flakes ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
                _psr.sharedMaterial = flakes ? FxKit.SoftAlphaMaterial() : FxKit.StreakMaterial();
                _psr.velocityScale = 0f;
                _psr.lengthScale = flakes ? 1f : Mathf.Max(1f, s.Scale.y / Mathf.Max(0.02f, s.Scale.x) * 0.5f);
            }

            // Intensity comes from the SMOOTHED client value (#900), so an episode's swell and fade shows
            // as a ramp in the drop count rather than a 5 s staircase.
            float strength = Game != null ? Game.WeatherIntensity : env.Intensity;
            int count = Mathf.RoundToInt(Pool * Mathf.Clamp01(0.4f + strength * 0.6f) * s.Density * (Particles ? 1f : 0.5f));
            count = Mathf.Min(count, Pool);
            var camPos = Cam.transform.position;
            float dt = Time.deltaTime, t = Time.time;

            // Wind blows the precipitation along the world's own wind direction, so a gale visibly drives
            // it sideways and a still sky drops it straight down.
            float wind = Game != null ? Game.WindSpeed : 0f;
            Vector3 windDir = Game != null ? Game.WindVector : Vector3.zero;
            float windPush = wind * 12f;
            float size = Mathf.Max(s.Scale.x, s.Scale.z) * (flakes ? 1.6f : 3.0f); // a streak needs some width to read at all
            int live = 0;

            for (int i = 0; i < Pool; i++)
            {
                if (i >= count)
                {
                    _alive[i] = false;
                    continue;
                }

                if (!_alive[i])
                {
                    if (!TryRespawn(i, camPos, s.Rises))
                    {
                        continue; // no open-sky column found this frame (e.g. deep underground)
                    }

                    _alive[i] = true;
                }

                float wobble = Mathf.Sin(t * 2.2f + i) * s.Drift; // flakes/embers/sand swirl sideways
                float vertical = _speed[i] * s.Fall * (s.Rises ? 1f : -1f);
                var step = new Vector3(
                    (s.Slant + wobble + windDir.x * windPush) * dt,
                    vertical * dt,
                    (wobble * 0.4f + windDir.z * windPush) * dt);
                var p = _pos[i] + step;
                // Rising motes leave upward; falling ones die below the camera or inside a block.
                bool gone = s.Rises ? p.y > camPos.y + SpawnUp + 8f : p.y < camPos.y - 7f;
                bool hit = !gone && InsideBlock(p);
                if (hit && !s.Rises && Particles && !flakes)
                {
                    Splash(p, step, camPos, styled, precip);
                }

                if (gone || hit || (p - camPos).sqrMagnitude > (SpawnRadius * 1.6f) * (SpawnRadius * 1.6f))
                {
                    if (!TryRespawn(i, camPos, s.Rises))
                    {
                        _alive[i] = false;
                        continue;
                    }

                    p = _pos[i];
                }
                else
                {
                    _pos[i] = p;
                }

                ref var part = ref _particles[live++];
                part.position = p;
                part.velocity = dt > 1e-5f ? step / dt : Vector3.down; // stretch direction + length (streaks)
                part.startSize = size;
                part.startColor = drop;
                part.startLifetime = 1000f;
                part.remainingLifetime = 1000f;
                part.rotation = 0f;
            }

            _ps.SetParticles(_particles, live);

            // Capture diagnostics (#2405): while a capture pins the environment, say what the drops are doing.
            if (Game != null && Game.CaptureEnvActive && Time.unscaledTime >= _nextDropLog)
            {
                _nextDropLog = Time.unscaledTime + 2f;
                var b = _psr.bounds;
                Debug.Log($"[Weather3D] drops live={live} count={count} strength={strength:F2} psCount={_ps.particleCount} playing={_ps.isPlaying} "
                          + $"visible={_psr.isVisible} enabled={_psr.enabled} active={_ps.gameObject.activeInHierarchy} layer={_ps.gameObject.layer} "
                          + $"mat={(_psr.sharedMaterial != null ? _psr.sharedMaterial.shader.name : "null")} bounds={b.center}/{b.size} cam={camPos} first={(live > 0 ? _particles[0].position : Vector3.zero)}");
            }
        }

        private float _nextDropLog;

        /// <summary>A small splash where a drop meets the ground within sight of the camera (#2399): a puff for rain,
        /// a brighter one for acid, nothing for the dry forms. Every fourth hit, so the dust budget is never flooded.</summary>
        private void Splash(Vector3 at, Vector3 step, Vector3 camPos, Color drop, string precip)
        {
            if (precip is "sandstorm" or "dust" or "meteor" || (at - camPos).sqrMagnitude > SplashRadius * SplashRadius
                || _rng.Next(4) != 0)
            {
                return;
            }

            // Back the point up out of the block it entered so the puff sits on the surface, not inside it.
            Vector3 surface = at - step.normalized * 0.3f;
            Color c = precip == "acid" ? new Color(0.7f, 1f, 0.5f, 0.6f) : new Color(drop.r, drop.g, drop.b, 0.45f);
            FxKit.Emit(FxKit.Kind.Dust, surface, Vector3.up * 0.6f, 0.22f, 0.28f, c);
        }

        /// <summary>Moves the drop to a fresh spawn above the camera, but only into a column with open
        /// sky overhead — drops must never appear under a cave ceiling or roof. False = no open column
        /// found this frame; the caller keeps the drop hidden and retries next frame.</summary>
        private bool TryRespawn(int i, Vector3 camPos, bool rises = false)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                // Falling weather appears above the camera; rising motes (spores) come up from below it.
                float y = rises
                    ? -4f - (float)_rng.NextDouble() * 4f
                    : SpawnUp + (float)_rng.NextDouble() * 6f;
                var p = camPos + new Vector3(
                    (float)(_rng.NextDouble() * 2 - 1) * SpawnRadius,
                    y,
                    (float)(_rng.NextDouble() * 2 - 1) * SpawnRadius);
                if (ColumnOpen(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z)))
                {
                    _pos[i] = p;
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when the drop entered a solid world block (terrain, roof, cave ceiling) or a parked
        /// ship's cell (ship-as-object — the hull is not in the world grid) — it must stop there instead of
        /// falling through into view below.</summary>
        private bool InsideBlock(Vector3 p)
        {
            var w = Game?.World;
            if (w == null)
            {
                return false;
            }

            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            return !w.GetBlock(x, y, z).IsAir || !Game.LandedShipBlockAt(x, y, z, out _, out _).IsAir;
        }

        /// <summary>Open sky above this spawn point? Same scan as <c>GameBootstrap.ComputeExposedToSky</c>,
        /// but per rain column instead of per player, cached per column for a second.</summary>
        private bool ColumnOpen(int x, int yFrom, int z)
        {
            var w = Game?.World;
            if (w == null)
            {
                return true;
            }

            long key = ((long)x << 32) ^ (uint)z;
            if (_skyOpen.TryGetValue(key, out bool open))
            {
                return open;
            }

            open = true;
            for (int y = yFrom; y <= yFrom + 40; y++)
            {
                if (!w.GetBlock(x, y, z).IsAir)
                {
                    open = false;
                    break;
                }
            }

            // Parked ship objects roof their columns too (the hull is not in the world grid).
            if (open && Game.LandedShipCovers(x, yFrom, z))
            {
                open = false;
            }

            _skyOpen[key] = open;
            return open;
        }

        /// <summary>True while the camera sits within a few blocks of the ground it stands on — the height
        /// gate for ground fog, which must hang in the valleys and not follow you up a mountain.</summary>
        private bool IsNearGround(Vector3 p)
        {
            var w = Game?.World;
            if (w == null)
            {
                return true;
            }

            int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.z);
            for (int dy = 1; dy <= GroundFogHeight; dy++)
            {
                if (!w.GetBlock(x, Mathf.FloorToInt(p.y) - dy, z).IsAir)
                {
                    return true; // solid ground close below → we're down in the mist
                }
            }

            return false;
        }

        /// <summary>How far above the ground the ground-fog layer reaches, in blocks.</summary>
        private const int GroundFogHeight = 7;

        private void HideAll()
        {
            if (_alive == null || _ps == null)
            {
                return;
            }

            bool any = false;
            for (int i = 0; i < _alive.Length; i++)
            {
                any |= _alive[i];
                _alive[i] = false;
            }

            if (any || _ps.particleCount > 0)
            {
                _ps.SetParticles(_particles, 0);
            }
        }

        /// <summary>Storm fog: a low grey-blue fog that cuts view distance during a storm (scaled by
        /// intensity), restoring the previous fog state when the storm passes or we leave the world.</summary>
        private void ApplyFog(BlocksBeyondTheStars.Networking.Messages.WorldEnvironment env, string precip, bool active)
        {
            // A sandstorm or a gale always blinds you; so do the obscuring events and a full storm (#900).
            // Ground fog is deliberately NOT global: it only closes in while the camera is near the ground,
            // so from a hilltop you look out OVER it — Unity's fog has no height falloff of its own.
            bool nearGround = Cam == null || Game?.World == null || IsNearGround(Cam.transform.position);
            bool fog = active && (precip is "sandstorm" or "dust"
                                  || env.Weather is "storm" or "toxic_storm" or "blizzard" or "fog" or "ion_storm"
                                  || (env.Weather == "ground_fog" && nearGround));
            if (fog)
            {
                if (!_fogSaved)
                {
                    _prevFog = RenderSettings.fog;
                    _prevFogColor = RenderSettings.fogColor;
                    _prevFogDensity = RenderSettings.fogDensity;
                    _prevFogMode = RenderSettings.fogMode;
                    _fogSaved = true;
                }

                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                float intensity = Mathf.Clamp01(Game != null ? Game.WeatherIntensity : env.Intensity);
                if (precip is "sandstorm" or "dust")
                {
                    RenderSettings.fogColor = new Color(0.78f, 0.66f, 0.42f);          // choking tan dust
                    RenderSettings.fogDensity = 0.012f + intensity * 0.020f;
                }
                else if (env.Weather is "fog" or "ground_fog")
                {
                    RenderSettings.fogColor = new Color(0.80f, 0.83f, 0.86f);          // soft grey murk
                    RenderSettings.fogDensity = (env.Weather == "fog" ? 0.010f : 0.006f) + intensity * 0.022f;
                }
                else if (env.Weather == "ion_storm")
                {
                    RenderSettings.fogColor = new Color(0.42f, 0.55f, 0.78f);          // charged blue haze
                    RenderSettings.fogDensity = 0.003f + intensity * 0.008f;
                }
                else
                {
                    RenderSettings.fogColor = precip == "ash" ? new Color(0.34f, 0.26f, 0.22f) // smoky
                                            : precip == "acid" ? new Color(0.40f, 0.52f, 0.32f) // acrid green
                                            : precip == "snow" || precip == "sleet" || precip == "hail" ? new Color(0.74f, 0.78f, 0.84f) // white-out
                                            : new Color(0.45f, 0.50f, 0.58f);          // grey-blue rain storm
                    // A blizzard closes in far harder than an ordinary storm.
                    float bite = env.Weather == "blizzard" ? 0.022f : 0.010f;
                    RenderSettings.fogDensity = 0.004f + intensity * bite;
                }
            }
            else if (_fogSaved)
            {
                RestoreFog();
            }
        }

        private void RestoreFog()
        {
            RenderSettings.fog = _prevFog;
            RenderSettings.fogColor = _prevFogColor;
            RenderSettings.fogDensity = _prevFogDensity;
            RenderSettings.fogMode = _prevFogMode;
            _fogSaved = false;
        }

        private void OnDestroy()
        {
            if (_fogSaved)
            {
                RestoreFog(); // don't leave storm fog on when the world tears down
            }
        }
    }
}
