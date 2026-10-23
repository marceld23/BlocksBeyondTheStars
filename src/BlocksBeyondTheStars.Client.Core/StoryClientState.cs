// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the client knows of the story (#2933): the VEGA story's shared progress, the relay network, the world's net
    /// fragments, the Story Log (fragments, memories, lore, the heard beats), the Journal's stories and objectives with its
    /// "new offer" badge, the stories this client does not have and the VEGA log. Unity-free, so the same class runs in the
    /// headless client tests (the <see cref="BioClientState"/> precedent); it only mirrors what the server sent. The content
    /// is passed per call — the rig loads it in <c>Start</c>, after this state exists, and a missing content means "every
    /// story is known". One instance per world session: GameBootstrap holds it and forwards its story members here.
    /// </summary>
    public sealed class StoryClientState
    {
        private readonly BlocksBeyondTheStars.Client.Core.StoryBeatLog _storyBeats = new();

        /// <summary>#2471: the offered chapters (<c>pack/chapter</c>) seen in this session — a new offer badges the Journal.</summary>
        private readonly HashSet<string> _journalOffers = new HashSet<string>();
        private bool _journalOffersSeeded;

        /// <summary>Set when new story content (a beat / net fragment / memory) arrives but the Story tab hasn't
        /// been opened since — the menu badges the Story entry while true. Cleared by <see cref="MarkSeen"/>.</summary>
        public bool NewStoryUnseen { get; private set; }

        /// <summary>The Story tab was opened: its badge goes out.</summary>
        public void MarkSeen() => NewStoryUnseen = false;

        /// <summary>The active story's shared progress (story P0) — drives the Story Log meter; null/inactive when off.</summary>
        public StoryStateMessage? Story { get; private set; }

        /// <summary>The SPS relay network (#1125): every player station's relay meter + the live jump lanes,
        /// as last sent by the server. Null until the join snapshot arrives; disabled when the data has no relay.</summary>
        public RelayNetworkState? RelayNetwork { get; private set; }

        /// <summary>Net fragments on the current world (synced) — rendered by <c>NetFragmentView</c>.</summary>
        public NetStoryFragment[] NetFragments { get; private set; } = System.Array.Empty<NetStoryFragment>();

        /// <summary>Story Log: revealed net-fragment entries (category + locale key), re-readable in the Story tab.</summary>
        public List<(string Category, string TextKey)> Fragments { get; } = new();

        /// <summary>Story Log: unlocked personal-memory locale keys.</summary>
        public List<string> Memories { get; } = new();

        /// <summary>Story Log: found environmental lore texts (site kind + locale key, #1111) — rune
        /// inscriptions, wreck logs, ruin notes. Per player; re-readable in the Story tab + Codex.</summary>
        public List<(string Site, string TextKey)> Lore { get; } = new();

        /// <summary>Story Log: the story's beats this player has heard (locale keys, in arc order). Rebuilt from the server's
        /// <c>StoryStateMessage.HeardBeats</c>, so it survives a rejoin, plus the beats heard live (#2462, B08). Holds only
        /// the pack's beats — other narrator lines (insights, the Guardian reveal, relay lines, old memories) go to the
        /// VEGA log.</summary>
        public List<string> Beats => _storyBeats.Keys;

        /// <summary>The beat indexes this player has heard (the server's snapshot plus this session's), ascending — the Story
        /// tab groups them under their chapters (#2470).</summary>
        public List<int> HeardBeats => _storyBeats.Heard;

        /// <summary>#2471: the Journal's stories as last sent by the server (tag 300) — null until a current server sent them
        /// (an older one never does; the Journal then falls back to the story state and the old tutorial button).</summary>
        public StoryPackStates? StoryPackStates { get; private set; }

        /// <summary>#2471: the player's objectives in the chip order (tag 299) — the Journal's live progress and the optional
        /// "Show all objectives" lines. Null until a current server sent it.</summary>
        public ObjectiveList? Objectives { get; private set; }

        /// <summary>#2471: bumped whenever the stories or the objective list arrive — the open Journal redraws on a change.</summary>
        public int JournalVersion { get; private set; }

        /// <summary>#2500: the stories this world runs that this client does not have (a newer build's pack) — the one-time
        /// notice VEGA's panel shows (<see cref="UnknownStoryPacks.TakeNotice"/>) and the neutral text their keys read as in the
        /// panel, on the chip and in the Journal. One per world session (the rig is rebuilt per world).</summary>
        public UnknownStoryPacks UnknownStories { get; } = new UnknownStoryPacks();

        /// <summary>Re-readable VEGA tips (locale keys, #737): rebuilt from the server's milestone snapshot on
        /// join (canonical order) and appended live as onboarding/advisor/system lines arrive this session. One list for
        /// this state's whole life — VEGA's panel appends its own one-shot lessons to it.</summary>
        public List<string> VegaLogKeys { get; } = new();

        /// <summary>#2500: whether this client has a story pack. Without content loaded yet every story counts as known — no
        /// notice on a guess.</summary>
        public static bool HasPack(GameContent? content, string packId)
            => content == null || (!string.IsNullOrEmpty(packId) && content.Stories.ContainsKey(packId));

        /// <summary>The definition of the save's active story, or null — no story state yet, the "none" sandbox, no content
        /// loaded yet, or a pack this client does not have.</summary>
        public BlocksBeyondTheStars.Shared.Story.StoryDefinition? ActivePack(GameContent? content)
            => Story != null && content != null && content.TryGetStory(Story.StoryId, out var pack) ? pack : null;

        /// <summary>#2471: takes a new story list; a chapter offered since the last one badges the Journal tab.</summary>
        public void OnStoryPackStates(StoryPackStates m, GameContent? content)
        {
            StoryPackStates = m;
            JournalVersion++;
            UnknownStories.Observe(UnknownStoryPacks.PackIdsOf(m), id => HasPack(content, id)); // #2500: a story this client lacks
            bool fresh = false;
            foreach (var pack in m?.Packs ?? System.Array.Empty<NetStoryPack>())
            {
                foreach (var chapter in pack.Chapters)
                {
                    // #2500: an offer with nothing left to practise has no Start / Later — it never badges the tab.
                    if (chapter.State == StoryChapterStates.Offered && !chapter.NothingLeft && _journalOffers.Add(pack.Id + "/" + chapter.Id))
                    {
                        fresh = true;
                    }
                }

                // #2476: a story mission VEGA just handed out lights the Journal tab too.
                foreach (var mission in pack.Missions ?? System.Array.Empty<NetStoryMission>())
                {
                    if (mission.State == StoryChapterStates.Active && _journalOffers.Add(pack.Id + "/mission/" + mission.Id))
                    {
                        fresh = true;
                    }
                }
            }

            if (fresh && _journalOffersSeeded)
            {
                NewStoryUnseen = true;
            }

            _journalOffersSeeded = true;
        }

        /// <summary>#2471: takes the objectives in the chip order (live progress, "show all objectives").</summary>
        public void OnObjectiveList(ObjectiveList m)
        {
            Objectives = m; // #2471: the objectives in the chip order (live progress, "show all objectives")
            JournalVersion++;
        }

        /// <summary>Takes the server's VEGA journal: the persisted milestones rebuild the VEGA log.</summary>
        public void OnVegaJournal(VegaJournal m, GameContent? content)
        {
            // The persisted snapshot replaces any live-collected keys — it is the authoritative set. The basic training's
            // lessons come from the tutorial pack's own steps (#2469; the client's copy of the old stage list is gone).
            VegaLogKeys.Clear();
            VegaLogKeys.AddRange(VegaText.JournalKeys(m.Milestones, content?.Stories.Values));
            MergeRelayInsightsIntoLog(); // #2470: the snapshot does not carry the save's relay insights
        }

        /// <summary>#1125: takes the relay network — server-authoritative, render-only.</summary>
        public void OnRelayNetwork(RelayNetworkState m) => RelayNetwork = m; // #1125: server-authoritative, render-only

        /// <summary>Takes the story state: the Story Log and the beat list are rebuilt from its found keys.</summary>
        public void OnStoryState(StoryStateMessage m, GameContent? content)
        {
            Story = m;
            UnknownStories.Observe(UnknownStoryPacks.PackIdsOf(m), id => HasPack(content, id)); // #2500: a world story this client lacks
            // Rejoin-proof logs (#1110/#1111): the snapshot carries the found KEYS; texts resolve from
            // the pack. Replaces the live-collected lists — the server is authoritative.
            var pack = ActivePack(content);
            if (pack != null)
            {
                Fragments.Clear();
                Fragments.AddRange(BlocksBeyondTheStars.Client.Core.StoryLog.Fragments(pack, m.FoundFragmentKeys));
                Memories.Clear();
                Memories.AddRange(BlocksBeyondTheStars.Client.Core.StoryLog.Memories(pack, m.PlayerMemoryKeys));
                Lore.Clear();
                Lore.AddRange(BlocksBeyondTheStars.Client.Core.StoryLog.Lore(pack, m.FoundLoreKeys));
            }

            // The beats this player heard (#2462, B08) — the beat list survives a rejoin now.
            _storyBeats.OnStoryState(pack, m.StoryId, m.HeardBeats);
            MergeRelayInsightsIntoLog(); // #2470: relay insights spoken before this player's story track started
        }

        /// <summary>Takes the current world's net fragments.</summary>
        public void OnNetFragments(NetFragmentList m) => NetFragments = m.Fragments;

        /// <summary>A VEGA line arrived: a story beat goes to the beat list, the lines worth re-reading to the VEGA log, and
        /// new story content or the Journal's news badges the Story entry.</summary>
        public void OnShipAiLine(ShipAiLine m, GameContent? content)
        {
            bool storyLine = m.Kind == 2 && !string.IsNullOrEmpty(m.LineKey);
            if (storyLine)
            {
                NewStoryUnseen = true;
                // Only the pack's beats go to the beat list (#2462, B08); the other narrator lines (insights, the
                // Guardian reveal, relay lines, old memories) stay re-readable in the VEGA log below.
                storyLine = _storyBeats.OnStoryLine(ActivePack(content), m.LineKey);
            }

            // Tips log (#737): onboarding (0), advisor (1) and system (3) lines become re-readable the
            // moment they arrive, and so do the narrator lines that are not beats. Kind 4 (prologue) is
            // intro-only by design. #2471: a line that only points at the Journal (or sets a flight target) is not kept.
            bool logged = (m.Kind is 0 or 1 or 3 or 5 || (m.Kind == 2 && !storyLine)) && !VegaText.IsTransientLine(m.LineKey);
            if (logged && !string.IsNullOrEmpty(m.LineKey) && !VegaLogKeys.Contains(m.LineKey))
            {
                VegaLogKeys.Add(m.LineKey);
            }

            // #2471 (B09): "N new entries in your Journal" — the beats went to the Journal silently; badge its tab.
            if (m.LineKey == BlocksBeyondTheStars.Shared.Story.StoryNarratorLines.JournalNews)
            {
                NewStoryUnseen = true;
            }
        }

        /// <summary>A net fragment was read: it joins the Story Log (once) and badges the Story entry.</summary>
        public void RevealFragment(NetFragmentRevealed m)
        {
            if (!Fragments.Contains((m.Category, m.TextKey)))
            {
                Fragments.Add((m.Category, m.TextKey));
            }

            NewStoryUnseen = true; // badge the Story menu entry until the player reads it
        }

        /// <summary>A personal memory unlocked: it joins the Story Log (once) and badges the Story entry.</summary>
        public void RevealMemory(PlayerMemoryRevealed m)
        {
            if (!Memories.Contains(m.TextKey))
            {
                Memories.Add(m.TextKey);
            }

            NewStoryUnseen = true;
        }

        /// <summary>An environmental lore text was found: it joins the Story Log (once) and badges the Story entry.</summary>
        public void RevealLore(LoreTextRevealed m)
        {
            if (!Lore.Contains((m.Site, m.TextKey)))
            {
                Lore.Add((m.Site, m.TextKey));
            }

            NewStoryUnseen = true;
        }

        /// <summary>The active world changed (travel): its net fragments go — the new world re-sends its own. The Story Log,
        /// the VEGA log and the Journal state are per player and stay.</summary>
        public void ResetWorld() => NetFragments = System.Array.Empty<NetStoryFragment>();

        /// <summary>Adds the relay insights the save has spoken (#2470, <c>StoryStateMessage.RelayInsightKeys</c>) and the world
        /// story's other narrator lines this player heard (#2471, <c>NoteKeys</c>: the Guardian reveal, the insights, the
        /// resolution line) to the VEGA log — the Journal's "VEGA's notes" — so they survive a rejoin.</summary>
        private void MergeRelayInsightsIntoLog()
        {
            AddToVegaLog(Story?.RelayInsightKeys);
            AddToVegaLog(Story?.NoteKeys);
        }

        private void AddToVegaLog(string[]? keys)
        {
            foreach (string key in keys ?? System.Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(key) && !VegaLogKeys.Contains(key))
                {
                    VegaLogKeys.Add(key);
                }
            }
        }
    }
}
