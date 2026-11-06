// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// In-world weather (M27 polish, P7 weather rest): actual 3D rain falling around the player during
    /// rain/storm. (The weather's fog lives in the shared haze since #3270: <c>Sky.Mist.cs</c> tints the sky, the distance
    /// haze and the mist toward the fog, dust or ion-storm colour — this class used to set Unity's fog, which none of the
    /// project's shaders read.) The drops are a recycled pool whose positions this
    /// class integrates itself; since #2399 they are DRAWN by one particle system (soft streaks stretched along
    /// their fall, soft flakes for snow and ash) fed through <c>SetParticles</c> each frame — the simulation has no
    /// emission and no gravity, so the world-aware placement below stays the only thing that moves a drop. Drops are gated per
    /// *column*, not by the player's own sky exposure: each drop only spawns where the sky is open above it and
    /// dies on hitting a solid block, so rain stays visible outside a cave mouth while the player stands inside,
    /// yet never falls through roofs or ceilings. A drop that reaches the ground near the camera leaves a small
    /// splash. The drops are off in the
    /// space view and while a menu is up. Density/speed/slant scale with the authoritative
    /// <c>WorldEnvironment.Intensity</c>. The looping rain/storm bed + thunder live in
    /// <see cref="ClientAudio"/>; the screen wash + lightning flash in <see cref="WeatherFx"/>.
    /// <para>#3203: everything that falls or blows rides the world's wind direction (like the grass sway and the cloud
    /// shadows), the pool thins with the quality preset, and the effect eases out when the weather ends. The blown grit of
    /// a gale ("dust") or a sandstorm is thin streaks in the ground's colour (white spindrift on frozen worlds), varied
    /// per grain, fading in, at the edge and near the camera, and dimmed by the world light at night — the rules live in
    /// <see cref="WindGrit"/> (Client.Core), the grit-only parts in <c>WeatherFx3D.Grit.cs</c>.</para>
    /// </summary>
    public sealed partial class WeatherFx3D : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Cam;

        /// <summary>Ground splashes and the full pool (#2399); off = half the drops, no splashes.</summary>
        public bool Particles = true;

        private const int Pool = 280;
        private const float SpawnRadius = 18f; // box half-extent around the camera the rain spawns in
        private const float SpawnUp = 12f;     // height above the camera it spawns at
        private const float SplashRadius = 14f; // splashes only where they can be seen
        private const float KillRadius = SpawnRadius * 1.6f; // a drop this far from the camera is recycled
        private Vector3[] _pos;
        private bool[] _alive;
        private float[] _speed;
        private float[] _age;                 // seconds since the drop (re)spawned — grit fades in on it
        private float _envelope;              // 0..1: the whole effect, eased in and out (#3203)
        private string _fadingPrecip = "none"; // the kind drawn while the envelope falls after the weather ended
        private bool _snapEnvelope;           // a capture snap: settle the envelope on the next frame
        private ParticleSystem _ps;
        private ParticleSystemRenderer _psr;
        private ParticleSystem.Particle[] _particles;
        private string _styledPrecip;         // the precipitation kind the renderer is currently set up for (mode, material, stretch)
        private readonly System.Random _rng = new System.Random(13);

        // Per-cell "open sky above?" cache so per-drop spawn checks stay cheap; cleared once a second so block edits and
        // freshly streamed chunks are picked up. Keyed by the spawn CELL, not the column: the answer depends on the height
        // (a probe above a roof is open, one below it is not) — a per-column key let grit spawn inside houses (#3203 review).
        private readonly Dictionary<(int X, int Y, int Z), bool> _skyOpen = new Dictionary<(int X, int Y, int Z), bool>(512);
        private float _skyCacheTimer;
        private string _loggedPrecip = "none"; // diagnostics: last precipitation kind written to the log

        private void Start()
        {
            _pos = new Vector3[Pool];
            _alive = new bool[Pool];
            _speed = new float[Pool];
            _age = new float[Pool];
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

        private void OnEnable() => GameBootstrap.CaptureSnap += OnCaptureSnap;

        private void OnDisable() => GameBootstrap.CaptureSnap -= OnCaptureSnap;

        /// <summary>A capture asks every eased look to settle (#2405): the next frame jumps the envelope to its target.</summary>
        private void OnCaptureSnap() => _snapEnvelope = true;

        private struct Style
        {
            public Color Color;
            public float Fall, Slant, Drift, Density;
            public Vector3 Scale;
            public Quaternion Tilt;

            /// <summary>Rises instead of falling — spores drifting up off the canopy.</summary>
            public bool Rises;
        }

        /// <summary>Per-precipitation look: colour, fall speed, own speed along the wind (<c>Slant</c>, m/s), swirl across it,
        /// density + drop shape (x, y, z in metres — the long axis is y for falling streaks, z for the blown grit).</summary>
        private static Style StyleFor(string precip, bool storm) => precip switch
        {
            "snow" => new Style { Color = new Color(0.96f, 0.97f, 1f), Fall = 0.22f, Slant = 0.8f, Drift = 1.5f, Density = 1.1f, Scale = new Vector3(0.09f, 0.09f, 0.09f), Tilt = Quaternion.identity },
            "sleet" => new Style { Color = new Color(0.88f, 0.92f, 0.98f), Fall = 0.7f, Slant = 2.5f, Drift = 0.9f, Density = 1.0f, Scale = new Vector3(0.05f, 0.22f, 0.05f), Tilt = Quaternion.Euler(9f, 0f, 0f) },
            "hail" => new Style { Color = new Color(0.86f, 0.90f, 0.96f), Fall = 1.9f, Slant = 1.5f, Drift = 0.3f, Density = 0.9f, Scale = new Vector3(0.11f, 0.13f, 0.11f), Tilt = Quaternion.identity },
            "ash" => new Style { Color = new Color(1f, 0.5f, 0.2f), Fall = 0.30f, Slant = 0.8f, Drift = 1.8f, Density = 1.0f, Scale = new Vector3(0.07f, 0.07f, 0.07f), Tilt = Quaternion.identity },
            // #3203: the blown grit flies nearly level with the wind (it used to sink fast toward +X); its colour comes from
            // the ground (WindGrit.LookFor) — Color here is only the fallback.
            "sandstorm" => new Style { Color = new Color(0.82f, 0.70f, 0.46f), Fall = 0.12f, Slant = 8f, Drift = 3f, Density = 1.3f, Scale = new Vector3(0.05f, 0.05f, 0.30f), Tilt = Quaternion.identity },
            // A gale carries grit, not water: thin, near-horizontal streaks.
            "dust" => new Style { Color = new Color(0.74f, 0.68f, 0.55f), Fall = 0.10f, Slant = 8f, Drift = 4f, Density = 1.1f, Scale = new Vector3(0.04f, 0.04f, 0.36f), Tilt = Quaternion.identity },
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

            // #3203: when the weather ends the drops ease out (the last kind keeps drawing while the envelope falls) instead
            // of vanishing in one frame. Leaving the world — the space view, a menu — still clears them at once.
            if (!inWorld)
            {
                _envelope = 0f;
                _fadingPrecip = "none";
            }
            else
            {
                // A capture snap settles the envelope at once, so the first recorded frame is already at full strength.
                _envelope = _snapEnvelope ? (active ? 1f : 0f) : WindGrit.Envelope(_envelope, active, Time.deltaTime);
                if (active)
                {
                    _fadingPrecip = precip;
                }
            }

            _snapEnvelope = false;

            if (Cam == null || _envelope <= 0f || _fadingPrecip == "none")
            {
                HideAll();
                return;
            }

            DrawPrecipitation(env, _fadingPrecip);
        }

        /// <summary>The per-frame values every drop of the pool shares (one precipitation kind, one wind, one camera).</summary>
        private struct DropFrame
        {
            public Style S;
            public string Precip;
            public bool Flakes, Grit;
            public Vector3 CamPos;
            public float Dt, T;
            public Vector3 Drive;   // horizontal velocity along the world's wind (m/s)
            public Vector3 Across;  // unit vector across the wind — the swirl axis
            public float Size;
            public Color Drop;      // linear vertex colour, alpha incl. the envelope (everything but grit)
            public Color Styled;    // sRGB style colour for the splashes
            public int Count;
        }

        /// <summary>Moves and draws one frame of <paramref name="precip"/>: the pool integrates its own positions and feeds the
        /// particle system through <c>SetParticles</c>.</summary>
        private void DrawPrecipitation(BlocksBeyondTheStars.Networking.Messages.WorldEnvironment env, string precip)
        {
            _skyCacheTimer -= Time.deltaTime;
            if (_skyCacheTimer <= 0f)
            {
                _skyCacheTimer = 1f;
                _skyOpen.Clear();
            }

            var f = new DropFrame { S = StyleFor(precip, env.Weather == "storm"), Precip = precip, Dt = Time.deltaTime, T = Time.time };
            LogPrecipitation(env, precip);
            // #2399: flakes (snow, hail, ash, spores) are round soft-dot billboards; everything else a streak stretched along
            // its motion. #3203: the blown grit (a gale's dust, a sandstorm) gets its per-world colour and per-grain variation.
            f.Flakes = precip is "snow" or "hail" or "ash" or "spores";
            f.Grit = WindGrit.IsGrit(precip);
            ApplyRendererStyle(precip, f.S, f.Flakes, f.Grit);
            f.Size = WindGrit.Shape(f.S.Scale.x, f.S.Scale.y, f.S.Scale.z, f.Flakes).Size;

            // #1758: WATER precipitation takes the world's water colour (rain, drizzle, sleet); snow, hail, ash, sand,
            // acid, meteors and spores keep their own look. A rainbow world's rain cycles through the colours.
            f.Styled = precip is "rain" or "drizzle" or "sleet" ? WaterColours.Blend(f.S.Color, env, 0.75f, Time.time) : f.S.Color;
            f.Drop = ShaderColor.Srgb(f.Styled); // particle vertex colours go to the shader as-is → hand over linear
            f.Drop.a = (precip is "snow" or "ash" or "spores" ? 0.85f : 0.9f) * _envelope;
            if (f.Grit)
            {
                UpdateGritColours(env);
            }

            // Intensity comes from the SMOOTHED client value (#900), so an episode's swell and fade shows as a ramp in the
            // drop count rather than a 5 s staircase. #3203: the quality preset and Reduced effects thin the pool too
            // (FxKit.Density, floored so a rain never vanishes on Potato).
            float strength = Game != null ? Game.WeatherIntensity : env.Intensity;
            float density = Mathf.Max(0.35f, FxKit.Density) * (Particles ? 1f : 0.5f);
            f.Count = Mathf.Min(Pool, Mathf.RoundToInt(Pool * Mathf.Clamp01(0.4f + strength * 0.6f) * f.S.Density * density));
            f.CamPos = Cam.transform.position;

            // #3203: everything that falls or blows rides the world's wind direction — the same direction the grass sways and
            // the cloud shadows drift. The grit is driven by the wind applied once; falling weather keeps its historical
            // strength (slant + 12 × wind²), only its direction changed (it used to slant toward world +X).
            float windDir = Game != null ? Game.WindDirection : 0f;
            float windSpeed = Game != null ? Game.WindSpeed : 0f;
            var (dx, dz) = f.Grit ? WindGrit.Drive(windDir, windSpeed, f.S.Slant) : WindGrit.FallDrive(windDir, windSpeed, f.S.Slant);
            f.Drive = new Vector3(dx, 0f, dz);
            f.Across = new Vector3(-Mathf.Sin(windDir), 0f, Mathf.Cos(windDir));

            int live = 0;
            for (int i = 0; i < Pool; i++)
            {
                StepDrop(i, in f, ref live);
            }

            _ps.SetParticles(_particles, live);
            LogDrops(live, f.Count, strength, f.CamPos);
        }

        /// <summary>Advances drop <paramref name="i"/> by one frame and writes it into the particle buffer (or retires it).</summary>
        private void StepDrop(int i, in DropFrame f, ref int live)
        {
            if (i >= f.Count)
            {
                _alive[i] = false;
                return;
            }

            if (!_alive[i])
            {
                if (!Respawn(i, in f))
                {
                    return; // no open-sky column found this frame (e.g. deep underground)
                }

                _alive[i] = true;
            }

            float wobble = Mathf.Sin(f.T * 2.2f + i) * f.S.Drift; // flakes/embers/sand swirl across the wind
            float vertical = _speed[i] * f.S.Fall * (f.S.Rises ? 1f : -1f);
            Vector3 step = (f.Drive + f.Across * wobble + new Vector3(0f, vertical, 0f)) * f.Dt;
            Vector3 p = _pos[i] + step;
            // Rising motes leave upward; falling ones die below the camera or inside a block.
            bool gone = f.S.Rises ? p.y > f.CamPos.y + SpawnUp + 8f : p.y < f.CamPos.y - 7f;
            bool hit = !gone && InsideBlock(p);
            if (hit && f.Grit && !InsideBlock(_pos[i] + new Vector3(step.x, 0f, step.z)))
            {
                // #3203: grit that meets the ground skims along it (sand hops in the wind) instead of popping out of view.
                step.y = 0f;
                p = _pos[i] + step;
                hit = false;
            }

            if (hit && !f.S.Rises && Particles && !f.Flakes)
            {
                Splash(p, step, f.CamPos, f.Styled, f.Precip);
            }

            if (gone || hit || (p - f.CamPos).sqrMagnitude > KillRadius * KillRadius)
            {
                if (!Respawn(i, in f))
                {
                    _alive[i] = false;
                    return;
                }

                p = _pos[i];
            }
            else
            {
                _pos[i] = p;
                _age[i] += f.Dt;
            }

            WriteParticle(i, p, step, f, ref live);
        }

        /// <summary>Writes drop <paramref name="i"/> into the particle buffer: its velocity sets the streak's direction and
        /// stretch; a grit grain gets its own size and colour.</summary>
        private void WriteParticle(int i, Vector3 p, Vector3 step, in DropFrame f, ref int live)
        {
            ref var part = ref _particles[live++];
            part.position = p;
            part.velocity = f.Dt > 1e-5f ? step / f.Dt : Vector3.down; // stretch direction + length (streaks)
            part.startSize = f.Grit ? f.Size * WindGrit.GrainSize(i) : f.Size;
            part.startColor = f.Grit ? GritColour(i, p, f.CamPos) : f.Drop;
            part.startLifetime = 1000f;
            part.remainingLifetime = 1000f;
            part.rotation = 0f;
        }

        /// <summary>Per precipitation kind (the first rain of a session included — this used to key on the flake/streak flip
        /// alone, so the first rain kept the stretch length of 1 and fell as dots): render mode, material and the streak
        /// length, which the style's own proportions set (#3203: from its thin and its long axis, <see cref="WindGrit.Shape"/>).</summary>
        private void ApplyRendererStyle(string precip, Style s, bool flakes, bool grit)
        {
            if (ReferenceEquals(precip, _styledPrecip) || precip == _styledPrecip)
            {
                return;
            }

            _styledPrecip = precip;
            _psr.renderMode = flakes ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            _psr.sharedMaterial = flakes ? FxKit.SoftAlphaMaterial() : FxKit.StreakMaterial();
            // Grit gets a touch of motion stretch on top of its shape, so a fast gust smears a little longer.
            _psr.velocityScale = grit ? 0.006f : 0f;
            _psr.lengthScale = WindGrit.Shape(s.Scale.x, s.Scale.y, s.Scale.z, flakes).LengthScale;
        }

        /// <summary>Playtest 2026-09-05 ("no rain in a thunderstorm"): one line per precipitation change so the Player.log
        /// says what the client was told and how many drops it tried to show.</summary>
        private void LogPrecipitation(BlocksBeyondTheStars.Networking.Messages.WorldEnvironment env, string precip)
        {
            if (!ReferenceEquals(precip, _loggedPrecip) && precip != _loggedPrecip)
            {
                _loggedPrecip = precip;
                Debug.Log($"[Weather3D] precipitation '{precip}' (weather {env.Weather}, intensity {env.Intensity:0.00}, exposed {Game?.ExposedToSky})");
            }
        }

        /// <summary>Capture diagnostics (#2405): while a capture pins the environment, say what the drops are doing.</summary>
        private void LogDrops(int live, int count, float strength, Vector3 camPos)
        {
            if (Game == null || !Game.CaptureEnvActive || Time.unscaledTime < _nextDropLog)
            {
                return;
            }

            _nextDropLog = Time.unscaledTime + 2f;
            var b = _psr.bounds;
            Debug.Log($"[Weather3D] drops live={live} count={count} strength={strength:F2} envelope={_envelope:F2} psCount={_ps.particleCount} playing={_ps.isPlaying} "
                      + $"visible={_psr.isVisible} enabled={_psr.enabled} active={_ps.gameObject.activeInHierarchy} layer={_ps.gameObject.layer} "
                      + $"mat={(_psr.sharedMaterial != null ? _psr.sharedMaterial.shader.name : "null")} bounds={b.center}/{b.size} cam={camPos} first={(live > 0 ? _particles[0].position : Vector3.zero)}");
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

        /// <summary>Moves the drop to a fresh spawn above the camera (shifted by <paramref name="lead"/>), but only into a
        /// column with open sky overhead — drops must never appear under a cave ceiling or roof — and only inside the sphere
        /// the drops live in. False = no such spot found this frame; the caller keeps the drop hidden and retries next frame.</summary>
        private bool TryRespawn(int i, Vector3 camPos, Vector3 lead, bool rises = false)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                // Falling weather appears above the camera; rising motes (spores) come up from below it.
                float y = rises
                    ? -4f - (float)_rng.NextDouble() * 4f
                    : SpawnUp + (float)_rng.NextDouble() * 6f;
                var p = camPos + lead + new Vector3(
                    (float)(_rng.NextDouble() * 2 - 1) * SpawnRadius,
                    y,
                    (float)(_rng.NextDouble() * 2 - 1) * SpawnRadius);
                if (InSpawnSphere(p, camPos) && ColumnOpen(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z)))
                {
                    _pos[i] = p;
                    _age[i] = 0f;
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

        /// <summary>True when a spawn point lies well inside the sphere the drops live in (<see cref="KillRadius"/> less a
        /// margin) — a corner of the spawn box outside it would be recycled on its very next frame, a one-frame flicker.</summary>
        private static bool InSpawnSphere(Vector3 p, Vector3 camPos)
        {
            const float Reach = KillRadius - WindGrit.EdgeBand;
            return (p - camPos).sqrMagnitude <= Reach * Reach;
        }

        /// <summary>Open sky above this spawn point? Same scan as <c>GameBootstrap.ComputeExposedToSky</c>,
        /// but per spawn cell instead of per player, cached per cell for a second.</summary>
        private bool ColumnOpen(int x, int yFrom, int z)
        {
            var w = Game?.World;
            if (w == null)
            {
                return true;
            }

            var key = (x, yFrom, z);
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
    }
}
