// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The escort wing's guard (#3897, the sleeper — <see cref="EscortGuardLook"/>): the full Guardian armour set exactly
    /// as a player wears it (<c>PlayerAvatar.GearSets.cs</c>, style Guardian — helmet, chest, legs, boots), with its small
    /// lights BLUE instead of red. The set's light material is this figure's own, so it is simply made a second time in
    /// blue for him; every other figure keeps the red one. Nothing here touches the gear mask on the wire.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        /// <summary>Dresses this figure as a guard of the escort wing. Call once, after <c>Build</c> (with the suit).</summary>
        public void SetEscortGuardLook()
        {
            if (_head == null)
            {
                return;
            }

            EnsureGearSetMaterials();
            int rgb = EscortGuardLook.LightRgb;
            var blue = OwnLit(new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f), null);
            if (blue.HasProperty("_Floor"))
            {
                blue.SetFloat("_Floor", 1f); // fully lit from every side — a small light, like the set's red one
            }

            _setRed = blue; // the set's light: blue on him (the red one made above goes with the figure's other materials)
            SetGear(EscortGuardLook.GearMask);
        }
    }
}
