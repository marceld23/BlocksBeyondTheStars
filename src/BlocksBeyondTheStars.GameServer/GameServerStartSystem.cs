// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3436: the start system is home. A world starts on the first planet of its start type, so the start system is not
/// always <c>sys0</c> — and only <c>sys0</c> was kept from rolling Desolate or a Pirate Haven. A kid whose world started in
/// a Pirate Haven met three drones, a UFO and bandit ships at home from day one, and after the win the home stayed hostile
/// forever (the Remnant Protocol keeps pirate havens dangerous on purpose). Now the start system's INHABITANTS follow the
/// home rule (<see cref="SystemArchetypes.ForStartSystem"/>): ambient drones and UFOs, bandit ships, traders and the bump's
/// verdict. Its LAYOUT keeps the rolled archetype (<see cref="RolledSystemArchetypeOf"/>): the generated bodies, the debris
/// fields and their themes, and the bandit camps, whose huts are stamped once into the terrain — a camp count that changed
/// afterwards would leave guardless huts behind. Existing worlds heal at their next start; nothing is persisted.
/// <para>And the win calms the space that is live at that moment (<see cref="CalmLiveSpaceAfterWin"/>): the planet machines
/// were already cleared on every loaded world, the ambient drones and UFOs of a flight in progress stayed until it ended.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The archetype a system ROLLED (#546) — Standard for every system of a pre-variance save. For layout passes
    /// only; everything that lives in a system asks <see cref="SystemArchetypeOf"/>.</summary>
    private SystemArchetype RolledSystemArchetypeOf(string? systemId)
        => SystemArchetypes.For(_meta.Seed, systemId, _meta.Description);

    /// <summary>The system of the save's start body (<c>_meta.ActiveLocationId</c>, the default join body), or null before
    /// the galaxy exists.</summary>
    private string? StartSystemId => _galaxy?.FindBody(_meta.ActiveLocationId)?.SystemId;

    /// <summary>Whether <paramref name="systemId"/> is the save's start system.</summary>
    private bool IsStartSystem(string? systemId)
        => !string.IsNullOrEmpty(systemId) && string.Equals(systemId, StartSystemId, System.StringComparison.Ordinal);

    /// <summary>The start system's archetype for its inhabitants: never Desolate or a Pirate Haven.</summary>
    private SystemArchetype StartSystemArchetype(string? systemId)
        => SystemArchetypes.ForStartSystem(_meta.Seed, systemId, _meta.Description);

    /// <summary>
    /// The win (called from <c>MarkGuardianDefeated</c>): every live flight instance outside the finale system whose space
    /// the Remnant Protocol no longer keeps dangerous (<see cref="RemnantSpaceHostile"/>) loses its ambient drones and UFOs
    /// at once — the only two spawn paths of those kinds outside the finale gauntlet are the ambient wave. Bandit ships,
    /// hunter shuttles, asteroids and everything else stay; pirate havens and the frontier-danger tier keep their wave.
    /// </summary>
    private void CalmLiveSpaceAfterWin()
    {
        foreach (var instance in _spaceInstances.Values)
        {
            string anchorId = SpaceInstanceAnchorId(instance.Id);
            if (IsGuardianSystemLocation(anchorId))
            {
                continue; // the finale's own space: its gauntlet ends with the duel
            }

            var archetype = SystemArchetypeOf(_galaxy?.FindBody(anchorId)?.SystemId);
            if (RemnantSpaceHostile(archetype, anchorId))
            {
                continue;
            }

            if (instance.Entities.RemoveAll(e => e.Kind is CombatEntityKind.Drone or CombatEntityKind.Ufo) > 0)
            {
                BroadcastSpaceState(instance);
            }
        }
    }

    /// <summary>Test seam: the archetype the start system's inhabitants live by, and the one it rolled.</summary>
    internal (SystemArchetype Effective, SystemArchetype Rolled) StartSystemArchetypesForTest()
        => (SystemArchetypeOf(StartSystemId), RolledSystemArchetypeOf(StartSystemId));

    /// <summary>Test seam: the save's start system id.</summary>
    internal string? StartSystemIdForTest => StartSystemId;

    /// <summary>Test seam: how many ambient hostiles (drones and UFOs) fly in the player's current space instance.</summary>
    internal int AmbientHostilesForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var id) && _spaceInstances.TryGetValue(id, out var instance)
            ? instance.Entities.Count(e => e.Kind is CombatEntityKind.Drone or CombatEntityKind.Ufo)
            : -1;
}
