// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Other players' tool use, made visible (#2158). The server relays every player's cosmetic <c>FxIntent</c> as an
    /// <see cref="ActionFx"/> to the others nearby — before this, another player's shots, swings, drill sparks, scans
    /// and gadget pulses were invisible, because the gameplay intents only ever travel client → server. The effects are
    /// the SAME looks the local player sees (data-driven by the item key), starting at the remote avatar's hand.
    /// <para>Gadget OUTCOMES also arrive here (<see cref="ActionFx.Outcome"/>), for everyone in range including the user:
    /// the heal / freeze / blast / scan wave plays only once the server has confirmed the use (#2151).</para>
    /// </summary>
    public sealed class FxRemote : MonoBehaviour
    {
        public GameBootstrap Game;
        public RemotePlayers Remotes;
        public PlayerController Player;

        private bool _subscribed;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.ActionFxReceived += OnAction;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.ActionFxReceived -= OnAction;
            }
        }

        private void OnAction(ActionFx m)
        {
            if (m == null || Game == null || Game.SpaceViewActive)
            {
                return;
            }

            bool self = !string.IsNullOrEmpty(Game.LocalPlayerId) && m.PlayerId == Game.LocalPlayerId;
            var from = Game.ScenePos(m.FromX, m.FromY, m.FromZ);
            var to = Game.ScenePos(m.ToX, m.ToY, m.ToZ);
            var look = FxLook.ForItem(Game.Content, m.ItemKey);

            if (m.Outcome)
            {
                // The confirmed gadget outcome. The user's own feet are the local rig; another user stands where the
                // server saw them (the remote avatar's feet when it is drawn).
                var feet = self && Player != null ? Player.transform.position : from; // the server position is the feet
                if (!self && Remotes != null && Remotes.TryGetAvatar(m.PlayerId, out var user))
                {
                    feet = user.transform.position;
                }

                FxGadgets.Outcome(look, feet, to, self);
                return;
            }

            if (self)
            {
                return; // the player's own actions are drawn the moment they happen
            }

            if (Camera.main != null && (to - Camera.main.transform.position).sqrMagnitude > 80f * 80f)
            {
                return; // out of sight
            }

            if (Remotes != null && Remotes.TryGetAvatar(m.PlayerId, out var avatar) && avatar.TryMuzzle(out var hand))
            {
                from = hand;
            }

            switch (m.Kind)
            {
                case FxActionKinds.Shot:
                    FxShots.Fire(look, from, to, m.Hit, Vector3.zero, local: false);
                    PlayShotCue(look, from);
                    break;
                case FxActionKinds.Melee:
                {
                    var dir = to - from;
                    FxShots.Swing(look, from, dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward, Vector3.up, m.Hit, to, local: false);
                    break;
                }

                case FxActionKinds.Mine:
                    MiningFx.Instance?.RemoteDrill(look, from, to);
                    break;
                case FxActionKinds.Scan:
                    FxGadgets.HandScan(look, from, new Bounds(to, Vector3.one * 1.2f), look.Is("scan_pro"));
                    break;
                case FxActionKinds.Gadget:
                    FxGadgets.Intent(look, from);
                    break;
            }
        }

        private static void PlayShotCue(FxLook look, Vector3 at)
        {
            string cue = look.Style switch
            {
                "slug" => "weapon_scrap",
                "rail" => "weapon_gauss",
                "laser" => "weapon_laser",
                "plasma" => "weapon_plasma",
                _ => null,
            };
            if (cue != null)
            {
                ClientAudio.Instance?.At(cue, at, Random.Range(0.95f, 1.05f), 0.8f);
            }
        }
    }
}
