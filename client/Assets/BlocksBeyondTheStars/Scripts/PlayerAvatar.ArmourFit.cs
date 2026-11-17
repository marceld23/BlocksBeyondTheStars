// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How worn armour sits on the figure (#3752 — Justus: "the worn armour goes through the skin, sticks in you and
    /// flickers"). The body boxes under the plates and the plates themselves come from <see cref="AvatarArmourFit"/>
    /// (Client.Core, tested headless): no face of a plate lies in the plane of a face of the body part it covers, so the
    /// skin never flickers through a plate. The suit's chest panel and its status light — the light stuck 5 mm out of a
    /// chest plate — hide while a chest piece is worn (<see cref="ApplySuitPackVisible"/>).
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        /// <summary>The suit's chest control panel and its status light: hidden under a worn chest piece.</summary>
        private readonly List<GameObject> _suitChest = new List<GameObject>();

        /// <summary>A cube with the place and the size of <paramref name="box"/>, in <paramref name="parent"/>'s frame.</summary>
        private GameObject AddCube(string partName, Transform parent, AvatarBox box, Material mat)
            => AddCube(partName, parent, At(box), Size(box), mat);

        private static Vector3 At(AvatarBox box) => new Vector3(box.X, box.Y, box.Z);

        private static Vector3 Size(AvatarBox box) => new Vector3(box.Width, box.Height, box.Depth);

        /// <summary>Whether a chest piece of any set is worn — the suit's chest panel is under it then.</summary>
        private bool ChestPieceWorn => GearLook.Has(_gearMask, GearLook.Chest);

        /// <summary>Shows or hides a group of the suit's own parts (the life-support pack, the chest panel).</summary>
        private static void ShowSuitParts(List<GameObject> parts, bool shown)
        {
            foreach (var p in parts)
            {
                var r = p != null ? p.GetComponent<Renderer>() : null;
                if (r != null)
                {
                    r.enabled = shown;
                }
            }
        }
    }
}
