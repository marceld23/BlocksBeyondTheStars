// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Carrying a block over the head and throwing it (#3766 — a young player's idea: "carry things with both hands and
    /// throw them at enemies"). The Feed control lifts one block of the selected hotbar stack (when there is nothing to
    /// feed) and puts it back; while it is up, the attack control throws it and nothing else works with the hands — no
    /// mining, no placing, no swing. The server owns the block and the hit (<c>GameServerCarryThrow</c>); this is the
    /// input and the pose. Selecting another slot, or a stack that is gone, ends the carry.
    /// </summary>
    public sealed partial class PlayerController
    {
        private bool _carryingBlock;
        private int _carrySlot = -1;
        private int _carryTickFrame;    // the last frame the on-foot input ran while carrying
        private int _carryPrimaryFrame; // the frame whose attack press was the throw
        private int _carryRejectionsSeen;
        private float _lastThrowAt = float.NegativeInfinity; // when the last throw went out (the world clock: it stands still with the server's)

        /// <summary>The shortest time between two throws sent, seconds: the server's gap and a little more — the two clocks
        /// do not see the time between two messages alike (the network's jitter, the server's tick).</summary>
        private const float ThrowGap = (float)BlockThrow.ThrowGapSeconds + 0.1f;

        /// <summary>True while a block is held over the head.</summary>
        public bool CarryingBlock => _carryingBlock;

        /// <summary>The on-foot input stood still for the action list — the pad's and the tablet's way to the carry's own
        /// controls — and for nothing that takes the block out of the hands: the carry is not stale (<see cref="CarryTick"/>).</summary>
        public void KeepCarry()
        {
            if (_carryingBlock)
            {
                _carryTickFrame = Time.frameCount;
            }
        }

        /// <summary>True if the hands are taken by the carry this frame: the block is up, or this frame's attack press
        /// threw it (the same press must not also mine or swing).</summary>
        private bool CarryHoldsHands => _carryingBlock || _carryPrimaryFrame == Time.frameCount;

        /// <summary>True if the selected hotbar slot holds an item that places a block — what can be lifted.</summary>
        public bool CanLiftBlock
            => Game?.Content != null
               && Game.ItemInSlot(Game.SelectedHotbarSlot) is { Length: > 0 } held
               && !string.IsNullOrEmpty(Game.Content.GetItem(held)?.PlacesBlock);

        /// <summary>The Feed control with nothing to feed: lifts the selected block, or puts a lifted one back.</summary>
        private void ToggleCarry()
        {
            if (_carryingBlock)
            {
                EndCarry(tellServer: true);
                return;
            }

            if (!CanLiftBlock || OverThere) // #3906: no block is lifted over there
            {
                return;
            }

            _carryingBlock = true;
            _carrySlot = Game.SelectedHotbarSlot;
            _carryTickFrame = Time.frameCount;
            _carryRejectionsSeen = Game.CarryRejections;
            Game.Network?.SendCarryBlock(true);
            ShowCarry(true);
            ClientAudio.Instance?.Voice("breath_effort", 0.3f); // a "hup" in the player's own voice (#3767)
            Game.ShowMessage(HudUi.ExpandKeyTokens(Game.Localizer, Game.Localizer?.Get("ui.hud.carry_lifted")
                ?? "Lifted! {key:PrimaryFire} throws it, {key:FeedCreature} puts it back."));
        }

        /// <summary>Once a frame from the on-foot input: keeps the carry honest and throws on the attack press. Returns true
        /// while the hands are taken, so the caller skips the swing.</summary>
        private bool CarryTick()
        {
            if (!_carryingBlock)
            {
                return CarryHoldsHands;
            }

            // Another slot, an emptied stack, or the on-foot input did not run for a while (seated, at the helm, a menu
            // that changed the world): the block is back in its stack. The server ends its side by the same rules.
            bool stale = Time.frameCount - _carryTickFrame > 30;
            _carryTickFrame = Time.frameCount;
            if (stale || Game.SelectedHotbarSlot != _carrySlot || !CanLiftBlock)
            {
                EndCarry(tellServer: true);
                return false;
            }

            if (Game.CarryRejections != _carryRejectionsSeen)
            {
                EndCarry(tellServer: false); // the server said no (its toast tells why) — the hands come down again
                return false;
            }

            // The server takes one throw per BlockThrow.ThrowGapSeconds and drops a sooner one without a word — it then still
            // has the block up while the hands here came down. Inside the gap the press does nothing: the block stays up.
            if (InputMap.Down(InputAction.PrimaryFire) && Camera != null && Game.WorldTime - _lastThrowAt >= ThrowGap)
            {
                var dir = Camera.transform.forward;
                Game.Network?.SendThrowBlock(dir.x, dir.y, dir.z);
                _lastThrowAt = Game.WorldTime;
                _carryPrimaryFrame = Time.frameCount;
                EndCarry(tellServer: false); // the server took the block; the hands come down
                TriggerSwing();
                ClientAudio.Instance?.Cue("melee_swing");
            }

            return true;
        }

        private void EndCarry(bool tellServer)
        {
            _carryingBlock = false;
            _carrySlot = -1;
            if (tellServer)
            {
                Game?.Network?.SendCarryBlock(false);
            }

            ShowCarry(false);
        }

        private void ShowCarry(bool on)
        {
            Avatar?.SetCarryingBlock(on);
            _viewmodel?.SetCarrying(on);
        }
    }
}
