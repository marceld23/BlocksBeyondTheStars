// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The sea animals of Earth (#3975). The dolphin is the standard swimmer with a beaked snout (<c>CreatureBuilder.Beak.cs</c>)
    /// and a horizontal fluke instead of the fish's vertical tail fin (<see cref="AddCaudalBlade"/>, from
    /// <see cref="NetCreature.TailFluke"/>). The whale is a body of its own (<see cref="BuildWhale"/>): a long, tapering body
    /// with a big head, a blowhole, throat grooves, long flippers and a fluke, in the whale's unit (<see cref="WhaleRules"/>,
    /// which the server's footprint and body height read too). It keeps its colliders like the arachnid, so a player bumps into
    /// it and can hit it; the animator beats its tail and fluke up and down (<see cref="RigDescription.Fluked"/>).
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        private static readonly Color BlowholeColor = new Color(0.1f, 0.1f, 0.12f);

        /// <summary>The tail fin on the caudal pivot of a finned standard body: the fish's vertical blade, or (#3975) a horizontal
        /// fluke for a species with <see cref="NetCreature.TailFluke"/>. Returns the fin's kind for the animator.</summary>
        private FinKind AddCaudalBlade(NetCreature c, Transform caudal, float unit, float finLen, Material mat)
        {
            if (!c.TailFluke)
            {
                AddPartTo(caudal, "FinTailBlade", new Vector3(0f, 0f, -finLen * 0.45f),
                    new Vector3(unit * 0.07f, finLen * 1.5f, finLen * 0.9f), mat);
                return FinKind.Caudal;
            }

            AddFlukeBlades(caudal, finLen * 0.9f, finLen * 0.55f, unit * 0.07f, mat);
            return FinKind.Fluke;
        }

        /// <summary>A fluke: two flat, horizontal blades on <paramref name="pivot"/>, swept back from the tail stock.</summary>
        private void AddFlukeBlades(Transform pivot, float span, float chord, float thick, Material mat)
        {
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                var root = NewPivot(pivot, s == 0 ? "FlukeL" : "FlukeR", Vector3.zero);
                root.localRotation = Quaternion.Euler(0f, side * 28f, 0f);
                AddPartTo(root, "FlukeBlade", new Vector3(side * span * 0.5f, 0f, -chord * 0.45f), new Vector3(span, thick, chord), mat);
            }
        }

        /// <summary>The whale (#3975): three tapering torso boxes (belly at the position, kept as colliders), the big head with its
        /// hinged jaw, small eyes low on the sides, a blowhole and throat grooves, a tail chain ending in the fluke, two long
        /// flippers and a small dorsal fin. Its length is <see cref="WhaleRules.BodyLength"/> (8u), centred on the position.</summary>
        private void BuildWhale(GameObject root, NetCreature c)
        {
            float u = WhaleRules.Unit(c.Size);
            float girth = WhaleRules.GirthUnits * u;
            NewCoat(c, MoodOf(c, 0.25f, true, true), 1.6f, 1.4f);
            _bodyMat = _coat.Mat(CoatPart.Torso);
            var accent = _coat.Mat(CoatPart.Ornament); // the flippers and the fluke: the belly tone (a humpback's white flippers)

            var body = new GameObject("BodyRig");
            body.transform.SetParent(root.transform, false);
            AddWhaleTorso(body.transform, u, girth);
            AddWhaleHead(c, body.transform, u, girth);

            // The tail stock: three links from the rear torso box back to the fluke.
            AddTail(body.transform, new Vector3(0f, girth * 0.42f, -1.8f * u), 2.0f * u, 0.75f * u, 3, _coat.Mat(CoatPart.Tail));
            var fluke = NewPivot(_tailChain[_tailChain.Count - 1], "FinTail", new Vector3(0f, 0f, -2.0f * u / 3f));
            AddFlukeBlades(fluke, 1.1f * u, 0.55f * u, 0.08f * u, accent);
            _fins.Add(new FinRig { Pivot = fluke, Kind = FinKind.Fluke });

            AddWhaleFins(body.transform, u, girth, accent);

            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            var rig = Describe(c, body.transform, u, u, idh);
            rig.Aquatic = true;
            rig.Fluked = true; // the whale's body always beats up and down
            var anim = root.AddComponent<CreatureAnimator>();
            anim.Init(rig);
            MakeGiantBody(root); // the kept torso and skull colliders go onto the giants' layer — the player bumps into it
        }

        /// <summary>The torso: chest, middle and rear boxes, tapering toward the tail (the belly rising a little, the back
        /// sloping down), with a belly plate under the chest and middle that carries the front of the throat grooves.</summary>
        private void AddWhaleTorso(Transform body, float u, float girth)
        {
            float[] widths = { 1.5f, 1.4f, 1.1f }, heights = { 1.5f, 1.38f, 1.05f }, fronts = { 2.6f, 1.0f, -0.6f }, lengths = { 1.6f, 1.6f, 1.2f };
            _keepColliders = true;
            for (int i = 0; i < 3; i++)
            {
                float h = heights[i] * u;
                float y = (girth * 0.5f) - ((girth - h) * 0.3f);
                AddPartTo(body, "Body" + i, new Vector3(0f, y, (fronts[i] - (lengths[i] * 0.5f)) * u), new Vector3(widths[i] * u, h, lengths[i] * u), _bodyMat);
            }

            _keepColliders = false;
            AddPartTo(body, "Belly", new Vector3(0f, 0.06f * u, 1.0f * u), new Vector3(1.25f * u, 0.14f * u, 3.2f * u), _coat.Mat(CoatPart.Belly));
            AddThroatGrooves(body, new Vector3(0f, -0.005f * u, 1.6f * u), 1.0f * u, 2.0f * u, u);
        }

        /// <summary>The head on its pivot at the chest's front: the box head with its hinged jaw (so the song moves the mouth),
        /// small eyes low on the sides near the mouth's corner, a blowhole on top toward the back, throat grooves under the jaw.</summary>
        private void AddWhaleHead(NetCreature c, Transform body, float u, float girth)
        {
            float w = 1.4f * u, h = 1.35f * u, d = 1.4f * u, headZ = 0.7f * u;
            _headPivot = NewPivot(body, "Head", new Vector3(0f, girth * 0.5f, 2.6f * u));
            _keepColliders = true;
            AddHeadBox(w, h, d, headZ);
            _keepColliders = false;

            if (Mathf.Clamp(c.Eyes, 0, 8) > 0)
            {
                var eyeMat = EyeFaceMaterial(c);
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    var at = new Vector3(side * w * 0.49f, -h * 0.12f, headZ - (d * 0.3f));
                    AddEye(_headPivot, "Eye" + s, at, 0.14f * u, new Vector3(side, 0f, 0.25f), eyeMat);
                    AddEyelid(at, 0.14f * u, _coat.Mat(CoatPart.Jaw));
                }
            }

            AddPartTo(_headPivot, "Blowhole", new Vector3(0f, h * 0.5f + (0.02f * u), headZ - (d * 0.3f)), new Vector3(0.24f * u, 0.05f * u, 0.12f * u),
                Lit(BlowholeColor, null));
            // The jaw box hangs 0.44d in front of its pivot and is 0.3h tall: its underside carries the grooves.
            AddThroatGrooves(_jawPivot, new Vector3(0f, -h * 0.15f - (0.005f * u), d * 0.44f), w * 0.8f, d * 0.85f, u);
        }

        /// <summary>A whale's throat grooves: thin, slightly darker stripes along the underside, across <paramref name="width"/>.</summary>
        private void AddThroatGrooves(Transform parent, Vector3 centre, float width, float length, float u)
        {
            var groove = Lit(_coat.Mat(CoatPart.Belly).color * 0.72f, null);
            const int Count = 5;
            for (int i = 0; i < Count; i++)
            {
                float x = Mathf.Lerp(-width * 0.4f, width * 0.4f, i / (float)(Count - 1));
                AddPartTo(parent, "Groove" + i, centre + new Vector3(x, 0f, 0f), new Vector3(0.05f * u, 0.03f * u, length), groove);
            }
        }

        /// <summary>The flippers — long, swept back and a little down from the chest's flanks (a humpback's) — and a small dorsal
        /// fin far back. The flippers' sweep is on an inner pivot, so the animator's scull poses the outer one freely.</summary>
        private void AddWhaleFins(Transform body, float u, float girth, Material accent)
        {
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                var pivot = NewPivot(body, s == 0 ? "FinL" : "FinR", new Vector3(side * 0.72f * u, girth * 0.3f, 1.6f * u));
                var sweep = NewPivot(pivot, "FlipperSweep", Vector3.zero);
                sweep.localRotation = Quaternion.Euler(0f, side * -30f, side * -22f);
                AddPartTo(sweep, "FinBlade", new Vector3(side * 0.85f * u, 0f, -0.1f * u), new Vector3(1.7f * u, 0.08f * u, 0.42f * u), accent);
                _fins.Add(new FinRig { Pivot = pivot, Kind = FinKind.Pectoral, Side = s, Row = 0, Rows = 1 });
            }

            var dorsal = NewPivot(body, "FinDorsal", new Vector3(0f, girth * 0.5f + (0.42f * u), -1.2f * u));
            AddPartTo(dorsal, "FinDorsalBlade", new Vector3(0f, 0.12f * u, -0.1f * u), new Vector3(0.07f * u, 0.3f * u, 0.5f * u), _bodyMat);
            _fins.Add(new FinRig { Pivot = dorsal, Kind = FinKind.Dorsal });
        }
    }
}
