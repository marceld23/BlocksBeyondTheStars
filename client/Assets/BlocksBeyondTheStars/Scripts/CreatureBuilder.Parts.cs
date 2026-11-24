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
    /// The limbs and heads every body plan shares: wings, tails and chains, legs, head boxes, the floral head, eyelids,
    /// coat overlays, the rig description and the pivot parts.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        /// <summary>The wire's head shape name → the enum; anything unknown (an older server sends nothing) is the box.</summary>
        private static CreatureHeadShape ParseHeadShape(string name)
            => !string.IsNullOrEmpty(name) && System.Enum.TryParse<CreatureHeadShape>(name, true, out var shape) ? shape : CreatureHeadShape.Box;

        /// <summary>A pair of two-panel wings: shoulder → inner panel → wrist → outer panel. The wrist is what
        /// makes a fold read as a fold — the single slab this replaces could only be rotated bodily up over
        /// the back, which is not what a bird does with its wings when it lands.</summary>
        private void AddWings(Transform parent, float span, float chord, float thick, Vector3 shoulderPos, Material mat,
            int pairs = 1, float zSpan = 0f)
        {
            float inner = span * 0.45f, outer = span - span * 0.45f;
            // Extra pairs (#1781) sit along the torso like the leg rows, each a little narrower in chord so they
            // do not overlap, and slightly shorter toward the rear (the dragonfly's hind wings are the broad ones,
            // but a rear pair that is longer than the body reads wrong on a blocky animal).
            if (pairs > 1)
            {
                chord = Mathf.Min(chord, 2f * zSpan / pairs * 0.85f);
            }

            for (int row = 0; row < pairs; row++)
            {
                float z = pairs == 1 ? shoulderPos.z : Mathf.Lerp(shoulderPos.z + zSpan, shoulderPos.z - zSpan, row / (float)(pairs - 1));
                float rowScale = 1f - 0.08f * row;
                for (int w = 0; w < 2; w++)
                {
                    float sx = w == 0 ? -1f : 1f;
                    string side = w == 0 ? "WingL" : "WingR";
                    var shoulder = NewPivot(parent, row == 0 ? side : side + row,
                        new Vector3(sx * shoulderPos.x, shoulderPos.y, z));
                    // On a feathered species the slab shrinks to the arm at the leading edge; the feathers
                    // (CreatureBuilder.Coat3d.cs) take the rest of the wing.
                    float armShare = _feathered ? CoatForms.ArmShare : 1f;
                    float armZ = _feathered ? CoatForms.ArmCentre * chord : 0f;
                    AddPartTo(shoulder, "WingInner", new Vector3(sx * inner * rowScale * 0.5f, 0f, armZ),
                        new Vector3(inner * rowScale, thick, chord * armShare), mat);

                    var wrist = NewPivot(shoulder, "WingWrist", new Vector3(sx * inner * rowScale, 0f, 0f));
                    AddPartTo(wrist, "WingOuter", new Vector3(sx * outer * rowScale * 0.5f, 0f, armZ),
                        new Vector3(outer * rowScale, thick * 0.8f, chord * 0.82f * armShare), mat);
                    if (_feathered)
                    {
                        _wingPanels.Add(new WingPanels
                        {
                            Shoulder = shoulder, Wrist = wrist, Inner = inner * rowScale, Outer = outer * rowScale,
                            Chord = chord, Thick = thick, Side = sx,
                        });
                    }

                    _wings.Add(new WingRig
                    {
                        Shoulder = shoulder,
                        Wrist = wrist,
                        Side = w,
                        Row = row,
                        Rows = pairs,
                        ShoulderRest = shoulder.localRotation,
                        WristRest = wrist.localRotation,
                    });
                }
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
        /// used to, and the hinge sits at the jaw's REAR so it swings open like a jaw rather than sliding. The skull wears the
        /// head's coat (an eye mask is drawn there, on a pinned mesh so the band sits at eye height); the jaw the head's
        /// hide without the pattern (#2830).</summary>
        private void AddHeadBox(float w, float h, float d, float headZ)
        {
            const float JawShare = 0.3f; // the lower 30 % of the head is jaw
            _faceZ = headZ + d * 0.5f;
            _pinV = _coat.PinsHead;
            AddPartTo(_headPivot, "HeadUpper", new Vector3(0f, h * JawShare * 0.5f, headZ),
                new Vector3(w, h * (1f - JawShare), d), _coat.Mat(CoatPart.Head));
            _pinV = false;
            _jawPivot = AddPivotPart(_headPivot, "Jaw",
                new Vector3(0f, -h * (0.5f - JawShare * 0.5f), headZ - d * 0.44f),
                new Vector3(0f, 0f, d * 0.44f), new Vector3(w * 0.92f, h * JawShare, d * 0.88f), _coat.Mat(CoatPart.Jaw));
            _heads.Add(_headPivot);
            _jaws.Add(_jawPivot);
        }

        /// <summary>#1760: the flowerling's flower head — eight petal boxes fanned around the head in the belly (petal)
        /// colour, a yellow disc behind the face, and the mouth: a wide dark grin when calm, a red open maw with teeth
        /// (the lower row on the hinged jaw) when hostile. #1997: the face comes from <see cref="FloralFaceLayout"/>, which
        /// keeps every part in front of the head — the first version built both faces inside the head cubes, so neither
        /// mood ever showed. An angry flowerling's petals flare out and flush red.</summary>
        private void AddFloralHead(NetCreature c, float unit, float headScale, Color petalColor)
        {
            float w = unit * 0.9f * headScale, h = unit * 0.85f * headScale, d = unit * 0.8f * headScale, headZ = unit * 0.45f;
            if (c.Hostile)
            {
                petalColor = Color.Lerp(petalColor, new Color(0.86f, 0.12f, 0.12f), 0.6f);
            }

            var petalMat = Lit(petalColor, HideOr("petal"));
            var discMat = Lit(new Color(0.98f, 0.82f, 0.25f), null);
            AddPartTo(_headPivot, "PetalDisc", new Vector3(0f, h * 0.15f, headZ - d * 0.35f), new Vector3(w * 1.35f, h * 1.35f, d * 0.12f), discMat);
            const int Petals = 8;
            float flare = c.Hostile ? 1.15f : 1f;
            for (int i = 0; i < Petals; i++)
            {
                float a = i / (float)Petals * Mathf.PI * 2f;
                float r = Mathf.Max(w, h) * 0.95f * flare;
                var petal = NewPivot(_headPivot, "Petal" + i, new Vector3(Mathf.Cos(a) * r, h * 0.15f + Mathf.Sin(a) * r, headZ - d * 0.4f));
                // A calm flower lies flat; an angry one flares its petals forward around the face.
                petal.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg) * Quaternion.Euler(c.Hostile ? -24f : 0f, 0f, 0f);
                AddPartTo(petal, "PetalBox", Vector3.zero, new Vector3(w * 0.95f * flare, h * 0.5f, d * 0.1f), petalMat);
            }

            var grinMat = Lit(new Color(0.12f, 0.08f, 0.10f), null);
            var mawMat = Lit(new Color(0.55f, 0.08f, 0.10f), null);
            var toothMat = ToothMaterial(c);
            int n = 0;
            foreach (var box in FloralFaceLayout.Build(w, h, d, headZ, c.Hostile))
            {
                var mat = box.Part switch
                {
                    FloralFacePart.Maw => mawMat,
                    FloralFacePart.Tooth => toothMat,
                    _ => grinMat,
                };
                AddPartTo(box.OnJaw ? _jawPivot : _headPivot, box.Part.ToString() + n++,
                    new Vector3(box.CenterX, box.CenterY, box.CenterZ), new Vector3(box.SizeX, box.SizeY, box.SizeZ), mat);
            }
        }

        /// <summary>#2830: the placed back patterns, as thin slabs in the pattern colour over the box torso parts collected in
        /// <see cref="_torsoBoxes"/> — each slab a little proud of the torso so nothing z-fights: a stripe along the spine
        /// (dorsal stripe), a blanket over the middle part's top half (saddle), or a cape over every part's top half (two-tone:
        /// dark above, the body colour and the light belly below). Render-only.</summary>
        private void AddCoatOverlays(Transform parent)
        {
            if (!CreatureCoat.IsOverlay(_coat.Pattern) || _torsoBoxes.Count == 0)
            {
                return;
            }

            var mat = _coat.Mat(CoatPart.Overlay);
            switch (_coat.Pattern)
            {
                case CoatPattern.DorsalStripe:
                    for (int i = 0; i < _torsoBoxes.Count; i++)
                    {
                        var (p, s) = _torsoBoxes[i];
                        float top = p.y + s.y * 0.5f;
                        AddPartTo(parent, "CoatStripe" + i, new Vector3(p.x, top + s.y * 0.025f, p.z),
                            new Vector3(s.x * 0.24f, s.y * 0.07f, s.z), mat);
                    }

                    break;
                case CoatPattern.Saddle:
                    {
                        var (p, s) = _torsoBoxes[_torsoBoxes.Count / 2];
                        float top = p.y + s.y * 0.5f;
                        float h = s.y * 0.5f;
                        AddPartTo(parent, "CoatSaddle", new Vector3(p.x, top - h * 0.5f + s.y * 0.03f, p.z),
                            new Vector3(s.x * 1.06f, h, s.z * 0.62f), mat);
                        break;
                    }

                case CoatPattern.TwoTone:
                    for (int i = 0; i < _torsoBoxes.Count; i++)
                    {
                        var (p, s) = _torsoBoxes[i];
                        float top = p.y + s.y * 0.5f;
                        float h = s.y * 0.48f;
                        AddPartTo(parent, "CoatCape" + i, new Vector3(p.x, top - h * 0.5f + s.y * 0.025f, p.z),
                            new Vector3(s.x * 1.05f, h, s.z * 1.005f), mat);
                    }

                    break;
            }
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
        /// carry weight on the plant, and the foot is what stops the leg ending in a cut-off pole. The thigh wears the limb
        /// coat, shin and sole the lower-limb coat (the socks, #2830).</summary>
        private LegRig AddLeg(Transform parent, int row, int side, int rows, Vector3 hipPos, float legLen, float thick)
        {
            float upper = legLen * UpperLegShare;
            float lower = legLen - upper;
            var upperMat = _coat.Mat(CoatPart.Limb);
            var lowerMat = _coat.Mat(CoatPart.LowerLimb);

            var hip = NewPivot(parent, (side == 0 ? "LegL" : "LegR") + row, hipPos);
            AddPartTo(hip, "Thigh", new Vector3(0f, -upper * 0.5f, 0f), new Vector3(thick, upper, thick), upperMat);

            var knee = NewPivot(hip, "Knee", new Vector3(0f, -upper, 0f));
            AddPartTo(knee, "Shin", new Vector3(0f, -lower * 0.5f, 0f), new Vector3(thick * 0.85f, lower, thick * 0.85f), lowerMat);

            var foot = NewPivot(knee, "Foot", new Vector3(0f, -lower, 0f));
            AddPartTo(foot, "Sole", new Vector3(0f, -thick * 0.26f, thick * 0.3f),
                new Vector3(thick * 1.4f, thick * 0.52f, thick * 1.8f), lowerMat);

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
                RayWings = _rayWings.ToArray(),
                Head = _heads.Count > 0 ? _heads[0] : _headPivot,
                Jaw = _jaws.Count > 0 ? _jaws[0] : _jawPivot,
                Heads = _heads.ToArray(),
                Jaws = _jaws.ToArray(),
                Eyelids = _eyelids.ToArray(),
                Ears = _ears.ToArray(),
                Body = body,
                Hostile = c.Hostile,
                Asleep = c.Asleep,
                Aquatic = c.Habitat == "Water" || c.Habitat == "Amphibian"
                    || CreatureMotion.IsSkyGliderBody(c.Habitat, c.BodyPlan, c.Legs), // #1779: an air fish weaves like a fish
                SkyGlider = CreatureMotion.IsSkyGliderBody(c.Habitat, c.BodyPlan, c.Legs),
                Fluked = c.TailFluke, // #3975
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
            if (col != null && !_keepColliders)
            {
                Object.Destroy(col);
            }

            go.transform.SetParent(pivot, false);
            go.transform.localPosition = cubeOffset;
            go.transform.localScale = scale;
            TileCube(go, PrimitiveType.Cube, scale, mat);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _renderers.Add(go.GetComponent<Renderer>());
            RecordCoatPart(go, scale, mat);
            return pivot;
        }
    }
}
