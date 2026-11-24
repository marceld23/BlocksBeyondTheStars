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
    /// The giants (#1998–#2001): colossus, sandworm, leviathan and sky giant, built from the same cube parts as every other
    /// creature; their core parts also carry colliders on the giant layer.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        // ------------------------------------------------------------------------------------------------
        // Giants (#1998-#2001): the colossus and the sandworm. Both are built from the same cube parts as every
        // other creature; their core parts also carry colliders on the giant layer, so the player bumps into a leg
        // or a worm segment (a kinematic rigidbody on the root keeps the moving compound cheap).
        // ------------------------------------------------------------------------------------------------

        /// <summary>The colossus (#1999): a 40-60 block quadruped laid out by the shared <see cref="ColossusBody"/> (the
        /// server takes its hit capsules and stomp reach from the same numbers), with jointed legs the gait and the foot
        /// planner drive, a neck chain per head, tusks, a tail and a rolled back: plates, spikes, crystals or a forest.</summary>
        private void BuildColossus(GameObject root, NetCreature c)
        {
            var body = ColossusBody.For(c.GiantHeight, c.LegRatio, c.NeckLength);
            NewCoat(c, MoodOf(c, 0.2f, false, false), 1.4f, 1f); // a giant: a softer red cast, no sleep dim, an unlit belly
            _bodyMat = _coat.Mat(CoatPart.Torso);
            var bellyMat = _coat.Mat(CoatPart.Belly);
            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            _keepColliders = true;

            var rig = new GameObject("BodyRig");
            rig.transform.SetParent(root.transform, false);

            // The torso: a heavy barrel with a raised shoulder hump and a belly slab.
            float tl = body.TorsoLength, tw = body.TorsoWidth, th = body.TorsoHeight;
            var torsoPos = new Vector3(0f, body.TorsoY, 0f);
            var torsoSize = new Vector3(tw, th, tl);
            var humpPos = new Vector3(0f, body.TorsoY + th * 0.35f, tl * 0.18f);
            var humpSize = new Vector3(tw * 0.9f, th * 0.55f, tl * 0.45f);
            AddPart(rig, "Torso", torsoPos, torsoSize, _bodyMat);
            AddPart(rig, "Hump", humpPos, humpSize, _bodyMat);
            _keepColliders = false;
            AddPart(rig, "Belly", new Vector3(0f, body.TorsoY - th * 0.42f, 0f), new Vector3(tw * 0.92f, th * 0.3f, tl * 0.88f), bellyMat);
            _torsoBoxes.Add((torsoPos, torsoSize));
            _torsoBoxes.Add((humpPos, humpSize));
            AddCoatOverlays(rig.transform);

            // Jointed legs in the shared order (front-left, front-right, rear-left, rear-right) — ColossusBody.Hip and the
            // server's stomp leg index use the same numbering.
            _keepColliders = true;
            for (int leg = 0; leg < 4; leg++)
            {
                var (hx, hy, hz) = body.Hip(leg);
                _legs.Add(AddLeg(rig.transform, leg >> 1, leg & 1, 2, new Vector3(hx, hy, hz), body.LegLength, body.LegThickness));
            }

            // Necks and heads — a hydra fans its necks out from the shoulders.
            var (ny, nz) = body.NeckBase;
            int heads = Mathf.Clamp(c.Heads, 1, 3);
            float headUnit = body.HeadSize / 0.95f;
            for (int hd = 0; hd < heads; hd++)
            {
                float fan = heads == 1 ? 0f : Mathf.Lerp(-1f, 1f, hd / (float)(heads - 1));
                Transform parent = NewPivot(rig.transform, heads == 1 ? "NeckRoot" : "NeckRoot" + hd, new Vector3(fan * tw * 0.3f, ny, nz));
                parent.localRotation = Quaternion.Euler(-ColossusBody.NeckPitchDeg, fan * 22f, 0f);
                for (int nk = 0; nk < body.NeckSegments; nk++)
                {
                    float taper = 1f - 0.12f * nk;
                    var seg = NewPivot(parent, "Neck" + nk, nk == 0 ? Vector3.zero : new Vector3(0f, 0f, body.NeckSegment));
                    AddPartTo(seg, "NeckSeg" + nk, new Vector3(0f, 0f, body.NeckSegment * 0.5f),
                        new Vector3(body.HeadSize * 0.6f * taper, body.HeadSize * 0.6f * taper, body.NeckSegment * 1.1f), _bodyMat);
                    _neckChain.Add(seg);
                    parent = seg;
                }

                _headPivot = NewPivot(parent, heads == 1 ? "Head" : "Head" + hd,
                    body.NeckSegments > 0 ? new Vector3(0f, 0f, body.NeckSegment) : Vector3.zero);
                _headPivot.localRotation = Quaternion.Euler(ColossusBody.NeckPitchDeg, 0f, 0f); // the face looks ahead again
                AddHeadBox(headUnit * 0.95f, headUnit * 0.85f, headUnit * 0.9f, headUnit * 0.45f);
                _keepColliders = false;
                AddEyes(c, headUnit, 1f);
                int tusks = Mathf.Clamp(c.Horns, 0, 4);
                if (tusks > 0)
                {
                    var tuskMat = TuskMaterial(c);
                    for (int tk = 0; tk < tusks; tk++)
                    {
                        float tx = tusks == 1 ? 0f : Mathf.Lerp(-headUnit * 0.32f, headUnit * 0.32f, tk / (float)(tusks - 1));
                        AddPartTo(_headPivot, "Tusk" + tk, new Vector3(tx, -headUnit * 0.25f, headUnit * 0.9f),
                            new Vector3(headUnit * 0.12f, headUnit * 0.12f, headUnit * 0.8f), tuskMat);
                    }
                }

                _keepColliders = true;
            }

            _keepColliders = false;
            if (c.HasTail)
            {
                AddTail(rig.transform, new Vector3(0f, body.TorsoY + th * 0.15f, -tl * 0.5f), body.TailLength, body.LegThickness * 0.6f, 5, _coat.Mat(CoatPart.Tail));
            }

            AddColossusBack(rig.transform, c, body, idh);

            if (c.Glows)
            {
                var go = new GameObject("Glow");
                go.transform.SetParent(rig.transform, false);
                go.transform.localPosition = new Vector3(0f, body.TorsoY, 0f);
                _glow = go.AddComponent<Light>();
                FxLightBridge.Mirror(_glow); // #2151: a glowing creature lights its surroundings
                _glow.type = LightType.Point;
                _glow.range = body.Top * 0.8f;
                _glow.intensity = 1.2f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }

            MakeGiantBody(root);
            var desc = Describe(c, rig.transform, body.LegLength * 0.25f, body.LegLength, idh);
            desc.Aquatic = false;
            desc.Giant = true;
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(desc);
            anim.CadenceScale = 0.6f; // a giant's stride is metres long; the beat is slow by construction, a touch slower still
        }

        /// <summary>What grows on the colossus's back: armour plates, a spike ridge, glowing crystals or a small forest.</summary>
        private void AddColossusBack(Transform rig, NetCreature c, ColossusBody body, int idh)
        {
            float top = body.TorsoY + body.TorsoHeight * 0.5f;
            float tl = body.TorsoLength;
            var rng = new System.Random(idh);
            switch (c.BackFeature)
            {
                case "plates":
                    {
                        var mat = Lit(_coat.Accent(Rgb(c.BellyRgb) * 0.85f), HideOr("plated"));
                        for (int i = 0; i < 7; i++)
                        {
                            float z = Mathf.Lerp(-tl * 0.4f, tl * 0.4f, i / 6f);
                            float h = body.TorsoHeight * (0.35f + 0.25f * Mathf.Sin(i / 6f * Mathf.PI));
                            AddPartTo(rig, "Plate" + i, new Vector3(0f, top + h * 0.45f, z), new Vector3(body.TorsoWidth * 0.12f, h, tl * 0.1f), mat);
                        }

                        break;
                    }

                case "spikes":
                    {
                        var mat = Lit(new Color(0.22f, 0.18f, 0.16f), null);
                        for (int i = 0; i < 12; i++)
                        {
                            float z = Mathf.Lerp(-tl * 0.45f, tl * 0.45f, i / 11f);
                            float side = (i & 1) == 0 ? -1f : 1f;
                            AddPartTo(rig, "Spike" + i, new Vector3(side * body.TorsoWidth * 0.18f, top + body.TorsoHeight * 0.25f, z),
                                new Vector3(body.LegThickness * 0.35f, body.TorsoHeight * 0.6f, body.LegThickness * 0.35f), mat);
                        }

                        break;
                    }

                case "crystals":
                    {
                        var mat = Unlit(Color.Lerp(Rgb(c.ColorRgb), new Color(0.6f, 0.95f, 1f), 0.6f));
                        for (int i = 0; i < 9; i++)
                        {
                            var p = new Vector3((float)(rng.NextDouble() - 0.5) * body.TorsoWidth * 0.7f, top, (float)(rng.NextDouble() - 0.5) * tl * 0.8f);
                            float h = body.TorsoHeight * (0.3f + (float)rng.NextDouble() * 0.5f);
                            var pivot = NewPivot(rig, "Crystal" + i, p);
                            pivot.localRotation = Quaternion.Euler((float)(rng.NextDouble() - 0.5) * 40f, (float)rng.NextDouble() * 90f, (float)(rng.NextDouble() - 0.5) * 40f);
                            AddPartTo(pivot, "CrystalCore", new Vector3(0f, h * 0.5f, 0f), new Vector3(h * 0.3f, h, h * 0.3f), mat);
                        }

                        break;
                    }

                case "forest":
                    {
                        var trunk = Lit(new Color(0.36f, 0.25f, 0.16f), HideOr("barkskin"));
                        var leaves = Lit(new Color(0.22f, 0.5f, 0.2f), HideOr("mossy"));
                        var moss = Lit(new Color(0.28f, 0.45f, 0.22f), HideOr("mossy"));
                        AddPartTo(rig, "Moss", new Vector3(0f, top + 0.2f, 0f), new Vector3(body.TorsoWidth * 0.9f, 0.5f, tl * 0.8f), moss);
                        for (int i = 0; i < 8; i++)
                        {
                            var p = new Vector3((float)(rng.NextDouble() - 0.5) * body.TorsoWidth * 0.7f, top, (float)(rng.NextDouble() - 0.5) * tl * 0.7f);
                            float h = 3f + (float)rng.NextDouble() * 4f;
                            AddPartTo(rig, "Trunk" + i, p + new Vector3(0f, h * 0.5f, 0f), new Vector3(0.6f, h, 0.6f), trunk);
                            AddPartTo(rig, "Crown" + i, p + new Vector3(0f, h + 1.2f, 0f), new Vector3(2.8f, 2.4f, 2.8f), leaves);
                        }

                        break;
                    }
            }

            if (c.HasCrest)
            {
                var mat = Lit(_coat.Accent(Rgb(c.BellyRgb)), HideOr("spined"));
                for (int i = 0; i < 6; i++)
                {
                    float z = Mathf.Lerp(tl * 0.1f, tl * 0.48f, i / 5f);
                    AddPartTo(rig, "Crest" + i, new Vector3(0f, top + body.TorsoHeight * 0.2f, z),
                        new Vector3(body.LegThickness * 0.2f, body.TorsoHeight * 0.4f, tl * 0.04f), mat);
                }
            }
        }

        /// <summary>The sandworm (#2001): the fixed archetype — a long tube of armoured ring segments tapering to the tail, a
        /// blunt head that opens into mandible petals around rings of teeth. The <see cref="SandwormView"/> on the root poses
        /// it every frame along the shared <c>SandwormPath</c>; there is no gait, so no <see cref="CreatureAnimator"/>.</summary>
        private void BuildSandworm(GameObject root, NetCreature c)
        {
            float girth = Mathf.Max(2f, c.WormGirth > 0f ? c.WormGirth : 8f);
            int segCount = Mathf.Clamp(c.BodySegments, 24, 48);
            float length = Mathf.Max(20f, c.WormLength > 0f ? c.WormLength : girth * 14f);
            float segLen = length / segCount;
            Color baseColor = Rgb(c.ColorRgb);
            Color bellyColor = Rgb(c.BellyRgb);
            NewCoat(c, MoodOf(c, 0f, false, false), 1f, 1f); // the archetype carries no mood tint and no glow factor
            _bodyMat = _coat.Mat(CoatPart.Torso);
            var plateMat = Lit(_coat.Accent(Color.Lerp(baseColor, bellyColor, 0.5f) * 0.9f), HideOr("plated"));
            var bellyMat = _coat.Mat(CoatPart.Belly);
            var spikeMat = DarkHornMaterial(c);
            var glowMat = Unlit(Color.Lerp(bellyColor, new Color(1f, 0.85f, 0.4f), 0.7f));
            int spikeRows = Mathf.Clamp(c.Horns, 0, 3);

            var segments = new Transform[segCount];
            for (int i = 0; i < segCount; i++)
            {
                float t = i / (float)(segCount - 1);
                float taper = Mathf.Lerp(1f, 0.55f, t * t);
                float g = girth * taper;
                var seg = new GameObject("Seg" + i).transform;
                seg.SetParent(root.transform, false);
                _keepColliders = true;
                AddPartTo(seg, "Core", Vector3.zero, new Vector3(g, g, segLen * 1.08f), _bodyMat);
                _keepColliders = false;
                // The armour ring: a plate over the back and both flanks, a paler belly plate — slightly proud of the core
                // and a little shorter, so the ring seams read between the segments.
                AddPartTo(seg, "PlateTop", new Vector3(0f, g * 0.5f, 0f), new Vector3(g * 0.92f, g * 0.12f, segLen * 0.9f), plateMat);
                AddPartTo(seg, "PlateL", new Vector3(-g * 0.5f, 0f, 0f), new Vector3(g * 0.12f, g * 0.8f, segLen * 0.9f), plateMat);
                AddPartTo(seg, "PlateR", new Vector3(g * 0.5f, 0f, 0f), new Vector3(g * 0.12f, g * 0.8f, segLen * 0.9f), plateMat);
                AddPartTo(seg, "Belly", new Vector3(0f, -g * 0.5f, 0f), new Vector3(g * 0.8f, g * 0.1f, segLen * 0.95f), bellyMat);
                if (c.HasCrest)
                {
                    AddPartTo(seg, "Ridge", new Vector3(0f, g * 0.62f, 0f), new Vector3(g * 0.14f, g * 0.2f, segLen * 0.7f), plateMat);
                }

                if (spikeRows > 0 && i % 3 == 1)
                {
                    for (int s = 0; s < spikeRows; s++)
                    {
                        float x = spikeRows == 1 ? 0f : Mathf.Lerp(-g * 0.3f, g * 0.3f, s / (float)(spikeRows - 1));
                        AddPartTo(seg, "Spike" + s, new Vector3(x, g * 0.62f, 0f), new Vector3(g * 0.08f, g * 0.35f, g * 0.08f), spikeMat);
                    }
                }

                if (c.Glows && (i & 1) == 0)
                {
                    AddPartTo(seg, "SpotL", new Vector3(-g * 0.57f, g * 0.1f, 0f), new Vector3(g * 0.03f, g * 0.14f, segLen * 0.3f), glowMat);
                    AddPartTo(seg, "SpotR", new Vector3(g * 0.57f, g * 0.1f, 0f), new Vector3(g * 0.03f, g * 0.14f, segLen * 0.3f), glowMat);
                }

                segments[i] = seg;
            }

            // The head: a blunt collar, a dark-red maw with two rings of teeth, and the mandible petals hinged at the rim.
            var head = new GameObject("WormHead").transform;
            head.SetParent(root.transform, false);
            _keepColliders = true;
            AddPartTo(head, "Collar", new Vector3(0f, 0f, -girth * 0.1f), new Vector3(girth * 1.12f, girth * 1.12f, girth * 0.55f), _bodyMat);
            _keepColliders = false;
            var mawMat = Lit(new Color(0.42f, 0.06f, 0.08f), null);
            var toothMat = ToothMaterial(c);
            AddPartTo(head, "Maw", new Vector3(0f, 0f, girth * 0.19f), new Vector3(girth * 0.85f, girth * 0.85f, girth * 0.05f), mawMat);
            for (int ring = 0; ring < 2; ring++)
            {
                int teeth = 10 + ring * 4;
                float r = girth * (0.25f + ring * 0.13f);
                for (int k = 0; k < teeth; k++)
                {
                    float a = (k + ring * 0.5f) / teeth * Mathf.PI * 2f;
                    var tooth = NewPivot(head, "Tooth" + ring + "_" + k, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, girth * 0.22f));
                    tooth.localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Cos(a), -Mathf.Sin(a), 1.2f));
                    AddPartTo(tooth, "Fang", new Vector3(0f, 0f, girth * 0.08f), new Vector3(girth * 0.05f, girth * 0.05f, girth * 0.18f), toothMat);
                }
            }

            int petals = Mathf.Clamp(c.Mandibles, 3, 5);
            var petalPivots = new Transform[petals];
            for (int k = 0; k < petals; k++)
            {
                float a = k / (float)petals * Mathf.PI * 2f + Mathf.PI * 0.5f;
                var rim = new Vector3(Mathf.Cos(a) * girth * 0.5f, Mathf.Sin(a) * girth * 0.5f, girth * 0.17f);
                var pivot = NewPivot(head, "Mandible" + k, rim);
                // The petal's local +Y points out from the mouth's centre; opening folds it back around its local X axis.
                pivot.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f);
                AddPartTo(pivot, "Petal", new Vector3(0f, -girth * 0.22f, girth * 0.28f),
                    new Vector3(girth * 0.62f, girth * 0.1f, girth * 0.62f), plateMat);
                AddPartTo(pivot, "PetalTooth", new Vector3(0f, -girth * 0.18f, girth * 0.6f),
                    new Vector3(girth * 0.08f, girth * 0.08f, girth * 0.14f), toothMat);
                petalPivots[k] = pivot;
            }

            MakeGiantBody(root);
            var view = root.AddComponent<SandwormView>();
            view.Init(segments, head, petalPivots, girth, length, _renderers.ToArray());
        }

        /// <summary>The leviathan (#2111): the sandworm's long body in the sea — a smooth tapering tube of segments with a pale
        /// belly, a dorsal fin on a crested species, pairs of flank fins along the body, a fluke at the tail, and a sleek head
        /// with two eyes and two hinged jaws (the worm's petals, two of them: top and bottom). The same <see cref="SandwormView"/>
        /// poses it along the shared <c>SandwormPath</c>; the view's water flag turns the sand fountains into spray.</summary>
        private void BuildLeviathan(GameObject root, NetCreature c)
        {
            float girth = Mathf.Max(2f, c.WormGirth > 0f ? c.WormGirth : 5f);
            int segCount = Mathf.Clamp(c.BodySegments, 20, 40);
            float length = Mathf.Max(20f, c.WormLength > 0f ? c.WormLength : girth * 16f);
            float segLen = length / segCount;
            Color baseColor = Rgb(c.ColorRgb);
            Color bellyColor = Rgb(c.BellyRgb);
            NewCoat(c, MoodOf(c, 0f, false, false), 1.3f, 1f);
            _bodyMat = _coat.Mat(CoatPart.Torso);
            var bellyMat = _coat.Mat(CoatPart.Belly);
            var finMat = Lit(_coat.Accent(Color.Lerp(baseColor, bellyColor, 0.35f) * 0.95f), HideOr("plated"));
            var spineMat = Lit(baseColor * 0.6f, null);
            var glowMat = Unlit(Color.Lerp(bellyColor, new Color(0.5f, 0.95f, 1f), 0.7f));
            int spineRows = Mathf.Clamp(c.Horns, 0, 2);
            int finPairs = Mathf.Clamp(c.FinPairs, 1, 3);

            var segments = new Transform[segCount];
            for (int i = 0; i < segCount; i++)
            {
                float t = i / (float)(segCount - 1);
                // Thickest a third of the way back, tapering to a narrow tail stock for the fluke.
                float taper = Mathf.Lerp(0.8f, 1f, Mathf.Clamp01(t / 0.3f)) * Mathf.Lerp(1f, 0.32f, Mathf.Clamp01((t - 0.3f) / 0.7f) * Mathf.Clamp01((t - 0.3f) / 0.7f));
                float g = girth * taper;
                var seg = new GameObject("Seg" + i).transform;
                seg.SetParent(root.transform, false);
                _keepColliders = true;
                AddPartTo(seg, "Core", Vector3.zero, new Vector3(g, g * 0.92f, segLen * 1.08f), _bodyMat);
                _keepColliders = false;
                AddPartTo(seg, "Belly", new Vector3(0f, -g * 0.42f, 0f), new Vector3(g * 0.7f, g * 0.14f, segLen * 1.0f), bellyMat);
                if (c.HasCrest && t > 0.15f && t < 0.5f)
                {
                    // The dorsal fin: tallest over the thickest part of the body, a thin blade.
                    float fin = g * (0.55f + 0.5f * Mathf.Sin((t - 0.15f) / 0.35f * Mathf.PI));
                    AddPartTo(seg, "Dorsal", new Vector3(0f, g * 0.46f + fin * 0.5f, 0f), new Vector3(g * 0.08f, fin, segLen * 0.9f), finMat);
                }

                if (spineRows > 0 && t > 0.5f && i % 2 == 0)
                {
                    for (int s = 0; s < spineRows; s++)
                    {
                        float x = spineRows == 1 ? 0f : (s == 0 ? -g * 0.18f : g * 0.18f);
                        AddPartTo(seg, "Spine" + s, new Vector3(x, g * 0.5f, 0f), new Vector3(g * 0.07f, g * 0.3f, g * 0.07f), spineMat);
                    }
                }

                if (c.Glows && (i & 1) == 0)
                {
                    AddPartTo(seg, "SpotL", new Vector3(-g * 0.5f, g * 0.05f, 0f), new Vector3(g * 0.03f, g * 0.1f, segLen * 0.35f), glowMat);
                    AddPartTo(seg, "SpotR", new Vector3(g * 0.5f, g * 0.05f, 0f), new Vector3(g * 0.03f, g * 0.1f, segLen * 0.35f), glowMat);
                }

                segments[i] = seg;
            }

            // Flank fins: pairs spread along the front two thirds, swept back, angled a little down.
            for (int p = 0; p < finPairs; p++)
            {
                int at = Mathf.Clamp(Mathf.RoundToInt(segCount * (0.18f + 0.22f * p)), 1, segCount - 2);
                float g = girth * Mathf.Lerp(1f, 0.7f, p / 2f);
                float finLen = g * (1.6f - 0.3f * p);
                foreach (int side in new[] { -1, 1 })
                {
                    var pivot = NewPivot(segments[at], "Fin" + p + (side < 0 ? "L" : "R"), new Vector3(side * g * 0.45f, -g * 0.1f, 0f));
                    pivot.localRotation = Quaternion.Euler(0f, side * -35f, side * 18f);
                    AddPartTo(pivot, "FinBlade", new Vector3(side * finLen * 0.5f, 0f, -finLen * 0.1f), new Vector3(finLen, g * 0.08f, g * 0.7f), finMat);
                }
            }

            // The fluke: two horizontal blades on the last segment.
            var tail = segments[segCount - 1];
            float tailG = girth * 0.32f;
            foreach (int side in new[] { -1, 1 })
            {
                var pivot = NewPivot(tail, "Fluke" + (side < 0 ? "L" : "R"), new Vector3(0f, 0f, -segLen * 0.3f));
                pivot.localRotation = Quaternion.Euler(0f, side * 28f, 0f);
                AddPartTo(pivot, "FlukeBlade", new Vector3(side * girth * 0.7f, 0f, -girth * 0.25f), new Vector3(girth * 1.4f, tailG * 0.25f, girth * 0.8f), finMat);
            }

            // The head: a sleek snout, two eyes, and two hinged jaws with a row of teeth each.
            var head = new GameObject("WormHead").transform;
            head.SetParent(root.transform, false);
            _keepColliders = true;
            AddPartTo(head, "Skull", new Vector3(0f, 0f, -girth * 0.05f), new Vector3(girth * 1.0f, girth * 0.85f, girth * 0.7f), _coat.Mat(CoatPart.Head));
            _keepColliders = false;
            AddPartTo(head, "Snout", new Vector3(0f, girth * 0.12f, girth * 0.45f), new Vector3(girth * 0.7f, girth * 0.4f, girth * 0.5f), _coat.Mat(CoatPart.Head));
            var eyeMat = Unlit(c.Glows ? new Color(0.6f, 1f, 0.95f) : new Color(0.95f, 0.9f, 0.5f));
            AddPartTo(head, "EyeL", new Vector3(-girth * 0.42f, girth * 0.2f, girth * 0.25f), new Vector3(girth * 0.16f, girth * 0.16f, girth * 0.16f), eyeMat);
            AddPartTo(head, "EyeR", new Vector3(girth * 0.42f, girth * 0.2f, girth * 0.25f), new Vector3(girth * 0.16f, girth * 0.16f, girth * 0.16f), eyeMat);
            var mawMat = Lit(new Color(0.5f, 0.1f, 0.14f), null);
            var toothMat = ToothMaterial(c);
            AddPartTo(head, "Maw", new Vector3(0f, -girth * 0.1f, girth * 0.3f), new Vector3(girth * 0.66f, girth * 0.3f, girth * 0.05f), mawMat);

            var jaws = new Transform[2];
            for (int k = 0; k < 2; k++)
            {
                float a = k == 0 ? Mathf.PI * 0.5f : Mathf.PI * 1.5f; // top, bottom
                var rim = new Vector3(0f, Mathf.Sin(a) * girth * 0.24f - girth * 0.1f, girth * 0.3f);
                var pivot = NewPivot(head, "Jaw" + k, rim);
                pivot.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f);
                AddPartTo(pivot, "Petal", new Vector3(0f, -girth * 0.12f, girth * 0.4f), new Vector3(girth * 0.7f, girth * 0.12f, girth * 0.85f), _coat.Mat(CoatPart.Jaw));
                for (int tth = 0; tth < 5; tth++)
                {
                    float x = Mathf.Lerp(-girth * 0.26f, girth * 0.26f, tth / 4f);
                    AddPartTo(pivot, "Tooth" + tth, new Vector3(x, -girth * 0.02f, girth * 0.25f + tth * girth * 0.12f), new Vector3(girth * 0.06f, girth * 0.14f, girth * 0.06f), toothMat);
                }

                jaws[k] = pivot;
            }

            MakeGiantBody(root);
            var view = root.AddComponent<SandwormView>();
            view.Water = true;
            view.Init(segments, head, jaws, girth, length, _renderers.ToArray());
        }

        /// <summary>The sky giant (#2112): a long tapering sailer — a tube of segments with a pale belly, a row of tall
        /// wing-sails standing up from the back (the species' <c>WingPairs</c>), a pair of broad glide fins under the front
        /// third, a fluke at the tail, a blunt head with two eyes. The <see cref="SkyGiantView"/> lays the body along the
        /// head's own track every frame; there is no gait, so no <see cref="CreatureAnimator"/>.</summary>
        private void BuildSkyGiant(GameObject root, NetCreature c)
        {
            float girth = Mathf.Max(2f, c.WormGirth > 0f ? c.WormGirth : 5f);
            int segCount = Mathf.Clamp(c.BodySegments, 14, 30);
            float length = Mathf.Max(20f, c.WormLength > 0f ? c.WormLength : girth * 10f);
            float segLen = length / (segCount - 1);
            Color baseColor = Rgb(c.ColorRgb);
            Color bellyColor = Rgb(c.BellyRgb);
            NewCoat(c, MoodOf(c, 0f, false, false), 1.3f, 1f);
            _bodyMat = _coat.Mat(CoatPart.Torso);
            var bellyMat = _coat.Mat(CoatPart.Belly);
            var sailMat = Lit(_coat.Accent(Color.Lerp(bellyColor, baseColor, 0.3f)), HideOr("plated"));
            var glowMat = Unlit(Color.Lerp(bellyColor, new Color(0.7f, 0.95f, 1f), 0.6f));
            int sailPairs = Mathf.Clamp(c.WingPairs, 2, 5);

            var segments = new Transform[segCount];
            var sails = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < segCount; i++)
            {
                float t = i / (float)(segCount - 1);
                float taper = Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(t / 0.25f)) * Mathf.Lerp(1f, 0.3f, Mathf.Clamp01((t - 0.25f) / 0.75f));
                float g = girth * taper;
                var seg = new GameObject("Seg" + i).transform;
                seg.SetParent(root.transform, false);
                _keepColliders = true;
                AddPartTo(seg, "Core", Vector3.zero, new Vector3(g, g * 0.8f, segLen * 1.08f), _bodyMat);
                _keepColliders = false;
                AddPartTo(seg, "Belly", new Vector3(0f, -g * 0.36f, 0f), new Vector3(g * 0.7f, g * 0.14f, segLen), bellyMat);
                if (c.Glows && (i & 1) == 0)
                {
                    AddPartTo(seg, "SpotL", new Vector3(-g * 0.5f, 0f, 0f), new Vector3(g * 0.03f, g * 0.12f, segLen * 0.3f), glowMat);
                    AddPartTo(seg, "SpotR", new Vector3(g * 0.5f, 0f, 0f), new Vector3(g * 0.03f, g * 0.12f, segLen * 0.3f), glowMat);
                }

                segments[i] = seg;
            }

            // The sails: tall thin blades standing up from the back, spread over the front two thirds, leaning back.
            for (int p = 0; p < sailPairs; p++)
            {
                int at = Mathf.Clamp(Mathf.RoundToInt(segCount * (0.12f + 0.55f * p / Mathf.Max(1, sailPairs - 1))), 1, segCount - 3);
                float g = girth * Mathf.Lerp(1f, 0.7f, p / (float)Mathf.Max(1, sailPairs - 1));
                float sail = g * (2.2f - 0.25f * p);
                foreach (int side in new[] { -1, 1 })
                {
                    var pivot = NewPivot(segments[at], "Sail" + p + (side < 0 ? "L" : "R"), new Vector3(side * g * 0.18f, g * 0.35f, 0f));
                    pivot.localRotation = Quaternion.Euler(-18f, 0f, side * -28f);
                    AddPartTo(pivot, "SailBlade", new Vector3(0f, sail * 0.5f, 0f), new Vector3(g * 0.06f, sail, segLen * 1.6f), sailMat);
                    sails.Add(pivot);
                }
            }

            // Glide fins under the front third, and the fluke at the tail.
            int finAt = Mathf.Clamp(Mathf.RoundToInt(segCount * 0.3f), 1, segCount - 2);
            foreach (int side in new[] { -1, 1 })
            {
                var pivot = NewPivot(segments[finAt], "Glide" + (side < 0 ? "L" : "R"), new Vector3(side * girth * 0.45f, -girth * 0.05f, 0f));
                pivot.localRotation = Quaternion.Euler(0f, side * -20f, side * 8f);
                AddPartTo(pivot, "GlideBlade", new Vector3(side * girth * 1.1f, 0f, -girth * 0.2f), new Vector3(girth * 2.2f, girth * 0.08f, girth * 0.9f), sailMat);
            }

            var tail = segments[segCount - 1];
            foreach (int side in new[] { -1, 1 })
            {
                var pivot = NewPivot(tail, "Fluke" + (side < 0 ? "L" : "R"), new Vector3(0f, 0f, -segLen * 0.3f));
                pivot.localRotation = Quaternion.Euler(0f, side * 30f, 0f);
                AddPartTo(pivot, "FlukeBlade", new Vector3(side * girth * 0.6f, 0f, -girth * 0.2f), new Vector3(girth * 1.2f, girth * 0.06f, girth * 0.7f), sailMat);
            }

            // The head: a blunt skull, a short snout, two eyes; the mouth stays shut — it eats nobody.
            var head = new GameObject("SkyHead").transform;
            head.SetParent(root.transform, false);
            _keepColliders = true;
            AddPartTo(head, "Skull", new Vector3(0f, 0f, 0f), new Vector3(girth * 0.95f, girth * 0.8f, girth * 0.8f), _coat.Mat(CoatPart.Head));
            _keepColliders = false;
            AddPartTo(head, "Snout", new Vector3(0f, -girth * 0.08f, girth * 0.5f), new Vector3(girth * 0.6f, girth * 0.4f, girth * 0.5f), _coat.Mat(CoatPart.Head));
            var eyeMat = Unlit(c.Glows ? new Color(0.7f, 1f, 0.95f) : new Color(0.95f, 0.85f, 0.45f));
            AddPartTo(head, "EyeL", new Vector3(-girth * 0.4f, girth * 0.18f, girth * 0.3f), new Vector3(girth * 0.15f, girth * 0.15f, girth * 0.15f), eyeMat);
            AddPartTo(head, "EyeR", new Vector3(girth * 0.4f, girth * 0.18f, girth * 0.3f), new Vector3(girth * 0.15f, girth * 0.15f, girth * 0.15f), eyeMat);
            if (c.HasCrest)
            {
                AddPartTo(head, "Crest", new Vector3(0f, girth * 0.55f, -girth * 0.1f), new Vector3(girth * 0.08f, girth * 0.5f, girth * 0.6f), sailMat);
            }

            MakeGiantBody(root);
            var view = root.AddComponent<SkyGiantView>();
            view.Init(segments, sails.ToArray(), head, length);
        }

        /// <summary>Puts a giant's collider parts on the giant layer and gives the root a kinematic rigidbody, so the moving
        /// compound collides with the player's capsule without costing a static-collider rebuild every frame.</summary>
        private static void MakeGiantBody(GameObject root)
        {
            var rb = root.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = root.AddComponent<Rigidbody>(); // Unity's null is not C#'s: no ?? on components
            }
            rb.isKinematic = true;
            rb.useGravity = false;
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                col.gameObject.layer = CreatureView.GiantLayer;
            }
        }
    }
}
