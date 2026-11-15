// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3676: the guardian giants among the planet enemies. An awake giant arrives in the enemy list like every machine
    /// (kind <see cref="GuardianGiantRules.NetKind"/>) but is drawn by its own <see cref="GuardianGiantView"/> — one per fight
    /// the client knows (<see cref="GameBootstrap.GuardianGiantFight"/>), so it can rise before its entry appears and finish
    /// its collapse after the entry is gone — and it has the boss bar instead of a floating health bar. Also the push of its
    /// shock waves on the local player, and the dormant giant's stir: when the server swaps one of its eye lights (a "moved"
    /// block change between the lit and the dark red light), the flicker is heard.
    /// </summary>
    public sealed partial class WorldEntities
    {
        private readonly Dictionary<string, GuardianGiantView> _giantViews = new Dictionary<string, GuardianGiantView>();
        private readonly Dictionary<string, NetCombatEntity> _giantEntities = new Dictionary<string, NetCombatEntity>();
        private readonly List<string> _giantScratch = new List<string>();
        private float _nextGiantFlicker;
        private PlayerController _giantKnockPlayer;

        /// <summary>The flicker of a stirring giant's eye is heard this close (blocks).</summary>
        private const float GiantFlickerHearing = 60f;

        /// <summary>Takes a guardian giant's planet-enemy entry for this frame (the enemy loop skips it otherwise).</summary>
        private void FeedGuardianGiant(NetCombatEntity e) => _giantEntities[e.Id] = e;

        /// <summary>Runs the giants' views after the enemy loop: a view per live fight, each fed its entry of this frame; a
        /// view that is done goes.</summary>
        private void TickGuardianGiants()
        {
            var fight = Game.GuardianGiantFight;
            float now = Game.WorldTime;
            fight.Prune(now);
            foreach (var t in fight.Tracks)
            {
                if (!_giantViews.ContainsKey(t.SiteId) && GuardianGiantView.Wanted(t, now))
                {
                    _giantViews[t.SiteId] = new GuardianGiantView(Game, transform, t.SiteId);
                }
            }

            _giantScratch.Clear();
            foreach (var kv in _giantViews)
            {
                var t = fight.Find(kv.Key);
                NetCombatEntity e = null;
                if (t != null && t.EntityId.Length > 0)
                {
                    _giantEntities.TryGetValue(t.EntityId, out e);
                }

                if (t == null || !kv.Value.Tick(t, e, now, Game.WorldDeltaTime))
                {
                    _giantScratch.Add(kv.Key);
                }
            }

            foreach (string id in _giantScratch)
            {
                _giantViews[id].Dispose();
                _giantViews.Remove(id);
            }

            _giantEntities.Clear();
        }

        /// <summary>A guardian giant's shock wave landed (<see cref="GuardianGiantRules.WaveFx"/>: a stomp, a leap's landing):
        /// the local player on foot near it is pushed away from its centre (<see cref="GuardianGiantPose.WavePush"/>, falling off
        /// like the damage). The dust, the ring and the camera shake are the giant view's, on the move's own clock.</summary>
        private void OnGiantWorldFx(WorldFx fx)
        {
            if (Game == null || fx == null || fx.Kind != GuardianGiantRules.WaveFx || Game.Aboard)
            {
                return;
            }

            var off = Game.PlayerPosition - Game.ScenePos(fx.X, fx.Y, fx.Z);
            var push = GuardianGiantPose.WavePush(new Vector3f(off.x, off.y, off.z), fx.Radius);
            if (push.X == 0f && push.Y == 0f && push.Z == 0f)
            {
                return;
            }

            _giantKnockPlayer = _giantKnockPlayer != null ? _giantKnockPlayer : FindAnyObjectByType<PlayerController>();
            if (_giantKnockPlayer != null)
            {
                _giantKnockPlayer.ApplyKnock(new Vector3(push.X, push.Y, push.Z));
            }
        }

        /// <summary>A server-side block swap between the lit and the dark red light near the player: a dormant giant's eye
        /// flickering as it stirs (at most twice a second). A hand-placed lamp is no "moved" change and stays silent.</summary>
        private void OnGiantBlockChanged(BlockChanged m)
        {
            if (m == null || !m.Moved || Game?.Content == null || Time.time < _nextGiantFlicker)
            {
                return;
            }

            string key = Game.Content.BlockById(new BlockId(m.Block))?.Key;
            if (key != GuardianGiantBody.LightOn && key != GuardianGiantBody.LightOff)
            {
                return;
            }

            var at = Game.ScenePos(m.X + 0.5f, m.Y + 0.5f, m.Z + 0.5f);
            if ((at - Game.PlayerPosition).sqrMagnitude > GiantFlickerHearing * GiantFlickerHearing)
            {
                return;
            }

            _nextGiantFlicker = Time.time + 0.5f;
            ClientAudio.Instance?.At("giant_flicker", at, Random.Range(0.95f, 1.05f), 0.8f);
        }

        private void DisposeGuardianGiants()
        {
            foreach (var view in _giantViews.Values)
            {
                view.Dispose();
            }

            _giantViews.Clear();
            _giantEntities.Clear();
        }
    }
}
