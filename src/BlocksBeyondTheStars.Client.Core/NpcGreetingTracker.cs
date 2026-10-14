// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>#2593: what the client does with an arriving <see cref="NpcGreeting"/>.</summary>
    public enum NpcGreetingDecision : byte
    {
        /// <summary>A new bubble: show the text, play the voice and the gesture once.</summary>
        New,

        /// <summary>The AI upgrade of the bubble that shows: replace the text only — no voice, no gesture.</summary>
        Upgrade,

        /// <summary>Too late or out of order: ignore it.</summary>
        Drop,
    }

    /// <summary>
    /// #2593: tells a new NPC speech bubble from the AI upgrade of one that already shows, by the greeting's id
    /// (<see cref="NpcGreeting.GreetingId"/>; the server reuses it for the upgrade). Unity-free, one record per NPC.
    /// <list type="bullet">
    /// <item>A higher id than the NPC's last one → <see cref="NpcGreetingDecision.New"/>.</item>
    /// <item>The same id → <see cref="NpcGreetingDecision.Upgrade"/> — unless the bubble expired more than
    /// <see cref="LateUpgradeGraceSeconds"/> ago: a late upgrade is dropped (a bubble popping up again reads as a
    /// glitch).</item>
    /// <item>A lower id → <see cref="NpcGreetingDecision.Drop"/> (stale, overtaken) — unless the NPC's last bubble came
    /// more than <see cref="RestartWindowSeconds"/> ago: then the server restarted its count and this is new.</item>
    /// <item>Id 0 (an older server): a second line for the same NPC within <see cref="LegacyUpgradeWindowSeconds"/> of its
    /// first is the upgrade (that server re-greets an NPC at most every 25 s), anything else new.</item>
    /// </list>
    /// An upgrade keeps the bubble up until max(its current end, now + the new text's hold).
    /// </summary>
    public sealed class NpcGreetingTracker
    {
        /// <summary>An upgrade arriving more than this long after its bubble expired is dropped.</summary>
        public const float LateUpgradeGraceSeconds = 3f;

        /// <summary>An older server (id 0) re-greets the same NPC at most this often, so a second line sooner is the
        /// upgrade.</summary>
        public const float LegacyUpgradeWindowSeconds = 25f;

        /// <summary>A lower id after this long without a bubble from that NPC means the server started counting anew.</summary>
        public const float RestartWindowSeconds = 60f;

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        private struct Entry
        {
            public int Id;
            public float At;     // when the bubble's first message arrived
            public float Until;  // when the bubble stops showing
        }

        /// <summary>
        /// Decides on a greeting with <paramref name="greetingId"/> for NPC <paramref name="npcId"/> arriving at
        /// <paramref name="now"/> and records it: a new bubble shows for <paramref name="holdSeconds"/>, an upgrade extends
        /// the bubble to at least now + <paramref name="holdSeconds"/>.
        /// </summary>
        public NpcGreetingDecision OnGreeting(int npcId, int greetingId, float now, float holdSeconds)
        {
            var decision = Decide(npcId, greetingId, now);
            if (decision == NpcGreetingDecision.New)
            {
                _entries[npcId] = new Entry { Id = greetingId, At = now, Until = now + holdSeconds };
            }
            else if (decision == NpcGreetingDecision.Upgrade)
            {
                var e = _entries[npcId];
                float until = now + holdSeconds;
                if (until > e.Until)
                {
                    e.Until = until;
                }

                _entries[npcId] = e;
            }

            return decision;
        }

        /// <summary>The decision <see cref="OnGreeting"/> would take, without recording anything.</summary>
        public NpcGreetingDecision Decide(int npcId, int greetingId, float now)
        {
            if (!_entries.TryGetValue(npcId, out var e))
            {
                return NpcGreetingDecision.New;
            }

            bool expiredLongAgo = now > e.Until + LateUpgradeGraceSeconds;
            if (greetingId == 0)
            {
                // An older server: no ids, so guess by timing.
                if (e.Id == 0 && now - e.At < LegacyUpgradeWindowSeconds)
                {
                    return expiredLongAgo ? NpcGreetingDecision.Drop : NpcGreetingDecision.Upgrade;
                }

                return NpcGreetingDecision.New;
            }

            if (greetingId > e.Id)
            {
                return NpcGreetingDecision.New;
            }

            if (greetingId == e.Id)
            {
                return expiredLongAgo ? NpcGreetingDecision.Drop : NpcGreetingDecision.Upgrade;
            }

            return now - e.At > RestartWindowSeconds ? NpcGreetingDecision.New : NpcGreetingDecision.Drop;
        }

        /// <summary>When NPC <paramref name="npcId"/>'s bubble stops showing (negative infinity = never had one).</summary>
        public float BubbleUntil(int npcId) => _entries.TryGetValue(npcId, out var e) ? e.Until : float.NegativeInfinity;

        /// <summary>Ends NPC <paramref name="npcId"/>'s bubble at <paramref name="now"/> (kept for the id order).</summary>
        public void Hide(int npcId, float now)
        {
            if (_entries.TryGetValue(npcId, out var e) && e.Until > now)
            {
                e.Until = now;
                _entries[npcId] = e;
            }
        }

        /// <summary>Forgets NPC <paramref name="npcId"/> (it left the area).</summary>
        public void Forget(int npcId) => _entries.Remove(npcId);

        /// <summary>Forgets every NPC (world change: the ids restart).</summary>
        public void Clear() => _entries.Clear();
    }
}
