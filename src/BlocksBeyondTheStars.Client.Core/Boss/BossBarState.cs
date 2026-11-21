// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What the HUD's boss bar draws this frame (#3901) — whichever boss it is.</summary>
    public readonly struct BossBarView
    {
        public BossBarView(string id, string nameKey, float health, float shield, byte phase, string source, float rageAt)
        {
            Id = id;
            NameKey = nameKey;
            Health = health;
            Shield = shield;
            Phase = phase;
            Source = source;
            RageAt = rageAt;
        }

        /// <summary>The boss's id: a space boss's entity id, a guardian giant's record key. A change means another boss.</summary>
        public string Id { get; }

        /// <summary>The locale key of the name on the bar.</summary>
        public string NameKey { get; }

        /// <summary>Hull left, 0..1 — the bar's fill.</summary>
        public float Health { get; }

        /// <summary>Shield left, 0..1 — the thin strip above the fill; 0 = none (no strip).</summary>
        public float Shield { get; }

        /// <summary>1 = the first phase, 2 = enraged (<see cref="EscortCruiserRules.PhaseEnraged"/>; a raging giant).</summary>
        public byte Phase { get; }

        /// <summary><see cref="BossSources.Giant"/> or <see cref="BossSources.Space"/>.</summary>
        public string Source { get; }

        /// <summary>The hull share at which the rage begins — the mark on the bar; 0 = no mark (the status does not say).</summary>
        public float RageAt { get; }

        /// <summary>The second phase: the bar turns a deeper, pulsing red.</summary>
        public bool Enraged => Phase >= EscortCruiserRules.PhaseEnraged;
    }

    /// <summary>
    /// The one "active boss" of the HUD (#3901), merged from the two sources a client hears of: the guardian giant's own
    /// status (<see cref="GuardianGiantFight.BossBar"/>, #3676 — unchanged) and the generic <see cref="BossStatus"/> the
    /// escort cruiser (#3900) is the first user of. The boss bar, the boss music in space and the flight view (which
    /// cruiser is the boss, which phase it is in) read it; Unity-free, fed with the local world clock.
    /// <para><b>The rules.</b> A <see cref="BossStatus"/> boss is live from its first active status until the status with
    /// <c>Active == false</c> — or until none came for <see cref="SilentSeconds"/> (the pilot left the flight). A boss that
    /// ended stays ended: a status that arrives late never brings its bar back.</para>
    /// <para><b>Two at once</b> (a pilot who took off out of a giant fight and flew into a cruiser within seconds): the
    /// boss of the scene the player is in wins — in flight the space boss, on foot the giant — so the bar never flickers
    /// between the two with every packet. Between two bosses of the same scene the fight this client heard of first
    /// keeps the bar; between two giants the existing rule stands (the one heard of most recently).</para>
    /// </summary>
    public sealed class BossBarState
    {
        /// <summary>A <see cref="BossStatus"/> boss not heard of for this long is gone for this client (the server sends
        /// one about every second while the pilot is in its flight).</summary>
        public const float SilentSeconds = 4f;

        /// <summary>A boss's record — also that it ended — is forgotten this long after its last status.</summary>
        public const float ForgetSeconds = 20f;

        /// <summary>The guardian giant's name on the bar (its Codex entry's) — its own status carries no name key.</summary>
        public const string GiantNameKey = "enemy.guardian_giant.name";

        private sealed class Track
        {
            public string Id = string.Empty;
            public string NameKey = string.Empty;
            public string Source = string.Empty;
            public float Health;
            public float Shield;
            public byte Phase;
            public float FirstHeard;
            public float LastHeard;
            public bool Ended;
            public bool Defeated;
        }

        private readonly Dictionary<string, Track> _tracks = new Dictionary<string, Track>(StringComparer.Ordinal);
        private readonly List<string> _stale = new List<string>();

        /// <summary>How many <see cref="BossStatus"/> fights ended since the start (each id once).</summary>
        public int EndCount { get; private set; }

        /// <summary>The id of the fight that ended last (empty = none yet).</summary>
        public string LastEndedId { get; private set; } = string.Empty;

        /// <summary>How the fight of <see cref="LastEndedId"/> ended: the boss was defeated (true) or it left (false).</summary>
        public bool LastEndDefeated { get; private set; }

        /// <summary>Takes a status that arrived at <paramref name="now"/> (the local world clock).</summary>
        public void Receive(BossStatus? status, float now)
        {
            if (status == null || string.IsNullOrEmpty(status.Id))
            {
                return;
            }

            Prune(now);
            if (_tracks.TryGetValue(status.Id, out var t) && now < t.LastHeard - 1f)
            {
                _tracks.Remove(status.Id); // a clock that went back (a new world): start afresh
                t = null;
            }

            if (t != null && t.Ended)
            {
                return; // over is over — a late status does not bring the bar back
            }

            if (t == null)
            {
                t = new Track { Id = status.Id, FirstHeard = now };
                _tracks[status.Id] = t;
            }

            t.LastHeard = now;
            if (!string.IsNullOrEmpty(status.NameKey))
            {
                t.NameKey = status.NameKey;
            }

            if (!string.IsNullOrEmpty(status.Source))
            {
                t.Source = status.Source;
            }

            if (!status.Active)
            {
                t.Ended = true;
                t.Defeated = status.Defeated;
                EndCount++;
                LastEndedId = t.Id;
                LastEndDefeated = status.Defeated;
                return;
            }

            t.Health = Clamp01(status.Health);
            t.Shield = Clamp01(status.Shield);
            t.Phase = Math.Max(t.Phase, Math.Max(EscortCruiserRules.PhaseNormal, status.Phase)); // a phase never goes back
        }

        /// <summary>The boss the bar shows at <paramref name="now"/>, or false (the bar hides). <paramref name="giants"/> is
        /// the guardian giants' own model; <paramref name="inFlight"/> says which scene the player is in (see the class
        /// remarks for two bosses at once).</summary>
        public bool TryGetBar(GuardianGiantFight? giants, float now, bool inFlight, out BossBarView bar)
        {
            var giant = giants?.BossBar(now);
            var status = BestStatus(now, inFlight);
            bool statusIsSpace = status != null && string.Equals(status.Source, BossSources.Space, StringComparison.Ordinal);
            if (status != null && (giant == null || (statusIsSpace && inFlight)))
            {
                bar = new BossBarView(status.Id, status.NameKey, status.Health, status.Shield, status.Phase, status.Source, 0f);
                return true;
            }

            if (giant != null)
            {
                byte phase = giant.Status.Rage ? EscortCruiserRules.PhaseEnraged : EscortCruiserRules.PhaseNormal;
                bar = new BossBarView(giant.SiteId, GiantNameKey, giant.HullFraction, 0f, phase, BossSources.Giant, GuardianGiantRules.RageAt);
                return true;
            }

            bar = default;
            return false;
        }

        /// <summary>Whether a space boss fights in the player's flight at <paramref name="now"/> — the boss music plays.</summary>
        public bool SpaceBossFighting(float now)
        {
            foreach (var t in _tracks.Values)
            {
                if (Live(t, now) && string.Equals(t.Source, BossSources.Space, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether the space entity <paramref name="entityId"/> is a live space boss at <paramref name="now"/>.</summary>
        public bool IsSpaceBoss(string? entityId, float now)
            => entityId != null && _tracks.TryGetValue(entityId, out var t) && Live(t, now)
               && string.Equals(t.Source, BossSources.Space, StringComparison.Ordinal);

        /// <summary>The phase of the live boss <paramref name="id"/> at <paramref name="now"/>; 0 = no live boss of that id.</summary>
        public byte PhaseOf(string? id, float now)
            => id != null && _tracks.TryGetValue(id, out var t) && Live(t, now) ? t.Phase : (byte)0;

        /// <summary>Whether the boss bar speaks for the entity <paramref name="id"/> — it is a live boss, or its fight just
        /// ended — so the flight view draws no small health bar over it.</summary>
        public bool SpeaksFor(string? id, float now)
            => id != null && _tracks.TryGetValue(id, out var t) && (t.Ended || Live(t, now));

        /// <summary>Forgets every boss (a world switch).</summary>
        public void Clear() => _tracks.Clear();

        /// <summary>Forgets the bosses of one source at once — the space bosses when the player's flight closes (landed,
        /// jumped to another system, the ship lost), so their bar does not linger for <see cref="SilentSeconds"/> on the
        /// ground. A boss still fighting where the player is brings its bar back with its next status.</summary>
        public void Leave(string source)
        {
            _stale.Clear();
            foreach (var t in _tracks.Values)
            {
                if (string.Equals(t.Source, source, StringComparison.Ordinal))
                {
                    _stale.Add(t.Id);
                }
            }

            foreach (string id in _stale)
            {
                _tracks.Remove(id);
            }
        }

        /// <summary>The hull share at which the boss named <paramref name="nameKey"/> enrages, from the enemy data
        /// (<c>enrageAtHull</c> of its normal tier) — the mark on the bar. 0 = unknown, or it never does.</summary>
        public static float EnrageShare(GameContent? content, string? nameKey)
        {
            if (content == null || string.IsNullOrEmpty(nameKey))
            {
                return 0f;
            }

            foreach (var enemy in content.Enemies)
            {
                if (string.Equals(enemy.NameKey, nameKey, StringComparison.Ordinal))
                {
                    float share = enemy.Tier(EnemyTiers.Normal).EnrageAtHull;
                    return share > 0f && share < 1f ? share : 0f;
                }
            }

            return 0f;
        }

        private static bool Live(Track t, float now)
            => !t.Ended && now - t.LastHeard < SilentSeconds && now >= t.LastHeard - 1f;

        /// <summary>The live status boss the bar would show: one of the player's scene first, then the fight heard of
        /// first (the id breaks a tie, so the choice never depends on the dictionary's order).</summary>
        private Track? BestStatus(float now, bool inFlight)
        {
            Track? best = null;
            bool bestHere = false;
            foreach (var t in _tracks.Values)
            {
                if (!Live(t, now))
                {
                    continue;
                }

                bool here = string.Equals(t.Source, BossSources.Space, StringComparison.Ordinal) == inFlight;
                if (best == null || (here && !bestHere)
                    || (here == bestHere && (t.FirstHeard < best.FirstHeard
                        || (t.FirstHeard == best.FirstHeard && string.CompareOrdinal(t.Id, best.Id) < 0))))
                {
                    best = t;
                    bestHere = here;
                }
            }

            return best;
        }

        private void Prune(float now)
        {
            _stale.Clear();
            foreach (var t in _tracks.Values)
            {
                if (now - t.LastHeard > ForgetSeconds || now < t.LastHeard - 1f)
                {
                    _stale.Add(t.Id);
                }
            }

            foreach (string id in _stale)
            {
                _tracks.Remove(id);
            }
        }

        private static float Clamp01(float v) => float.IsNaN(v) ? 0f : Math.Max(0f, Math.Min(1f, v));
    }
}
