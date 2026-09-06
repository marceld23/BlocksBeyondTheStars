// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
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
        /// <summary>How the finished rig's feet find real ground. <see cref="CreatureView"/> supplies it (it
        /// owns the world handle); left null the rig animates its gait in body space, as it always did.</summary>
        public CreatureFeet.GroundProbe Ground;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<LegRig> _legs = new List<LegRig>();
        private readonly List<WingRig> _wings = new List<WingRig>();
        private readonly List<Transform> _tailChain = new List<Transform>();
        private readonly List<Transform> _neckChain = new List<Transform>();
        private readonly List<Transform> _eyelids = new List<Transform>();
        private readonly List<Transform> _ears = new List<Transform>();
        private readonly List<Transform[]> _tentacleChains = new List<Transform[]>();
        private readonly List<Transform> _trunkChain = new List<Transform>();
        private readonly List<Transform> _fins = new List<Transform>();
        private Transform _headPivot;
        private Transform _jawPivot;
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
            // Head on a neck pivot (behind the head) so it can bob/graze/lunge as an idle gesture, with a
            // hinged lower jaw so the species' voice actually moves a mouth (the calls have been coming out
            // of a sealed head since #902).
            _headPivot = NewPivot(body.transform, "Head", new Vector3(0f, bodyY + unit * 0.2f, frontZ - unit * 0.45f));
            AddHeadBox(unit * 0.9f * headScale, unit * 0.85f * headScale, unit * 0.8f * headScale, unit * 0.45f, _bodyMat);

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

            // Legs: paired under the body along its FULL length, each on a hip pivot so it can swing.
            // Two fixes over the original layout: the hips sit at the body's real half-width (they used to be
            // pinned at 0.5 units while a wide body reaches 0.77, so broad species grew their legs out of the
            // middle of the belly), and the rows spread across the whole torso instead of over a single
            // segment's length (a 4-segment body used to carry all its legs in a cluster at the centre).
            // Row 0 is the FRONT pair on every body plan — the titan path numbered from the front and this one
            // from the rear, which mirrored any gait keyed off the row.
            int legs = Mathf.Clamp(c.Legs, 0, 8);
            int rows = legs / 2;
            float legH = bodyY * 0.9f * legLong;
            float legThick = unit * (0.13f + 0.09f * legLong) * Mathf.Clamp(Mathf.Pow(Mathf.Max(0.4f, c.Size), 0.3f), 0.75f, 1.5f);
            float hipSpan = (segments - 1) * 0.5f * segLen + segLen * 0.22f;
            for (int row = 0; row < rows; row++)
            {
                float z = rows == 1 ? segLen * 0.15f : Mathf.Lerp(hipSpan, -hipSpan, row / (float)(rows - 1));
                float hipX = unit * 0.55f * bodyWide * TaperAt(z, segLen, segments) + unit * 0.06f;
                _legs.Add(AddLeg(body.transform, row, 0, rows, new Vector3(-hipX, legH, z), legH, legThick, _bodyMat));
                _legs.Add(AddLeg(body.transform, row, 1, rows, new Vector3(hipX, legH, z), legH, legThick, _bodyMat));
            }

            if (c.HasWings)
            {
                AddWings(body.transform, unit * 0.9f, unit * 1.2f, unit * 0.08f,
                    new Vector3(unit * 0.45f, bodyY + unit * 0.2f, 0f), _bodyMat);
            }

            if (c.HasTail)
            {
                float tailBaseZ = -(segments - 1) * 0.5f * segLen - segLen * 0.2f;
                AddTail(body.transform, new Vector3(0f, bodyY, tailBaseZ), segLen * 1.1f, unit * 0.35f, 3, _bodyMat);
            }

            // Fins: a legless swimmer used to be a weaving box with no limbs at all. Pectorals on the flanks,
            // a vertical tail fin at the back (on the tail's last link when there is one), and a dorsal when
            // the species is not already wearing a crest there.
            if (c.HasFins)
            {
                float finLen = unit * 0.75f;
                for (int f = 0; f < 2; f++)
                {
                    float sx = f == 0 ? -1f : 1f;
                    var pec = NewPivot(body.transform, f == 0 ? "FinL" : "FinR",
                        new Vector3(sx * unit * 0.5f * bodyWide, bodyY - unit * 0.1f, frontZ - unit * 0.7f));
                    AddPartTo(pec, "FinBlade", new Vector3(sx * finLen * 0.5f, 0f, -finLen * 0.15f),
                        new Vector3(finLen, unit * 0.07f, finLen * 0.8f), bellyMat);
                    _fins.Add(pec);
                }

                var tailFinParent = _tailChain.Count > 0 ? _tailChain[_tailChain.Count - 1] : body.transform;
                float tailFinZ = _tailChain.Count > 0
                    ? -segLen * 1.1f / 3f
                    : -(segments - 1) * 0.5f * segLen - segLen * 0.55f;
                var caudal = NewPivot(tailFinParent, "FinTail",
                    new Vector3(0f, _tailChain.Count > 0 ? 0f : bodyY, tailFinZ));
                AddPartTo(caudal, "FinTailBlade", new Vector3(0f, 0f, -finLen * 0.45f),
                    new Vector3(unit * 0.07f, finLen * 1.5f, finLen * 0.9f), bellyMat);
                _fins.Add(caudal);

                if (!c.HasCrest)
                {
                    var dorsal = NewPivot(body.transform, "FinDorsal", new Vector3(0f, bodyY + unit * 0.45f, 0f));
                    AddPartTo(dorsal, "FinDorsalBlade", new Vector3(0f, finLen * 0.4f, -finLen * 0.1f),
                        new Vector3(unit * 0.07f, finLen * 0.8f, finLen), bellyMat);
                    _fins.Add(dorsal);
                }
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
                // Nested pivots, not a stack of static cubes: these are arms, and they were frozen solid.
                float tentLen = unit * (c.Legs > 0 ? 0.8f : 1.1f); // longer on legless swimmers/floaters
                for (int tn = 0; tn < tentacles; tn++)
                {
                    float fx = Mathf.Lerp(-unit * 0.42f * bodyWide, unit * 0.42f * bodyWide, tentacles == 1 ? 0.5f : tn / (float)(tentacles - 1));
                    float fz = frontZ - unit * (0.5f + 0.45f * (tn % 2)); // two staggered rows up front
                    _tentacleChains.Add(AddChain(body.transform, $"Tent{tn}_",
                        new Vector3(fx, bodyY - unit * 0.45f, fz), unit * 0.06f,
                        3, unit * 0.16f, tentLen * 0.4f, 0.25f, bellyMat));
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

            // Procedural limb animation (a speed-locked gait, wing flap, tail sway) + per-temperament idle
            // head gestures (graze / alert / lunge); aquatic species also undulate the body rig (swim).
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(Describe(c, body.transform, unit, legH, idh));
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
                    AddEyelid(top, eyeSize * 0.9f, _bodyMat);
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
                    AddEyelid(pos, eyeSize, _bodyMat);
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
            // Every segment is its own pivot, so the sway propagates down the arm as a travelling wave. As one
            // rigid rod per arm they swung like antennae rather than trailing behind the bell.
            int tentacles = Mathf.Clamp(c.Tentacles, 3, 10);
            float tentLen = unit * 2.2f;
            for (int tn = 0; tn < tentacles; tn++)
            {
                float a = tn / (float)tentacles * Mathf.PI * 2f;
                _tentacleChains.Add(AddChain(bell.transform, $"Tent{tn}_",
                    new Vector3(Mathf.Cos(a) * bellR * 0.75f, -bellR * 0.45f, Mathf.Sin(a) * bellR * 0.75f), 0f,
                    5, unit * 0.15f, tentLen * 0.26f, 0.14f, bellyMat));
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

            // No fish-weave and no gait — the bell pulse carries all of this body's motion.
            var rig = Describe(c, body.transform, unit, unit, StableIdHash(c.SpeciesId));
            rig.Aquatic = false;
            rig.Bell = bell.transform;
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
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

            // Pillar legs, row 0 at the front (the shared convention). The hips sit at the torso's real
            // half-width so a broad titan carries its pillars under the flanks, not under the belly.
            for (int row = 0; row < 2; row++)
            {
                float z = (row == 0 ? 1f : -1f) * segLen * (segments - 1) * 0.5f;
                float hipX = unit * 0.62f * bodyWide * TaperAt(z, segLen, segments);
                _legs.Add(AddLeg(body.transform, row, 0, 2, new Vector3(-hipX, legH, z), legH, unit * 0.34f, _bodyMat));
                _legs.Add(AddLeg(body.transform, row, 1, 2, new Vector3(hipX, legH, z), legH, unit * 0.34f, _bodyMat));
            }

            // Neck: stacked shrinking segments rising forward from the torso front; the head pivots at its top,
            // so the existing graze gesture becomes a giraffe lowering its neck — for free.
            float frontZ = (segments - 1) * 0.5f * segLen + segLen * 0.55f;
            // Nested pivots, so lowering the head actually lowers the NECK. As a static stack the graze
            // gesture could only nod the head at the top of a rigid column — a giraffe that cannot reach the
            // ground. The chain distributes the gesture and the animal really bends down.
            int neck = Mathf.Clamp(c.NeckLength, 0, 3);
            var neckParent = body.transform;
            float headY = bodyY + unit * 0.25f;
            float headZ = frontZ - unit * 0.35f;
            for (int nk = 0; nk < neck; nk++)
            {
                float taper = 1f - 0.15f * nk;
                var seg = NewPivot(neckParent, "Neck" + nk, nk == 0
                    ? new Vector3(0f, headY, headZ - unit * 0.05f)
                    : new Vector3(0f, unit * 0.62f, unit * 0.18f));
                AddPartTo(seg, "NeckSeg" + nk, new Vector3(0f, unit * 0.34f, unit * 0.09f),
                    new Vector3(unit * 0.55f * taper, unit * 0.75f, unit * 0.55f * taper), _bodyMat);
                _neckChain.Add(seg);
                neckParent = seg;
                headY += unit * 0.62f;
                headZ += unit * 0.18f;
            }

            _headPivot = NewPivot(neckParent, "Head", neck > 0
                ? new Vector3(0f, unit * 0.82f, unit * 0.23f)
                : new Vector3(0f, headY + unit * 0.2f, headZ));
            AddHeadBox(unit * 0.95f * headScale, unit * 0.85f * headScale, unit * 0.9f * headScale, unit * 0.45f, _bodyMat);

            AddEyes(c, unit, headScale);

            // Ears: two flat slabs at the head sides — the elephant read, and scale-scaffolding for the eye.
            // On pivots at the top edge, so they can flick on a long idle.
            for (int e = 0; e < 2; e++)
            {
                float ex = (e == 0 ? -1f : 1f) * unit * 0.55f * headScale;
                _ears.Add(AddPivotPart(_headPivot, e == 0 ? "EarL" : "EarR",
                    new Vector3(ex, unit * 0.5f * headScale, unit * 0.2f),
                    new Vector3(0f, -unit * 0.3f * headScale, 0f),
                    new Vector3(unit * 0.12f, unit * 0.6f * headScale, unit * 0.5f * headScale), _bodyMat));
            }

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
                // A chain, so the trunk can curl and sway instead of hanging off the head like a pipe.
                _trunkChain.AddRange(AddChain(_headPivot, "Trunk",
                    new Vector3(0f, -unit * 0.2f * headScale, unit * 0.5f * headScale), unit * 0.04f,
                    4, unit * 0.3f, unit * 0.42f, 0.17f, _bodyMat));
            }

            if (c.HasTail)
            {
                float tailBaseZ = -(segments - 1) * 0.5f * segLen - segLen * 0.15f;
                AddTail(body.transform, new Vector3(0f, bodyY + unit * 0.2f, tailBaseZ), segLen * 1.0f, unit * 0.22f, 4, _bodyMat);
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

            var rig = Describe(c, body.transform, unit, legH, idh);
            rig.Aquatic = false;
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
            // A giant's slow stride now falls out of the geometry — its stride length is metres, so the
            // speed-locked cycle rate is low by construction. This stays as a small extra drag on the beat
            // (the hand-tuned 1/size curve it replaces was doing the whole job on its own).
            anim.CadenceScale = Mathf.Clamp(1.15f / Mathf.Max(1f, c.Size * 0.35f), 0.6f, 1f);
        }

        /// <summary>A pair of two-panel wings: shoulder → inner panel → wrist → outer panel. The wrist is what
        /// makes a fold read as a fold — the single slab this replaces could only be rotated bodily up over
        /// the back, which is not what a bird does with its wings when it lands.</summary>
        private void AddWings(Transform parent, float span, float chord, float thick, Vector3 shoulderPos, Material mat)
        {
            float inner = span * 0.45f, outer = span - span * 0.45f;
            for (int w = 0; w < 2; w++)
            {
                float sx = w == 0 ? -1f : 1f;
                var shoulder = NewPivot(parent, w == 0 ? "WingL" : "WingR",
                    new Vector3(sx * shoulderPos.x, shoulderPos.y, shoulderPos.z));
                AddPartTo(shoulder, "WingInner", new Vector3(sx * inner * 0.5f, 0f, 0f),
                    new Vector3(inner, thick, chord), mat);

                var wrist = NewPivot(shoulder, "WingWrist", new Vector3(sx * inner, 0f, 0f));
                AddPartTo(wrist, "WingOuter", new Vector3(sx * outer * 0.5f, 0f, 0f),
                    new Vector3(outer, thick * 0.8f, chord * 0.82f), mat);

                _wings.Add(new WingRig
                {
                    Shoulder = shoulder,
                    Wrist = wrist,
                    Side = w,
                    ShoulderRest = shoulder.localRotation,
                    WristRest = wrist.localRotation,
                });
            }
        }

        /// <summary>A tapering tail as a chain of nested pivots, so the beat travels outward as a wave instead
        /// of the whole tail swinging as one rigid box.</summary>
        private void AddTail(Transform parent, Vector3 basePos, float length, float thick, int links, Material mat)
        {
            float linkLen = length / Mathf.Max(1, links);
            var chainParent = parent;
            for (int i = 0; i < links; i++)
            {
                float w = thick * (1f - 0.22f * i);
                var seg = NewPivot(chainParent, "Tail" + i, i == 0 ? basePos : new Vector3(0f, 0f, -linkLen));
                AddPartTo(seg, "TailSeg" + i, new Vector3(0f, 0f, -linkLen * 0.5f), new Vector3(w, w, linkLen), mat);
                _tailChain.Add(seg);
                chainParent = seg;
            }
        }

        /// <summary>A chain of nested, shrinking pivots — the shape every soft appendage on a creature wants:
        /// tentacles, an elephant's trunk, a medusa's rim arms. Returns the chain, root first.</summary>
        private Transform[] AddChain(Transform parent, string name, Vector3 basePos, float zDrift,
            int links, float width, float linkLen, float taper, Material mat)
        {
            var chain = new Transform[links];
            var chainParent = parent;
            float prevLen = 0f;
            for (int i = 0; i < links; i++)
            {
                float shrink = 1f - taper * i;
                float w = width * shrink;
                float h = linkLen * shrink;
                var seg = NewPivot(chainParent, name + i, i == 0 ? basePos : new Vector3(0f, -prevLen, zDrift));
                AddPartTo(seg, name + "Seg" + i, new Vector3(0f, -h * 0.5f, 0f), new Vector3(w, h, w), mat);
                chain[i] = seg;
                chainParent = seg;
                prevLen = h;
            }

            return chain;
        }

        /// <summary>How much of a leg is thigh; the rest is shin. Slightly over half, as in most animals (and
        /// in <see cref="PlayerAvatar"/>, whose legs this rig now matches).</summary>
        private const float UpperLegShare = 0.55f;

        /// <summary>A bare pivot (no geometry) — the hinge other parts hang from.</summary>
        private static Transform NewPivot(Transform parent, string partName, Vector3 localPos)
        {
            var pivot = new GameObject(partName).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = localPos;
            return pivot;
        }

        /// <summary>Splits the head box into a fixed upper skull and a hinged lower jaw, so a vocalising
        /// creature opens its mouth. The two parts together occupy exactly the volume the single head cube
        /// used to, and the hinge sits at the jaw's REAR so it swings open like a jaw rather than sliding.</summary>
        private void AddHeadBox(float w, float h, float d, float headZ, Material mat)
        {
            const float JawShare = 0.3f; // the lower 30 % of the head is jaw
            AddPartTo(_headPivot, "HeadUpper", new Vector3(0f, h * JawShare * 0.5f, headZ),
                new Vector3(w, h * (1f - JawShare), d), mat);
            _jawPivot = AddPivotPart(_headPivot, "Jaw",
                new Vector3(0f, -h * (0.5f - JawShare * 0.5f), headZ - d * 0.44f),
                new Vector3(0f, 0f, d * 0.44f), new Vector3(w * 0.92f, h * JawShare, d * 0.88f), mat);
        }

        /// <summary>An eyelid: a skin-coloured box over the eye, held at zero height (invisible) and scaled up
        /// to cover it for a blink. Cheap — one cube per eye — and blinking is out of all proportion to its
        /// cost for making a body read as alive rather than as a prop.</summary>
        private void AddEyelid(Vector3 eyePos, float eyeSize, Material mat)
        {
            var lid = NewPivot(_headPivot, "Eyelid" + _eyelids.Count, eyePos);
            AddPartTo(lid, "EyelidBox", Vector3.zero, new Vector3(eyeSize * 1.12f, eyeSize * 1.12f, eyeSize * 1.12f), mat);
            lid.localScale = new Vector3(1f, 0f, 1f); // open
            _eyelids.Add(lid);
        }

        /// <summary>Body half-width multiplier at a point along the spine. The segments taper toward the head,
        /// so a hip under the shoulders sits narrower than one at the hips — without this the flank offset is
        /// wrong for every body with more than one segment.</summary>
        private static float TaperAt(float z, float segLen, int segments)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(z / Mathf.Max(0.01f, segLen) + (segments - 1) * 0.5f), 0, segments - 1);
            return 1f - 0.12f * i;
        }

        /// <summary>Builds one jointed leg — hip → thigh → knee → shin → foot — and records the identity the
        /// gait needs (side, row, segment lengths, which way the knee folds). A single rigid stick can only
        /// pendulum; the knee is what lets the leg shorten to clear the ground on the swing and straighten to
        /// carry weight on the plant, and the foot is what stops the leg ending in a cut-off pole.</summary>
        private LegRig AddLeg(Transform parent, int row, int side, int rows, Vector3 hipPos, float legLen, float thick, Material mat)
        {
            float upper = legLen * UpperLegShare;
            float lower = legLen - upper;

            var hip = NewPivot(parent, (side == 0 ? "LegL" : "LegR") + row, hipPos);
            AddPartTo(hip, "Thigh", new Vector3(0f, -upper * 0.5f, 0f), new Vector3(thick, upper, thick), mat);

            var knee = NewPivot(hip, "Knee", new Vector3(0f, -upper, 0f));
            AddPartTo(knee, "Shin", new Vector3(0f, -lower * 0.5f, 0f), new Vector3(thick * 0.85f, lower, thick * 0.85f), mat);

            var foot = NewPivot(knee, "Foot", new Vector3(0f, -lower, 0f));
            AddPartTo(foot, "Sole", new Vector3(0f, -thick * 0.26f, thick * 0.3f),
                new Vector3(thick * 1.4f, thick * 0.52f, thick * 1.8f), mat);

            return new LegRig
            {
                Hip = hip,
                Knee = knee,
                Foot = foot,
                Side = side,
                Row = row,
                Rows = rows,
                UpperLen = upper,
                LowerLen = lower,
                // Positive folds the shin BACKWARDS (a +X rotation swings a downward-hanging segment toward
                // -Z). Fore-limbs fold back like our elbow, hind-limbs forward like a stifle — a quadruped
                // whose knees all bend the same way reads as a table, not an animal. Many-legged bodies fold
                // uniformly back and stand splayed instead.
                KneeSign = rows >= 3 ? 1 : row == 0 && rows >= 2 ? 1 : -1,
                HipRest = hip.localRotation,
                HipRestPos = hip.localPosition,
            };
        }

        /// <summary>Assembles the hand-over to the animator from whatever this build path collected.</summary>
        private RigDescription Describe(NetCreature c, Transform body, float unit, float legLength, int idHash)
            => new RigDescription
            {
                Legs = _legs.ToArray(),
                Wings = _wings.ToArray(),
                Tail = _tailChain.ToArray(),
                Neck = _neckChain.ToArray(),
                Tentacles = _tentacleChains.ToArray(),
                Trunk = _trunkChain.ToArray(),
                Fins = _fins.ToArray(),
                Head = _headPivot,
                Jaw = _jawPivot,
                Eyelids = _eyelids.ToArray(),
                Ears = _ears.ToArray(),
                Body = body,
                Hostile = c.Hostile,
                Asleep = c.Asleep,
                Aquatic = c.Habitat == "Water" || c.Habitat == "Amphibian",
                Temperament = c.Temperament ?? string.Empty,
                BodyPlan = c.BodyPlan ?? "Standard",
                Size = c.Size,
                LegCount = Mathf.Clamp(c.Legs, 0, 8),
                Giant = c.BodyPlan == "Titan" || c.Size >= CreatureMotion.GiantSize,
                LegLength = legLength,
                Unit = unit,
                IdHash = idHash,
                Ground = Ground,
            };

        /// <summary>Adds a part on its own pivot (hinge) so it can be rotated for animation. The cube hangs
        /// at <paramref name="cubeOffset"/> from the pivot; returns the pivot transform.</summary>
        private Transform AddPivotPart(GameObject root, string partName, Vector3 pivotPos, Vector3 cubeOffset, Vector3 scale, Material mat)
            => AddPivotPart(root.transform, partName, pivotPos, cubeOffset, scale, mat);

        /// <summary>Pivot overload for parts that hang off another pivot (a knee under a hip, a neck segment
        /// under the one below it) rather than off a build root.</summary>
        private Transform AddPivotPart(Transform parent, string partName, Vector3 pivotPos, Vector3 cubeOffset, Vector3 scale, Material mat)
        {
            var pivot = new GameObject(partName).transform;
            pivot.SetParent(parent, false);
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

        // #1514: creature materials are never mutated after creation, so identical (shader, colour, texture)
        // requests share ONE Material — every build used to create 8–16 fresh Materials (each behind a
        // Shader.Find string lookup) that nothing ever destroyed, growing for the whole session as fauna
        // spawned and despawned. Shared materials also let the SRP batcher group the parts.
        private static Shader _unlitShader, _litShader;
        private static readonly Dictionary<Color, Material> UnlitCache = new Dictionary<Color, Material>();
        private static readonly Dictionary<(Color Color, Texture2D Tex), Material> LitCache = new Dictionary<(Color, Texture2D), Material>();

        private static Material Unlit(Color color)
        {
            if (UnlitCache.TryGetValue(color, out var cached) && cached != null)
            {
                return cached;
            }

            _unlitShader ??= Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            var m = new Material(_unlitShader) { color = ShaderColor.Srgb(color) };
            UnlitCache[color] = m;
            return m;
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
            var key = (color, tex);
            if (LitCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            _litShader ??= Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(_litShader) { color = ShaderColor.Srgb(color) };
            if (tex != null)
            {
                m.mainTexture = tex;
            }

            if (m.HasProperty("_Floor"))
            {
                m.SetFloat("_Floor", CreatureFloor); // no-op on the Unlit/Color fallback
                m.SetFloat("_Fill", CreatureFill);
            }

            LitCache[key] = m;
            return m;
        }
    }
}
