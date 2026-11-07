// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class GameServer
{
    /// <summary>The prefix of the per-player reward milestones (the finale's reward crate, <c>reward:guardian_core</c>).</summary>
    private const string RewardMilestonePrefix = "reward:";

    /// <summary>
    /// #3386: the world story and the reporter's part of it, for the bump snapshot. A report "the cutscene did not come after
    /// I shut down the Guardian core" could not be answered from the snapshot — it listed only the basic training and VEGA's
    /// tips. Now it carries the world story (its id, the world's chapter, the beats, the finale's state, the running hack and
    /// duel), whether the player's track started, and the player's story milestones of every non-tutorial pack (beats, the
    /// cutscene ledger with its <c>:sent</c> keys, starts and opt-outs) plus the reward milestones.
    /// </summary>
    private object BumpStorySnapshot(PlayerSession session)
    {
        string tutorialPrefix = StoryPackKeys.Prefix(OfficialTutorialPackId);
        int chapter = WorldChapterIndex();
        return new
        {
            id = _story?.Id ?? StoryRegistry.NoneStoryId,
            worldChapter = chapter,
            worldChapterId = ChapterAt(chapter)?.Id ?? string.Empty,
            beatsRevealed = _storyState.BeatsRevealed,
            guardianSystemRevealed = _storyState.GuardianSystemRevealed,
            guardianDefeated = _storyState.GuardianDefeated,
            coreHacked = _coreHacked,
            duelNode = _coreHacked ? _duelNode : -1,
            trackStarted = StoryTrackStarted(session),
            milestones = session.State.Milestones
                .Where(m => (m.StartsWith("story:", StringComparison.Ordinal) && !m.StartsWith(tutorialPrefix, StringComparison.Ordinal))
                            || m.StartsWith(RewardMilestonePrefix, StringComparison.Ordinal))
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToList(),
        };
    }
}
