// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The Radio centre (#3040, Justus: "the base receives signals from traders and old distress calls from shipwrecks"):
/// <list type="bullet">
/// <item><b>Signals:</b> an owner in the star system of one of their Radio bases is reached by every NPC call of that system,
/// worn radio or not (<c>NpcRadioReaches</c>), and every trader that sets down in the system hails them
/// (<c>NpcRadioOnTraderLanded</c>).</item>
/// <item><b>Old distress calls:</b> while the owner is on the body of their Radio base, about every
/// <see cref="BaseFocus.RadioDistressGapSeconds"/> (the first a quarter of that after they arrive) VEGA passes on a distress
/// call recorded long ago: it names a space wreck of the system that the player has not read yet and nobody has carved away,
/// and makes it the space waypoint (<see cref="ShipAiLine.SpaceWaypointId"/> — the radar and the edge arrow lead there).
/// With every wreck of the system found, the radio stays quiet. A system line: the player chose to listen.</item>
/// </list>
/// The schedule is RAM only — a restart just waits a quarter gap again.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Player id → uptime of their next distress call.</summary>
    private readonly Dictionary<string, double> _radioDistressAt = new(StringComparer.Ordinal);

    /// <summary>Whether the player owns a Radio base in the star system of <paramref name="bodyId"/> and is in that system.</summary>
    private bool OwnsRadioBaseInSystem(PlayerSession session, string bodyId)
    {
        string? system = _galaxy?.FindBody(bodyId)?.SystemId;
        if (system is null || _galaxy?.FindBody(session.CurrentLocationId)?.SystemId != system)
        {
            return false;
        }

        string me = session.State.PlayerId;
        return _bases.Any(b => b.OwnerId == me && _galaxy?.FindBody(b.Planet)?.SystemId == system && BaseFocusOf(b) == BaseFocus.Radio);
    }

    /// <summary>One old distress call when it is due (see the class summary).</summary>
    private void TryRadioDistressCall(PlayerSession session)
    {
        string me = session.State.PlayerId;
        string here = session.CurrentLocationId;
        if (!_bases.Any(b => b.OwnerId == me && b.Planet == here && BaseFocusOf(b) == BaseFocus.Radio))
        {
            return;
        }

        if (!_radioDistressAt.TryGetValue(me, out double due))
        {
            _radioDistressAt[me] = _uptime + (BaseFocus.RadioDistressGapSeconds * 0.25);
            return;
        }

        if (_uptime < due)
        {
            return;
        }

        _radioDistressAt[me] = _uptime + BaseFocus.RadioDistressGapSeconds;
        if (DistressWreckFor(session) is not { } wreck)
        {
            return; // every wreck of the system found: the radio stays quiet
        }

        SendVegaWithObjective(session, new ShipAiLine
        {
            LineKey = "vega.radio.distress",
            LineArg = wreck.Name,
            Kind = 3,
            SpaceWaypointId = wreck.Id,
        });
    }

    /// <summary>The first space wreck of the player's star system (by id) they have not read yet and that is not carved away.</summary>
    private CelestialBody? DistressWreckFor(PlayerSession session)
    {
        string? system = _galaxy?.FindBody(session.CurrentLocationId)?.SystemId;
        var sys = system is null ? null : _galaxy?.Systems.FirstOrDefault(s => s.Id == system);
        return sys?.Bodies
            .Where(b => b.Kind == CelestialKind.Wreck && !IsWreckSalvaged(b.Id) && !session.State.Scanned.Contains(SpaceWreckScanPrefix + b.Id))
            .OrderBy(b => b.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>Test seam (#3040): makes the player's next distress call due now.</summary>
    public void DueRadioDistressForTest(PlayerSession session) => _radioDistressAt[session.State.PlayerId] = _uptime;

    /// <summary>Test seam (#3040): one radio scan step for the player (the distress call, if due).</summary>
    public void RadioDistressStepForTest(PlayerSession session) => TryRadioDistressCall(session);

    /// <summary>Test seam (#3040): whether the NPC radio reaches the player for a call from <paramref name="bodyId"/>.</summary>
    public bool NpcRadioReachesForTest(PlayerSession session, string bodyId) => NpcRadioReaches(session, bodyId);
}
