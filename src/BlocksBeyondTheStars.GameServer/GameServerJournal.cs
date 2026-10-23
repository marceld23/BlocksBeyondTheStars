// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Journal (#2471, synthesis §5.8) on the server: the facade of <see cref="JournalSystem"/> (ADR 0019), which builds
/// the Journal's two messages (<see cref="StoryPackStates"/>, <see cref="ObjectiveList"/>) and coalesces their pushes.
/// This partial keeps the members the story partials, the join, the tick and the tests call — same names, same
/// signatures, each one line delegating — and the explicit <see cref="IJournalHost"/> implementation.
/// </summary>
public sealed partial class GameServer : IJournalHost
{
    /// <summary>Shortest gap between two coalesced Journal pushes to one player (seconds of server uptime).</summary>
    internal const double JournalPushGapSeconds = JournalSystem.JournalPushGapSeconds;

    /// <summary>From this many missed story beats on, a started player's join catch-up speaks ONE line pointing at the
    /// Journal instead of one line per beat (#2471, B09).</summary>
    internal const int CatchUpCollapseThreshold = JournalSystem.CatchUpCollapseThreshold;

    /// <summary>Something the Journal shows changed for this player; the next <see cref="TickJournal"/> sends it.</summary>
    private static void MarkJournalDirty(PlayerSession s) => JournalSystem.MarkJournalDirty(s);

    /// <summary>Per server tick (wherever the players are — in space too): pushes the Journal of every dirty session whose
    /// last push is at least <see cref="JournalPushGapSeconds"/> old.</summary>
    private void TickJournal() => _journalSystem.TickJournal();

    /// <summary>Sends the Journal's two messages to a <c>packs1</c> client where they differ from what it has —
    /// <paramref name="force"/> sends both (the join, a request).</summary>
    private void PushJournal(PlayerSession s, bool force) => _journalSystem.PushJournal(s, force);

    /// <summary>The world story's narrator lines that are not beats which this player has heard (#2471).</summary>
    private string[] StoryNoteKeys(PlayerSession s) => _journalSystem.StoryNoteKeys(s);

    // ---------------------------------------------------------------------------------------------
    // Test seams.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test seam: the Journal's stories for a player as they would be sent now.</summary>
    internal StoryPackStates StoryPackStatesForTest(PlayerSession s) => _journalSystem.StoryPackStatesForTest(s);

    /// <summary>Test seam: the objective list for a player as it would be sent now.</summary>
    internal ObjectiveList ObjectiveListForTest(PlayerSession s) => _journalSystem.ObjectiveListForTest(s);

    /// <summary>Test seam: the coalesced Journal pass of one server tick.</summary>
    internal void TickJournalForTest() => TickJournal();

    /// <summary>Test seam: whether the player's Journal waits for a push.</summary>
    internal bool JournalDirtyForTest(PlayerSession s) => s.Story.JournalDirty;

    // IJournalHost: each member delegates to the server's own member of the same name (never to the interface member,
    // which would recurse).
    StoryDefinition? IJournalHost.Story => _story;

    StoryState IJournalHost.StoryState => _storyState;

    bool IJournalHost.StoryActive => StoryActive;

    bool IJournalHost.StoryTrackStarted(PlayerSession s) => StoryTrackStarted(s);

    string? IJournalHost.StoryTrackStartReason(PlayerSession s) => StoryTrackStartReason(s);

    NetStoryLead[] IJournalHost.JournalLeads(PlayerSession s) => JournalLeads(s);

    NetStoryMission[] IJournalHost.JournalMissions(PlayerSession s) => JournalMissions(s);

    string IJournalHost.GuardianRevealHeardKey => GuardianRevealHeardKey;

    string IJournalHost.ResolutionWatchedKey => ResolutionWatchedKey;

    IReadOnlyList<CompiledPack> IJournalHost.PlayerPacks => PlayerPacks;

    bool IJournalHost.TryGetPlayerPack(string? packId, out CompiledPack pack) => TryGetPlayerPack(packId, out pack);

    bool IJournalHost.PackRunsFor(PlayerSession s, CompiledPack pack) => PackRunsFor(s, pack);

    bool IJournalHost.StoryPackEnabledFor(PlayerSession who, string packId) => StoryPackEnabledFor(who, packId);

    bool IJournalHost.CanStartPackOnRequest(PlayerSession s, CompiledPack pack) => CanStartPackOnRequest(s, pack);

    bool IJournalHost.ChapterVisible(PlayerSession s, CompiledChapter ch) => ChapterVisible(s, ch);

    bool IJournalHost.ChapterRuns(PlayerSession s, CompiledChapter ch) => ChapterRuns(s, ch);

    bool IJournalHost.ChapterSpentFor(PlayerSession s, CompiledChapter ch) => ChapterSpentFor(s, ch);

    bool IJournalHost.EvaluateStoryCondition(StoryCondition? condition, PlayerSession who) => EvaluateStoryCondition(condition, who);

    CompiledStep? IJournalHost.ChipStepOf(PlayerSession s, CompiledPack pack) => ChipStepOf(s, pack);

    string IJournalHost.StepChipKey(PlayerSession s, CompiledStep step) => StepChipKey(s, step);

    string IJournalHost.StepStartKey(PlayerSession s, CompiledStep step) => StepStartKey(s, step);

    IEnumerable<VegaChip> IJournalHost.ChipCandidates(PlayerSession session, bool tutorialActive) => ChipCandidates(session, tutorialActive);
}
