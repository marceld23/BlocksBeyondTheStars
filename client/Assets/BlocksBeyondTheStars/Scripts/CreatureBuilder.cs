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
    public sealed partial class CreatureBuilder
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
        private readonly List<FinRig> _fins = new List<FinRig>();
        private readonly List<Transform[]> _rayWings = new List<Transform[]>();
        private readonly List<ArmRig> _arms = new List<ArmRig>(); // #2081: a biped's arms
        private readonly List<Transform> _heads = new List<Transform>();
        private readonly List<Transform> _jaws = new List<Transform>();
        private Transform _headPivot; // the head being built right now (a multi-headed body builds several in turn)
        private Transform _jawPivot;
        private Material _bodyMat;
        private Light _glow;

        /// <summary>#2830/#2831: this build's coat — the material of every body-part role (set first thing on every path).</summary>
        private CoatKit _coat;

        /// <summary>The box torso parts (centre, size) a placed back pattern lays its overlay slabs on (#2830).</summary>
        private readonly List<(Vector3 Pos, Vector3 Size)> _torsoBoxes = new List<(Vector3 Pos, Vector3 Size)>();

        /// <summary>While set, the next cube takes the pinned-v mesh (the eye-masked skull box, see <see cref="TiledCube"/>).</summary>
        private bool _pinV;

        /// <summary>#2868: the planet lookup the fauna theme reads (the view hands in the game's content); null = no theme,
        /// the hide pools pick freely.</summary>
        public System.Func<string, BlocksBeyondTheStars.Shared.Definitions.PlanetType> Planets { get; set; }

        /// <summary>#2868: this build's world look (theme + motif), null without one.</summary>
        private FaunaThemes.Look _look;

        /// <summary>#2865: this animal's coat placement — the part cubes shift and mirror their UVs by it.</summary>
        private int _placement;

        /// <summary>#2866: the z of the head box's front face (set by <see cref="AddHeadBox"/>, NaN on a body without one):
        /// the eyes sit just behind it so their front — where the eye texture is — stands out of the face.</summary>
        private float _faceZ = float.NaN;

        /// <summary>#1998: while set, the parts being added keep their box collider (a giant's legs, torso and segments —
        /// the player bumps into them); every other part is render-only.</summary>
        private bool _keepColliders;

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

            if (c.BodyPlan == "Ray")
            {
                BuildRay(root, c);
                return;
            }

            if (c.BodyPlan == "Colossus")
            {
                BuildColossus(root, c); // #1999
                return;
            }

            if (c.BodyPlan == "Sandworm")
            {
                BuildSandworm(root, c); // #2001
                return;
            }

            if (c.BodyPlan == "Arachnid")
            {
                BuildArachnid(root, c); // #2009
                return;
            }

            if (c.BodyPlan == "Biped")
            {
                BuildBiped(root, c); // #2081
                return;
            }

            if (c.BodyPlan == "Worm")
            {
                BuildWorm(root, c); // #2109
                return;
            }

            if (c.BodyPlan == "Leviathan")
            {
                BuildLeviathan(root, c); // #2111
                return;
            }

            if (c.BodyPlan == "SkyGiant")
            {
                BuildSkyGiant(root, c); // #2112
                return;
            }

            if (c.BodyPlan == "Whale")
            {
                BuildWhale(root, c); // #3975 (CreatureBuilder.Cetacean.cs)
                return;
            }

            if (BuildCompanionBody(root, c))
            {
                return; // #3295 the hunter robot (CreatureBuilder.Robot.cs), #3322 Rudolpho (CreatureBuilder.Reindeer.cs)
            }

            if (BuildSoundersBody(root, c))
            {
                return; // #3355/#3359 the Sounder and the creatures of Soaniri (CreatureBuilder.Sounder.cs and its siblings)
            }

            float unit = 0.5f * Mathf.Clamp(c.Size, 0.4f, 3f);
            Color bellyColor = Rgb(c.BellyRgb);

            // Per-species proportion jitter so two species with the same parts still differ in build — lanky vs squat,
            // big-headed vs small, slim vs broad. Keyed off the species' world seed (#2829), not the slot id: rolled
            // species are sp0..sp8 on every planet, so the id gave slot 3 the same build everywhere.
            int idh = CreatureCoat.ProportionHash(c.SpeciesId, c.VoiceSeed);
            float headScale = 0.75f + ((idh >> 2) & 7) / 7f * 0.65f; // 0.75..1.40
            float bodyWide = 0.85f + ((idh >> 5) & 7) / 7f * 0.55f;  // 0.85..1.40
            float legLong = 0.75f + ((idh >> 8) & 7) / 7f * 0.65f;   // 0.75..1.40

            // Hostiles read a touch more aggressive (a red cast), sleepers are dimmed (a gentle 0.85 — the "z z z" already
            // reads sleep; 0.6 crushed the tile+floor-dimmed body to black), companions get a green-cyan cast.
            var mood = MoodOf(c, 0.25f, true, true);
            Color baseColor = mood.Tint(Rgb(c.ColorRgb)) * mood.Dim; // the plain parts below (crest, gas sac) derive from it
            NewCoat(c, mood, 1.6f, 1.4f);
            _bodyMat = _coat.Mat(CoatPart.Torso);
            BeginCoat(c); // Fur and feathers as geometry (CreatureBuilder.Coat3d.cs)

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
                var segPos = new Vector3(0f, bodyY, z);
                var segSize = new Vector3(unit * 1.1f * taper * bodyWide, unit * 0.95f * taper, segLen);
                AddPart(body, "Body" + i, segPos, segSize, _bodyMat);
                _torsoBoxes.Add((segPos, segSize));
            }

            // Two-tone underside: a flatter belly slab in the accent colour, so the body isn't one flat hue.
            var bellyMat = _coat.Mat(CoatPart.Belly);
            AddPart(body, "Belly", new Vector3(0f, bodyY - unit * 0.42f, 0f),
                new Vector3(unit * 1.02f * bodyWide, unit * 0.32f, segments * segLen * 0.9f), bellyMat);
            AddCoatOverlays(body.transform);

            float frontZ = (segments - 1) * 0.5f * segLen + segLen * 0.6f;
            // Head on a neck pivot (behind the head) so it can bob/graze/lunge as an idle gesture, with a
            // hinged lower jaw so the species' voice actually moves a mouth (the calls have been coming out
            // of a sealed head since #902). A multi-headed body (#1780) carries its heads side by side at the
            // front, each with its own eyes, jaw and horns; the animator poses them out of step.
            int heads = Mathf.Clamp(c.Heads, 1, 3);
            float headW = unit * 0.9f * headScale;
            for (int hd = 0; hd < heads; hd++)
            {
                float hx = heads == 1 ? 0f : Mathf.Lerp(-headW * 0.5f * (heads - 1), headW * 0.5f * (heads - 1), hd / (float)(heads - 1));
                _headPivot = NewPivot(body.transform, heads == 1 ? "Head" : "Head" + hd, new Vector3(hx, bodyY + unit * 0.2f, frontZ - unit * 0.45f));
                AddHeadBox(headW, unit * 0.85f * headScale, unit * 0.8f * headScale, unit * 0.45f);

                // Eyes: optional (0 = eyeless) and a random count (often two, sometimes three/four/six). Bigger,
                // with a dark pupil so they clearly read as eyes — spread in a row across the head front.
                AddEyes(c, unit, headScale);

                // #3327: the Schuppenhund's muzzle, nose, teeth and ears (all zero on every other species — nothing is built).
                AddHoundHead(c, unit, headW, unit * 0.85f * headScale, unit * 0.8f * headScale, unit * 0.45f);

                // #3974: a bird's beak (a dolphin's snout, #3975) and a chicken's comb (none on every other species).
                AddBeak(c, unit, headW, unit * 0.85f * headScale, unit * 0.8f * headScale, unit * 0.45f);

                // #1760: the flowerling — a ring of petals around the head, and a face that is a wide grin while it is
                // calm and a toothed maw while it is hostile (the server flips Hostile with the mining grudge; the view
                // rebuilds the body on that flip, so the face follows).
                if (c.BodyPlan == "Floral")
                {
                    AddFloralHead(c, unit, headScale, bellyColor);
                }

                // Horns/spikes on top of the head — silhouette variety.
                int horns = Mathf.Clamp(c.Horns, 0, 4);
                if (horns > 0)
                {
                    var hornMat = HornMaterial(c); // #2867: a horn tile and colour per species
                    float hornH = unit * 0.5f * headScale;
                    for (int hn = 0; hn < horns; hn++)
                    {
                        float hxn = horns == 1 ? 0f : Mathf.Lerp(-unit * 0.30f * headScale, unit * 0.30f * headScale, hn / (float)(horns - 1));
                        AddPartTo(_headPivot, "Horn" + hn, new Vector3(hxn, unit * (0.5f * headScale + 0.25f * legLong), unit * 0.05f),
                            new Vector3(unit * 0.13f, hornH, unit * 0.13f), hornMat);
                    }
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
                _legs.Add(AddLeg(body.transform, row, 0, rows, new Vector3(-hipX, legH, z), legH, legThick));
                _legs.Add(AddLeg(body.transform, row, 1, rows, new Vector3(hipX, legH, z), legH, legThick));
            }

            AddHoundClaws(c, legThick); // #3327: three claws on every foot (a species without claws builds none)
            AddLabToes(c, legThick);    // Three blunt toes on every foot of a standard animal

            if (c.HasWings)
            {
                // A feathered wing is longer and a little slimmer — the old slab's span is a bat's.
                AddWings(body.transform, unit * 0.9f * (_feathered ? 1.45f : 1f), unit * 1.2f * (_feathered ? 0.92f : 1f), unit * 0.08f,
                    new Vector3(unit * 0.45f, bodyY + unit * 0.2f, 0f), _coat.Mat(CoatPart.Wing),
                    Mathf.Clamp(c.WingPairs, 1, 3), hipSpan + segLen * 0.1f);
            }

            if (c.HasTail)
            {
                float tailBaseZ = -(segments - 1) * 0.5f * segLen - segLen * 0.2f;
                bool houndTail = c.BodyPlan == "Hound"; // #3327: a dog's tail — set high on the rump, long and thin
                AddTail(body.transform, new Vector3(0f, bodyY + (houndTail ? unit * 0.22f : 0f), tailBaseZ),
                    segLen * (houndTail ? 1.5f : 1.1f), unit * (houndTail ? 0.2f : 0.35f), houndTail ? 4 : 3, _coat.Mat(CoatPart.Tail));
            }

            // Fins: a legless swimmer used to be a weaving box with no limbs at all. Pectorals on the flanks,
            // a vertical tail fin at the back (on the tail's last link when there is one), and a dorsal when
            // the species is not already wearing a crest there. Fins, crest and tentacles are the accents: the belly
            // colour, or the pattern colour on a crest-banded species (#2830).
            var accentMat = _coat.Mat(CoatPart.Ornament);
            if (c.HasFins)
            {
                float finLen = unit * 0.75f;
                int finPairs = Mathf.Clamp(c.FinPairs, 1, 3);
                float finFront = frontZ - unit * 0.7f;
                float finRear = -(segments - 1) * 0.5f * segLen + segLen * 0.1f;
                for (int row = 0; row < finPairs; row++)
                {
                    // One pair sits behind the head as before; extra pairs (#1782) space out toward the tail and
                    // shrink a little, like the fins along a cuttlefish's flank.
                    float fz = finPairs == 1 ? finFront : Mathf.Lerp(finFront, finRear, row / (float)(finPairs - 1));
                    float rowLen = finLen * (1f - 0.12f * row);
                    for (int f = 0; f < 2; f++)
                    {
                        float sx = f == 0 ? -1f : 1f;
                        var pec = NewPivot(body.transform, (f == 0 ? "FinL" : "FinR") + (row == 0 ? "" : row.ToString()),
                            new Vector3(sx * unit * 0.5f * bodyWide * TaperAt(fz, segLen, segments), bodyY - unit * 0.1f, fz));
                        AddPartTo(pec, "FinBlade", new Vector3(sx * rowLen * 0.5f, 0f, -rowLen * 0.15f),
                            new Vector3(rowLen, unit * 0.07f, rowLen * 0.8f), accentMat);
                        _fins.Add(new FinRig { Pivot = pec, Kind = FinKind.Pectoral, Side = f, Row = row, Rows = finPairs });
                    }
                }

                var tailFinParent = _tailChain.Count > 0 ? _tailChain[_tailChain.Count - 1] : body.transform;
                float tailFinZ = _tailChain.Count > 0
                    ? -segLen * 1.1f / 3f
                    : -(segments - 1) * 0.5f * segLen - segLen * 0.55f;
                var caudal = NewPivot(tailFinParent, "FinTail",
                    new Vector3(0f, _tailChain.Count > 0 ? 0f : bodyY, tailFinZ));
                _fins.Add(new FinRig { Pivot = caudal, Kind = AddCaudalBlade(c, caudal, unit, finLen, accentMat) }); // #3975: or a fluke

                if (!c.HasCrest)
                {
                    var dorsal = NewPivot(body.transform, "FinDorsal", new Vector3(0f, bodyY + unit * 0.45f, 0f));
                    AddPartTo(dorsal, "FinDorsalBlade", new Vector3(0f, finLen * 0.4f, -finLen * 0.1f),
                        new Vector3(unit * 0.07f, finLen * 0.8f, finLen), accentMat);
                    _fins.Add(new FinRig { Pivot = dorsal, Kind = FinKind.Dorsal });
                    if (finPairs >= 3)
                    {
                        // A three-paired body also carries a second, smaller dorsal toward the tail.
                        var dorsal2 = NewPivot(body.transform, "FinDorsal2", new Vector3(0f, bodyY + unit * 0.4f, finRear - segLen * 0.2f));
                        AddPartTo(dorsal2, "FinDorsalBlade", new Vector3(0f, finLen * 0.28f, -finLen * 0.05f),
                            new Vector3(unit * 0.07f, finLen * 0.55f, finLen * 0.7f), accentMat);
                        _fins.Add(new FinRig { Pivot = dorsal2, Kind = FinKind.Dorsal });
                    }
                }
            }

            // Dorsal crest: a row of spiny plates along the spine, tallest at the shoulders — silhouette variety.
            // (A feathered species wears a feather crest on its head instead.)
            if (c.HasCrest && !_feathered)
            {
                var crestMat = _coat.Pattern == CoatPattern.CrestBand || _coat.Glow == GlowPattern.Tips
                    ? accentMat
                    : Lit(c.Glows ? baseColor * 1.4f : baseColor * 0.7f, null);
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
                        3, unit * 0.16f, tentLen * 0.4f, 0.25f, accentMat));
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
                FxLightBridge.Mirror(_glow); // #2151: a glowing creature lights its surroundings
                _glow.type = LightType.Point;
                _glow.range = unit * 6f;
                _glow.intensity = 1.1f;
                _glow.color = Rgb(c.ColorRgb);
                _glow.shadows = LightShadows.None;
            }

            FinishCoat(root, c, body.transform, unit);

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

            // Rounded (sphere) eyes — bigger (B17); since #2866 the sphere's front carries the species' eye look (iris,
            // pupil and highlight drawn as a texture) instead of a pupil and a glint sphere.
            var eyeMat = EyeFaceMaterial(c);
            var skinMat = _coat.Mat(CoatPart.Jaw); // stalks and lids: the head's hide, never the pattern
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
                        new Vector3(eyeSize * 0.28f, stalkH, eyeSize * 0.28f), skinMat);
                    AddEye(_headPivot, "Eye" + e, top, eyeSize * 0.9f, Vector3.forward, eyeMat);
                    AddEyelid(top, eyeSize * 0.9f, skinMat);
                }
            }
            else
            {
                float spread = unit * headScale * (0.30f + 0.05f * eyes); // wider span the more eyes there are
                for (int e = 0; e < eyes; e++)
                {
                    float fx = eyes == 1 ? 0f : Mathf.Lerp(-spread, spread, e / (float)(eyes - 1));
                    // #2866: in front of the face box, so the eye's front (iris and pupil) shows — the classic spot put the
                    // whole sphere inside a small head (only the old pupil sphere poked out, and not on every head).
                    float ez = unit * 0.70f * headScale;
                    if (!float.IsNaN(_faceZ))
                    {
                        ez = Mathf.Max(ez, _faceZ - eyeSize * 0.12f);
                    }

                    var pos = new Vector3(fx, unit * 0.16f * headScale, ez);
                    AddEye(_headPivot, "Eye" + e, pos, eyeSize, Vector3.forward, eyeMat);
                    AddEyelid(pos, eyeSize, skinMat);
                }
            }
        }
    }
}
