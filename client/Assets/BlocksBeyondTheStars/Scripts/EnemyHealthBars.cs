// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Floating health bars over damageable entities (#692) — planet enemies/bandits, creatures and
    /// space hostiles all funnel through <see cref="Push"/> once per entity per frame. The helper owns
    /// the shared policy so every caller behaves identically:
    /// - a bar shows while the entity is "in combat" (took damage in the last few seconds) or is the
    ///   current crosshair/fire target — never permanently, so herds don't become a wall of gauges;
    /// - the fill lerps toward the replicated value (snapshots arrive at 0.15–0.5 s cadence);
    /// - colour ramps green→amber→red with remaining health (companions read friendly cyan instead);
    /// - a hull drop attributable to the local player's latest shot flashes the HUD hit marker (#693).
    /// The "Enemy health bars" client setting turns the bars off; hit attribution still runs.
    /// </summary>
    public static class EnemyHealthBars
    {
        private const float ShowSeconds = 6f;    // how long a damaged entity keeps its bar
        private const float LerpPerSecond = 1.2f; // fill fraction change per second toward the snapshot

        private static readonly Dictionary<string, float> _lastHull = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> _combatUntil = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> _shownFrac = new Dictionary<string, float>();

        /// <summary>Feeds one entity's replicated state for this frame and draws its bar when the policy
        /// says so. <paramref name="anchor"/> is the world-space point above the body; <paramref name="targeted"/>
        /// marks the caller's own current target (space fire lock) on top of the on-foot crosshair aim.</summary>
        public static void Push(GameBootstrap game, Camera cam, string id, Vector3 anchor,
                                float hull, float hullMax, bool friendly,
                                float fadeStart, float fadeEnd, bool targeted = false)
        {
            if (game == null || cam == null || string.IsNullOrEmpty(id) || hullMax <= 1f)
            {
                return; // hullMax 1 = the sentinel stations/drops use — nothing worth a gauge
            }

            // Damage detection: a hull drop marks the entity "in combat" and, when our own latest shot
            // went there, flashes the hit marker. Runs even with bars disabled so aiming feedback stays.
            if (_lastHull.TryGetValue(id, out var prev) && hull < prev - 0.01f)
            {
                _combatUntil[id] = Time.time + ShowSeconds;
                if (game.LastShotTargetId == id && Time.time - game.LastShotTime < 0.6f)
                {
                    HudUi.Instance?.ShowHitMarker();
                }
            }

            _lastHull[id] = hull;

            if (game.Settings != null && !game.Settings.ShowEnemyHealthBars)
            {
                return;
            }

            bool aimed = targeted || game.AimedEnemyId == id;
            bool inCombat = _combatUntil.TryGetValue(id, out var until) && Time.time < until;
            if (!aimed && !inCombat)
            {
                _shownFrac.Remove(id); // next appearance snaps to the live value instead of lerping in
                return;
            }

            float target = hullMax > 0f ? Mathf.Clamp01(hull / hullMax) : 0f;
            float shown = _shownFrac.TryGetValue(id, out var s)
                ? Mathf.MoveTowards(s, target, Time.deltaTime * LerpPerSecond)
                : target;
            _shownFrac[id] = shown;

            var col = friendly ? UiKit.Cyan : Ramp(shown);
            ScreenLabelLayer.Instance.WorldBar(cam, anchor, shown, col, 46f, fadeStart, fadeEnd);
        }

        /// <summary>Drops an entity's bookkeeping when it despawns/dies, so ids never accumulate.</summary>
        public static void Forget(string id)
        {
            _lastHull.Remove(id);
            _combatUntil.Remove(id);
            _shownFrac.Remove(id);
        }

        /// <summary>The speeder-gauge colour convention: green while healthy, amber when bruised, red when
        /// close to breaking — reads at a glance for kids.</summary>
        private static Color Ramp(float frac)
            => frac > 0.5f ? new Color(0.35f, 0.9f, 0.4f)
             : frac > 0.25f ? new Color(1f, 0.75f, 0.25f)
             : new Color(1f, 0.35f, 0.3f);
    }
}
