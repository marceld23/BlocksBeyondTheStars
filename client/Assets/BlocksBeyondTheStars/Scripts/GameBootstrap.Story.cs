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
    /// cutscene gate. The state itself and its rules live in the Unity-free <see cref="StoryClientState"/> (#2933); the
    /// members here forward to it under their old names.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>The story state (#2933). A field initializer, so it exists before <see cref="Start"/> runs — and stays
        /// valid when Start returns early.</summary>
        private readonly StoryClientState _story = new StoryClientState();

        /// <summary>Set when new story content (a beat / net fragment / memory) arrives but the Story tab hasn't
        /// been opened since — the menu badges the Story entry while true. Cleared by <see cref="MarkStorySeen"/>.</summary>
        public bool NewStoryUnseen => _story.NewStoryUnseen;
        public void MarkStorySeen() => _story.MarkSeen();

        // --- Story system ("The VEGA Protocol") ---

        /// <summary>The active story's shared progress (story P0) — drives the Story Log meter; null/inactive when off.</summary>
        public StoryStateMessage Story => _story.Story;

        /// <summary>The SPS relay network (#1125): every player station's relay meter + the live jump lanes,
        /// as last sent by the server. Null until the join snapshot arrives; disabled when the data has no relay.</summary>
        public RelayNetworkState RelayNetwork => _story.RelayNetwork;

        /// <summary>Net fragments on the current world (synced) — rendered by <c>NetFragmentView</c>.</summary>
        public NetStoryFragment[] NetFragments => _story.NetFragments;

        /// <summary>Story Log: revealed net-fragment entries (category + locale key), re-readable in the Story tab.</summary>
        public System.Collections.Generic.List<(string Category, string TextKey)> StoryLogFragments => _story.Fragments;

        /// <summary>Story Log: unlocked personal-memory locale keys.</summary>
        public System.Collections.Generic.List<string> StoryLogMemories => _story.Memories;

        /// <summary>Story Log: found environmental lore texts (site kind + locale key, #1111) — rune
        /// inscriptions, wreck logs, ruin notes. Per player; re-readable in the Story tab + Codex.</summary>
        public System.Collections.Generic.List<(string Site, string TextKey)> StoryLogLore => _story.Lore;

        /// <summary>Story Log: the story's beats this player has heard (locale keys, in arc order). Rebuilt from the server's
        /// <c>StoryStateMessage.HeardBeats</c>, so it survives a rejoin, plus the beats heard live (#2462, B08). Holds only
        /// the pack's beats — other narrator lines (insights, the Guardian reveal, relay lines, old memories) go to the
        /// VEGA log.</summary>
        public System.Collections.Generic.List<string> StoryLogBeats => _story.Beats;

        /// <summary>The beat indexes this player has heard (the server's snapshot plus this session's), ascending — the Story
        /// tab groups them under their chapters (#2470).</summary>
        public System.Collections.Generic.List<int> StoryHeardBeats => _story.HeardBeats;

        /// <summary>The definition of the save's active story, or null (#2470: the Story tab's chapter list reads it).</summary>
        public BlocksBeyondTheStars.Shared.Story.StoryDefinition StoryPack => _story.ActivePack(Content);

        /// <summary>#2471: the Journal's stories as last sent by the server (tag 300) — null until a current server sent them
        /// (an older one never does; the Journal then falls back to the story state and the old tutorial button).</summary>
        public StoryPackStates StoryPackStates => _story.StoryPackStates;

        /// <summary>#2471: the player's objectives in the chip order (tag 299) — the Journal's live progress and the optional
        /// "Show all objectives" lines. Null until a current server sent it.</summary>
        public ObjectiveList Objectives => _story.Objectives;

        /// <summary>#2471: bumped whenever the stories or the objective list arrive — the open Journal redraws on a change.</summary>
        public int JournalVersion => _story.JournalVersion;

        /// <summary>#2500: the stories this world runs that this client does not have (a newer build's pack) — the one-time
        /// notice VEGA's panel shows (<see cref="UnknownStoryPacks.TakeNotice"/>) and the neutral text their keys read as in the
        /// panel, on the chip and in the Journal. One per world session (this rig is rebuilt per world).</summary>
        public UnknownStoryPacks UnknownStories => _story.UnknownStories;

        /// <summary>#2500: whether this client has a story pack. Without content loaded yet every story counts as known — no
        /// notice on a guess.</summary>
        public bool HasStoryPack(string packId) => StoryClientState.HasPack(Content, packId);

        /// <summary>Re-readable VEGA tips (locale keys, #737): rebuilt from the server's milestone snapshot on
        /// join (canonical order) and appended live as onboarding/advisor/system lines arrive this session.</summary>
        public System.Collections.Generic.List<string> VegaLogKeys => _story.VegaLogKeys;

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

        /// <summary>Start's subscriptions for the story: VEGA's lines, story packs, objectives, the VEGA journal, the
        /// relay network, the story state and its fragments. Called once from <see cref="Start"/>, before the
        /// transport connects. The rules live in <see cref="StoryClientState"/>; the content is read as each message
        /// arrives.</summary>
        private void WireStory()
        {
            Network.ShipAiLineReceived += m =>
            {
                // The settings button offers "skip" while the tutorial runs and "restart" after it (#2462, B03): the server
                // says so outright, whatever the chip shows — the finale may sit on it in the Guardian system while the
                // lessons are unfinished. An older server is read from the chip's key prefix as before.
                OnboardingActive = ObjectiveSourceRules.TutorialRunning(m.ObjectiveSource, m.ObjectiveKey, m.TutorialActive);
                _story.OnShipAiLine(m, Content); // the beat list, the VEGA log and the Story badge

                // #2471 (B6.4): the dock lesson's help sets the station as the flight target — unless the player set one.
                if (ObjectiveTracker.WaypointToAdopt(SpaceWaypointId, SpaceWaypointPos.HasValue, m.SpaceWaypointId) is { } waypoint)
                {
                    SpaceWaypointId = waypoint;
                }
            };
            Network.StoryPackStatesReceived += m => _story.OnStoryPackStates(m, Content); // #2471: the Journal's stories
            Network.ObjectiveListReceived += m => _story.OnObjectiveList(m); // #2471: the objectives in the chip order
            Network.VegaJournalReceived += m => _story.OnVegaJournal(m, Content);
            Network.RelayNetworkReceived += m => _story.OnRelayNetwork(m); // #1125: server-authoritative, render-only
            Network.StoryStateReceived += m => _story.OnStoryState(m, Content);
            Network.NetFragmentsReceived += m => _story.OnNetFragments(m);
            Network.NetFragmentRevealedReceived += m =>
            {
                _story.RevealFragment(m); // into the Story Log, and the Story menu entry is badged
                // The reader panel (#1110) replaces the old toast; the Story Log keeps the text to re-read.
                OpenReader(Localizer?.Get("ui.reader.fragment") ?? "Net fragment",
                    Localizer?.Get("lore.cat." + m.Category) ?? m.Category, m.TextKey);
            };
            Network.PlayerMemoryReceived += m =>
            {
                _story.RevealMemory(m);
                OpenReader(Localizer?.Get("ui.reader.memory") ?? "Memory", string.Empty, m.TextKey);
            };
            Network.LoreTextRevealedReceived += m =>
            {
                _story.RevealLore(m);
                OpenReader(Localizer?.Get("ui.lore.site." + m.Site) ?? m.Site, string.Empty, m.TextKey);
            };
        }
    }
}
