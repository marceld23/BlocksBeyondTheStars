// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Justus' three worn sets (#3290–#3292), drawn from the style bits of the <see cref="GearLook"/> mask — the same mask
    /// on you and on everybody else. <see cref="SetGear"/> asks the three Build…Set methods first; a style they draw
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
    /// </list>
    /// Head-LOCAL units for the head pieces (the head is a 0.46-scaled unit cube, its front at z = 0.5 — see Build). The
    /// materials are this figure's own (<see cref="OwnLit"/>), made once on first use and destroyed with it.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        private Material _setDark, _setTrim, _setRed, _setCloth, _setCollar, _setFilter, _setGrille, _setBlossom, _setHeart, _setVine;
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
                default:
                    return false;
            }
        }

        /// <summary>The look of the leg pieces of <paramref name="style"/>; false for the classic ones.</summary>
        private bool BuildLegsSet(int style)
        {
            if (style != GearLook.StyleGuardian || _legL == null || _legR == null)
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
