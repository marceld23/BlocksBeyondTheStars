// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Justus' worn sets (#3290–#3292, #3337), drawn from the style bits of the <see cref="GearLook"/> mask — the same mask
    /// on you and on everybody else. <see cref="SetGear"/> asks the four Build…Set methods first; a style they draw
    /// replaces the classic piece of that slot, style 0 leaves the classic (plain or titanium) one to SetGear.
    /// <list type="bullet">
    /// <item><b>Guardian set</b>: dark-grey robot plates with SMALL self-lit red accents — a visor slit, a chest band, a dot
    /// on each knee. Red is the Guardian machines' colour (WorldEntities), so it stays a thin accent: a player must never
    /// read as an enemy machine.</item>
    /// <item><b>Robber disguise</b>: a black cloth mask over the lower face with two breathing-filter canisters and a grille
    /// (the mouth hides under it and comes back when it is taken off), and a black jacket over the torso with a turned-up
    /// collar and a zip.</item>
    /// <item><b>Flower set</b>: a green wreath round the head with eight blue blossoms (white hearts), and on the forearm ends
    /// two green bracelets with three blossoms each.</item>
    /// <item><b>Ressaw diving suit</b> (#3337): milky white plates with the purple and red trim of the G.D.S. — a
    /// round dome helmet with a big porthole that keeps the face in view, a chest plate with a glowing air gauge, leg plates
    /// and the only set boots so far (their style has the mask's fourth slot).</item>
    /// </list>
    /// Head-LOCAL units for the head pieces (the head is a 0.46-scaled unit cube, its front at z = 0.5 — see Build). The
    /// materials are this figure's own (<see cref="OwnLit"/>), made once on first use and destroyed with it.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        private Material _setDark, _setTrim, _setRed, _setCloth, _setCollar, _setFilter, _setGrille, _setBlossom, _setHeart, _setVine;
        private Material _ressawMilk, _ressawPurple, _ressawRed, _ressawGlow; // #3337
        private bool _setHidMouth; // the robber mask hid a mouth that was showing — give it back when the mask comes off

        /// <summary>The look of the head piece of <paramref name="style"/>; false for the classic one (SetGear draws it).</summary>
        private bool BuildHeadSet(int style)
        {
            switch (style)
            {
                case GearLook.StyleGuardian:
                    BuildGuardianHelmet();
                    return true;
                case GearLook.StyleRobber:
                    BuildRobberMask();
                    return true;
                case GearLook.StyleFlower:
                    BuildFlowerWreath();
                    return true;
                case GearLook.StyleRessaw:
                    BuildRessawHelmet();
                    return true;
                case GearLook.StyleObsidian: // #3354 (PlayerAvatar.Obsidian.cs)
                    BuildObsidianHelmet();
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The look of the chest piece of <paramref name="style"/>; false for the classic one.</summary>
        private bool BuildChestSet(int style)
        {
            switch (style)
            {
                case GearLook.StyleGuardian:
                    BuildGuardianChest();
                    return true;
                case GearLook.StyleRobber:
                    BuildRobberJacket();
                    return true;
                case GearLook.StyleFlower:
                    BuildFlowerBracelets();
                    return true;
                case GearLook.StyleRessaw:
                    BuildRessawChest();
                    return true;
                case GearLook.StyleObsidian:
                case GearLook.StyleObsidianGlow: // #3354: the light disc glows in the socket
                    BuildObsidianChest(style == GearLook.StyleObsidianGlow);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The look of the leg pieces of <paramref name="style"/>; false for the classic ones.</summary>
        private bool BuildLegsSet(int style)
        {
            if (_legL == null || _legR == null)
            {
                return false;
            }

            if (style == GearLook.StyleRessaw)
            {
                BuildRessawLegs();
                return true;
            }

            if (style == GearLook.StyleObsidian)
            {
                BuildObsidianLegs(); // #3354
                return true;
            }

            if (style != GearLook.StyleGuardian)
            {
                return false;
            }

            EnsureGearSetMaterials();
            _gear.Add(AddCube("SetLegL", _legL, new Vector3(0f, -0.22f, 0f), new Vector3(0.28f, 0.46f, 0.28f), _setDark));
            _gear.Add(AddCube("SetLegR", _legR, new Vector3(0f, -0.22f, 0f), new Vector3(0.28f, 0.46f, 0.28f), _setDark));
            if (_kneeL != null && _kneeR != null)
            {
                foreach (var knee in new[] { _kneeL, _kneeR })
                {
                    _gear.Add(AddCube("SetKnee", knee, new Vector3(0f, -0.02f, 0.12f), new Vector3(0.22f, 0.16f, 0.06f), _setTrim));
                    _gear.Add(AddCube("SetKneeLight", knee, new Vector3(0f, -0.02f, 0.155f), new Vector3(0.06f, 0.04f, 0.012f), _setRed));
                }
            }

            return true;
        }

        /// <summary>#3337: the look of the boots of <paramref name="style"/>; false for the classic ones (the plain or the
        /// spring boots — SetGear draws those). The Ressaw boots: milky boots over the feet like the classic ones (knee-local,
        /// the foot hangs at −0.44), a purple cuff round the ankle and a red toe plate.</summary>
        private bool BuildBootsSet(int style)
        {
            if (style != GearLook.StyleRessaw || _kneeL == null || _kneeR == null)
            {
                return false;
            }

            EnsureRessawMaterials();
            foreach (var knee in new[] { _kneeL, _kneeR })
            {
                RessawPart("Boot", knee, new Vector3(0f, -0.43f, 0.05f), new Vector3(0.29f, 0.16f, 0.37f), _ressawMilk);
                RessawPart("BootCuff", knee, new Vector3(0f, -0.34f, 0.04f), new Vector3(0.30f, 0.05f, 0.33f), _ressawPurple);
                RessawPart("BootToe", knee, new Vector3(0f, -0.45f, 0.24f), new Vector3(0.24f, 0.09f, 0.012f), _ressawRed);
            }

            return true;
        }

        /// <summary>Gives the mouth back that the robber mask hid (called at the start of every gear rebuild).</summary>
        private void RestoreGearSetFace()
        {
            if (!_setHidMouth)
            {
                return;
            }

            _setHidMouth = false;
            SetMouthShown(true);
        }

        private void SetMouthShown(bool shown)
        {
            foreach (var feature in _faceFeatures)
            {
                if (feature != null && feature.name == "Mouth")
                {
                    feature.SetActive(shown);
                }
            }
        }

        /// <summary>#3290: an open shell of dark robot plates (the face stays visible), a brow ridge with a thin red visor
        /// slit, and a sensor block over each ear.</summary>
        private void BuildGuardianHelmet()
        {
            EnsureGearSetMaterials();
            _gear.Add(AddCube("SetHelmetTop", _head, new Vector3(0f, 0.68f, 0f), new Vector3(1.34f, 0.16f, 1.34f), _setDark));
            _gear.Add(AddCube("SetHelmetBack", _head, new Vector3(0f, 0.08f, -0.66f), new Vector3(1.34f, 1.36f, 0.14f), _setDark));
            _gear.Add(AddCube("SetHelmetL", _head, new Vector3(-0.66f, 0.08f, 0f), new Vector3(0.14f, 1.36f, 1.2f), _setDark));
            _gear.Add(AddCube("SetHelmetR", _head, new Vector3(0.66f, 0.08f, 0f), new Vector3(0.14f, 1.36f, 1.2f), _setDark));
            _gear.Add(AddCube("SetHelmetBrow", _head, new Vector3(0f, 0.42f, 0.60f), new Vector3(1.34f, 0.14f, 0.16f), _setTrim));
            _gear.Add(AddCube("SetHelmetSlit", _head, new Vector3(0f, 0.42f, 0.685f), new Vector3(0.52f, 0.04f, 0.012f), _setRed));
            _gear.Add(AddCube("SetHelmetEarL", _head, new Vector3(-0.76f, 0.22f, -0.05f), new Vector3(0.08f, 0.26f, 0.26f), _setTrim));
            _gear.Add(AddCube("SetHelmetEarR", _head, new Vector3(0.76f, 0.22f, -0.05f), new Vector3(0.08f, 0.26f, 0.26f), _setTrim));
        }

        /// <summary>#3290: the dark chest plate with shoulder pads and one thin red light band across the front.</summary>
        private void BuildGuardianChest()
        {
            EnsureGearSetMaterials();
            _gear.Add(AddCube("SetChest", transform, new Vector3(0f, 1.45f, 0.02f), new Vector3(0.62f, 0.42f, 0.38f), _setDark));
            _gear.Add(AddCube("SetShoulderL", transform, new Vector3(-0.34f, 1.68f, 0f), new Vector3(0.28f, 0.10f, 0.36f), _setTrim));
            _gear.Add(AddCube("SetShoulderR", transform, new Vector3(0.34f, 1.68f, 0f), new Vector3(0.28f, 0.10f, 0.36f), _setTrim));
            _gear.Add(AddCube("SetChestBand", transform, new Vector3(0f, 1.52f, 0.215f), new Vector3(0.30f, 0.03f, 0.012f), _setRed));
        }

        /// <summary>#3291: the cloth mask over nose and mouth, the grille in front of the mouth and a filter canister on each
        /// side, tied at the back like the robbers' own (<see cref="SetRobberMask"/>). The mouth hides under it.</summary>
        private void BuildRobberMask()
        {
            EnsureGearSetMaterials();
            _gear.Add(AddCube("SetMaskWrap", _head, new Vector3(0f, -0.27f, 0f), new Vector3(1.05f, 0.48f, 1.05f), _setCloth));
            _gear.Add(AddCube("SetMaskNose", _head, new Vector3(0f, -0.09f, 0.53f), new Vector3(0.22f, 0.12f, 0.06f), _setCloth));
            _gear.Add(AddCube("SetMaskKnot", _head, new Vector3(0f, -0.12f, -0.56f), new Vector3(0.16f, 0.14f, 0.1f), _setCloth));
            _gear.Add(AddCube("SetMaskGrille", _head, new Vector3(0f, -0.30f, 0.555f), new Vector3(0.26f, 0.18f, 0.06f), _setGrille));
            for (int i = 0; i < 3; i++)
            {
                _gear.Add(AddCube("SetMaskSlot", _head, new Vector3(0f, -0.35f + (i * 0.05f), 0.59f), new Vector3(0.18f, 0.018f, 0.02f), _setFilter));
            }

            foreach (float side in new[] { -1f, 1f })
            {
                _gear.Add(AddCube("SetMaskFilter", _head, new Vector3(side * 0.33f, -0.31f, 0.56f), new Vector3(0.20f, 0.20f, 0.16f), _setFilter));
                _gear.Add(AddCube("SetMaskFilterCap", _head, new Vector3(side * 0.33f, -0.31f, 0.645f), new Vector3(0.14f, 0.14f, 0.03f), _setGrille));
            }

            foreach (var feature in _faceFeatures)
            {
                if (feature != null && feature.name == "Mouth" && feature.activeSelf)
                {
                    _setHidMouth = true; // only a mouth WE hid comes back (a robber NPC's stays under its own mask)
                }
            }

            SetMouthShown(false);
        }

        /// <summary>#3291: a black jacket a little wider than the torso, a turned-up collar behind and beside the neck and a
        /// grey zip down the front.</summary>
        private void BuildRobberJacket()
        {
            EnsureGearSetMaterials();
            _gear.Add(AddCube("SetJacket", transform, new Vector3(0f, 1.44f, 0f), new Vector3(0.64f, 0.44f, 0.40f), _setCloth));
            _gear.Add(AddCube("SetJacketHem", transform, new Vector3(0f, 1.18f, 0f), new Vector3(0.52f, 0.28f, 0.36f), _setCloth));
            _gear.Add(AddCube("SetCollarBack", transform, new Vector3(0f, 1.70f, -0.25f), new Vector3(0.50f, 0.14f, 0.05f), _setCollar));
            _gear.Add(AddCube("SetCollarL", transform, new Vector3(-0.26f, 1.69f, -0.06f), new Vector3(0.05f, 0.12f, 0.34f), _setCollar));
            _gear.Add(AddCube("SetCollarR", transform, new Vector3(0.26f, 1.69f, -0.06f), new Vector3(0.05f, 0.12f, 0.34f), _setCollar));
            _gear.Add(AddCube("SetJacketZip", transform, new Vector3(0f, 1.32f, 0.205f), new Vector3(0.03f, 0.62f, 0.012f), _setFilter));
        }

        /// <summary>#3292: a green ring round the head with eight blue blossoms on it — four at the corners, four between.</summary>
        private void BuildFlowerWreath()
        {
            EnsureGearSetMaterials();
            const float y = 0.46f, r = 0.56f;
            _gear.Add(AddCube("SetVineF", _head, new Vector3(0f, y, r), new Vector3(1.12f, 0.07f, 0.07f), _setVine));
            _gear.Add(AddCube("SetVineB", _head, new Vector3(0f, y, -r), new Vector3(1.12f, 0.07f, 0.07f), _setVine));
            _gear.Add(AddCube("SetVineL", _head, new Vector3(-r, y, 0f), new Vector3(0.07f, 0.07f, 1.12f), _setVine));
            _gear.Add(AddCube("SetVineR", _head, new Vector3(r, y, 0f), new Vector3(0.07f, 0.07f, 1.12f), _setVine));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float reach = i % 2 == 0 ? r : r * 1.41f; // the corners sit on the corners of the square ring
                AddBlossom(_head, new Vector3(dir.x * reach, y + 0.02f, dir.z * reach), dir, 0.20f);
            }
        }

        /// <summary>#3292: a green band at each forearm's end with three blue blossoms on it (outside, inside, front).</summary>
        private void BuildFlowerBracelets()
        {
            if (_handL == null || _handR == null)
            {
                return;
            }

            EnsureGearSetMaterials();
            foreach (var hand in new[] { _handL, _handR })
            {
                _gear.Add(AddCube("SetBracelet", hand, new Vector3(0f, 0.08f, 0f), new Vector3(0.19f, 0.06f, 0.19f), _setVine));
                AddBlossom(hand, new Vector3(-0.10f, 0.08f, 0f), Vector3.left, 0.08f);
                AddBlossom(hand, new Vector3(0.10f, 0.08f, 0f), Vector3.right, 0.08f);
                AddBlossom(hand, new Vector3(0f, 0.08f, 0.10f), Vector3.forward, 0.08f);
            }
        }

        /// <summary>One blossom: a blue cube with its white heart showing on the outward side.</summary>
        private void AddBlossom(Transform parent, Vector3 at, Vector3 outward, float size)
        {
            _gear.Add(AddCube("SetBlossom", parent, at, new Vector3(size, size, size), _setBlossom));
            float heart = size * 0.42f;
            _gear.Add(AddCube("SetBlossomHeart", parent, at + (outward * ((size * 0.5f) + (heart * 0.25f))), new Vector3(heart, heart, heart), _setHeart));
        }

        /// <summary>#3337: the Ressaw diving helmet — a round milky dome over the suit helmet, stepped at every edge (narrower
        /// slabs outside the crown, the back and the sides) so it reads round from every side, with a big round porthole in
        /// front (<see cref="BuildRessawPorthole"/>), open in the middle so the face stays visible (the avatar shader is
        /// opaque-only, see BuildSuit). A purple stripe runs over the crown, a red band round the lower edge and a milky
        /// Ressaw glow strip down the back. Head-LOCAL units; the right side keeps the suit lamp's spot free (head-local
        /// x 0.59–0.81, z 0.17–0.43), so the lamp still shows and shines.</summary>
        private void BuildRessawHelmet()
        {
            EnsureRessawMaterials();
            RessawPart("DomeTop", _head, new Vector3(0f, 0.70f, -0.02f), new Vector3(1.30f, 0.14f, 1.26f), _ressawMilk);
            RessawPart("DomeCap", _head, new Vector3(0f, 0.81f, -0.04f), new Vector3(0.84f, 0.08f, 0.88f), _ressawMilk);
            RessawPart("DomeBack", _head, new Vector3(0f, 0.05f, -0.68f), new Vector3(1.30f, 1.34f, 0.14f), _ressawMilk);
            RessawPart("DomeBackRound", _head, new Vector3(0f, 0.08f, -0.79f), new Vector3(0.84f, 0.96f, 0.08f), _ressawMilk);
            foreach (float side in new[] { -1f, 1f })
            {
                RessawPart("DomeSide", _head, new Vector3(side * 0.68f, 0.05f, -0.02f), new Vector3(0.14f, 1.34f, 1.22f), _ressawMilk);
                RessawPart("DomeSideRound", _head, new Vector3(side * 0.79f, 0.08f, -0.20f), new Vector3(0.08f, 0.96f, 0.70f), _ressawMilk);
                RessawPart("Cheek", _head, new Vector3(side * 0.635f, 0f, 0.60f), new Vector3(0.23f, 1.04f, 0.10f), _ressawMilk);
                RessawPart("Band", _head, new Vector3(side * 0.755f, -0.50f, -0.02f), new Vector3(0.02f, 0.06f, 1.22f), _ressawRed);
            }

            RessawPart("Brow", _head, new Vector3(0f, 0.64f, 0.60f), new Vector3(1.50f, 0.24f, 0.10f), _ressawMilk);
            RessawPart("Chin", _head, new Vector3(0f, -0.57f, 0.60f), new Vector3(1.50f, 0.10f, 0.10f), _ressawMilk);
            RessawPart("BandBack", _head, new Vector3(0f, -0.50f, -0.755f), new Vector3(1.30f, 0.06f, 0.02f), _ressawRed);
            RessawPart("Stripe", _head, new Vector3(0f, 0.855f, -0.04f), new Vector3(0.16f, 0.03f, 0.90f), _ressawPurple);
            RessawPart("Glow", _head, new Vector3(0f, 0.10f, -0.84f), new Vector3(0.12f, 0.70f, 0.03f), _ressawGlow);
            BuildRessawPorthole();
        }

        /// <summary>#3337: the helmet's round porthole — a square purple frame round the face (head-local, its opening ±0.40,
        /// which keeps eyes, brow and mouth clear) whose inner corners four diagonal bars cut off into an octagon, and a red
        /// rivet in the middle of each side.</summary>
        private void BuildRessawPorthole()
        {
            const float z = 0.64f;
            RessawPart("FrameTop", _head, new Vector3(0f, 0.46f, z), new Vector3(1.04f, 0.12f, 0.10f), _ressawPurple);
            RessawPart("FrameBottom", _head, new Vector3(0f, -0.46f, z), new Vector3(1.04f, 0.12f, 0.10f), _ressawPurple);
            foreach (float sx in new[] { -1f, 1f })
            {
                RessawPart("FrameSide", _head, new Vector3(sx * 0.46f, 0f, z), new Vector3(0.12f, 0.80f, 0.10f), _ressawPurple);
                RessawPart("Rivet", _head, new Vector3(sx * 0.46f, 0f, z + 0.06f), new Vector3(0.07f, 0.07f, 0.03f), _ressawRed);
                RessawPart("Rivet", _head, new Vector3(0f, sx * 0.46f, z + 0.06f), new Vector3(0.07f, 0.07f, 0.03f), _ressawRed);
                foreach (float sy in new[] { -1f, 1f })
                {
                    // A corner bar runs at 45° from the top or bottom bar to the side bar: its long axis lies on the diagonal
                    // that falls toward the side (−45° upper right and lower left, +45° in the other two corners). A hair
                    // thinner than the frame, so the two never fight over the same front face.
                    var bar = RessawPart("FrameCorner", _head, new Vector3(sx * 0.355f, sy * 0.355f, z - 0.005f),
                        new Vector3(0.30f, 0.14f, 0.09f), _ressawPurple);
                    bar.transform.localRotation = Quaternion.Euler(0f, 0f, sx * sy > 0f ? -45f : 45f);
                }
            }
        }

        /// <summary>#3337: the milky chest plate with purple shoulder pads, a purple band with a red stripe across the chest, a
        /// glowing air gauge in a purple bezel turned on its corner (together they read round) and a red belt.</summary>
        private void BuildRessawChest()
        {
            EnsureRessawMaterials();
            RessawPart("Chest", transform, new Vector3(0f, 1.45f, 0.02f), new Vector3(0.62f, 0.42f, 0.38f), _ressawMilk);
            RessawPart("ShoulderL", transform, new Vector3(-0.34f, 1.68f, 0f), new Vector3(0.28f, 0.10f, 0.36f), _ressawPurple);
            RessawPart("ShoulderR", transform, new Vector3(0.34f, 1.68f, 0f), new Vector3(0.28f, 0.10f, 0.36f), _ressawPurple);
            RessawPart("ChestBand", transform, new Vector3(0f, 1.575f, 0.215f), new Vector3(0.62f, 0.05f, 0.012f), _ressawPurple);
            RessawPart("ChestStripe", transform, new Vector3(0f, 1.54f, 0.215f), new Vector3(0.62f, 0.02f, 0.012f), _ressawRed);
            var bezel = RessawPart("GaugeBezel", transform, new Vector3(0.14f, 1.39f, 0.214f), new Vector3(0.12f, 0.12f, 0.01f), _ressawPurple);
            bezel.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            RessawPart("Gauge", transform, new Vector3(0.14f, 1.39f, 0.221f), new Vector3(0.08f, 0.08f, 0.01f), _ressawGlow);
            RessawPart("Belt", transform, new Vector3(0f, 1.22f, 0f), new Vector3(0.50f, 0.05f, 0.34f), _ressawRed);
        }

        /// <summary>#3337: milky plates over thigh and shin, a red stripe down the outside of each thigh, and purple knee pads
        /// with a small milky glow.</summary>
        private void BuildRessawLegs()
        {
            EnsureRessawMaterials();
            foreach (var (hip, side) in new[] { (_legL, -1f), (_legR, 1f) })
            {
                RessawPart("Thigh", hip, new Vector3(0f, -0.22f, 0f), new Vector3(0.28f, 0.46f, 0.28f), _ressawMilk);
                RessawPart("ThighStripe", hip, new Vector3(side * 0.145f, -0.22f, 0f), new Vector3(0.012f, 0.40f, 0.08f), _ressawRed);
            }

            if (_kneeL == null || _kneeR == null)
            {
                return;
            }

            foreach (var knee in new[] { _kneeL, _kneeR })
            {
                RessawPart("Shin", knee, new Vector3(0f, -0.20f, 0f), new Vector3(0.25f, 0.36f, 0.25f), _ressawMilk);
                RessawPart("Knee", knee, new Vector3(0f, -0.02f, 0.13f), new Vector3(0.22f, 0.16f, 0.06f), _ressawPurple);
                RessawPart("KneeGlow", knee, new Vector3(0f, -0.02f, 0.165f), new Vector3(0.07f, 0.05f, 0.012f), _ressawGlow);
            }
        }

        /// <summary>One cube of the Ressaw suit, kept with the gear (SetGear destroys it with the next change).</summary>
        private GameObject RessawPart(string part, Transform parent, Vector3 at, Vector3 size, Material mat)
        {
            var go = AddCube("SetRessaw" + part, parent, at, size, mat);
            _gear.Add(go);
            return go;
        }

        /// <summary>#3337: the Ressaw suit's materials, made once per figure on first use — this figure's own
        /// (<see cref="OwnLit"/>): the milky white plates, the G.D.S. trim in purple (#4B2A78) and red (#8A1C24), and a
        /// milky glow lit fully from every side, which reads as Ressaw's own light.</summary>
        private void EnsureRessawMaterials()
        {
            if (_ressawMilk != null)
            {
                return;
            }

            _ressawMilk = OwnLit(new Color(0.91f, 0.92f, 0.97f), _armorTex);
            _ressawPurple = OwnLit(new Color(0.294f, 0.165f, 0.471f), _armorTex);
            _ressawRed = OwnLit(new Color(0.541f, 0.110f, 0.141f), null);
            _ressawGlow = OwnLit(new Color(0.94f, 0.96f, 1f), null);
            if (_ressawGlow.HasProperty("_Floor"))
            {
                _ressawGlow.SetFloat("_Floor", 1f); // fully lit from every side — a small light, like the guardian set's red
            }
        }

        /// <summary>The sets' materials, made once per figure on first use — this figure's own (<see cref="OwnLit"/>).</summary>
        private void EnsureGearSetMaterials()
        {
            if (_setDark != null)
            {
                return;
            }

            _setDark = OwnLit(new Color(0.22f, 0.23f, 0.26f), _armorTex);
            _setTrim = OwnLit(new Color(0.34f, 0.35f, 0.39f), _armorTex);
            _setRed = OwnLit(new Color(1f, 0.20f, 0.18f), null);
            if (_setRed.HasProperty("_Floor"))
            {
                _setRed.SetFloat("_Floor", 1f); // fully lit from every side — a small light, like the guardian's eyes
            }

            _setCloth = OwnLit(new Color(0.07f, 0.07f, 0.08f), _suitTex);
            _setCollar = OwnLit(new Color(0.15f, 0.15f, 0.17f), _suitTex);
            _setFilter = OwnLit(new Color(0.46f, 0.48f, 0.52f), _armorTex);
            _setGrille = OwnLit(new Color(0.10f, 0.10f, 0.11f), null);
            _setBlossom = OwnLit(new Color(0.28f, 0.50f, 1f), null);
            _setHeart = OwnLit(new Color(0.98f, 0.97f, 0.88f), null);
            _setVine = OwnLit(new Color(0.24f, 0.52f, 0.22f), null);
        }
    }
}
