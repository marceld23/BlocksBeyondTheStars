// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A player pack that grew after a player was through it (#3910: the Sounders' Song got chapters 9 to 11). The step
/// runtime walks every chapter whatever the pack's "done" stamp says, so a player who <b>played</b> the story gets its
/// new chapters by themselves — and the Journal shows the pack as running until they are through
/// (<c>JournalSystem.NetPackOf</c> derives "done" from the chapters). A player who <b>skipped the pack as a whole</b>
/// ("Skip all") made another choice: for them the new chapters are closed as skipped, silently — nothing starts, nothing
/// is said, nothing is handed over. They can still restart the story from the Journal.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>What a pack needs before its chapters are walked: the one-time migration, then the rule for a pack that
    /// grew (<see cref="KeepWhollySkippedPackSkipped"/>).</summary>
    private void PreparePackWalk(PlayerSession s, CompiledPack pack)
    {
        MigratePackIfNeeded(s, pack);
        KeepWhollySkippedPackSkipped(s, pack);
    }

    /// <summary>
    /// Closes the new non-optional chapters of a pack as skipped for a player who skipped all of it before: the pack is
    /// stamped done, every non-optional chapter that is through was skipped (none done, none replayed), and the open ones
    /// have not begun. A pack without the stamp, a pack the player played any chapter of, and a chapter that already runs
    /// are left alone.
    /// </summary>
    private void KeepWhollySkippedPackSkipped(PlayerSession s, CompiledPack pack)
    {
        if (!PackDone(s, pack))
        {
            return;
        }

        var m = MilestonesOf(s);
        var mandatory = pack.Chapters.Where(c => !c.Optional).ToList();
        var open = mandatory.Where(c => !ChapterDoneOrSkipped(s, c)).ToList();
        if (open.Count == 0 || open.Count == mandatory.Count
            || mandatory.Any(c => m.Contains(StoryPackKeys.ChapterDone(pack.Id, c.Id)) || ChapterReplaying(s, c))
            || open.Any(c => m.Contains(StoryPackKeys.ChapterActive(pack.Id, c.Id)) || c.Steps.Any(st => StepActive(s, st) || StepDoneOrSkipped(s, st))))
        {
            return;
        }

        foreach (var chapter in open)
        {
            m.Add(StoryPackKeys.ChapterSkipped(pack.Id, chapter.Id));
        }

        MarkJournalDirty(s);
        _repo.SavePlayer(s.State);
        _log.Info($"Story pack '{pack.Id}' grew by {open.Count} chapter(s); '{s.State.Name}' had skipped it as a whole — they stay skipped.");
    }
}
