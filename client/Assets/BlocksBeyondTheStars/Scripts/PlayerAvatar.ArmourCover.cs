// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3861 (Justus, 2026-11-20: "an armour always looks the same in shape, and it covers only 2/4 of the body instead of
    /// 3/4"): worn armour now covers most of the figure, and every set has its own outline.
    /// <list type="bullet">
    /// <item><b>Coverage</b>: an armour chest piece also plates the upper arms, the forearms (bracers above the wrist, so a
    /// glove cuff stays free) and the waist down to the hips; an armour leg piece also plates the shins. The boxes are
    /// <see cref="AvatarArmourFit"/>'s, held clear of the body's faces by its test.</item>
    /// <item><b>Outline</b>: the classic plates stay plain; the titanium set wears a visor brim under its crest and two-tier
    /// shoulder pads; the guardian set cheek guards, a row of short fins over the crown and raised, stepped shoulder pads;
    /// the obsidian set domed shoulders (its tall ear fins were its own already); the Ressaw suit keeps its round dome.</item>
    /// <item><b>Boots</b> (#3862): the titanium boots in titanium with an ankle guard, the guardian boots dark with a red
    /// toe light, the obsidian boots glassy black with a violet seam.</item>
    /// </list>
    /// The robber disguise and the flower set are clothes, not armour, and stay as they are.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        /// <summary>The chest piece's cover over arms and waist in <paramref name="plate"/>, the bracers in
        /// <paramref name="trim"/>.</summary>
        private void AddArmourArms(Material plate, Material trim)
        {
            _gear.Add(AddCube("CoverWaist", transform, AvatarArmourFit.WaistPlate, plate));
            if (_armL == null || _armR == null || _elbowL == null || _elbowR == null)
            {
                return;
            }

            foreach (var (shoulder, elbow) in new[] { (_armL, _elbowL), (_armR, _elbowR) })
            {
                _gear.Add(AddCube("CoverArm", shoulder, AvatarArmourFit.ArmPlate, plate));
                _gear.Add(AddCube("CoverBracer", elbow, AvatarArmourFit.Bracer, trim));
            }
        }

        /// <summary>The leg piece's cover over the shins.</summary>
        private void AddArmourShins(Material plate)
        {
            if (_kneeL == null || _kneeR == null)
            {
                return;
            }

            _gear.Add(AddCube("CoverShinL", _kneeL, AvatarArmourFit.ShinPlate, plate));
            _gear.Add(AddCube("CoverShinR", _kneeR, AvatarArmourFit.ShinPlate, plate));
        }

        /// <summary>The titanium helmet's outline: a visor brim over the brow, under the crest (head-local units).</summary>
        private void AddTitanHelmetShape(Material m)
        {
            _gear.Add(AddCube("GearHelmetBrim", _head, new Vector3(0f, 0.53f, 0.66f), new Vector3(1.42f, 0.08f, 0.22f), m));
        }

        /// <summary>The titanium chest's two-tier shoulder pads (the lower tier is SetGear's).</summary>
        private void AddTitanShoulderTier(Material m)
        {
            _gear.Add(AddCube("GearShoulderTopL", transform, new Vector3(-0.36f, 1.75f, 0f), new Vector3(0.22f, 0.06f, 0.30f), m));
            _gear.Add(AddCube("GearShoulderTopR", transform, new Vector3(0.36f, 1.75f, 0f), new Vector3(0.22f, 0.06f, 0.30f), m));
        }

        /// <summary>The guardian helmet's outline: cheek guards beside the face (the face stays clear) and three short fins
        /// over the crown, front to back.</summary>
        private void AddGuardianHelmetShape()
        {
            _gear.Add(AddCube("SetHelmetCheekL", _head, new Vector3(-0.60f, -0.18f, 0.52f), new Vector3(0.16f, 0.56f, 0.20f), _setDark));
            _gear.Add(AddCube("SetHelmetCheekR", _head, new Vector3(0.60f, -0.18f, 0.52f), new Vector3(0.16f, 0.56f, 0.20f), _setDark));
            for (int i = -1; i <= 1; i++)
            {
                _gear.Add(AddCube("SetHelmetFin", _head, new Vector3(0f, 0.82f, i * 0.34f), new Vector3(0.10f, 0.14f, 0.22f), _setTrim));
            }
        }

        /// <summary>The guardian chest's raised, stepped shoulder pads over the flat ones.</summary>
        private void AddGuardianShoulderSpikes()
        {
            foreach (float side in new[] { -1f, 1f })
            {
                _gear.Add(AddCube("SetShoulderStep", transform, new Vector3(side * 0.40f, 1.76f, 0f), new Vector3(0.16f, 0.08f, 0.30f), _setDark));
                _gear.Add(AddCube("SetShoulderTip", transform, new Vector3(side * 0.44f, 1.83f, 0f), new Vector3(0.08f, 0.08f, 0.22f), _setTrim));
            }
        }

        /// <summary>The obsidian chest's domed shoulders: a narrower cap on each flat pad.</summary>
        private void AddObsidianShoulderDomes()
        {
            _gear.Add(AddCube("ObsShoulderCapL", transform, new Vector3(-0.34f, 1.75f, 0f), new Vector3(0.20f, 0.05f, 0.26f), _obsidian));
            _gear.Add(AddCube("ObsShoulderCapR", transform, new Vector3(0.34f, 1.75f, 0f), new Vector3(0.20f, 0.05f, 0.26f), _obsidian));
        }

        /// <summary>The classic boots: a wider block over each foot (knee-local, the foot hangs at −0.44) — in titanium with an
        /// ankle guard for the titanium boots (#3862), the spring boots' coils on top of the plain ones (#2295).</summary>
        private void BuildClassicBoots(int mask)
        {
            bool titan = GearLook.Has(mask, GearLook.TitanBoots);
            var m = titan ? _gearTitan : _gearPackMat;
            foreach (var (knee, side) in new[] { (_kneeL, "L"), (_kneeR, "R") })
            {
                _gear.Add(AddCube("GearBoot" + side, knee, new Vector3(0f, -0.43f, 0.05f), new Vector3(0.28f, 0.16f, 0.36f), m));
                if (titan)
                {
                    _gear.Add(AddCube("GearBootAnkle" + side, knee, new Vector3(0f, -0.33f, 0.02f), new Vector3(0.29f, 0.06f, 0.30f), m));
                }
            }

            if (GearLook.Has(mask, GearLook.SpringBoots))
            {
                _springL = BuildSpringBoot("L", _kneeL);
                _springR = BuildSpringBoot("R", _kneeR);
            }
        }

        /// <summary>#3862: the guardian boots (dark, a red light on each toe cap) and the obsidian boots (glassy black, a violet
        /// seam round the ankle). False for any other style.</summary>
        private bool BuildArmourBoots(int style)
        {
            if (_kneeL == null || _kneeR == null || (style != GearLook.StyleGuardian && style != GearLook.StyleObsidian))
            {
                return false;
            }

            bool guardian = style == GearLook.StyleGuardian;
            if (guardian)
            {
                EnsureGearSetMaterials();
            }
            else
            {
                EnsureObsidianMaterials();
            }

            var shell = guardian ? _setDark : _obsidian;
            var accent = guardian ? _setRed : _obsidianSeam;
            foreach (var knee in new[] { _kneeL, _kneeR })
            {
                _gear.Add(AddCube("SetBoot", knee, new Vector3(0f, -0.43f, 0.05f), new Vector3(0.29f, 0.16f, 0.37f), shell));
                _gear.Add(AddCube("SetBootCuff", knee, new Vector3(0f, -0.34f, 0.04f), new Vector3(0.30f, 0.05f, 0.33f), guardian ? _setTrim : _obsidian));
                _gear.Add(guardian
                    ? AddCube("SetBootLight", knee, new Vector3(0f, -0.45f, 0.24f), new Vector3(0.08f, 0.05f, 0.012f), accent)
                    : AddCube("SetBootSeam", knee, new Vector3(0f, -0.365f, 0.04f), new Vector3(0.31f, 0.015f, 0.34f), accent));
            }

            return true;
        }
    }
}
