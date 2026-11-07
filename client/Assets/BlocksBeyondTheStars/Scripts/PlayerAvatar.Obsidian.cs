// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Sounders' obsidian armour on the avatar (#3354), drawn from the obsidian style of the <see cref="GearLook"/> mask
    /// (<see cref="GearLook.StyleObsidian"/> in a slot) — the same mask on you and on everybody else:
    /// <list type="bullet">
    /// <item><b>Helmet</b>: an open shell of near-black glassy plates with a thin violet seam across the brow and two tall,
    /// narrow ear fins — the Sounders had long ears.</item>
    /// <item><b>Chest</b>: the dark plate with violet seams and, while a Song Disc sits in its socket
    /// (<see cref="GearLook.SocketDisc"/>), a round disc on the front. The light disc (<see cref="GearLook.StyleObsidianGlow"/>)
    /// glows and gives off a soft violet-white light (a point light mirrored into the FX lights, like a glowing creature).</item>
    /// <item><b>Legs</b>: dark plates with a violet seam at each knee.</item>
    /// </list>
    /// The materials are this figure's own (<see cref="OwnLit"/>), made once on first use and destroyed with it; the glow light
    /// is a gear part, so every gear rebuild removes it with the rest.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        private Material _obsidian, _obsidianSeam, _obsidianDisc, _obsidianDiscGlow;

        private void BuildObsidianHelmet()
        {
            EnsureObsidianMaterials();
            _gear.Add(AddCube("ObsHelmetTop", _head, new Vector3(0f, 0.68f, 0f), new Vector3(1.34f, 0.16f, 1.34f), _obsidian));
            _gear.Add(AddCube("ObsHelmetBack", _head, new Vector3(0f, 0.08f, -0.66f), new Vector3(1.34f, 1.36f, 0.14f), _obsidian));
            _gear.Add(AddCube("ObsHelmetL", _head, new Vector3(-0.66f, 0.08f, 0f), new Vector3(0.14f, 1.36f, 1.2f), _obsidian));
            _gear.Add(AddCube("ObsHelmetR", _head, new Vector3(0.66f, 0.08f, 0f), new Vector3(0.14f, 1.36f, 1.2f), _obsidian));
            _gear.Add(AddCube("ObsHelmetSeam", _head, new Vector3(0f, 0.46f, 0.605f), new Vector3(1.2f, 0.04f, 0.02f), _obsidianSeam));
            _gear.Add(AddCube("ObsEarL", _head, new Vector3(-0.78f, 0.62f, -0.12f), new Vector3(0.08f, 0.72f, 0.22f), _obsidian));
            _gear.Add(AddCube("ObsEarR", _head, new Vector3(0.78f, 0.62f, -0.12f), new Vector3(0.08f, 0.72f, 0.22f), _obsidian));
        }

        /// <summary>The chest plate; the disc in its socket while <see cref="GearLook.SocketDisc"/> is set, glowing with
        /// <paramref name="glow"/> (the light disc).</summary>
        private void BuildObsidianChest(bool glow)
        {
            EnsureObsidianMaterials();
            _gear.Add(AddCube("ObsChest", transform, new Vector3(0f, 1.45f, 0.02f), new Vector3(0.62f, 0.42f, 0.38f), _obsidian));
            _gear.Add(AddCube("ObsShoulderL", transform, new Vector3(-0.34f, 1.68f, 0f), new Vector3(0.28f, 0.10f, 0.36f), _obsidian));
            _gear.Add(AddCube("ObsShoulderR", transform, new Vector3(0.34f, 1.68f, 0f), new Vector3(0.28f, 0.10f, 0.36f), _obsidian));
            _gear.Add(AddCube("ObsChestSeamL", transform, new Vector3(-0.2f, 1.45f, 0.212f), new Vector3(0.02f, 0.34f, 0.012f), _obsidianSeam));
            _gear.Add(AddCube("ObsChestSeamR", transform, new Vector3(0.2f, 1.45f, 0.212f), new Vector3(0.02f, 0.34f, 0.012f), _obsidianSeam));
            if (!GearLook.Has(_gearMask, GearLook.SocketDisc))
            {
                return;
            }

            var disc = glow ? _obsidianDiscGlow : _obsidianDisc;
            _gear.Add(AddCube("ObsDisc", transform, new Vector3(0f, 1.47f, 0.22f), new Vector3(0.2f, 0.2f, 0.02f), disc));
            _gear.Add(AddCube("ObsDiscHub", transform, new Vector3(0f, 1.47f, 0.233f), new Vector3(0.05f, 0.05f, 0.012f), _obsidianSeam));
            if (glow)
            {
                AddObsidianGlowLight();
            }
        }

        private void BuildObsidianLegs()
        {
            EnsureObsidianMaterials();
            _gear.Add(AddCube("ObsLegL", _legL, new Vector3(0f, -0.22f, 0f), new Vector3(0.28f, 0.46f, 0.28f), _obsidian));
            _gear.Add(AddCube("ObsLegR", _legR, new Vector3(0f, -0.22f, 0f), new Vector3(0.28f, 0.46f, 0.28f), _obsidian));
            if (_kneeL != null && _kneeR != null)
            {
                foreach (var knee in new[] { _kneeL, _kneeR })
                {
                    _gear.Add(AddCube("ObsKnee", knee, new Vector3(0f, -0.02f, 0.12f), new Vector3(0.22f, 0.16f, 0.06f), _obsidian));
                    _gear.Add(AddCube("ObsKneeSeam", knee, new Vector3(0f, -0.02f, 0.155f), new Vector3(0.16f, 0.03f, 0.012f), _obsidianSeam));
                }
            }
        }

        /// <summary>The light disc's soft glow: a small violet-white point light on the chest, mirrored into the FX lights so the
        /// world around the wearer brightens a little (a gear part — the next rebuild removes it).</summary>
        private void AddObsidianGlowLight()
        {
            var go = new GameObject("ObsDiscGlow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.47f, 0.45f);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 5f;
            light.intensity = 0.9f;
            light.color = new Color(0.86f, 0.78f, 1f);
            light.shadows = LightShadows.None;
            FxLightBridge.Mirror(light);
            _gear.Add(go);
        }

        /// <summary>The obsidian materials, made once per figure on first use — this figure's own (<see cref="OwnLit"/>).</summary>
        private void EnsureObsidianMaterials()
        {
            if (_obsidian != null)
            {
                return;
            }

            _obsidian = OwnLit(new Color(0.11f, 0.09f, 0.15f), _armorTex);
            _obsidianSeam = SelfLit(new Color(0.66f, 0.48f, 1f));
            _obsidianDisc = OwnLit(new Color(0.62f, 0.60f, 0.72f), null);
            _obsidianDiscGlow = SelfLit(new Color(0.95f, 0.90f, 1f) * 1.6f);
        }

        /// <summary>A material lit from every side (<c>_Floor</c> 1) — a seam or a disc that shines by itself.</summary>
        private Material SelfLit(Color color)
        {
            var m = OwnLit(color, null);
            if (m.HasProperty("_Floor"))
            {
                m.SetFloat("_Floor", 1f);
            }

            return m;
        }
    }
}
