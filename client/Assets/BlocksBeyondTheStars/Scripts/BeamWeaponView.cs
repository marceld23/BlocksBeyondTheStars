// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Other players' held beams (#3898, the Guardian laser). While somebody near holds the beam, the server sends one
    /// <see cref="ActionFx"/> of kind <see cref="FxActionKinds.WeaponBeam"/> per resolved tick along the line it
    /// validated, and one <see cref="FxActionKinds.WeaponBeamEnd"/> when it is over. This keeps one
    /// <see cref="HeldBeamLine"/> per beaming player alive between the ticks — drawn every frame from that player's hand
    /// (the avatar's muzzle, when it is in sight) to the last end point, eased toward each new one — and drops it at the
    /// end message, or when no tick came for <see cref="BeamWeaponRules.TimeoutSeconds"/> (the end got lost).
    /// <para>The end message also reaches the shooter when the SERVER ended the beam (the suit's reserve, a seat, a
    /// crossing): the player's own beam is then taken away (<see cref="PlayerController.OnBeamStoppedByServer"/>). The
    /// own beam itself is drawn by the player controller the moment it happens, never from here.</para>
    /// </summary>
    public sealed class BeamWeaponView : MonoBehaviour
    {
        public GameBootstrap Game;
        public RemotePlayers Remotes;
        public PlayerController Player;

        private sealed class RemoteBeam
        {
            public HeldBeamLine Line;
            public Vector3 From;
            public Vector3 To;
            public Vector3 Shown;
            public bool Hit;
            public float LastTick;
        }

        private readonly Dictionary<string, RemoteBeam> _beams = new Dictionary<string, RemoteBeam>();
        private readonly List<string> _over = new List<string>();
        private bool _subscribed;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.ActionFxReceived += OnAction;
                _subscribed = true;
            }

            if (_beams.Count == 0)
            {
                return;
            }

            _over.Clear();
            bool hidden = Game == null || Game.SpaceViewActive;
            float ease = 1f - Mathf.Exp(-16f * Time.deltaTime);
            foreach (var kv in _beams)
            {
                var beam = kv.Value;
                if (hidden || Time.time - beam.LastTick > BeamWeaponRules.TimeoutSeconds)
                {
                    _over.Add(kv.Key);
                    continue;
                }

                var from = beam.From;
                if (Remotes != null && Remotes.TryGetAvatar(kv.Key, out var avatar) && avatar != null && avatar.TryMuzzle(out var hand))
                {
                    from = hand;
                }

                beam.Shown = Vector3.Lerp(beam.Shown, beam.To, ease);
                beam.Line.Draw(from, beam.Shown, beam.Hit, Vector3.zero);
            }

            for (int i = 0; i < _over.Count; i++)
            {
                EndBeam(_over[i]);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.ActionFxReceived -= OnAction;
            }

            foreach (var kv in _beams)
            {
                kv.Value.Line.Drop();
            }

            _beams.Clear();
        }

        private void OnAction(ActionFx m)
        {
            if (m == null || Game == null || (m.Kind != FxActionKinds.WeaponBeam && m.Kind != FxActionKinds.WeaponBeamEnd))
            {
                return;
            }

            string id = m.PlayerId ?? string.Empty;
            bool self = !string.IsNullOrEmpty(Game.LocalPlayerId) && id == Game.LocalPlayerId;
            if (m.Kind == FxActionKinds.WeaponBeamEnd)
            {
                if (self)
                {
                    Player?.OnBeamStoppedByServer();
                }
                else
                {
                    EndBeam(id);
                }

                return;
            }

            if (self || Game.SpaceViewActive)
            {
                return; // the own beam is drawn the moment it happens
            }

            var from = Game.ScenePos(m.FromX, m.FromY, m.FromZ);
            var to = Game.ScenePos(m.ToX, m.ToY, m.ToZ);
            if (Camera.main != null)
            {
                var eye = Camera.main.transform.position;
                if ((from - eye).sqrMagnitude > 80f * 80f && (to - eye).sqrMagnitude > 80f * 80f)
                {
                    EndBeam(id); // out of sight
                    return;
                }
            }

            if (!_beams.TryGetValue(id, out var beam))
            {
                beam = new RemoteBeam { Line = new HeldBeamLine(FxLook.ForItem(Game.Content, m.ItemKey), local: false), Shown = to };
                _beams[id] = beam;
                beam.Line.Draw(from, to, m.Hit, Vector3.zero); // laid at once: a rented line still holds its last user's points
            }

            beam.From = from;
            beam.To = to;
            beam.Hit = m.Hit;
            beam.LastTick = Time.time;
        }

        private void EndBeam(string playerId)
        {
            if (_beams.TryGetValue(playerId, out var beam))
            {
                _beams.Remove(playerId);
                beam.Line.End();
            }
        }
    }
}
