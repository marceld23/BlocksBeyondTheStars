// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What VEGA says about the sleeper (#3897, canon C21: a clone grown now from the imprint that lay sealed in the tank — never
/// an original survivor; he himself believes it is still that night). Her truth follows what the PLAYER'S story knows:
/// before "What You Are" (B9) she only says she does not understand what the tank did
/// (<see cref="SleeperUnsureLine"/>, once per player); from B9 she says it quietly — "He is like you" — as the pack's insight
/// <see cref="SleeperInsightEvent"/> (once per player, kept in the Journal's VEGA notes). Both come after the player talked
/// his dialog through, while they still stand near him; a player who heard the first line hears the second the next time
/// they are with him after B9. Nothing here tells him.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The story insight "He is like you" (held until the player's story heard B9).</summary>
    internal const string SleeperInsightEvent = "sps_guard";

    /// <summary>VEGA's line before the player's story knows what they are.</summary>
    internal const string SleeperUnsureLine = "story.vega.guard.unsure";

    /// <summary>The per-player once-key of <see cref="SleeperUnsureLine"/>.</summary>
    internal const string SleeperUnsureKey = "story:sps_guard:unsure";

    /// <summary>The VEGA beat after which a player's story knows what they are (B9, "What You Are").</summary>
    private const int SleeperCloneBeat = 9;

    /// <summary>How near the guard (blocks) VEGA speaks about him.</summary>
    private const float SleeperAftermathRange = 16f;

    /// <summary>The per-player key the dialog engine writes when the guard's dialog was talked through.</summary>
    private const string SleeperDialogDoneKey = DialogDoneMilestonePrefix + SleeperDialogKey + ":done";

    /// <summary>1 Hz per player on foot on a world with a tower: VEGA's words about the guard for a player who talked with him
    /// and stands near him (see the class summary). Costs a dictionary count while no guard is awake.</summary>
    private void TickSleeperAftermath(PlayerSession s)
    {
        if (_sleepers.Count == 0 || !SpsTowerStoryRuns || !s.State.Milestones.Contains(SleeperDialogDoneKey) || !NearASleeper(s))
        {
            return;
        }

        if (s.State.Milestones.Contains(StoryPackKeys.Beat(_story!.Id, SleeperCloneBeat)))
        {
            RevealStoryInsight(s, SleeperInsightEvent); // once (the pack's once-key); held while the world's arc is behind
            return;
        }

        if (s.State.Milestones.Add(SleeperUnsureKey))
        {
            _repo.SavePlayer(s.State);
            SendVegaLine(s, SleeperUnsureLine, 2);
        }
    }

    /// <summary>Whether the player stands within <see cref="SleeperAftermathRange"/> of a guard on the active world.</summary>
    private bool NearASleeper(PlayerSession s)
    {
        string here = _world.LocationId;
        foreach (var state in _sleepers.Values)
        {
            if (state.LocationId == here && _npcs.Contains(state.Npc)
                && WrapDistSq(s.State.Position, state.Npc.Pos) <= (double)SleeperAftermathRange * SleeperAftermathRange)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Test seam: the 1 Hz pass of VEGA's words about the guard for one player.</summary>
    internal void TickSleeperAftermathForTest(PlayerSession s) => TickSleeperAftermath(s);
}
