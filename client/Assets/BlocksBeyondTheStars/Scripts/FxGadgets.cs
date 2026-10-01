// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Scanner and gadget effects (#2153): pulses, waves and holograms instead of one ring of cubes for everything.
    /// <list type="bullet">
    /// <item><b>Hand scan</b> — a fan of light lines to the target, a holographic bracket box with a scan plane sweeping
    /// down through it, data motes streaming back into the device, a short local scan wave.</item>
    /// <item><b>Terrain scan</b> — the big scan wave rolling over the terrain, a ground ring and a faint shell (the ores
    /// themselves light up in <see cref="OreScanView"/> as the front reaches them).</item>
    /// <item><b>Weather scan</b> — a probe beam into the sky and a widening sky ring.</item>
    /// <item><b>Translator</b> — sound-wave rings travelling to the creature, glyph motes floating back.</item>
    /// <item><b>Medkit / stasis / blaster / pump</b> — a healing helix, a freezing shell, a detonation, a vortex.</item>
    /// </list>
    /// Gadget OUTCOMES play on the server's confirmation (<c>ActionFx</c> with the outcome flag, #2151/#2158) for the
    /// user and everyone nearby; the user's own button press only shows the device's short charge glow right away.
    /// </summary>
    public static class FxGadgets
    {
        /// <summary>Instant feedback on the device when the player triggers a gadget (before the server answers).</summary>
        public static void Intent(FxLook look, Vector3 device)
        {
            FxKit.Flash(device, look.Color, 0.25f, 0.18f);
            FxKit.Burst(FxKit.Kind.Motes, device, 5, Vector3.up, 80f, 0.3f, 0.8f, 0.025f, 0.045f, 0.2f, 0.35f, look.Color2);
        }

        /// <summary>The confirmed outcome of a gadget use at <paramref name="user"/> (feet-level position) aimed at
        /// <paramref name="target"/>.</summary>
        public static void Outcome(FxLook look, Vector3 user, Vector3 target, bool byLocalPlayer)
        {
            var chest = user + Vector3.up * 1.0f;
            var audio = ClientAudio.Instance;
            switch (look.Style)
            {
                case "heal_pulse":
                    Heal(look, user);
                    if (byLocalPlayer)
                    {
                        audio?.Cue("medkit_heal");
                    }
                    else
                    {
                        audio?.At("medkit_heal", chest);
                    }

                    break;
                case "stasis":
                    Stasis(look, target);
                    audio?.At("stasis_activate", target);
                    break;
                case "blast":
                    Blast(look, target);
                    audio?.At("terrain_blast", target);
                    break;
                case "terrain_scan":
                    TerrainScan(look, chest);
                    if (byLocalPlayer)
                    {
                        audio?.Cue("terrain_scan");
                    }
                    else
                    {
                        audio?.At("terrain_scan", chest);
                    }

                    break;
                case "weather_scan":
                    WeatherScan(look, user);
                    break;
                case "translate":
                    Translate(look, chest, target);
                    break;
                case "pump":
                    Pump(look, chest, target);
                    break;
                default:
                    FxKit.Ring(user + Vector3.up * 0.05f, Vector3.up, look.Color, 0.2f, 2.2f, 0.5f, thickness: 0.18f);
                    break;
            }
        }

        // ------------------------------------------------------------------ scanners

        /// <summary>The hand scanner at work on a target occupying <paramref name="bounds"/>.</summary>
        public static void HandScan(FxLook look, Vector3 device, Bounds bounds, bool pro)
        {
            var size = bounds.size;
            size = new Vector3(Mathf.Max(0.6f, size.x), Mathf.Max(0.6f, size.y), Mathf.Max(0.6f, size.z)) * 1.1f;
            var center = bounds.center;

            // 1. The light fan: thin lines from the device to the box's corners.
            var fanMat = FxKit.BeamMaterial("scanfan", look.Color2, coreWidth: 0.6f, noiseScale: 2.5f, noiseSpeed: 20f, intensity: 1.6f);
            for (int i = 0; i < 4; i++)
            {
                var corner = center + Vector3.Scale(size * 0.5f, new Vector3(i % 2 == 0 ? -1f : 1f, i < 2 ? 1f : -1f, 0f));
                FxKit.Beam(device, corner, look.Color, 0.012f, 0.45f, fanMat);
            }

            // 2. The holographic bracket box + a scan plane sweeping down through it.
            float hold = pro ? 0.9f : 1.2f;
            HoloBox(look.Color, center, size, hold);
            HoloPlane(look.Color2, center, size, pro ? 0.55f : 0.8f);

            // 3. Data motes stream from the target back into the device.
            int n = FxKit.Scaled(pro ? 26 : 16);
            for (int i = 0; i < n; i++)
            {
                var from = center + Vector3.Scale(Random.insideUnitSphere, size * 0.5f);
                float life = Random.Range(0.45f, 0.8f);
                FxKit.Emit(FxKit.Kind.Motes, from, (device - from) / life, Random.Range(0.025f, 0.05f), life, Color.Lerp(look.Color, look.Color2, Random.value));
            }

            // 4. A short local wave lights the ground and the creature as it passes.
            FxScanWave.Start(device, pro ? 9f : 6f, pro ? 22f : 16f, look.Color, width: 1.6f, strength: 0.7f, lineDensity: 2f);
            if (pro)
            {
                FxKit.Ring(center, Vector3.zero, look.Color2, 0.2f, size.magnitude * 0.7f, 0.5f, thickness: 0.1f, intensity: 2f);
            }
        }

        /// <summary>The terrain scanner's pulse: the big wave, a ground ring and a faint shell.</summary>
        public static void TerrainScan(FxLook look, Vector3 origin)
        {
            const float range = 20f;
            const float speed = 14f;
            FxScanWave.Start(origin, range, speed, look.Color, width: 4.5f, strength: 1f, lineDensity: 1.2f);
            FxKit.Ring(origin + Vector3.down * 0.95f, Vector3.up, look.Color, 0.3f, range, range / speed, thickness: 0.05f, fill: 0.02f, lines: 6f, intensity: 1.8f);
            FxKit.Shell(origin, look.Color, 0.4f, range, range / speed + 0.2f, rimPower: 4f, fill: 0f, intensity: 0.6f);
            FxKit.Flash(origin, look.Color2, 0.6f, 0.25f);
            FxLights.Flash(origin, look.Color, 1.2f, 7f, 0.4f);
        }

        /// <summary>A probe beam into the sky and a widening ring up there.</summary>
        public static void WeatherScan(FxLook look, Vector3 user)
        {
            var from = user + Vector3.up * 1.2f;
            var top = from + Vector3.up * 40f;
            var mat = FxKit.BeamMaterial("weather", look.Color2, coreWidth: 0.35f, noiseScale: 0.4f, noiseSpeed: 12f, pulse: 3f, intensity: 2.4f);
            FxKit.Beam(from, top, look.Color, 0.22f, 1.1f, mat);
            FxKit.Delay(0.35f, () => FxKit.Ring(top, Vector3.down, look.Color, 1f, 16f, 1.2f, thickness: 0.08f, fill: 0.03f, lines: 4f, intensity: 1.6f));
            FxKit.Burst(FxKit.Kind.Motes, from, 14, Vector3.up, 12f, 4f, 9f, 0.04f, 0.07f, 0.8f, 1.4f, look.Color2);
        }

        /// <summary>Sound-wave rings travelling from the translator to the creature, glyph motes floating back.</summary>
        public static void Translate(FxLook look, Vector3 from, Vector3 to)
        {
            var dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.1f)
            {
                return;
            }

            dir /= dist;
            var mat = FxKit.Cached("BlocksBeyondTheStars/FxRing", "ring");
            if (mat == null)
            {
                return;
            }

            var kit = FxKit.Ensure();
            for (int k = 0; k < 3; k++)
            {
                FxKit.Delay(k * 0.14f, () =>
                {
                    var mr = kit.RentMesh(FxKit.QuadMesh, mat);
                    var tr = mr.transform;
                    tr.rotation = Quaternion.LookRotation(-dir);
                    var lin = FxKit.Lin(look.Color);
                    FxKit.Animate(Mathf.Clamp(dist / 9f, 0.3f, 1.2f), t =>
                    {
                        tr.position = Vector3.Lerp(from, to, t);
                        float r = Mathf.Lerp(0.15f, 0.7f, t);
                        tr.localScale = new Vector3(r * 2f, r * 2f, 1f);
                        var b = FxKit.Block;
                        b.Clear();
                        b.SetColor("_Color", new Color(lin.r, lin.g, lin.b, 1f - t * t));
                        b.SetFloat("_Thickness", 0.18f);
                        b.SetFloat("_Fill", 0f);
                        b.SetFloat("_Lines", 0f);
                        b.SetFloat("_Intensity", 2.2f);
                        mr.SetPropertyBlock(b);
                    }, () => kit.ReleaseMesh(mr), mr);
                });
            }

            int n = FxKit.Scaled(10);
            for (int i = 0; i < n; i++)
            {
                var p = to + Random.insideUnitSphere * 0.5f + Vector3.up * 0.4f;
                float life = Random.Range(0.8f, 1.3f);
                FxKit.Emit(FxKit.Kind.Motes, p, (from - p) / life + Vector3.up * 0.4f, 0.06f, life, look.Color2);
            }
        }

        // ------------------------------------------------------------------ gadgets

        /// <summary>The medkit: a green ground ring and a rising helix of sparkles around the user.</summary>
        public static void Heal(FxLook look, Vector3 user)
        {
            FxKit.Ring(user + Vector3.up * 0.05f, Vector3.up, look.Color, 0.3f, 6f, 0.75f, thickness: 0.1f, fill: 0.05f, intensity: 2f);
            int n = FxKit.Scaled(26);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 4f;
                var p = user + new Vector3(Mathf.Cos(a) * 0.7f, 0.1f + i * 0.06f, Mathf.Sin(a) * 0.7f);
                var tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                FxKit.Emit(FxKit.Kind.Motes, p, Vector3.up * 1.4f + tangent * 0.8f, Random.Range(0.05f, 0.09f), Random.Range(0.7f, 1.1f), Color.Lerp(look.Color, look.Color2, Random.value * 0.6f));
            }

            FxLights.Flash(user + Vector3.up, look.Color, 1.2f, 6f, 0.6f);
        }

        /// <summary>The stasis projector: a cyan shockwave and a crystal shell at the aim point, snow motes drifting down.</summary>
        public static void Stasis(FxLook look, Vector3 at)
        {
            FxKit.Ring(at, Vector3.up, look.Color, 0.3f, 7f, 0.6f, thickness: 0.12f, fill: 0.06f, intensity: 2.2f);
            FxKit.Ring(at, Vector3.zero, look.Color2, 0.2f, 3f, 0.4f, thickness: 0.2f, intensity: 2f);
            FxKit.Shell(at, look.Color, 0.3f, 7f, 0.55f, rimPower: 2f, fill: 0.03f, intensity: 1.4f);
            int n = FxKit.Scaled(24);
            for (int i = 0; i < n; i++)
            {
                var p = at + new Vector3(Random.Range(-3.5f, 3.5f), Random.Range(0.5f, 3f), Random.Range(-3.5f, 3.5f));
                FxKit.Emit(FxKit.Kind.Motes, p, new Vector3(Random.Range(-0.2f, 0.2f), -0.5f, Random.Range(-0.2f, 0.2f)), Random.Range(0.04f, 0.08f), Random.Range(1.2f, 2f), look.Color2);
            }

            FxLights.Flash(at + Vector3.up, look.Color, 1.4f, 8f, 0.5f);
        }

        /// <summary>The terrain blaster's detonation: a white flash, a fireball glow, a ground shockwave, a sphere front,
        /// smoke, sparks, an FX light and a camera rattle that falls off with distance.</summary>
        public static void Blast(FxLook look, Vector3 at)
        {
            float flash = FxKit.FlashScale;
            FxKit.Flash(at, look.Color2, 1.6f * flash, 0.12f);
            FxKit.Flash(at, look.Color, 3.6f, 0.35f);
            FxKit.Ring(at + Vector3.down * 0.9f, Vector3.up, look.Color, 0.4f, 5f, 0.45f, thickness: 0.2f, fill: 0.08f, intensity: 2.6f);
            FxKit.Shell(at, look.Color, 0.3f, 3.6f, 0.35f, rimPower: 1.6f, fill: 0.1f, intensity: 1.8f);
            FxKit.Burst(FxKit.Kind.Sparks, at, 26, Vector3.zero, 0f, 5f, 12f, 0.05f, 0.09f, 0.4f, 0.8f, look.Color, look.Color2);
            FxKit.Burst(FxKit.Kind.Smoke, at, 10, Vector3.up, 80f, 0.6f, 1.8f, 0.6f, 1.1f, 1.2f, 2.2f, new Color(0.45f, 0.42f, 0.4f));
            FxLights.Flash(at, look.Color, 3.5f, 11f, 0.45f);
            var cam = Camera.main;
            if (cam != null)
            {
                float d = Vector3.Distance(cam.transform.position, at);
                FxCamera.AddTrauma(Mathf.Clamp01(0.75f - d / 30f));
            }
        }

        /// <summary>The fluid pump: a spiral vortex of the fluid sucked into the nozzle.</summary>
        public static void Pump(FxLook look, Vector3 nozzle, Vector3 at)
        {
            int n = FxKit.Scaled(18);
            for (int i = 0; i < n; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                var p = at + new Vector3(Mathf.Cos(a), Random.Range(-0.3f, 0.3f), Mathf.Sin(a)) * 0.6f;
                float life = Random.Range(0.35f, 0.6f);
                var swirl = Vector3.Cross(Vector3.up, p - at).normalized * 1.5f;
                FxKit.Emit(FxKit.Kind.Motes, p, (nozzle - p) / life + swirl, Random.Range(0.05f, 0.09f), life, look.Color);
            }

            FxKit.Ring(at, Vector3.up, look.Color, 0.9f, 0.1f, 0.4f, thickness: 0.3f, intensity: 1.6f);
        }

        /// <summary>A teleport column (the suit teleporter, beam pads, a bandit beaming away): a vertical light pillar
        /// with rings running up it and motes rising.</summary>
        public static void Teleport(Vector3 basePos, float height, Color color, Color core)
        {
            var mat = FxKit.BeamMaterial("teleport", core, coreWidth: 0.4f, noiseScale: 0.5f, noiseSpeed: 6f, pulse: 2.5f, intensity: 2.2f);
            FxKit.Beam(basePos, basePos + Vector3.up * (height + 0.8f), color, 1.1f, 0.9f, mat);
            for (int k = 0; k < 3; k++)
            {
                float h = k * height / 3f;
                FxKit.Delay(k * 0.12f, () =>
                {
                    var mr = RingAt(basePos + Vector3.up * h, color, 0.7f);
                    if (mr == null)
                    {
                        return;
                    }

                    var tr = mr.transform;
                    var start = tr.position;
                    FxKit.Animate(0.6f, t =>
                    {
                        tr.position = start + Vector3.up * (t * height * 0.6f);
                        var b = FxKit.Block;
                        b.Clear();
                        b.SetColor("_Color", FxKit.Lin(color) * new Color(1f, 1f, 1f, 1f - t));
                        b.SetFloat("_Thickness", 0.25f);
                        b.SetFloat("_Fill", 0.05f);
                        b.SetFloat("_Lines", 0f);
                        b.SetFloat("_Intensity", 2.2f);
                        mr.SetPropertyBlock(b);
                    }, () => FxKit.Ensure().ReleaseMesh(mr), mr);
                });
            }

            FxKit.Burst(FxKit.Kind.Motes, basePos + Vector3.up * 0.3f, 20, Vector3.up, 15f, 2f, 5f, 0.04f, 0.08f, 0.5f, 0.9f, color, core);
            FxLights.Flash(basePos + Vector3.up * height * 0.5f, color, 2f, 7f, 0.7f);
        }

        // ------------------------------------------------------------------ holograms

        private static MeshRenderer RingAt(Vector3 at, Color color, float radius)
        {
            var mat = FxKit.Cached("BlocksBeyondTheStars/FxRing", "ring");
            if (mat == null)
            {
                return null;
            }

            var mr = FxKit.Ensure().RentMesh(FxKit.QuadMesh, mat);
            mr.transform.position = at;
            mr.transform.rotation = Quaternion.LookRotation(Vector3.down);
            mr.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            return mr;
        }

        /// <summary>The holo material for a colour (one per colour, cached).</summary>
        public static Material HoloMaterial(Color color, float behind = 0.55f)
        {
            var c32 = (Color32)color;
            return FxKit.Cached("BlocksBeyondTheStars/FxHolo", c32.r + "," + c32.g + "," + c32.b + "/" + behind.ToString("0.00"), m =>
            {
                m.SetColor("_Color", FxKit.Lin(color));
                m.SetFloat("_Behind", behind);
            });
        }

        /// <summary>A holographic bracket box that flickers in (scale spring), holds and fades.</summary>
        public static void HoloBox(Color color, Vector3 center, Vector3 size, float hold)
        {
            var mat = HoloMaterial(color, 0.4f);
            if (mat == null)
            {
                return;
            }

            var kit = FxKit.Ensure();
            var mr = kit.RentMesh(FxKit.CubeMesh, mat);
            var tr = mr.transform;
            tr.position = center;
            var lin = FxKit.Lin(color);
            float life = hold + 0.35f;
            FxKit.Animate(life, t =>
            {
                float age = t * life;
                float pop = age < 0.12f ? Mathf.Lerp(0f, 1.08f, age / 0.12f) : Mathf.Lerp(1.08f, 1f, Mathf.Clamp01((age - 0.12f) / 0.1f));
                tr.localScale = new Vector3(size.x, size.y * pop, size.z);
                float fade = age > hold ? 1f - (age - hold) / 0.35f : 1f;
                var b = FxKit.Block;
                b.Clear();
                b.SetColor("_Color", new Color(lin.r, lin.g, lin.b, fade));
                b.SetFloat("_EdgeWidth", 0.04f);
                b.SetFloat("_Fill", 0.05f);
                b.SetFloat("_LineDensity", 3f);
                b.SetFloat("_Behind", 0.4f);
                b.SetFloat("_Intensity", 2f);
                mr.SetPropertyBlock(b);
            }, () => kit.ReleaseMesh(mr), mr);
        }

        /// <summary>A thin holo plane sweeping from the top of a box to its bottom (the scan line).</summary>
        public static void HoloPlane(Color color, Vector3 center, Vector3 size, float duration)
        {
            var mat = HoloMaterial(color, 0.3f);
            if (mat == null)
            {
                return;
            }

            var kit = FxKit.Ensure();
            var mr = kit.RentMesh(FxKit.CubeMesh, mat);
            var tr = mr.transform;
            var lin = FxKit.Lin(color);
            FxKit.Animate(duration, t =>
            {
                tr.position = center + Vector3.up * Mathf.Lerp(size.y * 0.5f, -size.y * 0.5f, t);
                tr.localScale = new Vector3(size.x * 1.04f, 0.02f, size.z * 1.04f);
                var b = FxKit.Block;
                b.Clear();
                b.SetColor("_Color", new Color(lin.r, lin.g, lin.b, Mathf.Sin(t * Mathf.PI)));
                b.SetFloat("_EdgeWidth", 0.5f);
                b.SetFloat("_Fill", 0.6f);
                b.SetFloat("_LineDensity", 0f);
                b.SetFloat("_Behind", 0.3f);
                b.SetFloat("_Intensity", 2.4f);
                mr.SetPropertyBlock(b);
            }, () => kit.ReleaseMesh(mr), mr);
        }
    }
}
