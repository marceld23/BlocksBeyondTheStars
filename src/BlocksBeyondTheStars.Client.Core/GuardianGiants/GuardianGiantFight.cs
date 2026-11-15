// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// One guardian giant fight as this client knows it (#3676): the newest <see cref="GuardianGiantStatus"/> and the move's
    /// clock on the local render clock, plus the move before it for the blend. Kept by <see cref="GuardianGiantFight"/>.
    /// </summary>
    public sealed class GuardianGiantTrack
    {
        internal GuardianGiantTrack(string siteId) => SiteId = siteId;

        /// <summary>The giant's record key (<c>GuardianGiantStatus.SiteId</c>).</summary>
        public string SiteId { get; }

        /// <summary>The newest status (never null once the track exists).</summary>
        public GuardianGiantStatus Status { get; internal set; } = new GuardianGiantStatus();

        /// <summary>The awake giant's planet-enemy id — the last non-empty one heard (a finished fight sends none).</summary>
        public string EntityId { get; internal set; } = string.Empty;

        /// <summary>The current move.</summary>
        public GuardianGiantMove Move => (GuardianGiantMove)Status.Move;

        /// <summary>When (local clock) the current move started.</summary>
        public float MoveStart { get; internal set; }

        /// <summary>Counts the move instances: it changes whenever a move starts — also the same move again (a second stomp).</summary>
        public int MoveSerial { get; internal set; }

        /// <summary>The move before the current one and when it had started (local clock), for the blend.</summary>
        public GuardianGiantMove PrevMove { get; internal set; }

        /// <summary>See <see cref="PrevMove"/>.</summary>
        public float PrevMoveStart { get; internal set; }

        /// <summary>When (local clock) the current move took over.</summary>
        public float SwitchedAt { get; internal set; }

        /// <summary>When (local clock) the newest status arrived.</summary>
        public float LastHeard { get; internal set; }

        /// <summary>Seconds into the current move at <paramref name="now"/> (the status's clock, counted on locally).</summary>
        public float Elapsed(float now) => Math.Max(0f, now - MoveStart);

        /// <summary>Seconds into the previous move at <paramref name="now"/>.</summary>
        public float PrevElapsed(float now) => Math.Max(0f, now - PrevMoveStart);

        /// <summary>How far the blend from the previous move into the current one is (0 … 1 over
        /// <see cref="GuardianGiantFight.BlendSeconds"/>).</summary>
        public float Blend(float now) => GuardianGiantPose.Clamp01((now - SwitchedAt) / GuardianGiantFight.BlendSeconds);

        /// <summary>The boss bar's fill: hull left over the full hull (0 … 1).</summary>
        public float HullFraction => Status.HullMax > 0f ? GuardianGiantPose.Clamp01(Status.Hull / Status.HullMax) : 0f;
    }

    /// <summary>
    /// The guardian giant fights near the player (#3676), fed by every <see cref="GuardianGiantStatus"/> (stamped with the local
    /// render clock when it arrives): the move clocks the model animates from, the boss bar (<see cref="BossBar"/>) and the
    /// fight music (<see cref="Fighting"/>). Unity-free; the view, the HUD and the music director only read it.
    /// <para>A move's clock is the status's <c>MoveElapsed</c> counted on locally from the arrival; a later status for the same
    /// move only moves it when it drifted more than <see cref="ResyncSeconds"/> (so the animation never stutters with every
    /// packet), and the same move starting again (two stomps in a row) counts as a new move.</para>
    /// </summary>
    public sealed class GuardianGiantFight
    {
        /// <summary>The cross-fade from one move's pose into the next (seconds).</summary>
        public const float BlendSeconds = 0.2f;

        /// <summary>A fight not heard of for this long is over for this client (it walked out of range, the link dropped).</summary>
        public const float StaleSeconds = 3f;

        /// <summary>A move's clock moves only when a status disagrees with it by more than this (seconds).</summary>
        public const float ResyncSeconds = 0.35f;

        /// <summary>A finished track is forgotten this long after its last status (the model has gone long before).</summary>
        public const float ForgetSeconds = 10f;

        private readonly Dictionary<string, GuardianGiantTrack> _tracks = new Dictionary<string, GuardianGiantTrack>(StringComparer.Ordinal);
        private readonly List<string> _stale = new List<string>();

        /// <summary>Every fight this client knows of.</summary>
        public IReadOnlyCollection<GuardianGiantTrack> Tracks => _tracks.Values;

        /// <summary>Takes a status that arrived at <paramref name="now"/> (the local render clock).</summary>
        public void Receive(GuardianGiantStatus? status, float now)
        {
            if (status == null || string.IsNullOrEmpty(status.SiteId))
            {
                return;
            }

            if (!_tracks.TryGetValue(status.SiteId, out var t) || now < t.LastHeard - 1f)
            {
                t = new GuardianGiantTrack(status.SiteId); // new, or a clock that went back (a new world): start afresh
                _tracks[status.SiteId] = t;
                Start(t, (GuardianGiantMove)status.Move, now - Math.Max(0f, status.MoveElapsed), now, blend: false);
            }
            else
            {
                var move = (GuardianGiantMove)status.Move;
                float start = now - Math.Max(0f, status.MoveElapsed);
                if (move != t.Move || start > t.MoveStart + ResyncSeconds)
                {
                    Start(t, move, start, now, blend: true);
                }
                else if (Math.Abs(start - t.MoveStart) > ResyncSeconds)
                {
                    t.MoveStart = start; // drifted (a long frame, a late packet): adopt the server's clock
                }
            }

            t.Status = status;
            t.LastHeard = now;
            if (!string.IsNullOrEmpty(status.EntityId))
            {
                t.EntityId = status.EntityId;
            }
        }

        /// <summary>The fight of a giant by its record key, or null.</summary>
        public GuardianGiantTrack? Find(string siteId)
            => siteId != null && _tracks.TryGetValue(siteId, out var t) ? t : null;

        /// <summary>The fight of an awake giant by its planet-enemy id, or null.</summary>
        public GuardianGiantTrack? FindByEntity(string entityId)
        {
            if (string.IsNullOrEmpty(entityId))
            {
                return null;
            }

            foreach (var t in _tracks.Values)
            {
                if (string.Equals(t.EntityId, entityId, StringComparison.Ordinal))
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>Whether a fight is live at <paramref name="now"/>: a move other than none, heard of recently.</summary>
        public static bool Live(GuardianGiantTrack t, float now)
            => t != null && t.Move != GuardianGiantMove.None && now - t.LastHeard < StaleSeconds && now >= t.LastHeard - 1f;

        /// <summary>The fight the boss bar shows at <paramref name="now"/>: a live one whose giant still has hull — the one heard
        /// of most recently when two giants fight at once — or null (the bar hides).</summary>
        public GuardianGiantTrack? BossBar(float now)
        {
            GuardianGiantTrack? best = null;
            foreach (var t in _tracks.Values)
            {
                if (Live(t, now) && t.Status.Hull > 0f && (best == null || t.LastHeard > best.LastHeard))
                {
                    best = t;
                }
            }

            return best;
        }

        /// <summary>Whether a giant is fighting near the player at <paramref name="now"/> — the boss music plays. Waking up
        /// counts; collapsing and lying down again do not (the fight is decided).</summary>
        public bool Fighting(float now)
        {
            foreach (var t in _tracks.Values)
            {
                if (Live(t, now) && t.Status.Hull > 0f && t.Move != GuardianGiantMove.Collapse && t.Move != GuardianGiantMove.LieDown)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Forgets the fights not heard of for <see cref="ForgetSeconds"/>.</summary>
        public void Prune(float now)
        {
            _stale.Clear();
            foreach (var t in _tracks.Values)
            {
                if (now - t.LastHeard > ForgetSeconds || now < t.LastHeard - 1f)
                {
                    _stale.Add(t.SiteId);
                }
            }

            foreach (string id in _stale)
            {
                _tracks.Remove(id);
            }
        }

        /// <summary>Forgets every fight (a world switch).</summary>
        public void Clear() => _tracks.Clear();

        private static void Start(GuardianGiantTrack t, GuardianGiantMove move, float start, float now, bool blend)
        {
            t.PrevMove = blend ? t.Move : move;
            t.PrevMoveStart = blend ? t.MoveStart : start;
            t.SwitchedAt = blend ? now : now - BlendSeconds;
            t.MoveStart = start;
            t.MoveSerial++;
        }
    }
}
