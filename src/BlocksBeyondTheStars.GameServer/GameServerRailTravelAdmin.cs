// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The playtest helpers of rail travel (#3447, admin, cheats on): <c>/guest</c> (<see cref="AdminSummonGuest"/>) — a guest
/// visit to the admin's nearest own base right now, by train when the base has a stop on a line to a far town, else the
/// walk-in — and <c>/railtrip [town]</c> (<see cref="AdminRailTrip"/>) — a villager trip right now on the line nearest the
/// admin (the daytime and the pace skipped; who may go, the per-world cap and a train that takes villagers still apply).
/// </summary>
public sealed partial class GameServer
{
    /// <summary><c>/guest</c>: a visitor comes to the admin's nearest base on this body — by train when it can, else on foot.
    /// The answer names who comes and how, or why nobody can.</summary>
    private void AdminSummonGuest(PlayerSession session)
    {
        Serve(session);
        var p = session.State;
        var b = _bases.Where(x => x.Planet == _world.LocationId && x.OwnerId == p.PlayerId)
            .OrderBy(x => WrapDistSq(p.Position, BaseCorePos(x))).FirstOrDefault();
        if (b is null)
        {
            Reject(session, "admin", "@srv.admin.guest_no_base");
            return;
        }

        if (_baseGuests.Any(g => g.BaseId == b.Id) || TrainGuestUnderway(b.Id))
        {
            Reject(session, "admin", "@srv.admin.guest_busy");
            return;
        }

        if (Rules.FriendlyVisitors && TryStartTrainGuest(b, session) is { } trip && NpcWithId(trip.NpcId) is { } traveller)
        {
            Send(session, new ServerMessage
            {
                Text = Localize(session.Locale, "srv.admin.guest_train").Replace("{name}", traveller.Name).Replace("{village}", trip.HomeName),
            });
            CheatLog(p, $"called a guest by train ('{traveller.Name}' of {trip.HomeName})");
            return;
        }

        var guest = Rules.FriendlyVisitors && GuestHost(b) is { } owner
            ? SpawnBaseGuest(b, owner, BaseGuestKind.Neighbour) ?? SpawnBaseGuest(b, owner, BaseGuestKind.Traveller)
            : null;
        if (guest is null || NpcWithId(guest.NpcId) is not { } walker)
        {
            Reject(session, "admin", "@srv.admin.guest_none");
            return;
        }

        Send(session, new ServerMessage { Text = Localize(session.Locale, "srv.admin.guest_walk").Replace("{name}", walker.Name) });
        CheatLog(p, $"called a guest on foot ('{walker.Name}')");
    }

    /// <summary><c>/railtrip [town]</c>: a villager (or a resident) takes the train now on the nearest line that can carry
    /// one — <paramref name="town"/> narrows the home place by name.</summary>
    private void AdminRailTrip(PlayerSession session, string? town)
    {
        Serve(session);
        var p = session.State;
        string? home = string.IsNullOrWhiteSpace(town) ? null : town.Trim();
        foreach (var line in Rails.Lines.Values.OrderBy(l => l.Spline.Nearest(p.Position).Distance))
        {
            var places = RailStopPlaces(line);
            if (places.Count < 2 || !LineCarriesNpcs(line.Id) || TryStartRailTrip(line, places, home, admin: true) is not { } trip
                || NpcWithId(trip.NpcId) is not { } traveller)
            {
                continue;
            }

            Send(session, new ServerMessage
            {
                Text = Localize(session.Locale, "srv.admin.railtrip_started")
                    .Replace("{name}", traveller.Name).Replace("{from}", trip.HomeName).Replace("{to}", trip.AwayName),
            });
            CheatLog(p, $"sent '{traveller.Name}' of {trip.HomeName} by train to {trip.AwayName}");
            return;
        }

        Reject(session, "admin", "@srv.admin.railtrip_none");
    }
}
