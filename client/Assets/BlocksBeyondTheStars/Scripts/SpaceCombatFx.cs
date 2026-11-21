// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The looks of the newer ship weapons and of what they hit (#3756, #3758), all world space and built on
    /// <see cref="FxKit"/> like <see cref="SpaceFx"/>:
    /// <list type="bullet">
    /// <item><b>missile</b> — it leaves the hull sideways, arcs and follows its target on a bright exhaust with a smoke
    /// trail, and goes up in a fireball with a shock ring;</item>
    /// <item><b>scatter</b> — short fat bolts, one per target, thrown out of a wide muzzle flash;</item>
    /// <item><b>ion_pulse</b> — a pale ring that runs to the target and bursts into crackling arcs;</item>
    /// <item>a hostile's <b>heavy shot</b> — a glow that gathers while it charges (the warning), then a thick beam that
    /// hits the shield or the hull, or passes the rolling ship;</item>
    /// <item>a shot that ends on a <b>shield</b> — the hex bubble ripples where it lands and the shot scatters off it.</item>
    /// </list>
    /// Cosmetic only: the server resolves every one of these.
    /// </summary>
    public static class SpaceCombatFx
    {
        private static int _side;

        /// <summary>A missile from <paramref name="nose"/> to wherever <paramref name="target"/> is by then.
        /// <paramref name="arrived"/> gets the point it went off at.</summary>
        public static void Missile(FxLook look, Vector3 nose, Vector3 forward, Vector3 up, Func<Vector3> target, Action<Vector3> arrived)
        {
            Vector3 end = target();
            float speed = look.Speed > 0f ? look.Speed : 45f;
            float life = Mathf.Clamp(Vector3.Distance(nose, end) / speed, 0.25f, 4f);
            float side = (_side++ & 1) == 0 ? -1f : 1f;
            Vector3 right = Vector3.Cross(up, forward).normalized;
            Vector3 swing = nose + (forward * 9f) + (right * (5f * side)) + (up * 2.5f); // out of the rack, then round onto the target
            Vector3 head = nose;
            int light = FxLights.Track(() => head, look.Color, 2.2f, 9f, life + 0.05f);
            float smoke = 0f;
            FxKit.Flash(nose, look.Color2, 1.6f * FxKit.FlashScale, 0.12f);
            FxKit.Burst(FxKit.Kind.Smoke, nose, 4, -forward, 40f, 1f, 3f, 0.5f, 0.9f, 0.5f, 0.9f, new Color(0.55f, 0.55f, 0.58f));
            FxKit.Animate(life, t =>
            {
                end = target();
                var prev = head;
                float u = 1f - t;
                head = (u * u * nose) + (2f * u * t * swing) + (t * t * end); // a curve whose end follows the target
                float flicker = 1f + (0.25f * Mathf.Sin(Time.time * 47f));
                FxKit.Emit(FxKit.Kind.Glow, head, Vector3.zero, 0.9f * look.Size * flicker, 0.06f, look.Color2);
                FxKit.Emit(FxKit.Kind.Glow, head, Vector3.zero, 1.9f * look.Size * flicker, 0.05f, look.Color);
                smoke += Vector3.Distance(prev, head) * FxKit.Density;
                while (smoke > 0.7f)
                {
                    smoke -= 0.7f;
                    var p = Vector3.Lerp(prev, head, UnityEngine.Random.value);
                    FxKit.Emit(FxKit.Kind.Motes, p, UnityEngine.Random.insideUnitSphere * 0.5f, 0.22f, UnityEngine.Random.Range(0.25f, 0.5f), look.Color);
                    FxKit.Emit(FxKit.Kind.Smoke, p, UnityEngine.Random.insideUnitSphere * 0.3f, UnityEngine.Random.Range(0.5f, 0.9f),
                        UnityEngine.Random.Range(0.7f, 1.2f), new Color(0.5f, 0.5f, 0.54f));
                }
            }, () =>
            {
                FxLights.Remove(light);
                arrived?.Invoke(end);
            });
        }

        /// <summary>A missile going off: a fireball, a shock ring and sparks — bigger than any beam's impact.</summary>
        public static void MissileBurst(FxLook look, Vector3 at)
        {
            FxKit.Flash(at, Color.white, 3.2f * FxKit.FlashScale, 0.1f);
            FxKit.Flash(at, look.Color, 5.5f * FxKit.FlashScale, 0.35f);
            for (int i = 0; i < FxKit.Scaled(6); i++)
            {
                FxKit.Emit(FxKit.Kind.Glow, at + (UnityEngine.Random.insideUnitSphere * 0.8f), UnityEngine.Random.insideUnitSphere * 1.5f,
                    UnityEngine.Random.Range(1.6f, 2.8f), UnityEngine.Random.Range(0.3f, 0.6f), Color.Lerp(look.Color, look.Color2, UnityEngine.Random.value));
            }

            FxKit.Ring(at, Vector3.zero, look.Color2, 0.4f, 7f, 0.45f, thickness: 0.1f, fill: 0.03f, intensity: 2.2f);
            FxKit.Burst(FxKit.Kind.Motes, at, 26, Vector3.zero, 0f, 6f, 18f, 0.1f, 0.18f, 0.35f, 0.8f, look.Color, Color.white);
            FxKit.Burst(FxKit.Kind.Smoke, at, 6, Vector3.zero, 0f, 0.4f, 1.6f, 1.2f, 2.2f, 1f, 1.7f, new Color(0.36f, 0.34f, 0.36f));
            FxLights.Flash(at, look.Color, 3.5f, 20f, 0.45f);
            var cam = Camera.main;
            float d = cam != null ? Vector3.Distance(cam.transform.position, at) : 0f;
            FxCamera.AddTrauma(Mathf.Clamp01(0.3f - (d / 200f)));
            ClientAudio.Instance?.At(ClientAudio.Instance.Has("ship_missile_hit") ? "ship_missile_hit" : "asteroid_break", at,
                UnityEngine.Random.Range(0.95f, 1.1f), Mathf.Clamp01(1.2f - (d / 160f)));
        }

        /// <summary>One bolt of a scatter shot. The first one of a shot (<paramref name="muzzle"/>) throws the wide flash.</summary>
        public static void ScatterBolt(FxLook look, Vector3 nose, Vector3 forward, Vector3 target, bool muzzle, Action arrived)
        {
            if (muzzle)
            {
                FxKit.Flash(nose, look.Color2, 1.5f * FxKit.FlashScale, 0.09f);
                FxKit.Flash(nose + (forward * 0.4f), look.Color, 2.6f * FxKit.FlashScale, 0.12f);
                FxKit.Burst(FxKit.Kind.Motes, nose, 14, forward, 32f, 14f, 30f, 0.07f, 0.12f, 0.12f, 0.25f, look.Color, look.Color2);
                FxLights.Flash(nose, look.Color, 2f, 9f, 0.14f);
                FxCamera.Kick(0.9f);
            }

            float speed = look.Speed > 0f ? look.Speed : 120f;
            FxShots.Projectile(nose, target, speed, 0.75f * look.Size, look.Color, look.Color2, trail: 0.7f, light: 0f, arrived);
        }

        /// <summary>The ion pulse: a ring that runs along the shot and, at the target, a burst of crackling arcs.</summary>
        public static void IonPulse(FxLook look, Vector3 nose, Vector3 target, Action arrived)
        {
            Vector3 dir = (target - nose).sqrMagnitude > 1e-4f ? (target - nose).normalized : Vector3.forward;
            FxKit.Ring(nose, dir, look.Color2, 0.2f, 2.4f, 0.3f, thickness: 0.16f, intensity: 2.6f);
            FxKit.Flash(nose, look.Color, 1.4f * FxKit.FlashScale, 0.12f);
            FxLights.Flash(nose, look.Color, 1.8f, 8f, 0.2f);
            float life = Mathf.Clamp(Vector3.Distance(nose, target) / 160f, 0.05f, 0.6f);
            float ringAcc = 0f;
            FxKit.Animate(life, t =>
            {
                var at = Vector3.Lerp(nose, target, t);
                FxKit.Emit(FxKit.Kind.Glow, at, Vector3.zero, 1.3f * look.Size, 0.07f, look.Color);
                ringAcc += Time.deltaTime;
                if (ringAcc > 0.045f)
                {
                    ringAcc = 0f;
                    FxKit.Ring(at, dir, look.Color, 0.25f, 1.5f, 0.22f, thickness: 0.12f, intensity: 1.8f);
                }
            }, () =>
            {
                IonBurst(look.Color, look.Color2, target, 1f);
                arrived?.Invoke();
            });
        }

        /// <summary>Arcs jumping off a target the ion pulse reached; also the sparking of a silenced hostile
        /// (<paramref name="strength"/> well below 1).</summary>
        public static void IonBurst(Color color, Color core, Vector3 at, float strength)
        {
            if (strength >= 0.9f)
            {
                FxKit.Flash(at, core, 2.4f * FxKit.FlashScale, 0.12f);
                FxKit.Ring(at, Vector3.zero, color, 0.3f, 5f, 0.4f, thickness: 0.08f, intensity: 2f);
                FxLights.Flash(at, color, 2.4f, 12f, 0.3f);
            }

            int arcs = Mathf.Max(1, FxKit.Scaled(Mathf.RoundToInt(5 * strength)));
            var mat = FxKit.BeamMaterial("ion_arc", core, coreWidth: 0.35f, noiseScale: 3.5f, noiseSpeed: 40f, intensity: 2.6f);
            for (int i = 0; i < arcs; i++)
            {
                var a = at + (UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(0.6f, 1.4f));
                var b = at + (UnityEngine.Random.onUnitSphere * UnityEngine.Random.Range(1.6f, 3.2f));
                FxKit.Beam(a, b, color, 0.12f, UnityEngine.Random.Range(0.08f, 0.16f), mat);
            }

            FxKit.Burst(FxKit.Kind.Motes, at, Mathf.RoundToInt(8 * strength) + 1, Vector3.zero, 0f, 1f, 5f, 0.06f, 0.12f, 0.2f, 0.5f, color, core);
        }

        /// <summary>A hostile's heavy shot gathering: light streams into a growing core at <paramref name="at"/> for
        /// <paramref name="seconds"/> — the pilot's warning. <paramref name="at"/> answers null once the hostile is gone
        /// (destroyed while it charged): the glow and its light end there and then, instead of gathering somewhere else.</summary>
        public static void HeavyCharge(Func<Vector3?> at, float seconds, Color color)
        {
            Vector3? start = at();
            if (start == null)
            {
                return;
            }

            var core = Color.Lerp(color, Color.white, 0.7f);
            float acc = 0f;
            Vector3 last = start.Value;
            bool gone = false;
            int light = FxLights.Track(() => last, color, 2.4f, 12f, seconds + 0.05f);
            FxKit.Animate(seconds, t =>
            {
                if (gone)
                {
                    return;
                }

                Vector3? now = at();
                if (now == null)
                {
                    gone = true;
                    FxLights.Remove(light);
                    return;
                }

                last = now.Value;
                FxKit.Emit(FxKit.Kind.Glow, last, Vector3.zero, Mathf.Lerp(0.8f, 3.6f, t), 0.07f, color);
                FxKit.Emit(FxKit.Kind.Glow, last, Vector3.zero, Mathf.Lerp(0.3f, 1.6f, t), 0.06f, core);
                acc += Time.deltaTime * FxKit.Density;
                while (acc > 0.03f)
                {
                    acc -= 0.03f;
                    var from = last + (UnityEngine.Random.onUnitSphere * Mathf.Lerp(5f, 2.5f, t));
                    FxKit.Emit(FxKit.Kind.Motes, from, (last - from) / 0.25f, 0.12f, 0.25f, core);
                }
            }, () => FxLights.Remove(light));
            FxKit.Ring(last, Vector3.zero, color, 6f, 0.6f, seconds, thickness: 0.05f, intensity: 1.6f);
        }

        /// <summary>The heavy shot fires: a thick beam from the hostile to <paramref name="to"/> with a muzzle ring.</summary>
        public static void HeavyBeam(Vector3 from, Vector3 to, Color color)
        {
            var core = Color.Lerp(color, Color.white, 0.75f);
            var mat = FxKit.BeamMaterial("hostile_heavy", core, coreWidth: 0.45f, noiseScale: 0.4f, noiseSpeed: 20f, intensity: 3.6f);
            FxKit.Beam(from, to, color, 1.1f, 0.45f, mat);
            FxKit.Ring(from, Vector3.zero, color, 0.5f, 5f, 0.4f, thickness: 0.16f, intensity: 2.4f);
            FxKit.Flash(from, core, 3f * FxKit.FlashScale, 0.15f);
            FxLights.Flash(from, color, 3f, 18f, 0.35f);
        }

        /// <summary>The escort cruiser's mega laser gathering (#3900): for <paramref name="seconds"/> light streams into a
        /// growing core at the barrel's <paramref name="muzzle"/>, and a thin pulsing aiming line runs from there to
        /// <paramref name="aim"/> (the targeted ship) — both followed every frame, the line pulsing faster toward the shot
        /// and strongest for the pilot it points at (<paramref name="atMe"/>, <see cref="MegaLaserAim"/>). Either point
        /// answering null ends the picture there and then (the cruiser is gone, or the shot fired early).</summary>
        public static void MegaLaserCharge(Func<Vector3?> muzzle, Func<Vector3?> aim, float seconds, Color color, bool atMe)
        {
            Vector3? start = muzzle();
            if (start == null)
            {
                return;
            }

            var core = Color.Lerp(color, Color.white, 0.7f);
            var lin = FxKit.Lin(color);
            var kit = FxKit.Ensure();
            LineRenderer line = RentAimLine(kit, core, start.Value);
            float acc = 0f;
            Vector3 last = start.Value;
            bool gone = false;
            bool still = UiKit.ReducedMotion;
            int light = FxLights.Track(() => last, color, 3f, 22f, seconds + 0.05f);
            FxKit.Animate(seconds, t =>
            {
                if (gone)
                {
                    return;
                }

                Vector3? now = muzzle();
                if (now == null)
                {
                    gone = true;
                    FxLights.Remove(light);
                    DrawAimLine(line, last, null, lin, 0f);
                    return;
                }

                last = now.Value;
                acc = MegaLaserGlow(last, t, color, core, acc);
                DrawAimLine(line, last, aim(), lin, MegaLaserAim.Alpha(t, seconds, atMe, still), MegaLaserAim.Width(t, atMe));
            }, () =>
            {
                FxLights.Remove(light);
                if (line != null)
                {
                    kit.ReleaseBeam(line);
                }
            });
        }

        /// <summary>The pooled line the mega laser's aim is drawn with — invisible until its first frame; null without the
        /// beam shader.</summary>
        private static LineRenderer RentAimLine(FxKit kit, Color core, Vector3 at)
        {
            var mat = FxKit.BeamMaterial("mega_aim", core, coreWidth: 0.25f, noiseScale: 0.2f, noiseSpeed: 14f, intensity: 2.4f);
            if (mat == null)
            {
                return null;
            }

            var line = kit.RentBeam(mat);
            line.widthMultiplier = 0f;
            line.SetPosition(0, at);
            line.SetPosition(1, at);
            return line;
        }

        /// <summary>One frame of the glow gathering at the muzzle (the heavy shot's, about twice as big): the growing core
        /// and the motes streaming in. Returns the mote clock <paramref name="acc"/> for the next frame.</summary>
        private static float MegaLaserGlow(Vector3 at, float t, Color color, Color core, float acc)
        {
            FxKit.Emit(FxKit.Kind.Glow, at, Vector3.zero, Mathf.Lerp(1.6f, 7f, t), 0.07f, color);
            FxKit.Emit(FxKit.Kind.Glow, at, Vector3.zero, Mathf.Lerp(0.6f, 3.2f, t), 0.06f, core);
            acc += Time.deltaTime * FxKit.Density;
            while (acc > 0.03f)
            {
                acc -= 0.03f;
                var from = at + (UnityEngine.Random.onUnitSphere * Mathf.Lerp(9f, 4f, t));
                FxKit.Emit(FxKit.Kind.Motes, from, (at - from) / 0.25f, 0.2f, 0.25f, core);
            }

            return acc;
        }

        /// <summary>One frame of the mega laser's aiming line; without a <paramref name="to"/> it is not drawn.</summary>
        private static void DrawAimLine(LineRenderer line, Vector3 from, Vector3? to, Color lin, float alpha, float width = 0f)
        {
            if (line == null)
            {
                return;
            }

            if (to == null)
            {
                line.widthMultiplier = 0f;
                return;
            }

            line.SetPosition(0, from);
            line.SetPosition(1, to.Value);
            line.widthMultiplier = width;
            var c = new Color(lin.r, lin.g, lin.b, alpha);
            line.startColor = c;
            line.endColor = c;
        }

        /// <summary>The mega laser fires (#3900): the heavy shot's beam made thicker and longer-lived, with a white-hot
        /// core, from the barrel's muzzle to <paramref name="to"/>. With <paramref name="impact"/> the beam ends on
        /// something solid there — the rock a pilot hid behind — and bursts on it.</summary>
        public static void MegaLaserBeam(Vector3 from, Vector3 to, Color color, bool impact)
        {
            var core = Color.Lerp(color, Color.white, 0.8f);
            var mat = FxKit.BeamMaterial("hostile_mega", core, coreWidth: 0.5f, noiseScale: 0.3f, noiseSpeed: 26f, intensity: 4.2f);
            FxKit.Beam(from, to, color, 2.8f, 0.9f, mat);
            FxKit.Beam(from, to, core, 1f, 0.6f, mat);
            FxKit.Ring(from, Vector3.zero, color, 1f, 11f, 0.6f, thickness: 0.16f, intensity: 2.6f);
            FxKit.Flash(from, core, 6f * FxKit.FlashScale, 0.2f);
            FxLights.Flash(from, color, 4f, 30f, 0.5f);
            if (!impact)
            {
                return;
            }

            FxKit.Flash(to, core, 4f * FxKit.FlashScale, 0.18f);
            FxKit.Ring(to, Vector3.zero, color, 0.5f, 6f, 0.5f, thickness: 0.12f, intensity: 2.2f);
            FxKit.Burst(FxKit.Kind.Motes, to, 22, from - to, 70f, 5f, 16f, 0.08f, 0.16f, 0.3f, 0.8f, color, Color.white);
            FxLights.Flash(to, color, 2.5f, 14f, 0.35f);
        }

        /// <summary>A shot scattering off a shield at <paramref name="at"/> (the shield's own ripple is
        /// <see cref="FxShield.Hit"/>): a flash, a flat ring on the bubble and sparks thrown back.</summary>
        public static void ShieldDeflect(Vector3 at, Vector3 outward, Color color)
        {
            var c = Color.Lerp(color, new Color(0.45f, 0.85f, 1f), 0.6f);
            FxKit.Flash(at, Color.white, 1.2f * FxKit.FlashScale, 0.08f);
            FxKit.Flash(at, c, 2.4f * FxKit.FlashScale, 0.16f);
            FxKit.Ring(at, outward, c, 0.3f, 3.2f, 0.35f, thickness: 0.09f, intensity: 2f);
            FxKit.Burst(FxKit.Kind.Motes, at, 12, outward, 60f, 4f, 11f, 0.07f, 0.13f, 0.25f, 0.55f, c, Color.white);
            FxLights.Flash(at, c, 1.8f, 10f, 0.2f);
        }
    }
}
