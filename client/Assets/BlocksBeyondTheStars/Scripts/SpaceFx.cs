// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Flight-view effects of the VFX overhaul (#2156/#2157). Everything is world space, built on <see cref="FxKit"/>:
    /// <list type="bullet">
    /// <item><b>Ship weapons</b> by the module's data-driven look — <c>twin_pulse</c> (alternating wing pulses),
    /// <c>plasma_bolt</c> (glowing bolts with a flight time), <c>heavy_beam</c> (a charge glow, then a thick wobbling beam
    /// with a muzzle ring), <c>drill_beam</c> (a pulsing beam with rings running into the rock, rock chips). All four
    /// ship weapons used to draw the same 0.16-unit cube.</item>
    /// <item><b>Hostile bolts</b> (cosmetic — the server's damage is an aura) whose arrival drives the shield / hull look.</item>
    /// <item><b>Explosions</b> for destroyed hostiles (flash, fireball glows, a shockwave ring, the model's own cube parts
    /// tumbling away, sparks, an FX light) and the <b>asteroid break-up</b> (a crack flash, tumbling rock chunks, dust
    /// and ore glints) — destroyed drones and asteroids used to just vanish.</item>
    /// <item><b>Tractor cone</b>, <b>trader warp zips</b>, <b>scan pulses</b> and the <b>planet-scanner sweep</b>
    /// (a band crossing the planet sphere, then the found resources glowing as points on the globe).</item>
    /// </list>
    /// Space has no gravity: the effects use the weightless kinds (motes, glows, floating debris).
    /// </summary>
    public static class SpaceFx
    {
        // ------------------------------------------------------------------ ship weapons

        private static int _alternate;

        /// <summary>Fires a ship weapon's look from the ship at <paramref name="target"/> (world positions). Mining shots
        /// (asteroids, wrecks) read amber whatever the weapon.</summary>
        public static void Fire(FxLook look, Transform ship, Bounds shipLocal, Vector3 target, bool mining)
        {
            var color = mining ? new Color(1f, 0.68f, 0.25f) : look.Color;
            var core = mining ? new Color(1f, 0.95f, 0.8f) : look.Color2;
            var fwd = ship.forward;
            var nose = ship.TransformPoint(new Vector3(shipLocal.center.x, shipLocal.center.y, shipLocal.max.z + 0.3f));
            switch (look.Style)
            {
                case "plasma_bolt":
                {
                    FxKit.Flash(nose, core, 1.2f, 0.12f);
                    float speed = look.Speed > 0f ? look.Speed : 140f;
                    FxShots.Projectile(nose, target, speed, 0.9f * look.Size, color, core, trail: 0.5f, light: 1.6f,
                        () => Impact(target, color, core, 1.2f, mining));
                    FxCamera.Kick(0.6f);
                    break;
                }

                case "heavy_beam":
                {
                    float charge = Mathf.Clamp(look.Charge, 0.05f, 0.5f);
                    FxKit.Emit(FxKit.Kind.Glow, nose, Vector3.zero, 1.4f, charge * 1.3f, color);
                    int n = FxKit.Scaled(14);
                    for (int i = 0; i < n; i++)
                    {
                        var from = nose + Random.onUnitSphere * 2.2f;
                        FxKit.Emit(FxKit.Kind.Motes, from, (nose - from) / charge, 0.12f, charge, core);
                    }

                    ClientAudio.Instance?.Cue("weapon_charge", 0.5f);
                    FxKit.Delay(charge, () =>
                    {
                        if (ship == null)
                        {
                            return;
                        }

                        var n2 = ship.TransformPoint(new Vector3(shipLocal.center.x, shipLocal.center.y, shipLocal.max.z + 0.3f));
                        var mat = FxKit.BeamMaterial("heavy_beam", core, coreWidth: 0.42f, noiseScale: 0.35f, noiseSpeed: 18f, intensity: 3.4f);
                        FxKit.Beam(n2, target, color, 0.75f * look.Size, 0.38f, mat);
                        FxKit.Ring(n2, Vector3.zero, color, 0.3f, 3f, 0.35f, thickness: 0.18f, intensity: 2.4f);
                        FxLights.Flash(n2, color, 2.5f, 14f, 0.3f);
                        Impact(target, color, core, 2f, mining);
                        FxCamera.Kick(1.4f);
                        FxCamera.AddTrauma(0.12f);
                    });
                    break;
                }

                case "drill_beam":
                {
                    var mat = FxKit.BeamMaterial("drill_beam", core, coreWidth: 0.5f, noiseScale: 0.5f, noiseSpeed: 16f, pulse: 2.5f, intensity: 3f);
                    FxKit.Beam(nose, target, color, 0.55f * look.Size, 0.3f, mat);
                    FxKit.Flash(nose, core, 1f, 0.12f);
                    Impact(target, color, core, 1.4f, true);
                    break;
                }

                default: // twin_pulse — short glowing pulses, alternating between the wings
                {
                    float side = (_alternate++ & 1) == 0 ? -1f : 1f;
                    var wing = ship.TransformPoint(new Vector3(
                        shipLocal.center.x + side * shipLocal.extents.x * 0.7f, shipLocal.center.y, shipLocal.max.z - shipLocal.extents.z * 0.3f));
                    FxKit.Flash(wing, core, 0.9f, 0.08f);
                    float speed = look.Speed > 0f ? look.Speed : 190f;
                    FxShots.Projectile(wing, target, speed, 0.5f * look.Size, color, core, trail: 0.25f, light: 0f,
                        () => Impact(target, color, core, 0.9f, mining));
                    break;
                }
            }
        }

        /// <summary>An impact in space: a flash, weightless sparks, an FX light, and rock chips when mining.</summary>
        public static void Impact(Vector3 at, Color color, Color core, float strength, bool mining)
        {
            FxKit.Flash(at, core, 1.4f * strength, 0.1f);
            FxKit.Flash(at, color, 2.6f * strength, 0.18f);
            FxKit.Burst(FxKit.Kind.Motes, at, Mathf.RoundToInt(10 * strength), Vector3.zero, 0f, 4f, 12f, 0.08f, 0.16f, 0.3f, 0.6f, color, core);
            FxLights.Flash(at, color, 1.8f * strength, 10f, 0.2f);
            if (mining)
            {
                FxKit.Burst(FxKit.Kind.DebrisFloat, at, Mathf.RoundToInt(4 * strength), Vector3.zero, 0f, 2f, 6f, 0.15f, 0.35f, 0.8f, 1.4f,
                    new Color(0.45f, 0.4f, 0.36f), new Color(0.62f, 0.56f, 0.48f));
            }
        }

        /// <summary>A hostile's cosmetic bolt flying at the ship; <paramref name="arrived"/> fires on impact (the shield
        /// or hull look plays there).</summary>
        public static void HostileBolt(Vector3 from, Vector3 to, Color color, System.Action arrived)
        {
            FxKit.Flash(from, color, 1f, 0.1f);
            FxShots.Projectile(from, to, 110f, 0.6f, color, Color.Lerp(color, Color.white, 0.7f), trail: 0.3f, light: 0f, arrived);
        }

        // ------------------------------------------------------------------ destruction

        /// <summary>A destroyed hostile ship: flash, fireball glows, a shockwave ring, the model's cube parts tumbling
        /// away, sparks and an FX light; the camera rattles with distance. <paramref name="model"/> (optional) is the
        /// entity's model — its parts are detached here, the caller destroys the root.</summary>
        public static void Explosion(Vector3 at, float scale, GameObject model, Color tint)
        {
            float flash = FxKit.FlashScale;
            FxKit.Flash(at, Color.white, 4f * scale * flash, 0.12f);
            FxKit.Flash(at, new Color(1f, 0.72f, 0.35f), 7f * scale, 0.45f);
            for (int i = 0; i < FxKit.Scaled(8); i++)
            {
                FxKit.Emit(FxKit.Kind.Glow, at + Random.insideUnitSphere * scale, Random.insideUnitSphere * 2f * scale,
                    Random.Range(2f, 3.6f) * scale, Random.Range(0.4f, 0.8f), Color.Lerp(new Color(1f, 0.55f, 0.2f), new Color(1f, 0.85f, 0.5f), Random.value));
            }

            FxKit.Ring(at, Vector3.zero, new Color(1f, 0.8f, 0.5f), 0.5f * scale, 9f * scale, 0.55f, thickness: 0.1f, fill: 0.04f, intensity: 2.4f);
            FxKit.Burst(FxKit.Kind.Motes, at, 30, Vector3.zero, 0f, 8f * scale, 22f * scale, 0.1f, 0.2f, 0.4f, 0.9f, new Color(1f, 0.8f, 0.4f), Color.white);
            FxKit.Burst(FxKit.Kind.Smoke, at, 8, Vector3.zero, 0f, 0.5f, 2f, 1.5f * scale, 2.6f * scale, 1.2f, 2f, new Color(0.35f, 0.33f, 0.36f));
            FxKit.Burst(FxKit.Kind.DebrisFloat, at, 10, Vector3.zero, 0f, 3f, 9f, 0.2f * scale, 0.45f * scale, 1.2f, 2f, new Color(0.3f, 0.32f, 0.36f), tint);
            FxLights.Flash(at, new Color(1f, 0.7f, 0.35f), 4f, 26f * Mathf.Max(1f, scale), 0.6f);
            if (model != null)
            {
                FlingParts(model, at, 6f * scale);
            }

            var cam = Camera.main;
            if (cam != null)
            {
                float d = Vector3.Distance(cam.transform.position, at);
                FxCamera.AddTrauma(Mathf.Clamp01(0.55f - d / 120f));
                ClientAudio.Instance?.At("ship_destroyed", at, Random.Range(1.05f, 1.25f), Mathf.Clamp01(1.1f - d / 120f));
            }
        }

        /// <summary>An asteroid breaking up: a hot crack flash, tumbling rock chunks in its colours, a dust cloud and a
        /// few ore glints (the loot reads before it drifts free).</summary>
        public static void AsteroidBreak(Vector3 at, float size, Color rock)
        {
            float s = Mathf.Clamp(size, 1f, 12f);
            FxKit.Flash(at, new Color(1f, 0.75f, 0.4f), 1.6f * s, 0.18f);
            FxKit.Burst(FxKit.Kind.DebrisFloat, at, Mathf.RoundToInt(10 + s * 2f), Vector3.zero, 0f, 1.5f, 6f, 0.2f * s * 0.35f, 0.5f * s * 0.35f, 1.4f, 2.4f,
                rock, Color.Lerp(rock, Color.white, 0.25f));
            FxKit.Burst(FxKit.Kind.Smoke, at, 6, Vector3.zero, 0f, 0.4f, 1.6f, 0.6f * s, 1.2f * s, 1.4f, 2.4f, Color.Lerp(rock, new Color(0.8f, 0.78f, 0.74f), 0.4f));
            FxKit.Burst(FxKit.Kind.Motes, at, 10, Vector3.zero, 0f, 2f, 6f, 0.1f, 0.2f, 0.6f, 1.2f, new Color(1f, 0.85f, 0.35f), new Color(0.5f, 1f, 1f));
            FxKit.Ring(at, Vector3.zero, new Color(1f, 0.8f, 0.55f), 0.3f * s, 2.5f * s, 0.45f, thickness: 0.08f, intensity: 1.4f);
            FxLights.Flash(at, new Color(1f, 0.7f, 0.4f), 2f, 10f * s, 0.35f);
            var cam = Camera.main;
            float d = cam != null ? Vector3.Distance(cam.transform.position, at) : 0f;
            ClientAudio.Instance?.At("asteroid_break", at, Random.Range(0.9f, 1.1f), Mathf.Clamp01(1.2f - d / 140f));
        }

        /// <summary>Detaches a model's cube parts and sends them tumbling away weightlessly, shrinking into glints.</summary>
        private static void FlingParts(GameObject model, Vector3 center, float speed)
        {
            var host = FxKit.Ensure().transform;
            var parts = new List<Transform>();
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(false))
            {
                parts.Add(r.transform);
                if (parts.Count >= 24)
                {
                    break;
                }
            }

            foreach (var tr in parts)
            {
                tr.SetParent(host, true);
            }

            foreach (var tr in parts)
            {
                foreach (var c in tr.GetComponents<Collider>())
                {
                    Object.Destroy(c);
                }

                var away = (tr.position - center).sqrMagnitude > 1e-4f ? (tr.position - center).normalized : Random.onUnitSphere;
                var vel = away * Random.Range(0.4f, 1f) * speed;
                var spin = Random.insideUnitSphere * 360f;
                var scale0 = tr.localScale;
                float life = Random.Range(1.1f, 1.8f);
                FxKit.Animate(life, t =>
                {
                    float dt = Time.deltaTime;
                    tr.position += vel * dt;
                    vel *= 1f - dt * 0.4f;
                    tr.Rotate(spin * dt, Space.World);
                    float k = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
                    tr.localScale = scale0 * Mathf.Max(0f, k);
                }, () =>
                {
                    if (tr == null)
                    {
                        return;
                    }

                    FxKit.Flash(tr.position, new Color(1f, 0.75f, 0.4f), 0.8f, 0.15f);
                    Object.Destroy(tr.gameObject);
                }, tr);
            }
        }

        // ------------------------------------------------------------------ engines

        /// <summary>A small always-on engine plume (other players' ships, hostile cruisers and bandit ships): a local-space
        /// cone of additive glow particles streaming astern (−Z) from <paramref name="localPos"/> under
        /// <paramref name="parent"/>. Returns null when the particle shader is missing.</summary>
        public static ParticleSystem EnginePlume(Transform parent, Vector3 localPos, float size, Color color)
        {
            var mat = FxKit.SparkMaterial();
            if (mat == null || parent == null)
            {
                return null;
            }

            var go = new GameObject("EnginePlume");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // the cone emits along +Z → the hull's −Z
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 0.3f;
            main.startSpeed = 6f * size;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f * size, 0.36f * size);
            main.startColor = FxKit.Lin(color);
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var em = ps.emission;
            em.rateOverTime = 45f * Mathf.Max(0.4f, FxKit.Density);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = 0.12f * size;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.4f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------ tractor, warp, scans

        /// <summary>The tractor beam: a translucent cone of rings running in toward the ship and spiralling motes.</summary>
        public static void Tractor(Vector3 ship, Vector3 target, Color color)
        {
            var mat = FxKit.BeamMaterial("tractor", Color.Lerp(color, Color.white, 0.5f), coreWidth: 0.15f, noiseScale: 0.3f, noiseSpeed: 6f, pulse: 3f, intensity: 1.6f);
            // Drawn from the target TO the ship, so the beam's pulse rings run inward (they travel along +uv.x).
            FxKit.Beam(target, ship, color, 1.3f, 0.45f, mat);
            var dir = ship - target;
            float dist = dir.magnitude;
            if (dist < 0.1f)
            {
                return;
            }

            int n = FxKit.Scaled(14);
            var axis = dir / dist;
            var perp = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                var offset = Quaternion.AngleAxis(a * Mathf.Rad2Deg, axis) * perp * 0.6f;
                var from = target + offset;
                float life = Random.Range(0.4f, 0.7f);
                var swirl = Vector3.Cross(axis, offset).normalized * 1.5f;
                FxKit.Emit(FxKit.Kind.Motes, from, (ship - from) / life + swirl, 0.12f, life, color);
            }
        }

        /// <summary>A trader warping in/out: a stretched light streak from far away, a flash and a ring.</summary>
        public static void WarpZip(Vector3 at, Vector3 dir, bool arriving)
        {
            var color = new Color(0.6f, 0.8f, 1f);
            var far = at - dir.normalized * 90f;
            var mat = FxKit.BeamMaterial("warpzip", Color.white, coreWidth: 0.6f, noiseScale: 0.1f, noiseSpeed: 40f, intensity: 3f);
            if (arriving)
            {
                FxKit.Beam(far, at, color, 0.9f, 0.3f, mat);
            }
            else
            {
                FxKit.Beam(at, at + dir.normalized * 90f, color, 0.9f, 0.3f, mat);
            }

            FxKit.Flash(at, Color.white, 3f * FxKit.FlashScale, 0.15f);
            FxKit.Flash(at, color, 5f, 0.35f);
            FxKit.Ring(at, Vector3.zero, color, 0.5f, 7f, 0.5f, thickness: 0.12f, intensity: 2.2f);
            FxLights.Flash(at, color, 2.5f, 18f, 0.4f);
        }

        /// <summary>A scan pulse from the ship: an expanding fresnel shell and a camera-facing ring.</summary>
        public static void ScanPulse(Vector3 ship, Color color, float radius)
        {
            FxKit.Shell(ship, color, 1f, radius, 0.9f, rimPower: 3f, fill: 0f, intensity: 1.2f);
            FxKit.Ring(ship, Vector3.zero, color, 1f, radius * 0.8f, 0.7f, thickness: 0.06f, lines: 3f, intensity: 1.6f);
        }

        private static readonly int BodyScanId = Shader.PropertyToID("_Sc_BodyScan");
        private static readonly int BodyScanPId = Shader.PropertyToID("_Sc_BodyScanP");
        private static readonly int BodyScanColId = Shader.PropertyToID("_Sc_BodyScanCol");
        private static readonly int BodyScanOreId = Shader.PropertyToID("_Sc_BodyScanOre");
        private static int _bodyScanRun;

        /// <summary>The planet scanner at work on <paramref name="body"/> (its sphere renderer): a pulse from the ship, a
        /// scan band sweeping the globe pole to pole (the <c>_Sc_BodyScan*</c> globals read by <c>SkyBodyPhase</c>), then
        /// the found resources glowing as points on the globe for a while. <paramref name="ores"/> are up to four ore
        /// colours (sRGB); the dot pattern is seeded by <paramref name="seed"/> so a body always shows the same points.</summary>
        public static void PlanetScan(Transform ship, Transform body, float radius, Color color, IReadOnlyList<Color> ores, int seed)
        {
            if (body == null)
            {
                return;
            }

            if (ship != null)
            {
                ScanPulse(ship.position, color, 18f);
                var mat = FxKit.BeamMaterial("planetscan", Color.white, coreWidth: 0.4f, noiseScale: 0.05f, noiseSpeed: 20f, pulse: 1.5f, intensity: 1.4f);
                FxKit.Beam(ship.position, body.position, color, 1.2f, 0.9f, mat);
            }

            var oreVec = new Vector4[4];
            for (int i = 0; i < 4; i++)
            {
                if (ores != null && ores.Count > 0)
                {
                    var c = FxKit.Lin(ores[i % ores.Count]);
                    oreVec[i] = new Vector4(c.r, c.g, c.b, 1f);
                }
            }

            Shader.SetGlobalVectorArray(BodyScanOreId, oreVec);
            var lin = FxKit.Lin(color);
            Shader.SetGlobalVector(BodyScanColId, new Vector4(lin.r, lin.g, lin.b, 1f));
            int run = ++_bodyScanRun;
            const float sweep = 2.4f;
            const float hold = 7f;
            FxKit.Animate(sweep + hold, t =>
            {
                if (body == null || run != _bodyScanRun)
                {
                    return;
                }

                float age = t * (sweep + hold);
                float band = age < sweep ? Mathf.Lerp(1.1f, -1.1f, age / sweep) : -1.2f;
                float sweepStrength = age < sweep ? 1f : Mathf.Clamp01(1f - (age - sweep) / 1.2f);
                float dots = age < sweep ? 0.6f : Mathf.Clamp01(1f - (age - sweep - hold + 1.5f) / 1.5f);
                Shader.SetGlobalVector(BodyScanId, new Vector4(body.position.x, body.position.y, body.position.z, radius));
                Shader.SetGlobalVector(BodyScanPId, new Vector4(band, sweepStrength, dots, seed % 97));
            }, () =>
            {
                if (run == _bodyScanRun)
                {
                    Shader.SetGlobalVector(BodyScanId, Vector4.zero);
                }
            });
        }
    }
}
