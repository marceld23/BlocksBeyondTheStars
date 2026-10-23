// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's story state (#2912): the VEGA story progress, the story log, the journal, story packs and the
    /// cutscene gate.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>Set when new story content (a beat / net fragment / memory) arrives but the Story tab hasn't
        /// been opened since — the menu badges the Story entry while true. Cleared by <see cref="MarkStorySeen"/>.</summary>
        public bool NewStoryUnseen { get; private set; }
        public void MarkStorySeen() => NewStoryUnseen = false;

        // --- Story system ("The VEGA Protocol") ---

        /// <summary>The active story's shared progress (story P0) — drives the Story Log meter; null/inactive when off.</summary>
        public StoryStateMessage Story { get; private set; }

        /// <summary>The SPS relay network (#1125): every player station's relay meter + the live jump lanes,
        /// as last sent by the server. Null until the join snapshot arrives; disabled when the data has no relay.</summary>
        public RelayNetworkState RelayNetwork { get; private set; }

        /// <summary>Net fragments on the current world (synced) — rendered by <c>NetFragmentView</c>.</summary>
        public NetStoryFragment[] NetFragments { get; private set; } = System.Array.Empty<NetStoryFragment>();

        /// <summary>Story Log: revealed net-fragment entries (category + locale key), re-readable in the Story tab.</summary>
        public readonly System.Collections.Generic.List<(string Category, string TextKey)> StoryLogFragments = new();

        /// <summary>Story Log: unlocked personal-memory locale keys.</summary>
        public readonly System.Collections.Generic.List<string> StoryLogMemories = new();

        /// <summary>Story Log: found environmental lore texts (site kind + locale key, #1111) — rune
        /// inscriptions, wreck logs, ruin notes. Per player; re-readable in the Story tab + Codex.</summary>
        public readonly System.Collections.Generic.List<(string Site, string TextKey)> StoryLogLore = new();

        /// <summary>Story Log: the story's beats this player has heard (locale keys, in arc order). Rebuilt from the server's
        /// <c>StoryStateMessage.HeardBeats</c>, so it survives a rejoin, plus the beats heard live (#2462, B08). Holds only
        /// the pack's beats — other narrator lines (insights, the Guardian reveal, relay lines, old memories) go to the
        /// VEGA log.</summary>
        public System.Collections.Generic.List<string> StoryLogBeats => _storyBeats.Keys;

        private readonly BlocksBeyondTheStars.Client.Core.StoryBeatLog _storyBeats = new();

        /// <summary>The beat indexes this player has heard (the server's snapshot plus this session's), ascending — the Story
        /// tab groups them under their chapters (#2470).</summary>
        public System.Collections.Generic.List<int> StoryHeardBeats => _storyBeats.Heard;

        /// <summary>The definition of the save's active story, or null (#2470: the Story tab's chapter list reads it).</summary>
        public BlocksBeyondTheStars.Shared.Story.StoryDefinition StoryPack => ActiveStoryPack();

        /// <summary>The definition of the save's active story, or null — no story state yet, the "none" sandbox, or a pack
        /// this client does not have.</summary>
        private BlocksBeyondTheStars.Shared.Story.StoryDefinition ActiveStoryPack()
            => Story != null && Content != null && Content.TryGetStory(Story.StoryId, out var pack) ? pack : null;

        /// <summary>Adds the relay insights the save has spoken (#2470, <c>StoryStateMessage.RelayInsightKeys</c>) and the world
        /// story's other narrator lines this player heard (#2471, <c>NoteKeys</c>: the Guardian reveal, the insights, the
        /// resolution line) to the VEGA log — the Journal's "VEGA's notes" — so they survive a rejoin.</summary>
        private void MergeRelayInsightsIntoLog()
        {
            AddToVegaLog(Story?.RelayInsightKeys);
            AddToVegaLog(Story?.NoteKeys);
        }

        private void AddToVegaLog(string[] keys)
        {
            foreach (string key in keys ?? System.Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(key) && !VegaLogKeys.Contains(key))
                {
                    VegaLogKeys.Add(key);
                }
            }
        }

        /// <summary>#2471: the Journal's stories as last sent by the server (tag 300) — null until a current server sent them
        /// (an older one never does; the Journal then falls back to the story state and the old tutorial button).</summary>
        public StoryPackStates StoryPackStates { get; private set; }

        /// <summary>#2471: the player's objectives in the chip order (tag 299) — the Journal's live progress and the optional
        /// "Show all objectives" lines. Null until a current server sent it.</summary>
        public ObjectiveList Objectives { get; private set; }

        /// <summary>#2471: bumped whenever the stories or the objective list arrive — the open Journal redraws on a change.</summary>
        public int JournalVersion { get; private set; }

        /// <summary>#2471: the offered chapters (<c>pack/chapter</c>) seen in this session — a new offer badges the Journal.</summary>
        private readonly System.Collections.Generic.HashSet<string> _journalOffers = new System.Collections.Generic.HashSet<string>();
        private bool _journalOffersSeeded;

        /// <summary>#2500: the stories this world runs that this client does not have (a newer build's pack) — the one-time
        /// notice VEGA's panel shows (<see cref="UnknownStoryPacks.TakeNotice"/>) and the neutral text their keys read as in the
        /// panel, on the chip and in the Journal. One per world session (this rig is rebuilt per world).</summary>
        public readonly UnknownStoryPacks UnknownStories = new UnknownStoryPacks();

        /// <summary>#2500: whether this client has a story pack. Without content loaded yet every story counts as known — no
        /// notice on a guess.</summary>
        public bool HasStoryPack(string packId)
            => Content == null || (!string.IsNullOrEmpty(packId) && Content.Stories.ContainsKey(packId));

        /// <summary>#2471: takes a new story list; a chapter offered since the last one badges the Journal tab.</summary>
        private void OnStoryPackStates(StoryPackStates m)
        {
            StoryPackStates = m;
            JournalVersion++;
            UnknownStories.Observe(UnknownStoryPacks.PackIdsOf(m), HasStoryPack); // #2500: a story this client lacks
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

        /// <summary>Re-readable VEGA tips (locale keys, #737): rebuilt from the server's milestone snapshot on
        /// join (canonical order) and appended live as onboarding/advisor/system lines arrive this session.</summary>
        public readonly System.Collections.Generic.List<string> VegaLogKeys = new();

        /// <summary>True while the first-spawn prologue overlay is up (#738) — it owns Esc, so the app shell
        /// must not treat that press as "leave game" (same contract as <see cref="MenuOpen"/>).</summary>
        public bool VegaPrologueActive { get; set; }

        /// <summary>True while a client-side cinematic (the staged prologue, #760) owns the camera — the
        /// on-foot controller freezes movement/look like an open menu, but the cursor stays locked.</summary>
        public bool CinematicCameraActive { get; set; }

        /// <summary>The one shared "a cutscene owns the screen" state (#2461). The staged prologue, the ending and the
        /// shell intro each open a reason while they play (<see cref="CutsceneGate.Begin"/>/<see cref="CutsceneGate.End"/>);
        /// every overlay reads <see cref="CutsceneActive"/> — the HUD (crosshair included), VEGA's panel and objective chip,
        /// the touch controls, the chat, the nameplates, the IMGUI overlays — and the controls freeze. Static because the
        /// shell intro runs before any world exists; the world's own reasons are cleared when the world goes.</summary>
        public static readonly CutsceneGate Cutscenes = new CutsceneGate();

        /// <summary>True while any cutscene owns the screen — see <see cref="Cutscenes"/>.</summary>
        public static bool CutsceneActive => Cutscenes.Active;

        /// <summary>#2496: true while the HUD steps aside — a cutscene owns the screen, or a scene hides only the HUD (its
        /// <c>hideHud</c> on a T3 overlay such as the tutorial's "Lift-off!", the controls stay free). Read by the HUD, the flight
        /// HUD, the space radar and the name tags; the touch controls, the chat and the controls keep reading
        /// <see cref="CutsceneActive"/>, so a touch pilot keeps the stick under an overlay.</summary>
        public static bool HudHidden => Cutscenes.HudHidden;

        /// <summary>A fresh process (or an Editor play session without a domain reload) starts with no cutscene.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCutscenesOnLoad() => Cutscenes.Clear();
    }
}
