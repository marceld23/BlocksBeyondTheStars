// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Builds a blocky creature from a server <see cref="NetCreature"/> descriptor — the parametric
    /// counterpart to <see cref="PlayerAvatar"/> (cubes in code, no art asset). The same descriptor
    /// always yields the same body, so every client draws a species identically. Body segments,
    /// head, legs, optional wings/tail, colour and a bioluminescent glow all come from the species.
    /// The server stays authoritative over which creatures exist and where; this is render-only.
    /// </summary>
    public sealed class CreatureBuilder
    {
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Transform> _legPivots = new List<Transform>();
        private readonly List<Transform> _wingPivots = new List<Transform>();
        private Transform _tailPivot;
        private Transform _headPivot;
        private Material _bodyMat;
        private Light _glow;

        /// <summary>Builds the body under <paramref name="root"/> from the descriptor. Non-standard body
        /// plans (#637/#638) branch into their own build paths; the default path is unchanged, so every
        /// pre-plan species still renders exactly as before.</summary>
        public void Build(GameObject root, NetCreature c)
        {
            EnsureTextures();
            if (c.BodyPlan == "Medusa")
            {
                BuildMedusa(root, c);
                return;
            }

            if (c.BodyPlan == "Titan")
            {
                BuildTitan(root, c);
                return;
            }

            float unit = 0.5f * Mathf.Clamp(c.Size, 0.4f, 3f);
            Color baseColor = Rgb(c.ColorRgb);
            Color bellyColor = Rgb(c.BellyRgb);

            // Per-species proportion jitter (from the id) so two species with the same parts still differ in
            // build — lanky vs squat, big-headed vs small, slim vs broad.
            int idh = StableIdHash(c.SpeciesId);
            float headScale = 0.75f + ((idh >> 2) & 7) / 7f * 0.65f; // 0.75..1.40
            float bodyWide = 0.85f + ((idh >> 5) & 7) / 7f * 0.55f;  // 0.85..1.40
            float legLong = 0.75f + ((idh >> 8) & 7) / 7f * 0.65f;   // 0.75..1.40

            // Hostiles read a touch more aggressive; sleepers are dimmed.
            if (c.Hostile)
            {
                baseColor = Color.Lerp(baseColor, new Color(0.85f, 0.2f, 0.15f), 0.25f);
            }

            if (c.Asleep)
            {
                baseColor *= 0.85f; // a gentle dim only — the "z z z" already reads sleep; 0.6 crushed the (already tile+floor-dimmed) body to black
            }

            // Tamed companions read with a gentle friendly green-cyan cast (never the hostile red) so you can
            // tell your pet apart from wild fauna at a glance (design: docs/developer/CREATURE_TAMING.md).
            if (!string.IsNullOrEmpty(c.OwnerId))
            {
                baseColor = Color.Lerp(baseColor, new Color(0.35f, 0.85f, 0.65f), 0.18f);
            }

            _bodyMat = Lit(c.Glows ? baseColor * 1.6f : baseColor, PickHide(c));

            // All parts hang off a body rig (a child of the root) so the animator can undulate the whole
            // creature for swimming without disturbing the root's movement-driven facing.
            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);

            // Body: a row of segments along +Z (forward). The front segment is the head.
            int segments = Mathf.Clamp(c.BodySegments, 1, 4);
            float segLen = unit * 1.1f;
            float bodyY = unit * (c.Legs > 0 ? 1.1f : 0.7f);
            for (int i = 0; i < segments; i++)
            {
                float z = (i - (segments - 1) * 0.5f) * segLen;
                float taper = 1f - 0.12f * i; // slimmer toward the tail
                AddPart(body, "Body" + i, new Vector3(0f, bodyY, z),
                    new Vector3(unit * 1.1f * taper * bodyWide, unit * 0.95f * taper, segLen), _bodyMat);
            }

            // Two-tone underside: a flatter belly slab in the accent colour, so the body isn't one flat hue.
            var bellyMat = Lit(c.Glows ? bellyColor * 1.4f : bellyColor, PickHide(c));
            AddPart(body, "Belly", new Vector3(0f, bodyY - unit * 0.42f, 0f),
                new Vector3(unit * 1.02f * bodyWide, unit * 0.32f, segments * segLen * 0.9f), bellyMat);

            float frontZ = (segments - 1) * 0.5f * segLen + segLen * 0.6f;
            // Head on a neck pivot (behind the head) so it can bob/graze/lunge as an idle gesture.
            _headPivot = AddPivotPart(body, "Head", new Vector3(0f, bodyY + unit * 0.2f, frontZ - unit * 0.45f),
                new Vector3(0f, 0f, unit * 0.45f), new Vector3(unit * 0.9f * headScale, unit * 0.85f * headScale, unit * 0.8f * headScale), _bodyMat);

            // Eyes: optional (0 = eyeless) and a random count (often two, sometimes three/four/six). Bigger,
            // with a dark pupil so they clearly read as eyes — spread in a row across the head front.
            AddEyes(c, unit, headScale);

            // Horns/spikes on top of the head — silhouette variety.
            int horns = Mathf.Clamp(c.Horns, 0, 4);
            if (horns > 0)
            {
                var hornMat = Lit(new Color(0.20f, 0.17f, 0.15f), null);
                float hornH = unit * 0.5f * headScale;
                for (int hn = 0; hn < horns; hn++)
                {
                    float hx = horns == 1 ? 0f : Mathf.Lerp(-unit * 0.30f * headScale, unit * 0.30f * headScale, hn / (float)(horns - 1));
                    AddPartTo(_headPivot, "Horn" + hn, new Vector3(hx, unit * (0.5f * headScale + 0.25f * legLong), unit * 0.05f),
                        new Vector3(unit * 0.13f, hornH, unit * 0.13f), hornMat);
                }
            }

            // Legs: paired under the body along its length, each on a hip pivot so it can swing.
            int legs = Mathf.Clamp(c.Legs, 0, 8);
            int pairs = legs / 2;
            float legH = bodyY * 0.9f * legLong;
            for (int p = 0; p < pairs; p++)
            {
                float z = pairs == 1 ? 0f : Mathf.Lerp(-segLen * 0.7f, segLen * 0.7f, p / (float)(pairs - 1));
                _legPivots.Add(AddPivotPart(body, "LegL" + p, new Vector3(-unit * 0.5f, legH, z),
                    new Vector3(0f, -legH * 0.5f, 0f), new Vector3(unit * 0.18f, legH, unit * 0.18f), _bodyMat));
                _legPivots.Add(AddPivotPart(body, "LegR" + p, new Vector3(unit * 0.5f, legH, z),
                    new Vector3(0f, -legH * 0.5f, 0f), new Vector3(unit * 0.18f, legH, unit * 0.18f), _bodyMat));
            }

            if (c.HasWings)
            {
                float wingW = unit * 0.9f;
                _wingPivots.Add(AddPivotPart(body, "WingL", new Vector3(-unit * 0.45f, bodyY + unit * 0.2f, 0f),
                    new Vector3(-wingW * 0.5f, 0f, 0f), new Vector3(wingW, unit * 0.08f, unit * 1.2f), _bodyMat));
                _wingPivots.Add(AddPivotPart(body, "WingR", new Vector3(unit * 0.45f, bodyY + unit * 0.2f, 0f),
                    new Vector3(wingW * 0.5f, 0f, 0f), new Vector3(wingW, unit * 0.08f, unit * 1.2f), _bodyMat));
            }

            if (c.HasTail)
            {
                float tailLen = segLen * 0.9f;
                float tailZ = -(segments - 1) * 0.5f * segLen - segLen * 0.6f;
                _tailPivot = AddPivotPart(body, "Tail", new Vector3(0f, bodyY, tailZ + tailLen * 0.5f),
                    new Vector3(0f, 0f, -tailLen * 0.5f), new Vector3(unit * 0.35f, unit * 0.35f, tailLen), _bodyMat);
            }

            // Dorsal crest: a row of spiny plates along the spine, tallest at the shoulders — silhouette variety.
            if (c.HasCrest)
            {
                var crestMat = Lit(c.Glows ? baseColor * 1.4f : baseColor * 0.7f, null);
                int fins = Mathf.Max(3, segments * 2);
                float z0 = -(segments - 1) * 0.5f * segLen - segLen * 0.2f;
                float z1 = (segments - 1) * 0.5f * segLen + segLen * 0.2f;
                float topY = bodyY + unit * 0.5f;
                for (int f = 0; f < fins; f++)
                {
                    float t = fins == 1 ? 0.5f : f / (float)(fins - 1);
                    float finH = unit * (0.5f - 0.22f * Mathf.Abs(t - 0.32f)); // peak near the front
                    AddPart(body, "Crest" + f, new Vector3(0f, topY, Mathf.Lerp(z0, z1, t)),
                        new Vector3(unit * 0.10f, Mathf.Max(unit * 0.2f, finH), unit * 0.42f), crestMat);
                }
            }

            // item-21 morphology: dangling tentacles — chains of shrinking segments hanging under the body
            // (front-loaded like a squid's arms), in the belly accent colour so they read against the body.
            int tentacles = Mathf.Clamp(c.Tentacles, 0, 8);
            if (tentacles > 0)
            {
                float tentLen = unit * (c.Legs > 0 ? 0.8f : 1.1f); // longer on legless swimmers/floaters
                for (int tn = 0; tn < tentacles; tn++)
                {
                    float fx = Mathf.Lerp(-unit * 0.42f * bodyWide, unit * 0.42f * bodyWide, tentacles == 1 ? 0.5f : tn / (float)(tentacles - 1));
                    float fz = frontZ - unit * (0.5f + 0.45f * (tn % 2)); // two staggered rows up front
                    float y = bodyY - unit * 0.45f;
                    for (int seg = 0; seg < 3; seg++)
                    {
                        float w = unit * (0.16f - 0.04f * seg);
                        float h = tentLen * (0.4f - 0.07f * seg);
                        AddPart(body, $"Tent{tn}_{seg}", new Vector3(fx, y - h * 0.5f, fz + seg * unit * 0.06f),
                            new Vector3(w, h, w), bellyMat);
                        y -= h;
                    }
                }
            }

            // item-21 morphology: a translucent buoyancy gas-sac floating above the body (alpha-blended via the
            // always-included Cloud shader), tinted to a pale wash of the body colour.
            if (c.HasGasSac)
            {
                var sacShader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Color");
                var sacMat = new Material(sacShader);
                var sacTint = Color.Lerp(baseColor, Color.white, 0.55f);
                sacTint.a = 0.45f;
                sacMat.color = sacTint;
                AddPartTo(body.transform, "GasSac",
                    new Vector3(0f, bodyY + unit * (1.0f + 0.25f * bodyWide), -segLen * 0.1f),
                    Vector3.one * unit * (1.15f * bodyWide), sacMat, PrimitiveType.Sphere);
            }

            if (c.Glows)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(body.transform, false);
                go.transform.localPosition = new Vector3(0f, bodyY, 0f);
                _glow = go.AddComponent<Light>();
                _glow.type = LightType.Point;
                _glow.range = unit * 6f;
                _glow.intensity = 1.1f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }

            // Procedural limb animation (leg swing while moving, wing flap, tail sway) + per-temperament
            // idle head gestures (graze / alert / lunge); aquatic species also undulate the body rig (swim).
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(_legPivots.ToArray(), _wingPivots.ToArray(), _tailPivot, _headPivot, body.transform,
                c.Hostile, c.Asleep, c.Habitat == "Water" || c.Habitat == "Amphibian", c.Temperament);
        }

        /// <summary>Rounded sphere eyes with a dark pupil (+ a white glint when not stalked), or snail-like
        /// eyestalks — extracted verbatim from the standard build path so every body plan shares one face.</summary>
        private void AddEyes(NetCreature c, float unit, float headScale)
        {
            int eyes = Mathf.Clamp(c.Eyes, 0, 8);
            if (eyes <= 0)
            {
                return;
            }

            // Rounded (sphere) eyes — bigger, with a dark pupil + a small white glint so they look glossy (B17).
            var eyeMat = Unlit(c.Glows ? new Color(0.85f, 1f, 0.95f) : new Color(0.97f, 0.97f, 0.88f));
            var pupilMat = Unlit(new Color(0.04f, 0.04f, 0.06f));
            var glintMat = Unlit(Color.white);
            float eyeSize = unit * 0.32f * headScale; // bigger (was 0.24)

            if (c.EyeStalks)
            {
                // item-21 morphology: snail-like eyestalks — each eye sits on a thin stalk atop the head,
                // staggered in height so multi-eyed stalked species read clearly.
                int stalks = Mathf.Min(eyes, 4); // more than 4 stalks turns to visual noise
                float spread2 = unit * headScale * 0.30f;
                for (int e = 0; e < stalks; e++)
                {
                    float fx = stalks == 1 ? 0f : Mathf.Lerp(-spread2, spread2, e / (float)(stalks - 1));
                    float stalkH = unit * headScale * (0.45f + 0.12f * (e % 2));
                    var top = new Vector3(fx, unit * 0.45f * headScale + stalkH, unit * 0.15f * headScale);
                    AddPartTo(_headPivot, "Stalk" + e, new Vector3(fx, unit * 0.45f * headScale + stalkH * 0.5f, unit * 0.15f * headScale),
                        new Vector3(eyeSize * 0.28f, stalkH, eyeSize * 0.28f), _bodyMat);
                    AddPartTo(_headPivot, "Eye" + e, top, Vector3.one * (eyeSize * 0.9f), eyeMat, PrimitiveType.Sphere);
                    AddPartTo(_headPivot, "Pupil" + e, top + new Vector3(0f, 0f, eyeSize * 0.38f), Vector3.one * (eyeSize * 0.5f), pupilMat, PrimitiveType.Sphere);
                }
            }
            else
            {
                float spread = unit * headScale * (0.30f + 0.05f * eyes); // wider span the more eyes there are
                for (int e = 0; e < eyes; e++)
                {
                    float fx = eyes == 1 ? 0f : Mathf.Lerp(-spread, spread, e / (float)(eyes - 1));
                    var pos = new Vector3(fx, unit * 0.16f * headScale, unit * 0.70f * headScale);
                    AddPartTo(_headPivot, "Eye" + e, pos, Vector3.one * eyeSize, eyeMat, PrimitiveType.Sphere);
                    AddPartTo(_headPivot, "Pupil" + e, pos + new Vector3(0f, 0f, eyeSize * 0.42f), Vector3.one * (eyeSize * 0.55f), pupilMat, PrimitiveType.Sphere);
                    AddPartTo(_headPivot, "Glint" + e, pos + new Vector3(eyeSize * 0.16f, eyeSize * 0.18f, eyeSize * 0.5f), Vector3.one * (eyeSize * 0.16f), glintMat, PrimitiveType.Sphere);
                }
            }
        }

        /// <summary>The medusa plan (#637): a translucent pulsing bell (Cloud shader — alpha-blended and
        /// always included) over an opaque nucleus, with long tapering tentacles hanging from the bell rim
        /// in a circle. No head, no legs — the drift and the pulse do the talking.</summary>
        private void BuildMedusa(GameObject root, NetCreature c)
        {
            float unit = 0.5f * Mathf.Clamp(c.Size, 0.4f, 3f);
            Color baseColor = Rgb(c.ColorRgb);
            Color bellyColor = Rgb(c.BellyRgb);
            if (c.Asleep)
            {
                baseColor *= 0.85f;
            }

            if (!string.IsNullOrEmpty(c.OwnerId))
            {
                baseColor = Color.Lerp(baseColor, new Color(0.35f, 0.85f, 0.65f), 0.18f);
            }

            _bodyMat = Lit(c.Glows ? baseColor * 1.6f : baseColor, PickHide(c));
            var bellyMat = Lit(c.Glows ? bellyColor * 1.4f : bellyColor, PickHide(c));

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);

            float bellR = unit * 1.5f;
            float bellY = unit * 1.7f; // rides high — the tentacles need room to trail below

            // The bell pivot is what the animator pulses (scale), so the dome + nucleus breathe together.
            var bell = new GameObject("Bell");
            bell.transform.SetParent(body.transform, false);
            bell.transform.localPosition = new Vector3(0f, bellY, 0f);

            var bellShader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Color");
            var bellMat = new Material(bellShader);
            var bellTint = Color.Lerp(baseColor, Color.white, 0.35f);
            bellTint.a = 0.45f;
            bellMat.color = bellTint;
            AddPartTo(bell.transform, "BellDome", Vector3.zero,
                new Vector3(bellR * 2f, bellR * 1.5f, bellR * 2f), bellMat, PrimitiveType.Sphere);

            // An opaque nucleus inside the bell so the creature reads solid through the translucency.
            AddPartTo(bell.transform, "Nucleus", new Vector3(0f, -bellR * 0.15f, 0f),
                Vector3.one * (bellR * 0.7f), _bodyMat, PrimitiveType.Sphere);

            // Eyes (usually none, at most two) sit on the nucleus front.
            int eyes = Mathf.Clamp(c.Eyes, 0, 2);
            if (eyes > 0)
            {
                var eyeMat = Unlit(c.Glows ? new Color(0.85f, 1f, 0.95f) : new Color(0.97f, 0.97f, 0.88f));
                var pupilMat = Unlit(new Color(0.04f, 0.04f, 0.06f));
                float eyeSize = bellR * 0.22f;
                for (int e = 0; e < eyes; e++)
                {
                    float fx = eyes == 1 ? 0f : (e == 0 ? -bellR * 0.18f : bellR * 0.18f);
                    var pos = new Vector3(fx, -bellR * 0.15f, bellR * 0.32f);
                    AddPartTo(bell.transform, "Eye" + e, pos, Vector3.one * eyeSize, eyeMat, PrimitiveType.Sphere);
                    AddPartTo(bell.transform, "Pupil" + e, pos + new Vector3(0f, 0f, eyeSize * 0.4f),
                        Vector3.one * (eyeSize * 0.55f), pupilMat, PrimitiveType.Sphere);
                }
            }

            // Tentacles: long tapering chains hanging from the bell RIM in a circle (not two chin rows) —
            // each on its own pivot so the animator can sway them out of phase.
            var tentaclePivots = new List<Transform>();
            int tentacles = Mathf.Clamp(c.Tentacles, 3, 10);
            float tentLen = unit * 2.2f;
            for (int tn = 0; tn < tentacles; tn++)
            {
                float a = tn / (float)tentacles * Mathf.PI * 2f;
                var pivot = new GameObject("TentP" + tn).transform;
                pivot.SetParent(bell.transform, false);
                pivot.localPosition = new Vector3(Mathf.Cos(a) * bellR * 0.75f, -bellR * 0.45f, Mathf.Sin(a) * bellR * 0.75f);

                float y = 0f;
                for (int seg = 0; seg < 5; seg++)
                {
                    float w = unit * (0.15f - 0.02f * seg);
                    float h = tentLen * (0.26f - 0.025f * seg);
                    AddPartTo(pivot, $"Tent{tn}_{seg}", new Vector3(0f, y - h * 0.5f, 0f), new Vector3(w, h, w), bellyMat);
                    y -= h;
                }

                tentaclePivots.Add(pivot);
            }

            if (c.Glows)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(bell.transform, false);
                _glow = go.AddComponent<Light>();
                _glow.type = LightType.Point;
                _glow.range = unit * 6f;
                _glow.intensity = 1.1f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }

            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(System.Array.Empty<Transform>(), System.Array.Empty<Transform>(), null, null, body.transform,
                c.Hostile, c.Asleep, false, c.Temperament); // no fish-weave — the bell pulse carries the motion
            anim.InitMedusa(bell.transform, tentaclePivots.ToArray());
        }

        /// <summary>The titan plan (#638): elephant/giraffe-scale megafauna — a heavy multi-segment torso on
        /// four pillar legs, an optional stacked neck (≥2 reads giraffe) or hanging trunk (elephant), ears,
        /// and the species' horns worn as forward tusks. Size runs to 6 here (the standard path stays at 3).</summary>
        private void BuildTitan(GameObject root, NetCreature c)
        {
            float unit = 0.5f * Mathf.Clamp(c.Size, 0.4f, 8f); // past the standard clamp — titans are the point
            Color baseColor = Rgb(c.ColorRgb);
            Color bellyColor = Rgb(c.BellyRgb);
            if (c.Hostile)
            {
                baseColor = Color.Lerp(baseColor, new Color(0.85f, 0.2f, 0.15f), 0.25f);
            }

            if (c.Asleep)
            {
                baseColor *= 0.85f;
            }

            if (!string.IsNullOrEmpty(c.OwnerId))
            {
                baseColor = Color.Lerp(baseColor, new Color(0.35f, 0.85f, 0.65f), 0.18f);
            }

            _bodyMat = Lit(c.Glows ? baseColor * 1.6f : baseColor, PickHide(c));
            var bellyMat = Lit(c.Glows ? bellyColor * 1.4f : bellyColor, PickHide(c));

            int idh = StableIdHash(c.SpeciesId);
            float headScale = 0.8f + ((idh >> 2) & 7) / 7f * 0.4f; // 0.8..1.2 — giants keep steadier proportions
            float bodyWide = 1.0f + ((idh >> 5) & 7) / 7f * 0.3f;  // 1.0..1.3

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);

            // Pillar legs first — the body rides on top of them.
            int segments = Mathf.Clamp(c.BodySegments, 2, 3);
            float segLen = unit * 1.15f;
            float legH = unit * 1.15f;
            float bodyY = legH + unit * 0.45f;
            for (int i = 0; i < segments; i++)
            {
                float z = (i - (segments - 1) * 0.5f) * segLen;
                float taper = 1f - 0.08f * i;
                AddPart(body, "Body" + i, new Vector3(0f, bodyY, z),
                    new Vector3(unit * 1.25f * taper * bodyWide, unit * 1.05f * taper, segLen), _bodyMat);
            }

            AddPart(body, "Belly", new Vector3(0f, bodyY - unit * 0.5f, 0f),
                new Vector3(unit * 1.15f * bodyWide, unit * 0.35f, segments * segLen * 0.9f), bellyMat);

            for (int p = 0; p < 2; p++)
            {
                float z = (p == 0 ? 1f : -1f) * segLen * (segments - 1) * 0.5f;
                _legPivots.Add(AddPivotPart(body, "LegL" + p, new Vector3(-unit * 0.55f, legH, z),
                    new Vector3(0f, -legH * 0.5f, 0f), new Vector3(unit * 0.34f, legH, unit * 0.34f), _bodyMat));
                _legPivots.Add(AddPivotPart(body, "LegR" + p, new Vector3(unit * 0.55f, legH, z),
                    new Vector3(0f, -legH * 0.5f, 0f), new Vector3(unit * 0.34f, legH, unit * 0.34f), _bodyMat));
            }

            // Neck: stacked shrinking segments rising forward from the torso front; the head pivots at its top,
            // so the existing graze gesture becomes a giraffe lowering its neck — for free.
            float frontZ = (segments - 1) * 0.5f * segLen + segLen * 0.55f;
            int neck = Mathf.Clamp(c.NeckLength, 0, 3);
            float headY = bodyY + unit * 0.25f;
            float headZ = frontZ - unit * 0.35f;
            for (int nk = 0; nk < neck; nk++)
            {
                float taper = 1f - 0.15f * nk;
                headY += unit * 0.62f;
                headZ += unit * 0.18f;
                AddPart(body, "Neck" + nk, new Vector3(0f, headY - unit * 0.28f, headZ - unit * 0.05f),
                    new Vector3(unit * 0.55f * taper, unit * 0.75f, unit * 0.55f * taper), _bodyMat);
            }

            _headPivot = AddPivotPart(body, "Head", new Vector3(0f, headY + unit * 0.2f, headZ),
                new Vector3(0f, 0f, unit * 0.45f), new Vector3(unit * 0.95f * headScale, unit * 0.85f * headScale, unit * 0.9f * headScale), _bodyMat);

            AddEyes(c, unit, headScale);

            // Ears: two flat slabs at the head sides — the elephant read, and scale-scaffolding for the eye.
            AddPartTo(_headPivot, "EarL", new Vector3(-unit * 0.55f * headScale, unit * 0.2f * headScale, unit * 0.2f),
                new Vector3(unit * 0.12f, unit * 0.6f * headScale, unit * 0.5f * headScale), _bodyMat);
            AddPartTo(_headPivot, "EarR", new Vector3(unit * 0.55f * headScale, unit * 0.2f * headScale, unit * 0.2f),
                new Vector3(unit * 0.12f, unit * 0.6f * headScale, unit * 0.5f * headScale), _bodyMat);

            // Tusks: the species' horns, worn forward from the lower jaw instead of upright on the crown.
            int tusks = Mathf.Clamp(c.Horns, 0, 4);
            if (tusks > 0)
            {
                var tuskMat = Lit(new Color(0.92f, 0.88f, 0.78f), null); // ivory
                for (int tk = 0; tk < tusks; tk++)
                {
                    float hx = tusks == 1 ? 0f : Mathf.Lerp(-unit * 0.3f * headScale, unit * 0.3f * headScale, tk / (float)(tusks - 1));
                    AddPartTo(_headPivot, "Tusk" + tk, new Vector3(hx, -unit * 0.25f * headScale, unit * 0.6f * headScale),
                        new Vector3(unit * 0.12f, unit * 0.12f, unit * 0.7f * headScale), tuskMat);
                }
            }

            // Trunk: shrinking segments hanging from the head front, slightly forward — the elephant.
            if (c.HasTrunk)
            {
                float ty = -unit * 0.2f * headScale;
                for (int seg = 0; seg < 4; seg++)
                {
                    float w = unit * (0.3f - 0.05f * seg);
                    float h = unit * 0.42f;
                    AddPartTo(_headPivot, "Trunk" + seg, new Vector3(0f, ty - h * 0.5f, unit * (0.5f + 0.04f * seg) * headScale),
                        new Vector3(w, h, w), _bodyMat);
                    ty -= h;
                }
            }

            if (c.HasTail)
            {
                float tailLen = segLen * 0.8f;
                float tailZ = -(segments - 1) * 0.5f * segLen - segLen * 0.55f;
                _tailPivot = AddPivotPart(body, "Tail", new Vector3(0f, bodyY + unit * 0.2f, tailZ + tailLen * 0.5f),
                    new Vector3(0f, 0f, -tailLen * 0.5f), new Vector3(unit * 0.22f, unit * 0.22f, tailLen), _bodyMat);
            }

            if (c.Glows)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(body.transform, false);
                go.transform.localPosition = new Vector3(0f, bodyY, 0f);
                _glow = go.AddComponent<Light>();
                _glow.type = LightType.Point;
                _glow.range = unit * 6f;
                _glow.intensity = 1.1f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }

            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(_legPivots.ToArray(), _wingPivots.ToArray(), _tailPivot, _headPivot, body.transform,
                c.Hostile, c.Asleep, false, c.Temperament);
            // Bigger animals stride slower — a giant taking sheep-paced steps is the classic scale-breaking
            // tell. Size 3.5 → ~0.8× cadence, size 6 → ~0.5×.
            anim.CadenceScale = Mathf.Clamp(1.6f / Mathf.Max(1f, c.Size * 0.55f), 0.4f, 1f);
        }

        /// <summary>Adds a part on its own pivot (hinge) so it can be rotated for animation. The cube hangs
        /// at <paramref name="cubeOffset"/> from the pivot; returns the pivot transform.</summary>
        private Transform AddPivotPart(GameObject root, string partName, Vector3 pivotPos, Vector3 cubeOffset, Vector3 scale, Material mat)
        {
            var pivot = new GameObject(partName).transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = pivotPos;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName + "Mesh";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col);
            }

            go.transform.SetParent(pivot, false);
            go.transform.localPosition = cubeOffset;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _renderers.Add(go.GetComponent<Renderer>());
            return pivot;
        }

        /// <summary>Adds a render-only cube parented to an arbitrary transform (e.g. eyes on the head pivot).</summary>
        private void AddPartTo(Transform parent, string partName, Vector3 localPos, Vector3 scale, Material mat, PrimitiveType shape = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _renderers.Add(go.GetComponent<Renderer>());
        }

        private void AddPart(GameObject root, string partName, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col); // render-only; never blocks the player
            }

            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _renderers.Add(go.GetComponent<Renderer>());
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        private static int StableIdHash(string s)
        {
            int h = 0;
            foreach (char ch in s ?? string.Empty)
            {
                h = h * 31 + ch;
            }

            return h & 0x7fffffff;
        }

        private static Material Unlit(Color color)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            return new Material(shader) { color = ShaderColor.Srgb(color) };
        }

        // Shared (loaded once) tintable grayscale hide tiles; the body multiplies them by the species colour.
        private static Texture2D _scales, _fur, _chitin, _hide, _slime;
        private static Texture2D _feathers, _spots, _stripes, _warty, _plated;
        private static Texture2D _finned, _tentacled;
        // Task 6 — more skin variety.
        private static Texture2D _mossy, _crystalline, _metallic, _banded, _shaggy;
        private static Texture2D _spined, _mottled, _iridescent, _barkskin, _veined;
        private static bool _texLoaded;

        private static void EnsureTextures()
        {
            if (_texLoaded)
            {
                return;
            }

            _texLoaded = true;
            _scales = LoadTex("creature_scales");
            _fur = LoadTex("creature_fur");
            _chitin = LoadTex("creature_chitin");
            _hide = LoadTex("creature_hide");
            _slime = LoadTex("creature_slime");
            _feathers = LoadTex("creature_feathers");
            _spots = LoadTex("creature_spots");
            _stripes = LoadTex("creature_stripes");
            _warty = LoadTex("creature_warty");
            _plated = LoadTex("creature_plated");
            _finned = LoadTex("creature_finned");
            _tentacled = LoadTex("creature_tentacled");
            _mossy = LoadTex("creature_mossy");
            _crystalline = LoadTex("creature_crystalline");
            _metallic = LoadTex("creature_metallic");
            _banded = LoadTex("creature_banded");
            _shaggy = LoadTex("creature_shaggy");
            _spined = LoadTex("creature_spined");
            _mottled = LoadTex("creature_mottled");
            _iridescent = LoadTex("creature_iridescent");
            _barkskin = LoadTex("creature_barkskin");
            _veined = LoadTex("creature_veined");
        }

        /// <summary>Picks a hide tile for the species: glowing → slime, winged → feathers, hostile → chitin/
        /// plated, otherwise a stable choice from a wide pool keyed off the species id (so each species looks
        /// consistent but the world's fauna spans many skins).</summary>
        private static Texture2D PickHide(NetCreature c)
        {
            int h = StableIdHash(c.SpeciesId);
            if (c.Glows)
            {
                var glow = new[] { _slime, _veined, _crystalline, _iridescent };
                return glow[h % glow.Length] ?? _slime ?? _hide;
            }

            if (c.HasWings && _feathers != null)
            {
                var winged = new[] { _feathers, _iridescent, _mottled };
                return winged[h % winged.Length] ?? _feathers;
            }

            if (c.Habitat == "Water" || c.Habitat == "Amphibian")
            {
                var aquatic = new[] { _finned, _tentacled, _slime, _scales, _iridescent, _banded };
                return aquatic[h % aquatic.Length] ?? _finned ?? _hide;
            }

            if (c.Hostile)
            {
                var hostileOpts = new[] { _chitin, _plated, _scales, _spined, _metallic, _crystalline, _barkskin };
                return hostileOpts[h % hostileOpts.Length] ?? _chitin ?? _hide;
            }

            var opts = new[]
            {
                _fur, _hide, _scales, _spots, _stripes, _warty, _plated,
                _mossy, _shaggy, _mottled, _banded, _barkskin, _veined,
            };
            return opts[h % opts.Length] ?? _hide;
        }

        private static Texture2D LoadTex(string key)
        {
            var asset = Resources.Load<TextAsset>("textures/" + key);
            if (asset == null || asset.bytes.Length != 64 * 64 * 4)
            {
                return null;
            }

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            tex.LoadRawTextureData(Brighten(asset.bytes));
            tex.Apply();
            return tex;
        }

        // The hide tiles are authored dark (~0.3 mean grey) but the body uses them as a MULTIPLY over the
        // species colour (colour * tile) — a multiply-tint workflow needs near-white maps, or the tile alone
        // eats ~70% of the brightness and the body reads black. Lift each tile toward white so it modulates as
        // gentle DETAIL instead of a darkener: v' = 1 - (1 - v) * TileDetail (white stays white; only the
        // shadows rise, so the pattern survives). Alpha is left alone (LitColor is opaque).
        private const float TileDetail = 0.45f; // 0 = flat white, 1 = original (too dark). Tunable.

        private static byte[] Brighten(byte[] raw)
        {
            var outp = new byte[raw.Length];
            for (int p = 0; p < raw.Length; p += 4)
            {
                outp[p] = (byte)(255f - (255 - raw[p]) * TileDetail);
                outp[p + 1] = (byte)(255f - (255 - raw[p + 1]) * TileDetail);
                outp[p + 2] = (byte)(255f - (255 - raw[p + 2]) * TileDetail);
                outp[p + 3] = raw[p + 3];
            }

            return outp;
        }

        // Ambient floor for creature bodies. Higher than the LitColor default (0.35) because a creature's
        // textured, camera-away faces (a flier's belly seen from below, a back turned to the fixed key light)
        // sit at the floor and otherwise sink to a black silhouette against a bright sky. Only creatures raise
        // it — every other LitColor user keeps the 0.35 shader default.
        private const float CreatureFloor = 0.62f;

        // Fill light from the flank opposite the fixed key. The single key light leaves the away-facing side of
        // a creature at the floor, so it still read dark; this unshadowed fill lifts that flank. Only creatures
        // set it — every other LitColor user keeps the 0 default (single key light, unchanged).
        private const float CreatureFill = 0.3f;

        private static Material Lit(Color color, Texture2D tex)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = ShaderColor.Srgb(color) };
            if (tex != null)
            {
                m.mainTexture = tex;
            }

            if (m.HasProperty("_Floor"))
            {
                m.SetFloat("_Floor", CreatureFloor); // no-op on the Unlit/Color fallback
                m.SetFloat("_Fill", CreatureFill);
            }

            return m;
        }
    }
}
