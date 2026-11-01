// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3070: the base landing pads on foot — the pad block in the crosshair (the HUD names it, E opens its panel,
    /// <see cref="BasePadUi"/>) and, while a pad is held, the landing square it would get (<see cref="BasePadPreview"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        private BasePadPreview _basePadPreview;
        private System.Func<int, int, int, bool> _padVolumeBlocked; // cached: the preview reads it per cell

        /// <summary>The base landing pad block in the crosshair (its scene cell), or null.</summary>
        private Vector3Int? AimedBasePadBlock()
        {
            if (Game?.Content == null || !AimBlock(out var hit, out _))
            {
                return null;
            }

            return Game.Content.BlockById(Game.World.GetBlock(hit.x, hit.y, hit.z))?.Key == BasePadRules.BlockKey ? hit : (Vector3Int?)null;
        }

        /// <summary>E at a base landing pad: its panel — name and access for the owner, who and what for everyone else.</summary>
        private bool TryOpenBasePad()
        {
            if (Game?.AimedBasePadCell is not { } cell)
            {
                return false;
            }

            Game.Context.Panel<BasePadUi>()?.Open(cell);
            return true;
        }

        /// <summary>While a base landing pad is held and aimed at a spot, shows the landing square it would get there;
        /// otherwise hides it. Parked-ship cells get none (a pad is no ship part).</summary>
        private void UpdateBasePadPreview(string held)
        {
            if (!HoldsBasePad(held) || !AimTarget(out _, out var placeCell, out var ship, PlaceFluidAim()) || ship != null)
            {
                _basePadPreview?.Hide();
                return;
            }

            _basePadPreview ??= new BasePadPreview();
            _padVolumeBlocked ??= PadVolumeBlocked;
            _basePadPreview.Show(placeCell, _padVolumeBlocked);
        }

        /// <summary>Whether the held item places the base landing pad.</summary>
        private bool HoldsBasePad(string held)
            => !string.IsNullOrEmpty(held)
               && Game?.Content?.GetItem(BlocksBeyondTheStars.Shared.State.ItemKey.Base(held))?.PlacesBlock == BasePadRules.BlockKey;

        /// <summary>Whether a cell of the kept-clear volume is in the way in the blocks this client knows: anything solid,
        /// and water, lava or another liquid. Grass and flowers are not.</summary>
        private bool PadVolumeBlocked(int x, int y, int z)
        {
            var id = Game.World.GetBlock(x, y, z);
            if (id.IsAir)
            {
                return false;
            }

            var def = Game.Content?.BlockById(id);
            return def == null || def.Solid || def.Liquid || def.Key is "water" or "lava";
        }
    }
}
