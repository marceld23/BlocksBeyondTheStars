// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The name tag (#3035, Justus: "name tags that give creatures names, shown as a floating hologram"): right-click with
    /// it at one of your companions and the name popup opens; the typed name goes to the server with
    /// <c>UseNameTag</c>, which takes the tag and makes the name a hologram (<c>CreatureView</c>). At a wild animal it says
    /// that only your companions can wear one. The server re-checks everything.
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>The name tag's item key.</summary>
        internal const string NameTagItem = "name_tag";

        /// <summary>How far the tag reaches (an arm's length plus a little — you put it on the animal).</summary>
        private const float NameTagReach = 6f;

        private void UseNameTag()
        {
            if (Game?.Creatures == null || Camera == null)
            {
                return;
            }

            var target = AimedCreature(NameTagReach);
            if (target == null)
            {
                return; // nothing in front of the tag
            }

            if (target.OwnerId != Game.LocalPlayerId)
            {
                Game.ShowMessage(Game.Localizer?.Get("creature.nametag.wild") ?? "creature.nametag.wild");
                return;
            }

            string current = string.IsNullOrEmpty(target.CustomName) ? target.Name : target.CustomName;
            string creatureId = target.Id;
            Game.Context.Panel<BeaconLabelUi>()?.Open(Game.Localizer?.Get("ui.companion.tag_prompt") ?? "Name tag", current, name =>
                Game.Network?.SendSetCompanionName(creatureId, string.IsNullOrWhiteSpace(name) ? current : name, useNameTag: true));
        }

        /// <summary>The nearest creature the view ray passes through within <paramref name="reach"/> (giants excluded).</summary>
        private NetCreature AimedCreature(float reach)
        {
            var o = Camera.transform.position;
            var dir = Camera.transform.forward;
            NetCreature best = null;
            float bestD = reach;
            foreach (var c in Game.Creatures)
            {
                if (c.GiantHeight > 0f)
                {
                    continue;
                }

                float size = Mathf.Clamp(c.Size, 0.4f, 8f);
                var center = Game.ScenePos(c.X, c.Y, c.Z) + Vector3.up * (0.6f * size);
                if (RayHitsSphere(o, dir, center, Mathf.Max(0.8f, 0.9f * size), out float d) && d < bestD)
                {
                    bestD = d;
                    best = c;
                }
            }

            return best;
        }
    }
}
