// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The sky-lights' story missions (#3899) — "The Sky-Light", "The Sleeper" and "ECHO's Call" are pack data
/// (<c>data/stories/vega_protocol/story.json</c>, kept missions of the chapters 2, 5 and 6) run by the mission runtime
/// (GameServerStoryMissions.cs). This part is what they need from the server besides the events the towers, the sleeper
/// and the cruiser raise:
/// <list type="bullet">
/// <item><b>Two predicates</b> for <c>alreadyDoneIf</c>: <c>world:sps_tower_lit_by_player</c> (a tower this player lit
/// chapters ago still counts for "light a tower") and <c>world:hacking_chip_carried</c>.</item>
/// <item><b>Evidence while a step runs</b> (<see cref="TickSkyLightMissions"/>, 1 Hz per player). A step's
/// <c>alreadyDoneIf</c> is asked once, when it activates; its event counts only from then on. Some of these steps come true
/// without their event ever reaching the player — the mission would wait for good (no dead ends, synthesis G7). So an
/// active mission step is also followed by what holds NOW, the way a lead bridge is followed by its lead's evidence:
/// a craft step by carrying what its recipe makes (a hacking chip a Hunter dropped, a crate held or a friend gave), "open a
/// capsule tank" by a tank opened in this save (somebody else did it, more than 24 blocks away), "fly out" by being in
/// flight already, and the steps of ECHO's call by her cruiser's wreck and by ECHO being free (another pilot's fight —
/// GameServerEchoCallStory.cs). The step completes like its event had come: its done line when it was on the chip.</item>
/// <item><b>The flight of ECHO's call</b> for a pilot whose mission still needs her cruiser (<see cref="TickEchoCallFlight"/>).</item>
/// </list>
/// Nothing here names a mission or step id: the rules read the steps' events and arguments, so they hold for any mission
/// a story pack writes with these events.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The key prefix of "this tower's light counts for me" (<see cref="SpsTowerLitKey"/>).</summary>
    private static readonly string SpsTowerLitKeyPrefix = SpsTowerLitKey(string.Empty);

    /// <summary><c>world:sps_tower_lit_by_player</c>: the player lit a guide tower before, or used the console of a lit one —
    /// their own <c>sps_tower:lit</c> was raised, on any world (the towers' per-player once-keys).</summary>
    private bool SpsTowerLitByPlayer(PlayerSession who)
    {
        foreach (string key in who.State.Milestones)
        {
            if (key.StartsWith(SpsTowerLitKeyPrefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>world:hacking_chip_carried</c>: a hacking chip in the backpack or in the hold of the active ship.</summary>
    private bool HackingChipCarried(PlayerSession who) => CarriesItem(who, SpsTowerChipItem);

    /// <summary>Whether the player carries <paramref name="item"/> — the backpack, or the hold of their active ship.</summary>
    private static bool CarriesItem(PlayerSession who, string item)
        => who.State.Inventory.Has(item, 1) || (who.Ships.TryGetValue(who.ActiveShipId, out var ship) && ship.Cargo.Has(item, 1));

    /// <summary>1 Hz per player from the story pass (after the missions' own pass): the running mission steps that hold by
    /// evidence complete (see the class summary), then the flight of ECHO's call. Cheap: a walk over the player's few active
    /// steps; nothing for a player without a running story mission.</summary>
    private void TickSkyLightMissions(PlayerSession s)
    {
        NoteEchoFreedInSave(s);
        if (s.State.StepProgress.Count == 0 || !StoryMissionsRunFor(s, out var pack))
        {
            return;
        }

        List<CompiledStep>? through = null;
        bool needsCruiser = false;
        foreach (string progressKey in s.State.StepProgress.Keys)
        {
            if (!_stepByProgressKey.TryGetValue(progressKey, out var step) || step.Pack != pack || StepComplete(s, step))
            {
                continue;
            }

            if (MissionStepEvidence(s, step))
            {
                (through ??= new List<CompiledStep>()).Add(step);
            }
            else
            {
                needsCruiser |= IsEchoCallStep(step);
            }
        }

        if (through is not null)
        {
            foreach (var step in through)
            {
                CompleteMissionStep(s, step);
            }

            AdvanceStoryMissions(s);
            SendVegaObjective(s);
            return; // the next pass sees the steps that activated now
        }

        if (needsCruiser)
        {
            TickEchoCallFlight(s);
        }
    }

    /// <summary>Whether an active mission step holds by what is true now (see the class summary). Only a step whose
    /// objective is one of these events — and only in the form the rule knows (a recipe with an output, no argument on
    /// "fly out", the story's cruiser).</summary>
    private bool MissionStepEvidence(PlayerSession s, CompiledStep step) => step.Event switch
    {
        GameEventKeys.CraftAny => step.Args.Any(recipe => CraftedItemCarried(s, recipe)),
        GameEventKeys.CapsuleOpened => step.Args.Length == 0 && _meta.SpsTowers.Exists(t => t.TankOpened),
        GameEventKeys.SpaceEnter => step.Args.Length == 0 && InSpace(s.State.PlayerId),
        GameEventKeys.CruiserDefeated => IsEchoCallStep(step) && (EchoFreedInSave || EchoWreckWaits),
        GameEventKeys.CruiserBoarded or GameEventKeys.EchoFreed => IsEchoCallStep(step) && EchoFreedInSave,
        _ => false,
    };

    /// <summary>Whether the player carries what the recipe <paramref name="recipeKey"/> makes (its first output).</summary>
    private bool CraftedItemCarried(PlayerSession s, string recipeKey)
        => _content.GetRecipe(recipeKey)?.Outputs is { Count: > 0 } outputs && CarriesItem(s, outputs[0].Item);

    /// <summary>Test seam (#3899): the 1 Hz pass of the sky-lights' missions for one player.</summary>
    internal void TickSkyLightMissionsForTest(PlayerSession s) => TickSkyLightMissions(s);
}
