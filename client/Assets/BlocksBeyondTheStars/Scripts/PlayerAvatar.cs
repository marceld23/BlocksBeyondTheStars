// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A blocky humanoid avatar built from cubes in code (M23b; proportions + jointed limbs overhaul).
    /// The body has a head + neck, a tapered torso (chest/abdomen/pelvis), and two-segment arms and legs
    /// that bend at the elbow/knee. Limbs hang from shoulder/hip <b>pivots</b> so the avatar animates
    /// procedurally: a speed-scaled walk/run cycle with knee + elbow bends, an idle sway with a slow
    /// look-around, a jump/fall tuck, and a tool/weapon <see cref="Swing"/> chop. Per-part colours come
    /// from <see cref="ClientSettings"/> (or explicit colours for remotes / NPCs); equipped gear and a
    /// held item are layered on via <see cref="SetGear"/> / <see cref="SetHeldItem"/>.
    ///
    /// <b>Spacesuit mode</b> (players only — local third-person, remotes, avatar-editor previews): the same
    /// body wears a suit so players read as astronauts and are instantly distinguishable from civilian NPCs
    /// (who keep the bare-headed look): gloved hands + a suit-coloured neck seal instead of bare skin, an
    /// open helmet shell with a raised glossy visor band (the face — including a custom pixel face — stays
    /// visible through the opening), a chest control panel and a life-support backpack. Suit parts reuse the
    /// torso/arm materials, so the player's chosen colours tint the suit.
    /// </summary>
    public sealed class PlayerAvatar : MonoBehaviour
    {
        private const float SwingDuration = 0.42f;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private Material _skin, _torso, _arms, _legs;

        private Transform _head, _armL, _armR, _legL, _legR;
        private Transform _elbowL, _elbowR, _kneeL, _kneeR, _handR;
        private readonly List<GameObject> _gear = new List<GameObject>();
        private GameObject _held;
        private bool _visible = true;

        private bool _suit; // spacesuit mode (players); NPCs keep the civilian bare-headed look
        private readonly List<GameObject> _suitPack = new List<GameObject>(); // hidden while armor-pack gear is worn
        private bool _gearPack; // armor pack currently worn — suppresses the suit pack (also across SetVisible)

        // Custom pixel face (FaceEditor): a textured plate on the head front that replaces the procedural
        // eyes/brow/mouth/visor when set. Placed in head-LOCAL space, where the head is a unit-cube primitive
        // (scaled by 0.46) — so its FRONT surface is at local z = 0.5. The plate centre must therefore sit at
        // ~0.5 so its front face protrudes past that surface; the old 0.27 left the whole plate buried inside
        // the opaque head (front at 0.295 < 0.5), which is why a drawn face showed nothing. Nudge if a build
        // shows it floating/clipping.
        private const float FacePlateZ = 0.5f;        // head front (local) — plate front (0.5 + half-depth) sits just proud
        private const float FacePlateScale = 0.9f;    // covers the face, leaving a thin skin border
        private readonly List<GameObject> _faceFeatures = new List<GameObject>();
        private GameObject _facePlate;
        private Material _faceMat;
        private Texture2D _faceTex;
        private string _faceString = string.Empty;
        private Color _skinColor = new Color(0.85f, 0.68f, 0.55f); // original (pre-sRGB) skin tone, for face compositing

        private float _phase;     // per-instance offset so avatars don't move in lockstep
        private Vector3 _lastPos;
        private float _prevY;
        private bool _hasPrev;
        private float _walkPhase;
        private float _swingTimer;

        public void Build(ClientSettings s) => Build(s.SkinColor, s.TorsoColor, s.ArmColor, s.LegColor, spacesuit: true);

        public void Build(Color skin, Color torso, Color arms, Color legs, bool spacesuit = false, int variantSeed = 0, Color? hair = null)
        {
            EnsureTextures();
            _suit = spacesuit;
            _phase = (GetEntityId().GetHashCode() & 0x3ff) * 0.11f;
            _skinColor = skin;
            _skin = Lit(skin, _skinTex);
            _torso = Lit(torso, _suitTex);
            _arms = Lit(arms, _suitTex);
            _legs = Lit(legs, _suitTex);

            // Per-NPC face variation (#711): tiny deterministic offsets from the seed so a settlement crowd
            // isn't a row of identical clones. Seed 0 (players, previews) keeps the exact stock face.
            float eyeDX = 0.11f, eyeY = 0.075f, browY = 0.175f, browW = 0.38f;
            float mouthW = 0.20f, mouthY = -0.175f, pupilW = 0.085f;
            if (variantSeed != 0)
            {
                eyeDX += Jit(variantSeed, 1) * 0.02f;
                eyeY += Jit(variantSeed, 2) * 0.02f;
                browY += Jit(variantSeed, 3) * 0.025f;
                browW += Jit(variantSeed, 4) * 0.06f;
                mouthW += Jit(variantSeed, 5) * 0.06f;
                mouthY += Jit(variantSeed, 6) * 0.015f;
                pupilW += Jit(variantSeed, 7) * 0.012f;
            }

            // Torso, tapered: pelvis → abdomen → wider chest.
            AddCube("Pelvis", transform, new Vector3(0f, 0.97f, 0f), new Vector3(0.46f, 0.22f, 0.28f), _legs);
            AddCube("Abdomen", transform, new Vector3(0f, 1.18f, 0f), new Vector3(0.46f, 0.26f, 0.30f), _torso);
            AddCube("Chest", transform, new Vector3(0f, 1.45f, 0.0f), new Vector3(0.58f, 0.40f, 0.34f), _torso);
            AddCube("ShoulderL", transform, new Vector3(-0.30f, 1.55f, 0f), new Vector3(0.18f, 0.18f, 0.30f), _torso);
            AddCube("ShoulderR", transform, new Vector3(0.30f, 1.55f, 0f), new Vector3(0.18f, 0.18f, 0.30f), _torso);

            // Neck + head + a dark visor strip on the front. Suited players get a suit-coloured neck seal
            // (no bare skin between collar and helmet); NPCs keep the skin neck.
            AddCube("Neck", transform, new Vector3(0f, 1.69f, 0f), new Vector3(0.18f, 0.14f, 0.18f), _suit ? _torso : _skin);
            _head = AddCube("Head", transform, new Vector3(0f, 1.86f, 0f), new Vector3(0.46f, 0.46f, 0.46f), _skin).transform;
            // Face features sit on the head's FRONT surface. The head is a unit-cube primitive, so that surface is
            // at head-LOCAL z = 0.5; features must protrude past it (z ≳ 0.5). The old z ≈ 0.235–0.275 placed them
            // at ~half depth, buried INSIDE the opaque head — the real reason "the face read as blank before".
            // Visor = a lower-face breather strip (below the eyes).
            _faceFeatures.Add(AddCube("Visor", _head, new Vector3(0f, -0.10f, 0.49f), new Vector3(0.34f, 0.12f, 0.05f), Lit(new Color(0.12f, 0.5f, 0.62f), _visorTex)));

            // Eyes (whites + pupils + a brow + a mouth) so the face reads clearly — bigger/clearer (B20).
            var eyeWhite = Lit(new Color(0.96f, 0.97f, 1f), null);
            var pupil = Lit(new Color(0.04f, 0.04f, 0.07f), null);
            var brow = hair is { } bc ? Lit(bc, null) : Lit(new Color(0.18f, 0.14f, 0.11f), null);
            var mouth = Lit(new Color(0.32f, 0.16f, 0.14f), null);
            // Default procedural features — collected so a custom pixel face (SetFace) can hide them. The
            // visor (above) is included so a drawn face fully replaces the stock look.
            // z values are head-LOCAL and must clear the head front (0.5); the +0.255 shift over the old
            // 0.235–0.275 lifts them just proud of the surface while preserving the relief (pupils ahead of the
            // whites, etc.). See the Visor note above.
            _faceFeatures.Add(AddCube("EyeL", _head, new Vector3(-eyeDX, eyeY, 0.50f), new Vector3(0.17f, 0.13f, 0.05f), eyeWhite));
            _faceFeatures.Add(AddCube("EyeR", _head, new Vector3(eyeDX, eyeY, 0.50f), new Vector3(0.17f, 0.13f, 0.05f), eyeWhite));
            _faceFeatures.Add(AddCube("PupilL", _head, new Vector3(-eyeDX, eyeY - 0.015f, 0.53f), new Vector3(pupilW, 0.10f, 0.03f), pupil));
            _faceFeatures.Add(AddCube("PupilR", _head, new Vector3(eyeDX, eyeY - 0.015f, 0.53f), new Vector3(pupilW, 0.10f, 0.03f), pupil));
            _faceFeatures.Add(AddCube("Brow", _head, new Vector3(0f, browY, 0.50f), new Vector3(browW, 0.05f, 0.045f), brow));
            _faceFeatures.Add(AddCube("Mouth", _head, new Vector3(0f, mouthY, 0.49f), new Vector3(mouthW, 0.045f, 0.04f), mouth));

            // Optional hair cap + back (civilian NPCs): head-LOCAL units — the head is a 0.46-scaled unit
            // cube, so anything wrapping its ±0.5 surfaces needs a scale > 1 (see the face-feature note).
            // NOT in _faceFeatures: a custom pixel face replaces the face, not the hair.
            if (hair is { } hairCol)
            {
                var hairMat = Lit(hairCol, null);
                AddCube("HairTop", _head, new Vector3(0f, 0.56f, -0.02f), new Vector3(1.08f, 0.14f, 1.06f), hairMat);
                AddCube("HairBack", _head, new Vector3(0f, 0.18f, -0.53f), new Vector3(1.08f, 0.92f, 0.10f), hairMat);
            }

            // Jointed arms (shoulder → elbow → hand) and legs (hip → knee → foot).
            _armL = AddArm("ArmLeft", -0.32f, out _elbowL, out _);
            _armR = AddArm("ArmRight", 0.32f, out _elbowR, out _handR);
            _legL = AddLeg("LegLeft", -0.13f, out _kneeL);
            _legR = AddLeg("LegRight", 0.13f, out _kneeR);

            if (_suit)
            {
                BuildSuit();
            }
        }

        /// <summary>
        /// The spacesuit layer for players: an OPEN helmet shell (top/back/sides/chin) with a raised glossy
        /// visor band above the eyes, a collar ring, a chest control panel and a life-support backpack.
        /// Helmet parts are children of the head (they follow the idle look-around) and use head-LOCAL units —
        /// the head is a unit cube scaled 0.46, so its surfaces are at ±0.5 and anything wrapping it needs a
        /// scale &gt; 1 (see the face-feature note above; the pre-suit gear helmet got this wrong and was
        /// buried invisibly inside the head). The front stays open so the face — procedural or custom pixel
        /// (<see cref="SetFace"/>) — remains visible; there is no transparent shader to see through a closed
        /// visor (LitColor is opaque-only), which is why the visor is styled as flipped up.
        /// </summary>
        private void BuildSuit()
        {
            // Helmet shell in the torso material: tints with the player's suit colour.
            AddCube("SuitHelmetTop", _head, new Vector3(0f, 0.56f, 0f), new Vector3(1.16f, 0.16f, 1.16f), _torso);
            AddCube("SuitHelmetBack", _head, new Vector3(0f, 0.04f, -0.55f), new Vector3(1.16f, 1.2f, 0.14f), _torso);
            AddCube("SuitHelmetL", _head, new Vector3(-0.55f, 0.04f, 0.03f), new Vector3(0.14f, 1.2f, 1.1f), _torso);
            AddCube("SuitHelmetR", _head, new Vector3(0.55f, 0.04f, 0.03f), new Vector3(0.14f, 1.2f, 1.1f), _torso);
            AddCube("SuitHelmetChin", _head, new Vector3(0f, -0.56f, 0.03f), new Vector3(1.16f, 0.14f, 1.1f), _torso);

            // Raised visor band across the forehead — dark glossy glass, clear of the brow (brow top ≈ 0.20).
            AddCube("SuitVisorBand", _head, new Vector3(0f, 0.36f, 0.52f), new Vector3(1.0f, 0.3f, 0.12f),
                Lit(new Color(0.10f, 0.22f, 0.28f), _visorTex));

            // Collar ring where the helmet locks onto the suit (world units — child of the body root).
            AddCube("SuitCollar", transform, new Vector3(0f, 1.645f, 0f), new Vector3(0.32f, 0.11f, 0.32f), _torso);

            // Chest control panel + a small status light.
            var panel = Lit(new Color(0.15f, 0.17f, 0.20f), _armorTex);
            AddCube("SuitChestPanel", transform, new Vector3(0f, 1.50f, 0.185f), new Vector3(0.22f, 0.14f, 0.04f), panel);
            AddCube("SuitStatusLight", transform, new Vector3(0.075f, 1.50f, 0.205f), new Vector3(0.045f, 0.045f, 0.02f),
                Lit(new Color(0.30f, 0.90f, 0.50f), null));

            // Life-support backpack with twin tanks; swapped out for the armor pack gear when carried.
            var packMat = Lit(new Color(0.30f, 0.34f, 0.40f), _armorTex);
            var tankMat = Lit(new Color(0.55f, 0.58f, 0.62f), _armorTex);
            _suitPack.Add(AddCube("SuitPack", transform, new Vector3(0f, 1.40f, -0.24f), new Vector3(0.38f, 0.48f, 0.15f), packMat));
            _suitPack.Add(AddCube("SuitTankL", transform, new Vector3(-0.09f, 1.42f, -0.33f), new Vector3(0.10f, 0.34f, 0.06f), tankMat));
            _suitPack.Add(AddCube("SuitTankR", transform, new Vector3(0.09f, 1.42f, -0.33f), new Vector3(0.10f, 0.34f, 0.06f), tankMat));
        }

        private Transform AddArm(string name, float x, out Transform elbow, out Transform hand)
        {
            var shoulder = NewPivot(name, transform, new Vector3(x, 1.5f, 0f));
            AddCube(name + "Upper", shoulder, new Vector3(0f, -0.21f, 0f), new Vector3(0.16f, 0.42f, 0.16f), _arms);
            elbow = NewPivot(name + "Elbow", shoulder, new Vector3(0f, -0.42f, 0f));
            AddCube(name + "Lower", elbow, new Vector3(0f, -0.21f, 0f), new Vector3(0.15f, 0.42f, 0.15f), _arms);
            hand = NewPivot(name + "Hand", elbow, new Vector3(0f, -0.44f, 0f));
            // Suited players wear gloves (arm colour); NPCs keep bare hands.
            AddCube(name + "HandMesh", hand, new Vector3(0f, -0.06f, 0f), new Vector3(0.2f, 0.16f, 0.2f), _suit ? _arms : _skin);
            return shoulder;
        }

        private Transform AddLeg(string name, float x, out Transform knee)
        {
            var hip = NewPivot(name, transform, new Vector3(x, 0.92f, 0f));
            AddCube(name + "Upper", hip, new Vector3(0f, -0.23f, 0f), new Vector3(0.22f, 0.46f, 0.22f), _legs);
            knee = NewPivot(name + "Knee", hip, new Vector3(0f, -0.46f, 0f));
            AddCube(name + "Lower", knee, new Vector3(0f, -0.21f, 0f), new Vector3(0.2f, 0.42f, 0.2f), _legs);
            AddCube(name + "Foot", knee, new Vector3(0f, -0.44f, 0.06f), new Vector3(0.24f, 0.12f, 0.32f), _legs);
            return hip;
        }

        private static Transform NewPivot(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        private GameObject AddCube(string partName, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col); // visual only — must not interfere with the CharacterController
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.enabled = _visible;
            _renderers.Add(r);
            return go;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _armR == null)
            {
                return;
            }

            // Self-drive from world movement so it works for the local player, remotes and NPCs alike.
            var pos = transform.position;
            float speed = 0f, vy = 0f;
            if (_hasPrev)
            {
                var d = pos - _lastPos;
                vy = (pos.y - _prevY) / dt;
                d.y = 0f;
                speed = d.magnitude / dt;
            }

            _lastPos = pos;
            _prevY = pos.y;
            _hasPrev = true;

            float moving = Mathf.Clamp01(speed / 3.2f); // legs reach a full stride a bit sooner (slow NPC strolls read as walking)
            float run = Mathf.Clamp01((speed - 4.5f) / 4f); // extra emphasis once sprinting
            _walkPhase += dt * (4f + speed * 1.4f);
            float t = Time.time + _phase;

            float swing = Mathf.Sin(_walkPhase) * Mathf.Lerp(2f, 40f, moving);
            float armL = -swing, armR = swing, legL = swing, legR = -swing;
            float kneeL = 0f, kneeR = 0f, elbowL = 12f + run * 25f, elbowR = 12f + run * 25f;
            float headYaw = 0f;

            bool airborne = Mathf.Abs(vy) > 2.6f;
            bool idle = moving < 0.03f && !airborne;

            if (airborne)
            {
                // Jump/fall tuck: legs up + bent, arms raised a little.
                legL = legR = -38f;
                kneeL = kneeR = 55f;
                armL = -48f; armR = -48f;
                elbowL = elbowR = 35f;
            }
            else if (idle)
            {
                float breath = Mathf.Sin(Time.time * 1.5f) * 2.5f; // idle sway
                armL = -breath; armR = breath; legL = 0f; legR = 0f;
                headYaw = Mathf.Sin(t * 0.5f) * 9f;                 // slow look-around
            }
            else
            {
                // Walk/run: the recovering leg bends at the knee, arms counter-swing with elbow bend.
                kneeL = Mathf.Max(0f, Mathf.Sin(_walkPhase)) * Mathf.Lerp(10f, 45f, moving);
                kneeR = Mathf.Max(0f, -Mathf.Sin(_walkPhase)) * Mathf.Lerp(10f, 45f, moving);
                elbowL += Mathf.Max(0f, -Mathf.Sin(_walkPhase)) * 22f * moving;
                elbowR += Mathf.Max(0f, Mathf.Sin(_walkPhase)) * 22f * moving;
            }

            // Tool/weapon chop overrides the right arm: raise, then drive down (with an elbow snap).
            if (_swingTimer > 0f)
            {
                _swingTimer -= dt;
                float c = 1f - Mathf.Clamp01(_swingTimer / SwingDuration);
                armR = Mathf.Lerp(-115f, 30f, Mathf.SmoothStep(0f, 1f, c));
                elbowR = 20f + 45f * Mathf.Sin(Mathf.Clamp01(c) * Mathf.PI);
            }

            _armL.localRotation = Quaternion.Euler(armL, 0f, 0f);
            _armR.localRotation = Quaternion.Euler(armR, 0f, 0f);
            _legL.localRotation = Quaternion.Euler(legL, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(legR, 0f, 0f);
            if (_elbowL != null) _elbowL.localRotation = Quaternion.Euler(elbowL, 0f, 0f);
            if (_elbowR != null) _elbowR.localRotation = Quaternion.Euler(elbowR, 0f, 0f);
            if (_kneeL != null) _kneeL.localRotation = Quaternion.Euler(-kneeL, 0f, 0f); // bends the lower leg back
            if (_kneeR != null) _kneeR.localRotation = Quaternion.Euler(-kneeR, 0f, 0f);
            if (_head != null) _head.localRotation = Quaternion.Euler(0f, headYaw, 0f);
        }

        /// <summary>Plays a tool/weapon swing of the right arm (mining, attacking, placing). Re-calling
        /// while a swing is in progress is ignored, so holding to drill produces a continuous chop.</summary>
        public void Swing()
        {
            if (_swingTimer <= 0f)
            {
                _swingTimer = SwingDuration;
            }
        }

        /// <summary>Shows the held tool/weapon/block in the right hand (call only when it changes).</summary>
        public void SetHeldItem(HeldItem.Kind kind, Color tint, string blockKey = null)
        {
            if (_handR == null)
            {
                return;
            }

            if (_held != null)
            {
                Destroy(_held);
                _held = null;
            }

            if (kind == HeldItem.Kind.None)
            {
                return;
            }

            _held = HeldItem.Build(_handR, kind, tint, blockKey);
            if (_held != null)
            {
                _held.transform.localPosition = new Vector3(0f, -0.1f, 0.06f); // in the palm, pointing forward
                ApplyHeldVisible();
            }
        }

        private void ApplyHeldVisible()
        {
            if (_held == null)
            {
                return;
            }

            foreach (var r in _held.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = _visible;
            }
        }

        /// <summary>
        /// Layers equipped gear over the body: a helmet shell, a chest plate, leg plates, a back
        /// pack/tank and a helmet lamp. Rebuilds the gear set from the flags (cheap; only on change).
        /// </summary>
        public void SetGear(bool helmet, bool chest, bool legs, bool pack, bool lamp = false)
        {
            if (_head == null)
            {
                return;
            }

            foreach (var g in _gear)
            {
                if (g != null)
                {
                    Destroy(g);
                }
            }

            _gear.Clear();

            var plate = Lit(new Color(0.62f, 0.66f, 0.72f), _armorTex);
            var packMat = Lit(new Color(0.30f, 0.34f, 0.40f), _armorTex);

            if (helmet)
            {
                // An open armor shell OUTSIDE the suit helmet, so the face (and a custom pixel face) stays
                // visible. Head-LOCAL units: the head is a 0.46-scaled unit cube, so wrapping it needs scales
                // > 1 — the old single 0.54-cube was smaller than the head itself and sat buried invisibly
                // inside it (same trap as the face features, see the Build note).
                _gear.Add(AddCube("GearHelmetTop", _head, new Vector3(0f, 0.68f, 0f), new Vector3(1.34f, 0.16f, 1.34f), plate));
                _gear.Add(AddCube("GearHelmetBack", _head, new Vector3(0f, 0.08f, -0.66f), new Vector3(1.34f, 1.36f, 0.14f), plate));
                _gear.Add(AddCube("GearHelmetL", _head, new Vector3(-0.66f, 0.08f, 0f), new Vector3(0.14f, 1.36f, 1.2f), plate));
                _gear.Add(AddCube("GearHelmetR", _head, new Vector3(0.66f, 0.08f, 0f), new Vector3(0.14f, 1.36f, 1.2f), plate));
            }

            if (chest)
            {
                _gear.Add(AddCube("GearChest", transform, new Vector3(0f, 1.45f, 0.02f), new Vector3(0.62f, 0.42f, 0.38f), plate));
            }

            if (legs)
            {
                _gear.Add(AddCube("GearLegL", _legL, new Vector3(0f, -0.22f, 0f), new Vector3(0.28f, 0.46f, 0.28f), plate));
                _gear.Add(AddCube("GearLegR", _legR, new Vector3(0f, -0.22f, 0f), new Vector3(0.28f, 0.46f, 0.28f), plate));
            }

            if (pack)
            {
                _gear.Add(AddCube("GearPack", transform, new Vector3(0f, 1.4f, -0.24f), new Vector3(0.4f, 0.5f, 0.2f), packMat));
            }

            // The armor pack replaces the suit's life-support pack (they occupy the same spot on the back).
            _gearPack = pack;
            ApplySuitPackVisible();

            if (lamp)
            {
                // A small bright lamp on the side of the helmet (the actual light cone is the suit lamp).
                // Head-LOCAL units — outside the suit/armor helmet side plates (see the helmet note above).
                _gear.Add(AddCube("GearLamp", _head, new Vector3(0.70f, 0.16f, 0.30f), new Vector3(0.22f, 0.22f, 0.26f),
                    Lit(new Color(1f, 0.96f, 0.7f), null)));
            }
        }

        /// <summary>Re-applies the per-part colours (e.g. after the player changed them in settings).</summary>
        public void ApplyColors(ClientSettings s) => ApplyColors(s.SkinColor, s.TorsoColor, s.ArmColor, s.LegColor);

        /// <summary>Re-applies explicit per-part colours (used by the avatar designer preview).</summary>
        public void ApplyColors(Color skin, Color torso, Color arms, Color legs)
        {
            if (_skin == null)
            {
                return;
            }

            _skinColor = skin;
            _skin.color = skin;
            _torso.color = torso;
            _arms.color = arms;
            _legs.color = legs;

            // A custom face composites its transparent pixels onto the skin, so re-bake it when skin changes.
            if (!FacePalette.IsEmpty(_faceString))
            {
                SetFace(_faceString);
            }
        }

        /// <summary>Applies a custom pixel face (drawn in the <see cref="FaceEditor"/>, encoded by
        /// <see cref="FacePalette"/>). A non-empty face shows a textured plate on the head front and hides the
        /// stock eyes/brow/mouth/visor; an empty face restores the default look. Safe to call before/after
        /// <see cref="SetVisible"/>.</summary>
        public void SetFace(string face)
        {
            _faceString = face ?? string.Empty;

            if (FacePalette.IsEmpty(_faceString))
            {
                ApplyFaceVisibility();
                return;
            }

            EnsureFacePlate();
            if (_faceTex != null)
            {
                Destroy(_faceTex);
            }

            _faceTex = FacePalette.BuildAvatarTexture(_faceString, _skinColor);
            if (_faceMat != null)
            {
                _faceMat.mainTexture = _faceTex;
            }

            ApplyFaceVisibility();
        }

        private void EnsureFacePlate()
        {
            if (_facePlate != null || _head == null)
            {
                return;
            }

            _facePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _facePlate.name = "FacePlate";
            var col = _facePlate.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col); // visual only
            }

            _facePlate.transform.SetParent(_head, false);
            _facePlate.transform.localPosition = new Vector3(0f, 0f, FacePlateZ);
            _facePlate.transform.localScale = new Vector3(FacePlateScale, FacePlateScale, 0.05f);
            _faceMat = Lit(Color.white, null); // white tint so the face texture shows its true colours
            _facePlate.GetComponent<Renderer>().sharedMaterial = _faceMat;
        }

        /// <summary>Reconciles the face plate + stock-feature renderers with the current visibility and whether
        /// a custom face is set. Idempotent — called from both <see cref="SetFace"/> and <see cref="SetVisible"/>.</summary>
        private void ApplyFaceVisibility()
        {
            bool custom = !FacePalette.IsEmpty(_faceString);
            foreach (var f in _faceFeatures)
            {
                if (f != null)
                {
                    var r = f.GetComponent<Renderer>();
                    if (r != null)
                    {
                        r.enabled = _visible && !custom;
                    }
                }
            }

            if (_facePlate != null)
            {
                var pr = _facePlate.GetComponent<Renderer>();
                if (pr != null)
                {
                    pr.enabled = _visible && custom;
                }
            }
        }

        private void OnDestroy()
        {
            if (_faceTex != null)
            {
                Destroy(_faceTex);
            }
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            foreach (var r in _renderers)
            {
                if (r != null)
                {
                    r.enabled = visible;
                }
            }

            ApplyHeldVisible();
            ApplyFaceVisibility(); // re-suppress stock features / show the plate when a custom face is set
            ApplySuitPackVisible(); // re-suppress the suit pack while the armor pack is worn
        }

        /// <summary>Reconciles the suit life-support pack with visibility and the armor-pack gear (which
        /// replaces it on the back). Idempotent — called from <see cref="SetGear"/> and <see cref="SetVisible"/>.</summary>
        private void ApplySuitPackVisible()
        {
            foreach (var p in _suitPack)
            {
                if (p != null)
                {
                    var r = p.GetComponent<Renderer>();
                    if (r != null)
                    {
                        r.enabled = _visible && !_gearPack;
                    }
                }
            }
        }

        // Shared (loaded once) tintable grayscale textures for the suit/armor/visor/skin.
        private static Texture2D _suitTex, _armorTex, _visorTex, _skinTex;
        private static bool _texLoaded;

        private static void EnsureTextures()
        {
            if (_texLoaded)
            {
                return;
            }

            _texLoaded = true;
            _suitTex = LoadTex("avatar_suit");
            _armorTex = LoadTex("avatar_armor");
            _visorTex = LoadTex("avatar_visor");
            _skinTex = LoadTex("avatar_skin");
        }

        private static Texture2D LoadTex(string key)
        {
            var asset = Resources.Load<TextAsset>("textures/" + key);
            if (asset == null || asset.bytes.Length != 64 * 64 * 4)
            {
                return null;
            }

            // Normalise the greyscale tint texture toward white (#711): the authored bytes average ~100/255,
            // and LitColor computes _Color * tex — so every avatar surface rendered at ~40 % of its tint's
            // perceptual brightness and whole outfits sank to near-black. Scaling the mean to ~200/255 keeps
            // the pixel detail (weave, panels) but stops the texture eating the colour.
            var data = (byte[])asset.bytes.Clone();
            long sum = 0;
            for (int i = 0; i < data.Length; i += 4)
            {
                sum += data[i] + data[i + 1] + data[i + 2];
            }

            float mean = sum / (data.Length * 3f / 4f);
            if (mean > 1f && mean < 200f)
            {
                float k = 200f / mean;
                for (int i = 0; i < data.Length; i += 4)
                {
                    data[i] = (byte)Mathf.Min(255f, data[i] * k);
                    data[i + 1] = (byte)Mathf.Min(255f, data[i + 1] * k);
                    data[i + 2] = (byte)Mathf.Min(255f, data[i + 2] * k);
                }
            }

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            tex.LoadRawTextureData(data);
            tex.Apply();
            return tex;
        }

        /// <summary>Deterministic jitter in [-1, 1] from a (seed, salt) pair — stable across sessions and
        /// machines (unlike string hash codes), so an NPC keeps the same face every visit.</summary>
        private static float Jit(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)(salt * 19349663);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return ((h & 0xFFFF) / 32767.5f) - 1f;
            }
        }

        // Ambient floor + opposite-flank fill for avatar materials, same failure mode and values as
        // creatures (see CreatureBuilder): LitColor's single FIXED key light leaves camera-away /
        // backlit faces at the floor, and in Linear colour space the tinted suit textures then sink
        // to a full black silhouette (obvious once the whole figure is suit — no bright skin head to
        // save it). The fill lifts the flank facing away from the key without darkening anything.
        private const float AvatarFloor = 0.62f;
        private const float AvatarFill = 0.3f;

        /// <summary>A lit, tinted, optionally-textured material (the grayscale texture tints by the colour).</summary>
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
                m.SetFloat("_Floor", AvatarFloor); // no-op on the Unlit/Color fallback
                m.SetFloat("_Fill", AvatarFill);
            }

            return m;
        }
    }
}
