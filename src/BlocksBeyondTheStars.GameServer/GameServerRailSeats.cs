// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Where a boarding rider is put in the wagon (#2113): on the seat they asked for, where they stand (brought into the
/// wagon's box) — or, standing up from a seat of the same wagon (#3472), into the aisle beside it. The seat's own spot lies
/// on its bench or berth cell (<see cref="RailWagonLayout"/>): a body set down there stood inside the furnishing, whose
/// floor below has no top face, and fell through the moving train.
/// </summary>
public sealed partial class GameServer
{
    private Vector3f BoardingLocal(ServerTrain t, PlayerState p, int wagon, int seat, Vector3f wagonPos, float wagonYaw)
    {
        if (seat >= 0)
        {
            return RailRules.SeatOffsets(t.Wagons[wagon])[seat];
        }

        if (t.Riders.TryGetValue(p.PlayerId, out var was) && was.Seat >= 0 && was.Wagon == wagon)
        {
            return RailWagonLayout.StandUpPoint(was.Local);
        }

        var un = Unwrapped(wagonPos, p.Position);
        return RailRules.ClampLocal(RailRules.WorldToLocal(wagonPos, wagonYaw, un));
    }
}
