// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The blown grit of a gale ("dust") or a sandstorm (#3203). It used to draw as one-metre brown squares of one colour
    /// on every world that glowed at night; now each grain is a thin streak whose colours come from the world's ground
    /// (<see cref="WindGrit.LookFor"/>: the surface block's average atlas colour — the same source the sandworm's dust
    /// uses — or white spindrift on frozen worlds), varied per grain, faded in and out, and dimmed by the world light
    /// (the unlit particle shader would otherwise keep its full colour in the dark). It spawns through the air around the
    /// camera, upwind, so it blows past instead of raining down.
    /// </summary>
    public sealed partial class WeatherFx3D
    {
        private static readonly int LightId = Shader.PropertyToID("_Sc_Light");

        /// <summary>The grit's screen-wash colour (sRGB, already dimmed by the world light) — <see cref="WeatherFx"/> washes
        /// the screen with it during a gale or a sandstorm. Plain tan until the first grit frame.</summary>
        public static Color GritWash { get; private set; } = new Color(0.74f, 0.62f, 0.40f);

        private GritLook _gritLook = WindGrit.Plain;
        private string _gritBiome;   // the planet type the look was made for (null = none yet)
        private bool _gritFrozen;    // ... and whether the air was freezing then
        private float _gritRetryAt;  // no ground colour yet (atlas not built) → try again at this time
        private Color _gritA, _gritB; // this frame's two grain colours, linear and lit

        /// <summary>A fresh spawn for drop <paramref name="i"/>: grit blows in through the air, everything else falls in from
        /// above (<see cref="TryRespawn"/>), a little upwind so it reaches eye level around the camera.</summary>
        private bool Respawn(int i, in DropFrame f)
            => f.Grit ? TryRespawnGrit(i, f.CamPos, f.Drive) : TryRespawn(i, f.CamPos, FallLead(f), f.S.Rises);

        /// <summary>How far upwind falling weather spawns: the wind's drive times the time a drop takes to fall from the spawn
        /// height, capped at 4 m — a slanting snowfall still reaches eye level around the camera, and the shifted spawn box
        /// stays inside the sphere the drops live in (<see cref="InSpawnSphere"/> rejects the rest).</summary>
        private static Vector3 FallLead(in DropFrame f)
        {
            if (f.S.Rises || f.Drive.sqrMagnitude < 1e-4f)
            {
                return Vector3.zero;
            }

            float fallTime = (SpawnUp + 3f) / Mathf.Max(1f, 34f * f.S.Fall);
            return -Vector3.ClampMagnitude(f.Drive * fallTime, 4f);
        }

        /// <summary>Grit spawns anywhere from just above the ground to a few metres overhead, shifted upwind so the grains
        /// cross the camera's surroundings on their way downwind — never under a roof or a parked ship (the same open-sky rule
        /// as rain, checked per spawn cell: a grain may start in the open air beside a house, not in its rooms).</summary>
        private bool TryRespawnGrit(int i, Vector3 camPos, Vector3 drive)
        {
            Vector3 upwind = drive.sqrMagnitude > 1e-4f ? -drive.normalized * (SpawnRadius * 0.35f) : Vector3.zero;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var p = camPos + upwind + new Vector3(
                    (float)(_rng.NextDouble() * 2 - 1) * SpawnRadius,
                    -1.2f + (float)_rng.NextDouble() * 9f,
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

        /// <summary>This frame's grit colours: the world's look (re-made on another planet or when the air freezes), lit by
        /// the world light; also refreshes <see cref="GritWash"/>.</summary>
        private void UpdateGritColours(WorldEnvironment env)
        {
            string biome = env.Biome ?? string.Empty;
            bool frozen = env.Temperature <= WindGrit.FreezingAir;
            bool stale = _gritBiome == null || _gritFrozen != frozen || !string.Equals(_gritBiome, biome, System.StringComparison.Ordinal);
            if (stale || (_gritRetryAt > 0f && Time.time >= _gritRetryAt))
            {
                _gritBiome = biome;
                _gritFrozen = frozen;
                var planet = Game != null && Game.Content != null ? Game.Content.GetPlanet(biome) : null;
                bool hasGround = TryGroundColour(planet, out Color ground);
                _gritRetryAt = hasGround ? 0f : Time.time + 1f;
                _gritLook = WindGrit.LookFor(biome, planet?.FloraTheme, env.Temperature, hasGround, ground.r, ground.g, ground.b);
            }

            Color light = WorldLight();
            _gritA = Lit(new Color(_gritLook.R, _gritLook.G, _gritLook.B), light);
            _gritB = Lit(new Color(_gritLook.R2, _gritLook.G2, _gritLook.B2), light);
            Color lightSrgb = light.gamma; // the IMGUI wash is composed in sRGB
            GritWash = new Color(_gritLook.R * lightSrgb.r, _gritLook.G * lightSrgb.g, _gritLook.B * lightSrgb.b);
        }

        /// <summary>The average atlas colour (sRGB) of the planet's surface block — what its ground looks like from a step
        /// back. False before the atlas exists or for an unknown block.</summary>
        private bool TryGroundColour(PlanetType planet, out Color ground)
        {
            ground = default;
            var def = planet != null && Game != null && Game.Content != null ? Game.Content.GetBlock(planet.SurfaceBlock) : null;
            if (def == null || Game.Atlas == null)
            {
                return false;
            }

            ground = Game.Atlas.AverageColor(def.NumericId.Value);
            return true;
        }

        /// <summary>The world's block light right now (linear: the star's colour × day brightness × weather dim, set by the
        /// Sky), floored so a grain never goes fully black; white before the Sky has lit the world.</summary>
        private static Color WorldLight()
        {
            Color l = Shader.GetGlobalColor(LightId);
            if (l.a < 0.5f)
            {
                return Color.white;
            }

            return new Color(Mathf.Clamp(l.r, 0.04f, 1f), Mathf.Clamp(l.g, 0.04f, 1f), Mathf.Clamp(l.b, 0.04f, 1f), 1f);
        }

        /// <summary>An sRGB-authored colour converted for the linear particle stream and multiplied by the world light.</summary>
        private static Color Lit(Color srgb, Color light)
        {
            Color c = FxKit.Lin(srgb);
            return new Color(c.r * light.r, c.g * light.g, c.b * light.b, 1f);
        }

        /// <summary>Grain <paramref name="i"/>'s colour this frame: its mix of the two grit colours and its own opacity,
        /// faded in after it spawned, at the edge of the pool and near the camera, and by the whole effect's envelope.</summary>
        private Color GritColour(int i, Vector3 p, Vector3 camPos)
        {
            Color c = Color.Lerp(_gritA, _gritB, WindGrit.GrainMix(i));
            c.a = WindGrit.GrainAlpha(i) * WindGrit.GrainFade(_age[i], Vector3.Distance(p, camPos), KillRadius) * _envelope;
            return c;
        }
    }
}
