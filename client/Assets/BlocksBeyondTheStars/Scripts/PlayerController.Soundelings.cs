// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Sounders;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3366: the Feed key hands a light to a Soundeling — a lamp block item (a torch, a lantern, any lamp:
    /// <see cref="SoundelingRules.IsLightItem"/>) in the selected slot and an awake Soundeling that carries nothing within
    /// <see cref="SoundelingRules.GiveRange"/>. The probe is part of <see cref="CanFeedCreature"/> (cached a quarter second, like
    /// the beggars' one); the server decides the rest (by day only, a dark spot near) and says why when it will not.
    /// </summary>
    public sealed partial class PlayerController
    {
        private bool _feedGivesLight;

        /// <summary>The Feed key would hand a light to a Soundeling right now (the HUD's prompt says so instead of "feed").</summary>
        public bool FeedGivesLight => CanFeedCreature && _feedGivesLight;

        private bool ComputeCanGiveLight(string held)
        {
            if (string.IsNullOrEmpty(held) || Game?.Content == null || Game.Creatures == null)
            {
                return false;
            }

            var item = Game.Content.GetItem(held);
            var block = item?.PlacesBlock is { Length: > 0 } key ? Game.Content.GetBlock(key) : null;
            if (!SoundelingRules.IsLightItem(item, block))
            {
                return false;
            }

            const float range = SoundelingRules.GiveRange;
            foreach (var c in Game.Creatures)
            {
                if (c.BodyPlan == "Soundeling" && !c.Asleep && !c.CarriesLight && string.IsNullOrEmpty(c.OwnerId)
                    && (Game.ScenePos(c.X, c.Y, c.Z) - Game.PlayerPosition).sqrMagnitude <= range * range)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
