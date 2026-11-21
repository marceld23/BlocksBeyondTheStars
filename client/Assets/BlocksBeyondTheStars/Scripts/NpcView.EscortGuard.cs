// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The sleeper as an NPC (#3897). An NPC whose look is <see cref="EscortGuardLook.LookKey"/> (the server's
    /// <c>NetNpc.Look</c>) is built in the suit and dressed in the Guardian armour with blue lights
    /// (<see cref="PlayerAvatar.SetEscortGuardLook"/>), and carries the Guardian sword — its own held model with the
    /// lights turned blue — in his hand at his side while he stands and the server names no tool for him.
    /// </summary>
    public sealed partial class NpcView
    {
        private List<HeldModelPart> _escortSword;

        /// <summary>Puts the guard's sword into the hand when <paramref name="held"/> is his sword's id
        /// (<see cref="EscortGuardLook.HeldFor"/>); false for every other held id (the caller draws that one).</summary>
        private bool HoldEscortSword(Npc n, string held)
        {
            if (held != EscortGuardLook.SwordHeldId || n.Avatar == null)
            {
                return false;
            }

            if (_escortSword == null)
            {
                _escortSword = EscortGuardLook.SwordParts(Game?.Content?.GetItem(EscortGuardLook.SwordItemKey));
            }

            // Without the item's model the sword is drawn as the item itself (or the plain blade): never nothing.
            n.Avatar.SetHeldItem(HeldItem.Kind.Blade, Color.white, null, EscortGuardLook.SwordItemKey,
                _escortSword.Count > 0 ? _escortSword : null);
            return true;
        }
    }
}
