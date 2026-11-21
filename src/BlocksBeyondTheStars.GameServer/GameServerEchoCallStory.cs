// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>#3899: the bookkeeping of ECHO's call in a flight instance — her cruiser is brought once per flight.</summary>
public sealed partial class SpaceInstance
{
    /// <summary>Server uptime at which a pilot who follows ECHO's call was first seen flying here (0 = not yet).</summary>
    public double EchoCallSince { get; set; }

    /// <summary>The story asked for ECHO's cruiser in this flight (it did, or the summon was refused): never a second time
    /// here — a later flight asks again.</summary>
    public bool EchoCallSummoned { get; set; }
}

/// <summary>
/// ECHO's call in the story "The VEGA Protocol" (#3899; canon C22 in docs/developer/LORE_STRUCTURE.md: the escort cruisers
/// were the escort wing's, the Guardian core took them, and the first one a player meets carries VEGA's sister core ECHO).
/// The mission "ECHO's Call" is pack data; the cruiser, its wreck and the bridge console are GameServerEscortCruiser*.cs.
/// This part ties them together:
/// <list type="bullet">
/// <item><b>The cruiser comes.</b> While a pilot whose mission still needs her cruiser (<see cref="IsEchoCallStep"/>) is
/// in flight, the story calls <see cref="SummonEscortCruiser(SpaceInstance, bool)"/> with ECHO aboard — after
/// <see cref="EchoCallFlightSeconds"/> of that flight, never in the Guardian's finale system, <b>once per flight
/// instance</b> (a refused summon — the rules changed — waits for a later flight too). A pilot who flees or loses the ship
/// keeps the step: the next flight brings the cruiser again. An escort cruiser that is already there or on its way (the
/// rare ambush) becomes hers instead of a second one coming — it is the first one this pilot meets.</item>
/// <item><b>Only while ECHO is still held</b> (<see cref="EchoStillHeld"/>): never after she was freed in this save, and
/// not while her cruiser's wreck drifts somewhere waiting to be boarded. A wreck that was lost before anybody freed her
/// (another cruiser fell in its system and took its place) lets her cruiser come again.</item>
/// <item><b>"Freed in this save"</b> must outlive the wreck's record, which goes when a later wreck replaces it: the
/// once-key <see cref="EchoFreedSaveKey"/> in the story's state (<see cref="StoryState.MilestoneKeys"/> — a key there
/// is no score).</item>
/// <item><b>The arrival</b>: the VEGA pack's card <see cref="StoryCutsceneIds.CruiserArrives"/> with VEGA's line
/// <see cref="EchoArrivalLine"/> ("That hull… that is one of ours"), once per player, when her cruiser is in their
/// flight.</item>
/// <item><b>Multiplayer</b>: every pilot who fought gets <c>cruiser:defeated</c>; a player whose steps run while the
/// wreck already drifts, or ECHO is already free, is passed on by evidence (GameServerSkyLightMissions.cs), and the
/// predicate <c>world:echo_freed</c> closes the steps of one who gets the mission afterwards.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How long a pilot who follows ECHO's call flies before her cruiser is called (seconds; VEGA's warning comes
    /// then, the cruiser <see cref="Shared.Definitions.EscortCruiserRules.WarnSeconds"/> later).</summary>
    internal const double EchoCallFlightSeconds = 25.0;

    /// <summary>The once-key of "ECHO was freed in this save" in <see cref="StoryState.MilestoneKeys"/>.</summary>
    internal const string EchoFreedSaveKey = "echo:freed";

    /// <summary>VEGA's line when ECHO's cruiser is there (the VEGA pack's locale; the arrival card shows it).</summary>
    internal const string EchoArrivalLine = "story.vega.cruiser.arrives";

    /// <summary>The per-player once-key of the arrival card and its line.</summary>
    internal const string EchoArrivalKey = "story:echo_call:arrival";

    /// <summary><c>world:echo_freed</c>: ECHO was freed in this save — the story's once-key, or a wreck's record that
    /// still says so (a world without a story keeps no key).</summary>
    internal bool EchoFreedInSave => _storyState.MilestoneKeys.Contains(EchoFreedSaveKey) || _meta.CruiserWrecks.Exists(w => w.EchoFreed);

    /// <summary>Whether a wreck that carries ECHO drifts somewhere, not freed yet (and not replaced by a later wreck).</summary>
    internal bool EchoWreckWaits => _meta.CruiserWrecks.Exists(w => w.CarriesEcho && !w.EchoFreed && !w.Replaced);

    /// <summary>Whether ECHO is still held aboard a flying cruiser: not freed, and no wreck of hers waits.</summary>
    private bool EchoStillHeld => !EchoFreedInSave && !EchoWreckWaits;

    /// <summary>Whether a mission step follows ECHO's call: its objective is the cruiser's defeat, the boarding of her
    /// wreck or her freeing, in a mission that ends on freeing her (pack data — no mission id is named here).</summary>
    private static bool IsEchoCallStep(CompiledStep step)
        => step.Event is GameEventKeys.CruiserDefeated or GameEventKeys.CruiserBoarded or GameEventKeys.EchoFreed
           && step.Pack.IsStoryMissions && step.Chapter.Steps.Any(st => st.Event == GameEventKeys.EchoFreed);

    /// <summary>Writes the save's once-key the first time a player who freed ECHO is seen (the bridge code marks the
    /// player and the wreck; the wreck's record may go later).</summary>
    private void NoteEchoFreedInSave(PlayerSession s)
    {
        if (_story is null || _storyState.MilestoneKeys.Contains(EchoFreedSaveKey)
            || !(s.State.Milestones.Contains(EchoFreedMilestone) || _meta.CruiserWrecks.Exists(w => w.EchoFreed)))
        {
            return;
        }

        _storyState.MilestoneKeys.Add(EchoFreedSaveKey);
        PersistStoryState();
    }

    /// <summary>1 Hz for a pilot whose mission still needs ECHO's cruiser (see the class summary): her cruiser in this
    /// flight shows the arrival card; a cruiser there or coming becomes hers; else she is called, once per flight.</summary>
    private void TickEchoCallFlight(PlayerSession s)
    {
        if (!_playerInstance.TryGetValue(s.State.PlayerId, out var id) || !_spaceInstances.TryGetValue(id, out var instance))
        {
            return; // on foot, docked or aboard a wreck: nothing comes
        }

        bool held = EchoStillHeld;
        if (instance.BossId.Length > 0)
        {
            var runtime = instance.Entities.FirstOrDefault(e => e.Id == instance.BossId)?.Boss;
            if (runtime is not null && !runtime.CarriesEcho && held)
            {
                runtime.CarriesEcho = true; // C22: the first cruiser this pilot meets is the one that carries her
                _log.Info($"The escort cruiser in '{instance.Id}' carries ECHO now — '{s.State.Name}' follows her call.");
            }

            if (runtime is { CarriesEcho: true })
            {
                PlayEchoCruiserArrival(s);
            }

            return;
        }

        if (!held)
        {
            return;
        }

        if (instance.BossArrivalAt > 0)
        {
            instance.BossCarriesEcho = true; // one is on its way already (the rare ambush): it is hers
            return;
        }

        if (instance.EchoCallSummoned || IsGuardianSystemLocation(InstanceBodyId(instance)))
        {
            return;
        }

        if (instance.EchoCallSince <= 0)
        {
            instance.EchoCallSince = _uptime;
        }

        if (_uptime - instance.EchoCallSince < EchoCallFlightSeconds)
        {
            return;
        }

        instance.EchoCallSummoned = true; // once per flight — also when the summon is refused: a later flight asks again
        if (SummonEscortCruiser(instance, carriesEcho: true))
        {
            _log.Info($"ECHO's cruiser was called into '{instance.Id}' — '{s.State.Name}' follows her call.");
        }
    }

    /// <summary>The arrival card and VEGA's line for a pilot whose flight ECHO's cruiser is in — once per player. The line
    /// goes out with the card: a client that plays the card keeps it out of VEGA's panel, any other reads it there.</summary>
    private void PlayEchoCruiserArrival(PlayerSession s)
    {
        if (!SpsTowerStoryRuns || !s.State.Milestones.Add(EchoArrivalKey))
        {
            return;
        }

        _repo.SavePlayer(s.State);
        if (_story!.FindCutscene(StoryCutsceneIds.CruiserArrives) is not null)
        {
            PlayCutscene(s, _story.Id, StoryCutsceneIds.CruiserArrives);
        }

        SendVegaLine(s, EchoArrivalLine, 3);
    }

    /// <summary>Test seam (#3899): whether the save's once-key of ECHO's freeing is written (it outlives the wreck's record).</summary>
    internal bool EchoFreedKeyWrittenForTest => _storyState.MilestoneKeys.Contains(EchoFreedSaveKey);

    /// <summary>Test seam (#3899): whether the story already asked for ECHO's cruiser in the player's flight, and since when
    /// a pilot who follows her call has been seen there (0 = not yet; null = the player is not flying).</summary>
    internal (bool Summoned, double Since)? EchoCallFlightForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var id) && _spaceInstances.TryGetValue(id, out var instance)
            ? (instance.EchoCallSummoned, instance.EchoCallSince)
            : null;
}
