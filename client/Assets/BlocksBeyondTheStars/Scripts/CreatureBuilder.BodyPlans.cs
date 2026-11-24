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
    /// The body plans beyond the standard quadruped: the medusa, the titan, the arachnid (with its eye cluster), the biped,
    /// the worm and the ray. Each is picked by <see cref="Build"/> from the species' body plan and rigs its own parts.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        /// <summary>The medusa plan (#637): a translucent pulsing bell (Cloud shader — alpha-blended and
        /// always included) over an opaque nucleus, with long tapering tentacles hanging from the bell rim
        /// in a circle. No head, no legs — the drift and the pulse do the talking.</summary>
        private void BuildMedusa(GameObject root, NetCreature c)
        {
            float unit = 0.5f * Mathf.Clamp(c.Size, 0.4f, 3f);
            var mood = MoodOf(c, 0f, true, true); // no red cast: a medusa's bell carries no mood
            Color baseColor = mood.Tint(Rgb(c.ColorRgb)) * mood.Dim;
            NewCoat(c, mood, 1.6f, 1.4f);
            _bodyMat = _coat.Mat(CoatPart.Torso);
            var bellyMat = _coat.Mat(CoatPart.Ornament); // the rim tentacles: the accent colour

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
                var eyeMat = EyeFaceMaterial(c);
                float eyeSize = bellR * 0.22f;
                for (int e = 0; e < eyes; e++)
                {
                    float fx = eyes == 1 ? 0f : (e == 0 ? -bellR * 0.18f : bellR * 0.18f);
                    var pos = new Vector3(fx, -bellR * 0.15f, bellR * 0.32f);
                    AddEye(bell.transform, "Eye" + e, pos, eyeSize, Vector3.forward, eyeMat);
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
                FxLightBridge.Mirror(_glow); // #2151: a glowing creature lights its surroundings
                _glow.type = LightType.Point;
                _glow.range = unit * 6f;
                _glow.intensity = 1.1f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }

            // No fish-weave and no gait — the bell pulse carries all of this body's motion.
            var rig = Describe(c, body.transform, unit, unit, CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed));
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
            NewCoat(c, MoodOf(c, 0.25f, true, true), 1.6f, 1.4f);
            _bodyMat = _coat.Mat(CoatPart.Torso);
            var bellyMat = _coat.Mat(CoatPart.Belly);

            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
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
                var segPos = new Vector3(0f, bodyY, z);
                var segSize = new Vector3(unit * 1.25f * taper * bodyWide, unit * 1.05f * taper, segLen);
                AddPart(body, "Body" + i, segPos, segSize, _bodyMat);
                _torsoBoxes.Add((segPos, segSize));
            }

            AddPart(body, "Belly", new Vector3(0f, bodyY - unit * 0.5f, 0f),
                new Vector3(unit * 1.15f * bodyWide, unit * 0.35f, segments * segLen * 0.9f), bellyMat);
            AddCoatOverlays(body.transform);

            // Pillar legs, row 0 at the front (the shared convention). The hips sit at the torso's real
            // half-width so a broad titan carries its pillars under the flanks, not under the belly.
            for (int row = 0; row < 2; row++)
            {
                float z = (row == 0 ? 1f : -1f) * segLen * (segments - 1) * 0.5f;
                float hipX = unit * 0.62f * bodyWide * TaperAt(z, segLen, segments);
                _legs.Add(AddLeg(body.transform, row, 0, 2, new Vector3(-hipX, legH, z), legH, unit * 0.34f));
                _legs.Add(AddLeg(body.transform, row, 1, 2, new Vector3(hipX, legH, z), legH, unit * 0.34f));
            }

            // Neck: stacked shrinking segments rising forward from the torso front; the head pivots at its top,
            // so the existing graze gesture becomes a giraffe lowering its neck — for free.
            float frontZ = (segments - 1) * 0.5f * segLen + segLen * 0.55f;
            // Nested pivots, so lowering the head actually lowers the NECK. As a static stack the graze
            // gesture could only nod the head at the top of a rigid column — a giraffe that cannot reach the
            // ground. The chain distributes the gesture and the animal really bends down.
            // A hydra (#1780) grows one neck per head, fanned out from the shoulders.
            int neck = Mathf.Clamp(c.NeckLength, 0, 3);
            int heads = Mathf.Clamp(c.Heads, 1, 3);
            for (int hd = 0; hd < heads; hd++)
            {
                float fan = heads == 1 ? 0f : Mathf.Lerp(-1f, 1f, hd / (float)(heads - 1));
                float xOff = fan * unit * 0.5f * bodyWide;
                float yaw = fan * 24f;
                var neckParent = body.transform;
                float headY = bodyY + unit * 0.25f;
                float headZ = frontZ - unit * 0.35f;
                for (int nk = 0; nk < neck; nk++)
                {
                    float taper = 1f - 0.15f * nk;
                    var seg = NewPivot(neckParent, "Neck" + nk + (heads == 1 ? "" : "_" + hd), nk == 0
                        ? new Vector3(xOff, headY, headZ - unit * 0.05f)
                        : new Vector3(0f, unit * 0.62f, unit * 0.18f));
                    if (nk == 0)
                    {
                        seg.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    }

                    AddPartTo(seg, "NeckSeg" + nk, new Vector3(0f, unit * 0.34f, unit * 0.09f),
                        new Vector3(unit * 0.55f * taper, unit * 0.75f, unit * 0.55f * taper), _coat.Mat(CoatPart.Neck));
                    _neckChain.Add(seg);
                    neckParent = seg;
                    headY += unit * 0.62f;
                    headZ += unit * 0.18f;
                }

                _headPivot = NewPivot(neckParent, heads == 1 ? "Head" : "Head" + hd, neck > 0
                    ? new Vector3(0f, unit * 0.82f, unit * 0.23f)
                    : new Vector3(xOff, headY + unit * 0.2f, headZ));
                if (neck == 0)
                {
                    _headPivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }

                AddHeadBox(unit * 0.95f * headScale, unit * 0.85f * headScale, unit * 0.9f * headScale, unit * 0.45f);

                AddEyes(c, unit, headScale);

                // Ears: two flat slabs at the head sides — the elephant read, and scale-scaffolding for the eye.
                // On pivots at the top edge, so they can flick on a long idle.
                for (int e = 0; e < 2; e++)
                {
                    float ex = (e == 0 ? -1f : 1f) * unit * 0.55f * headScale;
                    _ears.Add(AddPivotPart(_headPivot, e == 0 ? "EarL" : "EarR",
                        new Vector3(ex, unit * 0.5f * headScale, unit * 0.2f),
                        new Vector3(0f, -unit * 0.3f * headScale, 0f),
                        new Vector3(unit * 0.12f, unit * 0.6f * headScale, unit * 0.5f * headScale), _coat.Mat(CoatPart.Wing)));
                }

                // Tusks: the species' horns, worn forward from the lower jaw instead of upright on the crown.
                int tusks = Mathf.Clamp(c.Horns, 0, 4);
                if (tusks > 0)
                {
                    var tuskMat = TuskMaterial(c); // ivory (#2867: a tone per species)
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
                        4, unit * 0.3f, unit * 0.42f, 0.17f, _coat.Mat(CoatPart.Neck)));
                }
            }

            if (c.HasTail)
            {
                float tailBaseZ = -(segments - 1) * 0.5f * segLen - segLen * 0.15f;
                AddTail(body.transform, new Vector3(0f, bodyY + unit * 0.2f, tailBaseZ), segLen * 1.0f, unit * 0.22f, 4, _coat.Mat(CoatPart.Tail));
            }

            if (c.Glows)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(body.transform, false);
                go.transform.localPosition = new Vector3(0f, bodyY, 0f);
                _glow = go.AddComponent<Light>();
                FxLightBridge.Mirror(_glow); // #2151: a glowing creature lights its surroundings
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

        /// <summary>The arachnid (#2009): a speeder-sized eight-legger — a cephalothorax on eight splayed jointed legs (four
        /// rows), a rounder abdomen behind it, a head that is a box or one of the pyramid family
        /// (<see cref="ArachnidHeadMesh"/>), spider eye clusters on the head's front face or slope, fangs. The thorax,
        /// abdomen and head keep their colliders on the giants' layer (#1998) so the player bumps into the body and hits
        /// it where the ray meets it; the legs stay render-only so nobody gets wedged between a shin and the belly.</summary>
        private void BuildArachnid(GameObject root, NetCreature c)
        {
            float unit = 0.5f * Mathf.Clamp(c.Size, 0.4f, 8f);
            NewCoat(c, MoodOf(c, 0.25f, true, true), 1.6f, 1.4f);
            _bodyMat = _coat.Mat(CoatPart.Torso);
            var bellyMat = _coat.Mat(CoatPart.Belly);

            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            float headScale = 0.85f + ((idh >> 2) & 7) / 7f * 0.4f;  // 0.85..1.25
            float bodyWide = 0.9f + ((idh >> 5) & 7) / 7f * 0.35f;   // 0.9..1.25
            float legLong = 0.9f + ((idh >> 8) & 7) / 7f * 0.35f;    // 0.9..1.25

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);

            // The hip height IS the leg length (the shared convention): the shins fold to meet the ground.
            float legH = unit * 1.15f * legLong;
            float bodyY = legH + unit * 0.2f;                        // the thorax rides just above the hips
            float thoraxW = unit * 2.0f * bodyWide, thoraxH = unit * 1.2f, thoraxL = unit * 1.6f;
            float abdomenL = unit * 2.0f;
            float abdomenZ = -(thoraxL * 0.5f + abdomenL * 0.42f);

            _keepColliders = true;
            AddPart(body, "Thorax", new Vector3(0f, bodyY, 0f), new Vector3(thoraxW, thoraxH, thoraxL), _bodyMat);
            // The abdomen: a sphere behind the thorax, a little higher and a little wider — the spider read.
            AddPartTo(body.transform, "Abdomen", new Vector3(0f, bodyY + unit * 0.15f, abdomenZ),
                new Vector3(unit * 2.3f * bodyWide, unit * 1.7f, abdomenL), _bodyMat, PrimitiveType.Sphere);
            _keepColliders = false;
            AddPart(body, "Belly", new Vector3(0f, bodyY - thoraxH * 0.42f, 0f),
                new Vector3(thoraxW * 0.9f, unit * 0.3f, thoraxL * 0.9f), bellyMat);
            // A belly patch under the abdomen too, so the two-tone reads from every side.
            AddPartTo(body.transform, "AbdomenBelly", new Vector3(0f, bodyY - unit * 0.5f, abdomenZ),
                new Vector3(unit * 1.6f * bodyWide, unit * 0.4f, abdomenL * 0.7f), bellyMat, PrimitiveType.Sphere);

            if (c.HasCrest)
            {
                // A dorsal ridge of small plates along the abdomen, in the belly tone.
                for (int i = 0; i < 4; i++)
                {
                    float z = -(thoraxL * 0.5f + abdomenL * (0.15f + 0.18f * i));
                    AddPart(body, "Crest" + i, new Vector3(0f, bodyY + unit * (0.95f - 0.08f * i), z),
                        new Vector3(unit * 0.16f, unit * 0.32f, unit * 0.22f), _coat.Mat(CoatPart.Ornament));
                }
            }

            // Eight legs in four rows on the thorax flanks, row 0 at the front (the shared convention). The crawler
            // stance splays them 22° outward (PoseLegs), so the span reaches well past the body: the spider silhouette.
            const int Rows = 4;
            float hipX = thoraxW * 0.5f;
            float legThick = unit * 0.2f;
            for (int row = 0; row < Rows; row++)
            {
                float z = Mathf.Lerp(thoraxL * 0.42f, -thoraxL * 0.42f, row / (float)(Rows - 1));
                _legs.Add(AddLeg(body.transform, row, 0, Rows, new Vector3(-hipX, legH, z), legH, legThick));
                _legs.Add(AddLeg(body.transform, row, 1, Rows, new Vector3(hipX, legH, z), legH, legThick));
            }

            // The head on a neck pivot in front of the thorax: the classic box with its hinged jaw, or a pyramid mesh
            // (apex up, its base a little below the pivot so the eyes on its slope sit at eye height).
            float headW = unit * 1.0f * headScale, headD = unit * 0.9f * headScale, headH = unit * 0.9f * headScale;
            float headZ = unit * 0.45f;
            _headPivot = NewPivot(body.transform, "Head", new Vector3(0f, bodyY + unit * 0.1f, thoraxL * 0.5f + unit * 0.1f));
            var shape = ParseHeadShape(c.HeadShape);
            _keepColliders = true;
            if (ArachnidRules.IsPyramid(shape))
            {
                headH *= ArachnidRules.HeightScale(shape);
                AddMeshPart(_headPivot, "HeadPyramid", new Vector3(0f, -headH * 0.5f, headZ),
                    new Vector3(headW, headH, headD), _coat.Mat(CoatPart.Jaw), ArachnidHeadMesh.For(shape)); // a mesh without UVs: never patterned
                _heads.Add(_headPivot);
            }
            else
            {
                AddHeadBox(headW, headH, headD, headZ);
            }

            _keepColliders = false;
            AddArachnidEyes(c, unit, headScale, headW, headH, headD, headZ, shape);

            // Fangs (the species' horns, worn as chelicerae): two dark spikes hanging from the head's front, angled forward.
            if (c.Horns >= 2)
            {
                var fangMat = DarkHornMaterial(c);
                for (int f = 0; f < 2; f++)
                {
                    float x = (f == 0 ? -1f : 1f) * headW * 0.22f;
                    var fang = NewPivot(_headPivot, "Fang" + f, new Vector3(x, -headH * 0.45f, headZ + headD * 0.35f));
                    fang.localRotation = Quaternion.Euler(-28f, 0f, (f == 0 ? 1f : -1f) * 8f);
                    AddPartTo(fang, "FangBox", new Vector3(0f, -unit * 0.22f, 0f), new Vector3(unit * 0.13f, unit * 0.45f, unit * 0.13f), fangMat);
                    AddPartTo(fang, "FangTip", new Vector3(0f, -unit * 0.5f, 0f), new Vector3(unit * 0.08f, unit * 0.14f, unit * 0.08f), fangMat);
                }
            }

            if (c.Glows)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(body.transform, false);
                go.transform.localPosition = new Vector3(0f, bodyY, 0f);
                _glow = go.AddComponent<Light>();
                FxLightBridge.Mirror(_glow); // #2151: a glowing creature lights its surroundings
                _glow.type = LightType.Point;
                _glow.range = unit * 6f;
                _glow.intensity = 1.1f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }

            MakeGiantBody(root); // #1998: a kinematic compound on the giants' layer — the player bumps into the body

            var rig = Describe(c, body.transform, unit, legH, idh);
            rig.Aquatic = false;
            rig.LeggedCrawler = true; // a crawler by leg count, but it walks — no beetle weave, full stride
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
            anim.CadenceScale = Mathf.Clamp(1.15f / Mathf.Max(1f, c.Size * 0.35f), 0.6f, 1f);
        }

        /// <summary>The biped plan (#2081, the school club idea of Paul and Ben — Mini-Michi-Paul): an upright two-legger with two
        /// arms and a big head. Built from the same shares the server measures its body height with (<see cref="BipedRules"/>):
        /// two jointed legs under a stubby torso, a belly patch on the front, arms from the shoulders (shoulder → elbow → hand),
        /// and the big head on a neck pivot at the top of the torso — the head is the classic box (skull + hinged jaw), so the
        /// shared eye, eyelid, jaw and gaze code all work on it unchanged. Optional antennae (the species' horns) and a stubby
        /// tail. Small and peaceful: render-only, no colliders.</summary>
        private void BuildBiped(GameObject root, NetCreature c)
        {
            float s = Mathf.Clamp(c.Size, 0.3f, 2.5f);                      // the size IS the standing scale in blocks
            float headRatio = c.HeadRatio > 0f ? c.HeadRatio : 1.6f;
            float unit = 0.5f * s;
            NewCoat(c, MoodOf(c, 0f, true, true), 1.6f, 1.4f); // small and peaceful: no red cast
            _bodyMat = _coat.Mat(CoatPart.Torso);
            var bellyMat = _coat.Mat(CoatPart.Belly);
            var accentMat = _coat.Mat(CoatPart.Ornament);
            var limbMat = _coat.Mat(CoatPart.Limb);

            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            float bodyWide = 0.9f + ((idh >> 5) & 7) / 7f * 0.25f;          // 0.9..1.15 — chubby or slim

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);

            float legH = s * BipedRules.LegShare;                            // hip height = leg length (the shared convention)
            float torsoH = s * BipedRules.TorsoShare;
            float torsoW = s * 0.40f * bodyWide, torsoD = s * 0.30f;
            float torsoY = legH + torsoH * 0.5f;
            AddPart(body, "Torso", new Vector3(0f, torsoY, 0f), new Vector3(torsoW, torsoH, torsoD), _bodyMat);
            AddPartTo(body.transform, "Belly", new Vector3(0f, torsoY - torsoH * 0.08f, torsoD * 0.5f),
                new Vector3(torsoW * 0.7f, torsoH * 0.7f, s * 0.03f), bellyMat);
            // A little hip block, so the legs hang from a pelvis rather than from the torso's corners.
            AddPartTo(body.transform, "Hips", new Vector3(0f, legH + s * 0.02f, 0f), new Vector3(torsoW * 0.85f, s * 0.07f, torsoD * 0.9f), _bodyMat);

            // Two legs, one row; a biped's knee folds the shin BACK (like ours), unlike a quadruped's hind leg.
            float legThick = s * 0.11f;
            for (int side = 0; side < 2; side++)
            {
                float x = (side == 0 ? -1f : 1f) * torsoW * 0.26f;
                var leg = AddLeg(body.transform, 0, side, 1, new Vector3(x, legH, 0f), legH, legThick);
                leg.KneeSign = 1;
                _legs.Add(leg);
            }

            // Two arms from the shoulders: upper arm → elbow → forearm → a hand in the belly tone.
            float armLen = s * BipedRules.ArmShare;
            float armThick = s * 0.08f;
            float upper = armLen * 0.5f, lower = armLen * 0.5f;
            var handMat = accentMat;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                var shoulder = NewPivot(body.transform, side == 0 ? "ArmL" : "ArmR",
                    new Vector3(sx * (torsoW * 0.5f + armThick * 0.55f), legH + torsoH * 0.88f, 0f));
                AddPartTo(shoulder, "UpperArm", new Vector3(0f, -upper * 0.5f, 0f), new Vector3(armThick, upper, armThick), limbMat);
                var elbow = NewPivot(shoulder, "Elbow", new Vector3(0f, -upper, 0f));
                AddPartTo(elbow, "Forearm", new Vector3(0f, -lower * 0.5f, 0f), new Vector3(armThick * 0.9f, lower, armThick * 0.9f), limbMat);
                AddPartTo(elbow, "Hand", new Vector3(0f, -lower - armThick * 0.45f, 0f), Vector3.one * (armThick * 1.35f), handMat);
                shoulder.localRotation = Quaternion.Euler(0f, 0f, -sx * 6f);    // the arms hang a touch away from the body
                _arms.Add(new ArmRig
                {
                    Shoulder = shoulder,
                    Elbow = elbow,
                    Side = side,
                    ShoulderRest = shoulder.localRotation,
                    ElbowRest = elbow.localRotation,
                });
            }

            // The big head on a neck pivot at the top of the torso. The classic head box is built around a centre pivot
            // lifted by half the head (so the head sits ON the torso and turns about the neck); its "unit" is chosen so the box
            // is exactly the head edge the server measured.
            float headEdge = BipedRules.HeadEdge(s, headRatio);
            float hu = headEdge / 0.9f;                                        // the standard head is 0.9 units wide
            var neck = NewPivot(body.transform, "Head", new Vector3(0f, legH + torsoH, 0f));
            _headPivot = NewPivot(neck, "HeadCentre", new Vector3(0f, headEdge * 0.47f, -hu * 0.45f + torsoD * 0.05f));
            AddHeadBox(hu * 0.9f, hu * 0.85f, hu * 0.8f, hu * 0.45f);
            _heads[_heads.Count - 1] = neck;                                   // the animator turns the neck, the face comes along
            AddEyes(c, hu, 1f);

            // Antennae (the species' horns): thin stalks with a bobble, the belly tone on top.
            int antennae = Mathf.Clamp(c.Horns, 0, 2);
            for (int a = 0; a < antennae; a++)
            {
                float ax = antennae == 1 ? 0f : (a == 0 ? -1f : 1f) * hu * 0.22f;
                var stalk = NewPivot(_headPivot, "Antenna" + a, new Vector3(ax, hu * 0.36f, hu * 0.40f));
                stalk.localRotation = Quaternion.Euler(-12f, 0f, antennae == 1 ? 0f : (a == 0 ? 14f : -14f));
                AddPartTo(stalk, "Stalk", new Vector3(0f, hu * 0.22f, 0f), new Vector3(hu * 0.05f, hu * 0.44f, hu * 0.05f), _coat.Mat(CoatPart.Jaw));
                AddPartTo(stalk, "Bobble", new Vector3(0f, hu * 0.46f, 0f), Vector3.one * (hu * 0.13f), accentMat, PrimitiveType.Sphere);
            }

            if (c.HasTail)
            {
                AddTail(body.transform, new Vector3(0f, legH + s * 0.04f, -torsoD * 0.45f), s * 0.22f, s * 0.07f, 2, _coat.Mat(CoatPart.Tail));
            }

            if (c.Glows)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(body.transform, false);
                go.transform.localPosition = new Vector3(0f, torsoY, 0f);
                _glow = go.AddComponent<Light>();
                FxLightBridge.Mirror(_glow); // #2151: a glowing creature lights its surroundings
                _glow.type = LightType.Point;
                _glow.range = s * 4f;
                _glow.intensity = 1.0f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }

            var rig = Describe(c, body.transform, unit, legH, idh);
            rig.Arms = _arms.ToArray();
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
        }

        /// <summary>The worm (#2109, the sandworms had legs): a legless slitherer — the classic box head
        /// (skull, hinged jaw, eyes, optional antennae) on a low pivot at the front, and behind it a chain of
        /// <c>BodySegments</c> tapering links built with <see cref="AddTail"/> and handed to the animator AS the tail: its beat
        /// already travels link by link (each lags the one before), and a legless land crawler "undulates", so the whole body
        /// runs the wave that is the slither — no new animator code. A belly stripe under the front links, a dorsal ridge when
        /// the species has a crest. Small: render-only, no colliders.</summary>
        private void BuildWorm(GameObject root, NetCreature c)
        {
            float unit = 0.5f * Mathf.Clamp(c.Size, 0.4f, 3f);
            NewCoat(c, MoodOf(c, 0.25f, true, true), 1.6f, 1.4f);
            _bodyMat = _coat.Mat(CoatPart.Torso);
            var bellyMat = _coat.Mat(CoatPart.Belly);
            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            float girth = unit * (0.55f + ((idh >> 5) & 7) / 7f * 0.25f);   // 0.55..0.8 units — slim or plump
            float bodyY = girth * 0.55f;                                      // lies on the ground

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);

            // The head: a blunt box on a low pivot at the front — AddHeadBox registers it, so the gesture / gaze / jaw / eye
            // code works on it unchanged; the horns are antennae with a bobble, like the biped's.
            float headScale = 0.9f;
            _headPivot = NewPivot(body.transform, "Head", new Vector3(0f, bodyY + unit * 0.05f, girth * 0.7f));
            AddHeadBox(unit * 0.8f * headScale, unit * 0.7f * headScale, unit * 0.75f * headScale, unit * 0.4f);
            AddEyes(c, unit, headScale);
            int antennae = Mathf.Clamp(c.Horns, 0, 2);
            for (int a = 0; a < antennae; a++)
            {
                float ax = antennae == 1 ? 0f : (a == 0 ? -1f : 1f) * unit * 0.2f;
                var stalk = NewPivot(_headPivot, "Antenna" + a, new Vector3(ax, unit * 0.3f, unit * 0.35f));
                stalk.localRotation = Quaternion.Euler(-14f, 0f, antennae == 1 ? 0f : (a == 0 ? 14f : -14f));
                AddPartTo(stalk, "Stalk", new Vector3(0f, unit * 0.2f, 0f), new Vector3(unit * 0.05f, unit * 0.4f, unit * 0.05f), _coat.Mat(CoatPart.Jaw));
                AddPartTo(stalk, "Bobble", new Vector3(0f, unit * 0.42f, 0f), Vector3.one * (unit * 0.12f), _coat.Mat(CoatPart.Ornament), PrimitiveType.Sphere);
            }

            // The body: the chain of links, root link right behind the head, tapering to the tail tip. Its total length grows
            // with the link count, so a twelve-link worm is a long one.
            int links = Mathf.Clamp(c.BodySegments, 6, 12);
            float length = unit * 0.3f * links;
            AddTail(body.transform, new Vector3(0f, bodyY, girth * 0.2f), length, girth, links, _coat.Mat(CoatPart.Tail)); // the body IS the tail chain: tail rings band the whole worm

            // A paler belly stripe and, on a crested species, a dorsal ridge — both on the first few links so the ridge rides
            // the wave with the body.
            for (int i = 0; i < Mathf.Min(_tailChain.Count, 4); i++)
            {
                float w = girth * (1f - 0.22f * i);
                float linkLen = length / links;
                AddPartTo(_tailChain[i], "Belly" + i, new Vector3(0f, -w * 0.42f, -linkLen * 0.5f), new Vector3(w * 0.8f, w * 0.14f, linkLen * 0.95f), bellyMat);
                if (c.HasCrest)
                {
                    AddPartTo(_tailChain[i], "Ridge" + i, new Vector3(0f, w * 0.52f, -linkLen * 0.5f), new Vector3(w * 0.18f, w * 0.22f, linkLen * 0.7f), _coat.Mat(CoatPart.Ornament));
                }
            }

            if (c.Glows)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(body.transform, false);
                go.transform.localPosition = new Vector3(0f, bodyY, 0f);
                _glow = go.AddComponent<Light>();
                FxLightBridge.Mirror(_glow); // #2151: a glowing creature lights its surroundings
                _glow.type = LightType.Point;
                _glow.range = unit * 4f;
                _glow.intensity = 1.0f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }

            var rig = Describe(c, body.transform, unit, girth, idh);
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
        }

        /// <summary>Spider eye clusters (#2009): up to eight eyes in two rows on the head's front — on the face of a box head,
        /// or ON the front slope of a pyramid, each pushed out along the slope's normal so none sinks into the mesh (the
        /// flowerling's face was lost inside its head exactly that way, #1997). Every eye gets its eyelid, so the cluster blinks.</summary>
        private void AddArachnidEyes(NetCreature c, float unit, float headScale, float headW, float headH, float headD, float headZ, CreatureHeadShape shape)
        {
            int eyes = Mathf.Clamp(c.Eyes, 0, 8);
            if (eyes <= 0)
            {
                return;
            }

            var eyeMat = EyeFaceMaterial(c);
            float eyeSize = unit * 0.26f * headScale * (eyes > 4 ? 0.8f : 1f);
            int rows = eyes <= 2 ? 1 : 2;
            int topRow = rows == 1 ? eyes : Mathf.CeilToInt(eyes / 2f);
            bool pyramid = ArachnidRules.IsPyramid(shape);
            for (int e = 0; e < eyes; e++)
            {
                int row = e < topRow ? 0 : 1;
                int inRow = row == 0 ? topRow : eyes - topRow;
                int idx = row == 0 ? e : e - topRow;
                float spread = headW * (inRow > 2 ? 0.34f : 0.22f);
                float x = inRow == 1 ? 0f : Mathf.Lerp(-spread, spread, idx / (float)(inRow - 1));
                Vector3 pos, normal;
                if (pyramid)
                {
                    // Where the front slope is at this height, and which way it faces: the footprint's change over a
                    // small step of height gives the slope, the normal is perpendicular to it (out and up).
                    float y01 = rows == 1 ? 0.42f : row == 0 ? 0.5f : 0.28f;
                    float foot = ArachnidRules.FootprintAt(shape, y01);
                    float dz = (ArachnidRules.FootprintAt(shape, Mathf.Min(1f, y01 + 0.05f)) - ArachnidRules.FootprintAt(shape, Mathf.Max(0f, y01 - 0.05f))) * headD * 0.5f;
                    float dy = 0.1f * headH;
                    normal = new Vector3(0f, -dz, dy).normalized;
                    pos = new Vector3(x, -headH * 0.5f + headH * y01, headZ + headD * 0.5f * foot);
                }
                else
                {
                    float y = rows == 1 ? headH * 0.12f : row == 0 ? headH * 0.22f : -headH * 0.02f;
                    pos = new Vector3(x, y, headZ + headD * 0.5f);
                    normal = Vector3.forward;
                }

                pos += normal * (eyeSize * 0.35f);
                AddEye(_headPivot, "Eye" + e, pos, eyeSize, normal, eyeMat);
                AddEyelid(pos, eyeSize, _coat.Mat(CoatPart.Jaw));
            }
        }

        /// <summary>The ray plan (#1778): a flat disc on one pair of wing panels — each side a chain of three panels
        /// along the span, so the animator can run a travelling wave outward the way a real ray's fin edge ripples —
        /// a long whip tail, eyes on the top surface, and on some species a pair of cephalic lobes at the front.
        /// The same body swims along the sea bed and, on an Air species, glides through the sky.</summary>
        private void BuildRay(GameObject root, NetCreature c)
        {
            float unit = 0.5f * Mathf.Clamp(c.Size, 0.4f, 3.5f);
            NewCoat(c, MoodOf(c, 0.25f, true, true), 1.6f, 1.4f);
            _bodyMat = _coat.Mat(CoatPart.Torso);
            var bellyMat = _coat.Mat(CoatPart.Belly);

            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            float wide = 0.9f + ((idh >> 5) & 7) / 7f * 0.4f; // 0.9..1.3 — some rays are broader than long

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);

            int segments = Mathf.Clamp(c.BodySegments, 1, 2);
            float length = unit * (1.7f + 0.5f * (segments - 1));
            float halfW = unit * 0.8f * wide;
            float thick = unit * 0.32f;
            float bodyY = unit * 0.7f;
            AddPart(body, "Disc", new Vector3(0f, bodyY, 0f), new Vector3(halfW * 2f, thick, length), _bodyMat);
            AddPart(body, "Belly", new Vector3(0f, bodyY - thick * 0.45f, 0f), new Vector3(halfW * 1.9f, thick * 0.3f, length * 0.92f), bellyMat);

            // The wing panels: three per side, nested, each a little shorter in chord than the one inboard of it,
            // so the silhouette is the classic diamond.
            float panelW = unit * 0.7f * wide;
            float panelThick = unit * 0.1f;
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -1f : 1f;
                var chain = new Transform[3];
                var parent = body.transform;
                for (int k = 0; k < 3; k++)
                {
                    float chord = length * (k == 0 ? 0.95f : k == 1 ? 0.72f : 0.42f);
                    var pivot = NewPivot(parent, (side == 0 ? "RayWingL" : "RayWingR") + k,
                        k == 0 ? new Vector3(sx * halfW, bodyY, 0f) : new Vector3(sx * panelW, 0f, 0f));
                    AddPartTo(pivot, "Panel", new Vector3(sx * panelW * 0.5f, 0f, -length * 0.02f * k),
                        new Vector3(panelW, panelThick, chord), _bodyMat);
                    chain[k] = pivot;
                    parent = pivot;
                }

                _rayWings.Add(chain);
            }

            // The whip tail: five thin links off the rear edge.
            AddTail(body.transform, new Vector3(0f, bodyY, -length * 0.5f), unit * 2.2f, unit * 0.14f, 5, _coat.Mat(CoatPart.Ornament)); // the accent tone, as the fins

            // Eyes on the top surface, up front; the pivot lets the head-idle nod and the gaze work on them.
            float headScale = 0.6f;
            _headPivot = NewPivot(body.transform, "Head", new Vector3(0f, bodyY + thick * 0.5f, length * 0.28f));
            AddEyes(c, unit, headScale);
            _heads.Add(_headPivot);

            // Cephalic lobes (the species' horns worn forward): two flat flaps at the front corners.
            if (c.Horns > 0)
            {
                for (int l = 0; l < 2; l++)
                {
                    float sx = l == 0 ? -1f : 1f;
                    AddPart(body, "Lobe" + l, new Vector3(sx * halfW * 0.55f, bodyY, length * 0.5f + unit * 0.2f),
                        new Vector3(unit * 0.22f, thick * 0.8f, unit * 0.45f), _coat.Mat(CoatPart.Ornament));
                }
            }

            if (c.Glows)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(body.transform, false);
                go.transform.localPosition = new Vector3(0f, bodyY, 0f);
                _glow = go.AddComponent<Light>();
                FxLightBridge.Mirror(_glow); // #2151: a glowing creature lights its surroundings
                _glow.type = LightType.Point;
                _glow.range = unit * 6f;
                _glow.intensity = 1.1f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }

            var rig = Describe(c, body.transform, unit, unit, idh);
            rig.Aquatic = true; // the body glides and rolls gently; the wing wave is the rest of the performance
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
        }
    }
}
