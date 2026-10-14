// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>#2592: how much an NPC's speech matters to the player — a higher priority cuts a lower one's babble.</summary>
    public enum NpcVoicePriority : byte
    {
        /// <summary>The idle murmur's rank. It plays on the reaction channel, which any babble cuts.</summary>
        Idle = 0,

        /// <summary>Villagers talking to each other, a festival cheer.</summary>
        Chatter = 1,

        /// <summary>A passing greeting, a welcome back, a visitor.</summary>
        Greeting = 2,

        /// <summary>The NPC the player talks to: the dialog panel, the E greeting, a market stall or mission board.</summary>
        Addressed = 3,
    }

    /// <summary>#2592: the voice budget's answer to one request.</summary>
    public readonly struct NpcVoiceGrant
    {
        /// <summary>A refused request: the bubble still shows, just silent.</summary>
        public static readonly NpcVoiceGrant Denied = new NpcVoiceGrant(false, 0f, NpcVoiceGate.NoNpc, false);

        public NpcVoiceGrant(bool granted, float volume, int cutNpcId, bool cutReaction)
        {
            Granted = granted;
            Volume = volume;
            CutNpcId = cutNpcId;
            CutReaction = cutReaction;
        }

        /// <summary>Whether the sound may play.</summary>
        public bool Granted { get; }

        /// <summary>The factor on the sound's own volume: ×0.6 for a greeting in a crowd, ×0.5 for the addressed NPC while
        /// VEGA speaks (<see cref="NpcVoiceGate.NarratorDuck"/>); 1 otherwise, 0 when denied.</summary>
        public float Volume { get; }

        /// <summary>The NPC whose babble this one cuts (fade it out over <see cref="NpcVoiceGate.CutFadeSeconds"/>), or
        /// <see cref="NpcVoiceGate.NoNpc"/>.</summary>
        public int CutNpcId { get; }

        /// <summary>A reaction is playing and must fade out — a babble never plays over one.</summary>
        public bool CutReaction { get; }
    }

    /// <summary>
    /// #2592: the NPC voice budget — EVERY NPC sound (the speech babble and the reaction sounds) asks here first, so a
    /// station deck or a busy village never turns into a jumble of voices (maintainer requirement 2026-10-14). Pure: the
    /// caller passes the time, the distance to the listener, whether the place is crowded and whether VEGA or a cutscene
    /// is speaking. A refused request is dropped, never queued — the bubble still shows, just silent.
    /// <list type="bullet">
    /// <item><b>One babble at a time</b>, globally. <see cref="NpcVoicePriority.Addressed"/> &gt; Greeting &gt; Chatter
    /// &gt; Idle: a higher priority cuts the current babble (<see cref="NpcVoiceGrant.CutNpcId"/>, fade ~0.15 s), a lower
    /// or equal one is dropped. The speaking NPC itself may start its next line (a dialog answer) at an equal or higher
    /// priority — that replaces its own babble.</item>
    /// <item><b>Audible radius:</b> babble only within <see cref="BabbleRadius"/> (the addressed NPC
    /// <see cref="AddressedRadius"/>), reactions within <see cref="ReactionRadius"/>.</item>
    /// <item><b>Crowd</b> (more than <see cref="CrowdThreshold"/> NPCs within <see cref="CrowdRadius"/> of the player,
    /// <see cref="IsCrowd"/>): every non-addressed babble shares one gap of <see cref="CrowdGapSeconds"/>; chatter babbles
    /// only for the conversation nearest the player; a greeting at ×<see cref="CrowdGreetingVolume"/>; no idle
    /// murmurs.</item>
    /// <item><b>Reactions</b> never overlap a babble or each other (one reaction channel); an alarm at most every
    /// <see cref="AlertGapSeconds"/>, an idle murmur at most every <see cref="IdleGapSeconds"/> and not within
    /// <see cref="IdleQuietAfterSpeechSeconds"/> of a babble. Off entirely when <see cref="NpcVoice.ReactionsEnabled"/>
    /// is.</item>
    /// <item><b>VEGA and cutscenes first:</b> while either speaks, only the addressed NPC babbles, at
    /// ×<see cref="NarratorAddressedVolume"/> (<see cref="NarratorDuck"/>).</item>
    /// <item><b>Per NPC</b> at least <see cref="PerNpcGapSeconds"/> between two of its own babbles. (An AI text upgrade of a
    /// bubble never asks at all — <see cref="NpcGreetingTracker"/>.)</item>
    /// </list>
    /// </summary>
    public sealed class NpcVoiceGate
    {
        /// <summary>"No NPC" in <see cref="NpcVoiceGrant.CutNpcId"/> / <see cref="BabbleNpc"/>.</summary>
        public const int NoNpc = int.MinValue;

        /// <summary>A babble is audible only from NPCs this close to the listener (metres).</summary>
        public const float BabbleRadius = 12f;

        /// <summary>… the addressed NPC's from this close.</summary>
        public const float AddressedRadius = 20f;

        /// <summary>Reactions only from NPCs this close.</summary>
        public const float ReactionRadius = 10f;

        /// <summary>The radius around the player the crowd is counted in.</summary>
        public const float CrowdRadius = 15f;

        /// <summary>More NPCs than this within <see cref="CrowdRadius"/> make a crowd.</summary>
        public const int CrowdThreshold = 6;

        /// <summary>In a crowd, non-addressed babbles start at most this often (one shared gap).</summary>
        public const float CrowdGapSeconds = 6f;

        /// <summary>A greeting's volume in a crowd.</summary>
        public const float CrowdGreetingVolume = 0.6f;

        /// <summary>In a crowd, a chatter line babbles only when its speaker stands within this much of the nearest
        /// recent chatter speaker's distance — the two partners of the nearest conversation stand ≤ 3.5 m apart.</summary>
        public const float ConversationSlack = 4f;

        /// <summary>How long a chatter line counts as "a conversation going on there".</summary>
        public const float ChatterMemorySeconds = 10f;

        /// <summary>An alarm reaction at most this often.</summary>
        public const float AlertGapSeconds = 8f;

        /// <summary>An idle murmur at most this often (globally).</summary>
        public const float IdleGapSeconds = 20f;

        /// <summary>No idle murmur this soon after a babble ended.</summary>
        public const float IdleQuietAfterSpeechSeconds = 5f;

        /// <summary>At least this long between two babbles of the same NPC.</summary>
        public const float PerNpcGapSeconds = 1f;

        /// <summary>A cut babble or reaction fades out over this long.</summary>
        public const float CutFadeSeconds = 0.15f;

        /// <summary>The addressed NPC's volume while VEGA or a cutscene speaks.</summary>
        public const float NarratorAddressedVolume = 0.5f;

        private int _babbleNpc = NoNpc;
        private NpcVoicePriority _babblePriority;
        private float _babbleEnd = float.NegativeInfinity;
        private float _reactionEnd = float.NegativeInfinity;
        private float _lastCrowdBabble = float.NegativeInfinity;
        private float _lastAlert = float.NegativeInfinity;
        private float _lastIdle = float.NegativeInfinity;
        private readonly Dictionary<int, float> _lastStart = new Dictionary<int, float>();
        private readonly List<ChatterLine> _chatter = new List<ChatterLine>();

        private readonly struct ChatterLine
        {
            public ChatterLine(int npcId, float distance, float at)
            {
                NpcId = npcId;
                Distance = distance;
                At = at;
            }

            public int NpcId { get; }

            public float Distance { get; }

            public float At { get; }
        }

        /// <summary>Whether a crowd stands around the player: more than <see cref="CrowdThreshold"/> NPCs within
        /// <see cref="CrowdRadius"/>.</summary>
        public static bool IsCrowd(int npcsWithinCrowdRadius) => npcsWithinCrowdRadius > CrowdThreshold;

        /// <summary>The babble priority of a bubble of <see cref="NpcGreeting.Kind"/> <paramref name="kind"/>
        /// (<see cref="NpcGreetingKinds"/>); an unknown (newer) kind counts as a greeting.</summary>
        public static NpcVoicePriority PriorityOf(byte kind) => kind switch
        {
            NpcGreetingKinds.Addressed => NpcVoicePriority.Addressed,
            NpcGreetingKinds.Chatter => NpcVoicePriority.Chatter,
            NpcGreetingKinds.Cheer => NpcVoicePriority.Chatter,
            _ => NpcVoicePriority.Greeting,
        };

        /// <summary>The volume factor of a babble of <paramref name="priority"/> while VEGA or a cutscene speaks
        /// (<paramref name="narratorBusy"/>): the addressed NPC at ×<see cref="NarratorAddressedVolume"/>, everyone else
        /// silent (0 = cut it). 1 when nobody else speaks. Asked again for every clip of a chain.</summary>
        public static float NarratorDuck(NpcVoicePriority priority, bool narratorBusy)
            => !narratorBusy ? 1f : priority == NpcVoicePriority.Addressed ? NarratorAddressedVolume : 0f;

        /// <summary>Whether a babble plays at <paramref name="now"/>.</summary>
        public bool BabbleBusy(float now) => now < _babbleEnd;

        /// <summary>The NPC whose babble plays at <paramref name="now"/>, or <see cref="NoNpc"/>.</summary>
        public int BabbleNpc(float now) => BabbleBusy(now) ? _babbleNpc : NoNpc;

        /// <summary>The priority of the babble that plays (meaningful while <see cref="BabbleBusy"/>).</summary>
        public NpcVoicePriority BabblePriority => _babblePriority;

        /// <summary>Whether a reaction plays at <paramref name="now"/>.</summary>
        public bool ReactionBusy(float now) => now < _reactionEnd;

        /// <summary>
        /// Asks to start a babble of <paramref name="seconds"/> for NPC <paramref name="npcId"/> at
        /// <paramref name="distance"/> metres from the listener. See the class summary for the rules; a grant books the
        /// babble channel until <paramref name="now"/> + <paramref name="seconds"/> (or until <see cref="EndBabble"/>).
        /// </summary>
        public NpcVoiceGrant RequestBabble(int npcId, NpcVoicePriority priority, float now, float seconds, float distance,
            bool crowd, bool narratorBusy)
        {
            bool addressed = priority == NpcVoicePriority.Addressed;
            if (priority == NpcVoicePriority.Chatter)
            {
                NoteChatter(npcId, distance, now); // every chatter line marks where a conversation is, granted or not
            }

            if (distance > (addressed ? AddressedRadius : BabbleRadius) || float.IsNaN(distance))
            {
                return NpcVoiceGrant.Denied; // too far away: a silent bubble
            }

            float duck = NarratorDuck(priority, narratorBusy);
            if (duck <= 0f)
            {
                return NpcVoiceGrant.Denied; // VEGA or a cutscene speaks — nobody talks over them but the addressed NPC
            }

            if (_lastStart.TryGetValue(npcId, out float last) && now - last < PerNpcGapSeconds)
            {
                return NpcVoiceGrant.Denied;
            }

            float volume = duck;
            if (crowd && !addressed)
            {
                if (priority == NpcVoicePriority.Idle || now - _lastCrowdBabble < CrowdGapSeconds)
                {
                    return NpcVoiceGrant.Denied;
                }

                if (priority == NpcVoicePriority.Chatter && distance > NearestChatter(now) + ConversationSlack)
                {
                    return NpcVoiceGrant.Denied; // only the conversation nearest the player speaks in a crowd
                }

                if (priority == NpcVoicePriority.Greeting)
                {
                    volume *= CrowdGreetingVolume;
                }
            }

            int cut = NoNpc;
            if (BabbleBusy(now))
            {
                bool own = _babbleNpc == npcId && priority >= _babblePriority; // the speaker's own next line replaces its babble
                if (!own && priority <= _babblePriority)
                {
                    return NpcVoiceGrant.Denied; // the channel is busy with something at least as important — dropped
                }

                if (!own)
                {
                    cut = _babbleNpc;
                }
            }

            bool cutReaction = ReactionBusy(now);
            if (cutReaction)
            {
                _reactionEnd = now; // a babble never plays over a reaction
            }

            _babbleNpc = npcId;
            _babblePriority = priority;
            _babbleEnd = now + (seconds > 0f ? seconds : NpcVoice.ClipSeconds);
            _lastStart[npcId] = now;
            if (!addressed && crowd)
            {
                _lastCrowdBabble = now;
            }

            Prune(now);
            return new NpcVoiceGrant(true, volume, cut, cutReaction);
        }

        /// <summary>
        /// Asks to play a reaction (<see cref="NpcReaction"/>) of <paramref name="seconds"/> for NPC
        /// <paramref name="npcId"/>: only with <see cref="NpcVoice.ReactionsEnabled"/>, within <see cref="ReactionRadius"/>,
        /// while nobody babbles, no other reaction plays and neither VEGA nor a cutscene speaks; an alarm at most every
        /// <see cref="AlertGapSeconds"/>; an idle murmur not in a crowd, at most every <see cref="IdleGapSeconds"/> and not
        /// within <see cref="IdleQuietAfterSpeechSeconds"/> of a babble.
        /// </summary>
        public NpcVoiceGrant RequestReaction(int npcId, NpcReaction kind, float now, float seconds, float distance, bool crowd,
            bool narratorBusy)
        {
            if (!NpcVoice.ReactionsEnabled || narratorBusy || distance > ReactionRadius || float.IsNaN(distance)
                || BabbleBusy(now) || ReactionBusy(now))
            {
                return NpcVoiceGrant.Denied;
            }

            if (kind == NpcReaction.Alert && now - _lastAlert < AlertGapSeconds)
            {
                return NpcVoiceGrant.Denied;
            }

            if (kind == NpcReaction.Idle
                && (crowd || now - _lastIdle < IdleGapSeconds || now < _babbleEnd + IdleQuietAfterSpeechSeconds))
            {
                return NpcVoiceGrant.Denied;
            }

            _reactionEnd = now + (seconds > 0f ? seconds : 1f);
            if (kind == NpcReaction.Alert)
            {
                _lastAlert = now;
            }
            else if (kind == NpcReaction.Idle)
            {
                _lastIdle = now;
            }

            return new NpcVoiceGrant(true, 1f, NoNpc, false);
        }

        /// <summary>The babble of NPC <paramref name="npcId"/> ended early (cut, its NPC left, VEGA started speaking) — the
        /// channel is free from <paramref name="now"/>. A no-op when another NPC holds the channel.</summary>
        public void EndBabble(int npcId, float now)
        {
            if (_babbleNpc == npcId && now < _babbleEnd)
            {
                _babbleEnd = now;
            }
        }

        /// <summary>The reaction that plays ended early — the reaction channel is free from <paramref name="now"/>.</summary>
        public void EndReaction(float now)
        {
            if (now < _reactionEnd)
            {
                _reactionEnd = now;
            }
        }

        /// <summary>Forgets NPC <paramref name="npcId"/> (it left the area): frees the channel if it spoke.</summary>
        public void Forget(int npcId, float now)
        {
            EndBabble(npcId, now);
            _lastStart.Remove(npcId);
        }

        /// <summary>Back to silence (world change).</summary>
        public void Reset()
        {
            _babbleNpc = NoNpc;
            _babbleEnd = float.NegativeInfinity;
            _reactionEnd = float.NegativeInfinity;
            _lastCrowdBabble = float.NegativeInfinity;
            _lastAlert = float.NegativeInfinity;
            _lastIdle = float.NegativeInfinity;
            _lastStart.Clear();
            _chatter.Clear();
        }

        private void NoteChatter(int npcId, float distance, float now)
        {
            for (int i = _chatter.Count - 1; i >= 0; i--)
            {
                if (_chatter[i].NpcId == npcId || now - _chatter[i].At > ChatterMemorySeconds)
                {
                    _chatter.RemoveAt(i);
                }
            }

            _chatter.Add(new ChatterLine(npcId, distance, now));
        }

        /// <summary>The distance of the nearest chatter speaker heard within <see cref="ChatterMemorySeconds"/>.</summary>
        private float NearestChatter(float now)
        {
            float best = float.PositiveInfinity;
            foreach (var line in _chatter)
            {
                if (now - line.At <= ChatterMemorySeconds && line.Distance < best)
                {
                    best = line.Distance;
                }
            }

            return best;
        }

        /// <summary>Keeps the per-NPC table small: entries older than a minute can no longer gate anything.</summary>
        private void Prune(float now)
        {
            if (_lastStart.Count <= 64)
            {
                return;
            }

            var old = new List<int>();
            foreach (var pair in _lastStart)
            {
                if (now - pair.Value > 60f)
                {
                    old.Add(pair.Key);
                }
            }

            foreach (int id in old)
            {
                _lastStart.Remove(id);
            }
        }
    }
}
