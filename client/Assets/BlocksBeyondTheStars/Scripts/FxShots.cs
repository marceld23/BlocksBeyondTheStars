// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// On-foot weapon personalities (#2154). Every hand weapon has its own look, picked by the data-driven
    /// <see cref="FxLook.Style"/> of the held item:
    /// <list type="bullet">
    /// <item><c>slug</c> (scrap pistol) — a brass muzzle star, a chunky glowing slug with a short trail, "clonk" sparks.</item>
    /// <item><c>rail</c> (gauss) — the coils charge, then an instant rail line leaves expanding rings along its path.</item>
    /// <item><c>laser</c> — a layered red beam (white core, red glow, flowing noise), a glowing hot spot where it lands.</item>
    /// <item><c>plasma</c> — a violet wobbling plasma ball with a spark trail that lights the walls as it flies, a splash ring.</item>
    /// <item><c>slash</c> / <c>vibro</c> / <c>plasma_blade</c> / <c>fist</c> — curved slash ribbons (steel, jittering
    /// blue with electric arcs, glowing pink with an afterimage, a faint whoosh).</item>
    /// </list>
    /// Purely cosmetic (the server resolves every hit); the same code draws remote players' shots (#2158).
    /// </summary>
    public static class FxShots
    {
        /// <summary>Fires the look of a ranged weapon from <paramref name="muzzle"/> toward <paramref name="target"/>.
        /// <paramref name="impact"/> = the shot ends on something (entity or terrain) rather than fizzling at max range;
        /// <paramref name="normal"/> is the surface normal there (zero = unknown). <paramref name="local"/> = it is the
        /// player's own shot (camera kick and the charge-up run only then).</summary>
        public static void Fire(FxLook look, Vector3 muzzle, Vector3 target, bool impact, Vector3 normal, bool local)
        {
            var dir = target - muzzle;
            if (dir.sqrMagnitude < 1e-4f)
            {
                dir = Vector3.forward;
            }

            dir.Normalize();
            if (normal.sqrMagnitude < 1e-4f)
            {
                normal = -dir;
            }

            switch (look.Style)
            {
                case "rail":
                    Rail(look, muzzle, target, dir, impact, normal, local);
                    break;
                case "slug":
                    Slug(look, muzzle, target, dir, impact, normal, local);
                    break;
                case "plasma":
                    Plasma(look, muzzle, target, dir, impact, normal, local);
                    break;
                default:
                    Laser(look, muzzle, target, dir, impact, normal, local);
                    break;
            }
        }

        // ------------------------------------------------------------------ ranged

        private static void Laser(FxLook look, Vector3 muzzle, Vector3 target, Vector3 dir, bool impact, Vector3 normal, bool local)
        {
            Muzzle(look, muzzle, dir, 0.32f);
            var mat = FxKit.BeamMaterial("laser", look.Color2, coreWidth: 0.34f, noiseScale: 1.4f, noiseSpeed: 16f, intensity: 3f);
            FxKit.Beam(muzzle, target, look.Color, 0.075f * look.Size, 0.17f, mat);
            if (local)
            {
                FxCamera.Kick(1.1f);
            }

            if (impact)
            {
                Impact(look, target, normal, dir, 1f);
                // A hot spot that cools: a lingering ember glow where the beam landed.
                FxKit.Emit(FxKit.Kind.Glow, target + normal * 0.04f, Vector3.zero, 0.32f, 0.9f, Color.Lerp(look.Color, new Color(1f, 0.55f, 0.2f), 0.5f));
                if (FxKit.Rich)
                {
                    FxKit.Burst(FxKit.Kind.Smoke, target + normal * 0.1f, 2, normal, 25f, 0.2f, 0.5f, 0.18f, 0.3f, 0.8f, 1.3f, new Color(0.35f, 0.33f, 0.32f));
                }
            }
            else
            {
                FxKit.Flash(target, look.Color, 0.18f, 0.1f);
            }
        }

        private static void Rail(FxLook look, Vector3 muzzle, Vector3 target, Vector3 dir, bool impact, Vector3 normal, bool local)
        {
            float charge = local ? Mathf.Clamp(look.Charge, 0f, 0.4f) : 0f;
            if (charge > 0f)
            {
                // The coils charge: motes converge on the muzzle while a glow grows there.
                FxKit.Emit(FxKit.Kind.Glow, muzzle, Vector3.zero, 0.22f, charge * 1.3f, look.Color);
                int n = FxKit.Scaled(8);
                for (int i = 0; i < n; i++)
                {
                    var from = muzzle + Random.onUnitSphere * 0.32f;
                    FxKit.Emit(FxKit.Kind.Motes, from, (muzzle - from) / charge, 0.035f, charge, look.Color2);
                }
            }

            FxKit.Delay(charge, () =>
            {
                Muzzle(look, muzzle, dir, 0.36f);
                var mat = FxKit.BeamMaterial("rail", look.Color2, coreWidth: 0.55f, noiseScale: 0.6f, noiseSpeed: 30f, intensity: 3.4f);
                FxKit.Beam(muzzle, target, look.Color, 0.05f * look.Size, 0.32f, mat);
                // Rings left hanging along the rail, each blooming outward a beat after the one before.
                float len = Vector3.Distance(muzzle, target);
                int rings = Mathf.Min(FxKit.Scaled(14), Mathf.FloorToInt(len / 1.4f));
                for (int i = 1; i <= rings; i++)
                {
                    var at = muzzle + dir * (i * len / (rings + 1));
                    float delay = i * 0.012f;
                    FxKit.Delay(delay, () => FxKit.Ring(at, dir, look.Color, 0.05f, 0.42f, 0.45f, thickness: 0.35f, fill: 0f, intensity: 2.4f));
                }

                if (local)
                {
                    FxCamera.Kick(2.2f);
                    FxCamera.FovPunch(1.2f);
                }

                if (impact)
                {
                    Impact(look, target, normal, dir, 1.2f);
                }
            });
        }

        private static void Slug(FxLook look, Vector3 muzzle, Vector3 target, Vector3 dir, bool impact, Vector3 normal, bool local)
        {
            // A brass muzzle star: a flash plus a few fast streaks fanning forward.
            Muzzle(look, muzzle, dir, 0.3f);
            FxKit.Burst(FxKit.Kind.Sparks, muzzle, 5, dir, 22f, 9f, 16f, 0.035f, 0.05f, 0.05f, 0.09f, look.Color2);
            if (local)
            {
                FxCamera.Kick(1.8f);
            }

            float speed = look.Speed > 0f ? look.Speed : 45f;
            Projectile(muzzle, target, speed, 0.16f * look.Size, look.Color, look.Color2, trail: 0.6f, light: 0f, () =>
            {
                if (impact)
                {
                    Impact(look, target, normal, dir, 0.9f);
                    FxKit.Burst(FxKit.Kind.Dust, target + normal * 0.08f, 4, normal, 50f, 0.4f, 1.2f, 0.12f, 0.22f, 0.4f, 0.8f, new Color(0.62f, 0.57f, 0.5f));
                }
            });
        }

        private static void Plasma(FxLook look, Vector3 muzzle, Vector3 target, Vector3 dir, bool impact, Vector3 normal, bool local)
        {
            Muzzle(look, muzzle, dir, 0.42f);
            if (local)
            {
                FxCamera.Kick(1.5f);
            }

            float speed = look.Speed > 0f ? look.Speed : 32f;
            Projectile(muzzle, target, speed, 0.34f * look.Size, look.Color, look.Color2, trail: 1.4f, light: 1.4f, () =>
            {
                if (!impact)
                {
                    FxKit.Flash(target, look.Color, 0.5f, 0.2f);
                    return;
                }

                Impact(look, target, normal, dir, 1.4f);
                FxKit.Ring(target + normal * 0.05f, normal, look.Color, 0.15f, 1.6f, 0.45f, thickness: 0.22f, fill: 0.12f, intensity: 2.4f);
                FxKit.Burst(FxKit.Kind.Motes, target, 10, normal, 70f, 0.6f, 2.2f, 0.04f, 0.08f, 0.5f, 0.9f, look.Color, look.Color2);
                FxLights.Flash(target + normal * 0.4f, look.Color, 2.2f, 8f, 0.3f);
            });
        }

        /// <summary>A travelling glowing projectile: the head is a stream of glow cards re-emitted every frame (so it
        /// needs no renderer of its own), the trail motes are left behind, and an optional FX light rides along.</summary>
        public static void Projectile(Vector3 from, Vector3 to, float speed, float size, Color color, Color core, float trail, float light, System.Action arrived)
        {
            float dist = Vector3.Distance(from, to);
            float life = Mathf.Clamp(dist / Mathf.Max(1f, speed), 0.02f, 2f);
            Vector3 head = from;
            int lightId = light > 0f ? FxLights.Track(() => head, color, light, 6f, life + 0.05f) : 0;
            float trailAcc = 0f;
            FxKit.Animate(life, t =>
            {
                var prev = head;
                head = Vector3.Lerp(from, to, t);
                float wobble = 1f + 0.18f * Mathf.Sin(Time.time * 37f);
                FxKit.Emit(FxKit.Kind.Glow, head, Vector3.zero, size * wobble, 0.06f, core);
                FxKit.Emit(FxKit.Kind.Glow, head, Vector3.zero, size * 1.9f * wobble, 0.05f, color);
                trailAcc += Vector3.Distance(prev, head) * trail * FxKit.Density;
                while (trailAcc > 0.25f)
                {
                    trailAcc -= 0.25f;
                    var p = Vector3.Lerp(prev, head, Random.value);
                    FxKit.Emit(FxKit.Kind.Motes, p, Random.insideUnitSphere * 0.4f, size * 0.35f, Random.Range(0.15f, 0.35f), color);
                }
            }, () =>
            {
                if (lightId != 0)
                {
                    FxLights.Remove(lightId);
                }

                arrived?.Invoke();
            });
        }

        /// <summary>A muzzle flash: a glow card, a short burst of forward sparks and a quick FX light.</summary>
        public static void Muzzle(FxLook look, Vector3 at, Vector3 dir, float size)
        {
            FxKit.Flash(at, look.Color2, size * look.Size, 0.07f);
            FxKit.Flash(at + dir * 0.05f, look.Color, size * 1.7f * look.Size, 0.09f);
            FxKit.Burst(FxKit.Kind.Sparks, at, 3, dir, 30f, 3f, 7f, 0.025f, 0.04f, 0.06f, 0.12f, look.Color);
            FxLights.Flash(at + dir * 0.3f, look.Color, 1.4f, 5f, 0.09f);
        }

        /// <summary>An impact: a flash, stretched sparks thrown back off the surface, a short FX light.</summary>
        public static void Impact(FxLook look, Vector3 at, Vector3 normal, Vector3 dir, float strength)
        {
            FxKit.Flash(at + normal * 0.05f, look.Color2, 0.35f * strength, 0.08f);
            FxKit.Flash(at + normal * 0.05f, look.Color, 0.7f * strength, 0.14f);
            var bounce = Vector3.Reflect(dir, normal) * 0.6f + normal * 0.4f;
            FxKit.Burst(FxKit.Kind.Sparks, at + normal * 0.05f, Mathf.RoundToInt(10 * strength), bounce, 55f, 3f, 8.5f, 0.04f, 0.07f, 0.25f, 0.5f, look.Color, look.Color2);
            FxLights.Flash(at + normal * 0.5f, look.Color, 1.6f * strength, 5.5f, 0.16f);
        }

        // ------------------------------------------------------------------ melee

        private static Mesh _arc;

        /// <summary>Swings a melee weapon's slash ribbon in front of <paramref name="center"/> (along <paramref name="forward"/>);
        /// a whiff still sweeps. <paramref name="hit"/> adds the impact at <paramref name="hitPoint"/>.</summary>
        public static void Swing(FxLook look, Vector3 center, Vector3 forward, Vector3 up, bool hit, Vector3 hitPoint, bool local)
        {
            float radius;
            float intensity;
            float roll;
            switch (look.Style)
            {
                case "fist":
                    radius = 0.55f;
                    intensity = 0.9f;
                    roll = -10f;
                    break;
                case "plasma_blade":
                    radius = 1.15f;
                    intensity = 3.2f;
                    roll = -30f;
                    break;
                case "vibro":
                    radius = 0.85f;
                    intensity = 2.4f;
                    roll = 20f;
                    break;
                default:
                    radius = 1f;
                    intensity = 2f;
                    roll = -28f;
                    break;
            }

            SlashRibbon(look, center, forward, up, radius, intensity, roll, 0f, look.Is("vibro"));
            if (look.Is("plasma_blade") && FxKit.Rich)
            {
                SlashRibbon(look, center, forward, up, radius * 0.92f, intensity * 0.45f, roll, 0.05f, false); // the afterimage
            }

            if (look.Is("plasma_blade"))
            {
                FxLights.Flash(center + forward * 0.8f, look.Color, 1.6f, 5f, 0.25f);
            }

            if (local)
            {
                FxCamera.Kick(look.Is("fist") ? 0.4f : 0.8f);
            }

            if (!hit)
            {
                return;
            }

            var n = -forward;
            Impact(look, hitPoint, n, forward, look.Is("fist") ? 0.5f : 0.9f);
            if (look.Is("vibro"))
            {
                ElectricArcs(hitPoint, look.Color, look.Color2, 3, 0.28f);
            }
            else if (look.Is("slash"))
            {
                FxKit.Emit(FxKit.Kind.Glow, hitPoint, Vector3.zero, 0.5f, 0.12f, Color.white); // the blade glint
            }
        }

        private static void SlashRibbon(FxLook look, Vector3 center, Vector3 forward, Vector3 up, float radius, float intensity, float roll, float delay, bool jitter)
        {
            var mat = FxKit.Cached("BlocksBeyondTheStars/FxBeam", "slash/" + look.Style + "/" + ColorKey(look.Color), m =>
            {
                m.SetColor("_Color2", FxKit.Lin(look.Color2));
                m.SetColor("_Tint", FxKit.Lin(look.Color));
                m.SetFloat("_CoreWidth", 0.22f);
                m.SetFloat("_NoiseScale", 0.8f);
                m.SetFloat("_NoiseSpeed", 7f);
                m.SetFloat("_Pulse", 0f);
                m.SetFloat("_Intensity", 2f);
            });
            if (mat == null)
            {
                return;
            }

            FxKit.Delay(delay, () =>
            {
                var kit = FxKit.Ensure();
                var mr = kit.RentMesh(ArcMesh, mat);
                var tr = mr.transform;
                var baseRot = Quaternion.LookRotation(forward, up) * Quaternion.Euler(0f, 0f, roll);
                tr.position = center;
                tr.localScale = Vector3.one * radius;
                FxKit.Animate(0.17f, t =>
                {
                    float sweep = Mathf.Lerp(-42f, 42f, 1f - (1f - t) * (1f - t));
                    tr.rotation = baseRot * Quaternion.Euler(0f, sweep, 0f);
                    if (jitter)
                    {
                        tr.position = center + Random.insideUnitSphere * 0.035f;
                    }

                    var b = FxKit.Block;
                    b.Clear();
                    b.SetFloat("_Intensity", intensity * (1f - t) * (1f - t));
                    mr.SetPropertyBlock(b);
                }, () => kit.ReleaseMesh(mr), mr);
            });
        }

        /// <summary>A 110° slash ribbon in the XZ plane around the origin (inner radius 0.45, outer 1.35 at scale 1);
        /// vertex alpha rises toward the leading edge so the ribbon reads as a streak, not a fan.</summary>
        private static Mesh ArcMesh
        {
            get
            {
                if (_arc != null)
                {
                    return _arc;
                }

                const int seg = 20;
                const float span = 110f;
                const float rIn = 0.45f;
                const float rOut = 1.35f;
                var verts = new Vector3[(seg + 1) * 2];
                var uvs = new Vector2[verts.Length];
                var cols = new Color[verts.Length];
                var tris = new int[seg * 6];
                float arcLen = span * Mathf.Deg2Rad * (rIn + rOut) * 0.5f;
                for (int i = 0; i <= seg; i++)
                {
                    float f = i / (float)seg;
                    float a = Mathf.Lerp(-span * 0.5f, span * 0.5f, f) * Mathf.Deg2Rad;
                    var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    verts[i * 2] = d * rIn;
                    verts[i * 2 + 1] = d * rOut;
                    uvs[i * 2] = new Vector2(f * arcLen, 0f);
                    uvs[i * 2 + 1] = new Vector2(f * arcLen, 1f);
                    float alpha = Mathf.Pow(f, 1.6f);
                    cols[i * 2] = new Color(1f, 1f, 1f, alpha);
                    cols[i * 2 + 1] = new Color(1f, 1f, 1f, alpha);
                }

                for (int i = 0; i < seg; i++)
                {
                    int v = i * 2;
                    int t = i * 6;
                    tris[t] = v;
                    tris[t + 1] = v + 1;
                    tris[t + 2] = v + 2;
                    tris[t + 3] = v + 1;
                    tris[t + 4] = v + 3;
                    tris[t + 5] = v + 2;
                }

                _arc = new Mesh { name = "FxSlashArc", vertices = verts, uv = uvs, colors = cols, triangles = tris };
                _arc.RecalculateBounds();
                return _arc;
            }
        }

        /// <summary>A few jagged electric arcs crackling around a point for <paramref name="life"/> seconds (vibro knife,
        /// stun hits, shield sparks).</summary>
        public static void ElectricArcs(Vector3 at, Color color, Color core, int count, float life)
        {
            var mat = FxKit.BeamMaterial("arc", core, coreWidth: 0.6f, noiseScale: 3f, noiseSpeed: 30f, intensity: 3f);
            if (mat == null)
            {
                return;
            }

            var kit = FxKit.Ensure();
            int n = FxKit.Scaled(count);
            for (int k = 0; k < n; k++)
            {
                var lr = kit.RentBeam(mat);
                lr.positionCount = 7;
                lr.widthMultiplier = 0.025f;
                var a = at + Random.insideUnitSphere * 0.15f;
                var b = at + Random.onUnitSphere * Random.Range(0.35f, 0.7f);
                var lin = FxKit.Lin(color);
                float next = 0f;
                FxKit.Animate(life, t =>
                {
                    if (Time.time >= next)
                    {
                        next = Time.time + 0.045f; // re-jag ~20 times a second
                        for (int i = 0; i < 7; i++)
                        {
                            float f = i / 6f;
                            var p = Vector3.Lerp(a, b, f);
                            if (i > 0 && i < 6)
                            {
                                p += Random.insideUnitSphere * 0.08f;
                            }

                            lr.SetPosition(i, p);
                        }
                    }

                    var c = new Color(lin.r, lin.g, lin.b, 1f - t);
                    lr.startColor = c;
                    lr.endColor = c;
                }, () =>
                {
                    lr.positionCount = 2;
                    kit.ReleaseBeam(lr);
                }, lr);
            }
        }

        private static string ColorKey(Color c)
        {
            var c32 = (Color32)c;
            return c32.r + "," + c32.g + "," + c32.b;
        }
    }
}
