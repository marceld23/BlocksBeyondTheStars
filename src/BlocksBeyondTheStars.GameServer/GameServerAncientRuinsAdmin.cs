// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The admin's way into a buried G.D.S. ruin (#3421): <c>/ressaw</c> (verb <c>goto_ancient</c>, behind the world's cheat rule
/// like <c>/gotocore</c>). On a body that has one, it drops the admin INSIDE the ruin, on dry floor beside the diving-suit
/// crate (crate 0). Elsewhere it predicts from the world seed alone — no world is loaded — which bodies the boot stamp gives a
/// G.D.S. ruin, by the stamp's own rule (<see cref="AncientRuinSlotsFor"/>, <see cref="UndergroundPick"/>,
/// <see cref="AncientRuinFitsOpenLand"/>), numbers them nearest first (the body the admin is on, its system, then by star-map
/// distance, then by body id), travels to the first with the admin bypass (no jump drive; booked silently, like a fleet admin's
/// <c>/goto</c>, #2493) and drops the admin inside after the landing. <c>/ressaw 2</c> takes the second, <c>/ressaw list</c>
/// lists them. What the seed cannot tell — a seat that finds no dry ground, a world built on before its first load — shows on
/// arrival: the answer says so, and the slot's pinned skip record (in the save's metadata, no world load either) takes the
/// body off the list, so the next <c>/ressaw</c> goes on to the next ruin instead of back.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The rows a standing spot beside a crate is searched on: the crate's own, then one up, then one down.</summary>
    private static readonly int[] AncientRuinStandRows = { 0, 1, -1 };

    /// <summary>A body the seed says carries a G.D.S. ruin, its system, and the slot that holds the ruin (the city slot when a
    /// city world has both).</summary>
    private readonly record struct AncientRuinCandidate(CelestialBody Body, StarSystem System, int Slot);

    /// <summary><c>/ressaw</c> (the nearest ruin — the one right here first), <c>/ressaw &lt;n&gt;</c> (candidate n),
    /// <c>/ressaw list</c>.</summary>
    private void AdminGotoAncient(PlayerSession session, string? argument)
    {
        Serve(session);
        string arg = (argument ?? string.Empty).Trim();
        if (_generator.TerrainGeneration < WorldDescription.AncientRuinsGeneration)
        {
            Reject(session, "admin", "@srv.ancient.too_old");
            return;
        }

        if (arg.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            ListAncientRuinCandidates(session, AncientRuinCandidates(session));
            return;
        }

        int number = 1;
        if (arg.Length > 0 && (!int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) || number < 1))
        {
            Reject(session, "admin", "@srv.ancient.usage");
            return;
        }

        // A ruin right where the admin stands: straight in, nothing to predict.
        if (arg.Length == 0 && !InSpace(session.State.PlayerId) && TrySnapIntoAncientRuin(session, UndergroundCitySlot))
        {
            CheatLog(session.State, $"went into the G.D.S. ruin on {_world.LocationId}");
            return;
        }

        var candidates = AncientRuinCandidates(session);
        if (candidates.Count == 0)
        {
            Reject(session, "admin", "@srv.ancient.none");
            return;
        }

        if (number > candidates.Count)
        {
            Reject(session, "admin", Localize(session.Locale, "srv.ancient.out_of_range")
                .Replace("{count}", candidates.Count.ToString(CultureInfo.InvariantCulture)));
            return;
        }

        GoIntoAncientRuin(session, candidates[number - 1], number);
    }

    /// <summary>Travels to candidate <paramref name="number"/> (unless the admin already stands on it) and drops them inside its
    /// ruin. A seat that failed there is answered — the body is off the list from now on (<see cref="AncientRuinSkipped"/>), so
    /// <c>/ressaw</c> again reaches the next one.</summary>
    private void GoIntoAncientRuin(PlayerSession session, AncientRuinCandidate target, int number)
    {
        var p = session.State;
        bool here = string.Equals(target.Body.Id, session.CurrentLocationId, StringComparison.Ordinal);
        if (!here || InSpace(p.PlayerId))
        {
            // The /gotocore path: the normal landing (it stamps the ruin as the world boots) with the jump gate bypassed. A
            // teleport, not a voyage (#2493): no jump achievement, no story milestone, no frontier, no explorer credit.
            ClearTransit(session);
            HandleTravel(session, new TravelIntent { DestinationBodyId = target.Body.Id }, quickTravel: false, adminBypass: true,
                allowCurrentBody: here, adminTeleport: true);
            if (!string.Equals(session.CurrentLocationId, target.Body.Id, StringComparison.Ordinal) || InSpace(p.PlayerId))
            {
                return; // the travel was refused, and the refusal already went out
            }
        }

        if (!TrySnapIntoAncientRuin(session, target.Slot))
        {
            Reject(session, "admin", "@srv.ancient.seat_failed:" + target.Body.Name);
            return;
        }

        CheatLog(p, $"went into the G.D.S. ruin on {target.Body.Id} (/ressaw {number})");
    }

    /// <summary>Drops the admin into a G.D.S. ruin of the world they stand on — the one in <paramref name="slot"/> if it holds
    /// one, else the city's, else any — beside its diving-suit crate. False when the world has no G.D.S. ruin.</summary>
    private bool TrySnapIntoAncientRuin(PlayerSession session, int slot)
    {
        var site = _worlds.Active.AncientRuins
            .Where(s => s.Gds && s.Caches.Count > 0)
            .OrderBy(s => s.Slot == slot ? 0 : s.Slot == UndergroundCitySlot ? 1 : 2)
            .FirstOrDefault();
        if (site is null)
        {
            return false;
        }

        var spot = AncientRuinStandingSpot(site);
        var (_, planetName) = ActiveLocationNames();
        session.Streaming.SentChunks.Clear(); // far from the pad and deep down: stream the ruin afresh (as /gotocore does)
        StreamFootingNow(session, spot); // the floor reaches the client before the snap (#1449)
        session.AwaitingSpawnAdopt = true; // #865: the client's stale stream must not drag the admin back up
        SnapPlayerTo(session, spot, "@srv.ancient.dropped:" + planetName);
        UpdateAboard(session);
        return true;
    }

    /// <summary>Where the admin stands in a ruin: on dry floor beside the diving-suit crate (crate 0), else beside the next
    /// crate, else at the foot of the entrance ladder.</summary>
    private Vector3f AncientRuinStandingSpot(AncientRuinSite site)
    {
        foreach (var crate in site.Caches)
        {
            if (DryStandBeside(crate) is { } spot)
            {
                return spot;
            }
        }

        return new Vector3f(site.Entrance.X + 0.5f, site.Origin.Y + 1, site.Entrance.Z + 0.5f);
    }

    /// <summary>The nearest standing spot around <paramref name="cell"/> — ring by ring up to three cells out, each ring on the
    /// cell's own row first, then one up, then one down: a dry floor cell (<see cref="IsDryStand"/>). The spot is that floor
    /// cell + (0.5, 1, 0.5). Null when there is none.</summary>
    private Vector3f? DryStandBeside(Vector3i cell)
    {
        for (int r = 1; r <= 3; r++)
        {
            foreach (int dy in AncientRuinStandRows)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r)
                        {
                            continue; // inside the ring — an earlier ring has asked it
                        }

                        var floor = new Vector3i(WorldConstants.WrapX(cell.X + dx, _world.Circumference), cell.Y - 1 + dy, cell.Z + dz);
                        if (IsDryStand(floor))
                        {
                            return new Vector3f(floor.X + 0.5f, floor.Y + 1, floor.Z + 0.5f);
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>A solid, non-liquid floor cell (never Ressaw) with air in the two cells over it.</summary>
    private bool IsDryStand(Vector3i floor)
        => _content.BlockById(_world.GetBlock(floor)) is { Solid: true, Liquid: false }
           && _world.GetBlock(new Vector3i(floor.X, floor.Y + 1, floor.Z)).IsAir
           && _world.GetBlock(new Vector3i(floor.X, floor.Y + 2, floor.Z)).IsAir;

    /// <summary>Every body the seed says carries a G.D.S. ruin (<see cref="PredictedAncientRuinSlots"/>) in a system the admin
    /// may see (a hidden one could not be reached) — minus the slots a landing has already found no room for — nearest first:
    /// the body they are on, its system, then by star-map distance, then by body id.</summary>
    private List<AncientRuinCandidate> AncientRuinCandidates(PlayerSession session)
    {
        var list = new List<AncientRuinCandidate>();
        foreach (var system in _galaxy?.Systems ?? new List<StarSystem>())
        {
            if (SystemHiddenFor(session, system.Id))
            {
                continue;
            }

            foreach (var body in system.Bodies)
            {
                foreach (int slot in PredictedAncientRuinSlots(body).Where(s => !AncientRuinSkipped(body.Id, s)).Take(1))
                {
                    list.Add(new AncientRuinCandidate(body, system, slot));
                }
            }
        }

        string hereId = session.CurrentLocationId;
        string? homeId = ResolveLocationHostBody(hereId)?.SystemId;
        var home = homeId is null ? null : _galaxy?.Systems.FirstOrDefault(s => s.Id == homeId);
        return list
            .OrderBy(c => string.Equals(c.Body.Id, hereId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(c => string.Equals(c.System.Id, homeId, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(c => home is null ? 0.0 : SystemMapDistanceSq(c.System, home))
            .ThenBy(c => c.Body.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static double SystemMapDistanceSq(StarSystem a, StarSystem b)
    {
        double dx = a.MapX - b.MapX, dy = a.MapY - b.MapY;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>The slots whose G.D.S. ruin the boot stamp gives <paramref name="body"/> — the city slot first — by the stamp's
    /// own rule on the seed alone: a surface body other than the Guardian core (its boot stamps nothing else), the slots of
    /// <see cref="AncientRuinSlotsFor"/>, a G.D.S. ruin kit drawn by <see cref="UndergroundPick"/>, and for the roll slot a body
    /// big enough for its open land (<see cref="AncientRuinFitsOpenLand"/>; the city slot sits under the city). Empty for a
    /// body without one.</summary>
    private List<int> PredictedAncientRuinSlots(CelestialBody body)
    {
        var slots = new List<int>(2);
        if (body.Kind is not (CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField)
            || string.IsNullOrEmpty(body.PlanetType)
            || string.Equals(body.Id, GuardianCoreBodyId, StringComparison.Ordinal)
            || _content.GetPlanet(body.PlanetType) is not { } planet)
        {
            return slots;
        }

        var (city, roll) = AncientRuinSlotsFor(planet, body.Id, StructuresRestricted(planet));
        if (city && PredictedGdsPlan(UndergroundCitySlot, body.Id, planet.Key) is not null)
        {
            slots.Add(UndergroundCitySlot);
        }

        if (roll && PredictedGdsPlan(UndergroundRollSlot, body.Id, planet.Key) is { } plan
            && AncientRuinFitsOpenLand(WorldCircumferenceOf(body), plan.Width, plan.Length))
        {
            slots.Add(UndergroundRollSlot);
        }

        return slots;
    }

    /// <summary>Whether a landing on body <paramref name="locationId"/> already pinned a skip for <paramref name="slot"/> — its
    /// seat found no room. The save's metadata knows it without loading the world.</summary>
    private bool AncientRuinSkipped(string locationId, int slot)
        => _meta.Placements.Exists(r => r.LocationId == locationId && r.Kind == UndergroundKind && r.Index == slot && !r.Placed);

    /// <summary>The G.D.S. ruin a slot of body <paramref name="locationId"/> draws, sized but not seated (nothing of a world is
    /// read) — null when the slot draws an authored underground structure, or nothing.</summary>
    private AncientRuinPlan? PredictedGdsPlan(int slot, string locationId, string planetKey)
        => UndergroundPick(slot, locationId, planetKey).Kit is { } kit && AncientRuinGenerator.IsGdsRuinKit(kit)
            ? PlanForKit(kit, slot, 0L)
            : null;

    /// <summary><c>/ressaw list</c>: one chat line per candidate — the number to type, the body, its system and where the ruin
    /// lies (under the city, or under a vent tower) — capped like the <c>/tp</c> list.</summary>
    private void ListAncientRuinCandidates(PlayerSession session, List<AncientRuinCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            Reject(session, "admin", "@srv.ancient.none");
            return;
        }

        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.ancient.list_header").Replace("{count}", candidates.Count.ToString(CultureInfo.InvariantCulture)),
        });
        string city = Localize(session.Locale, "srv.ancient.kind_city"), vent = Localize(session.Locale, "srv.ancient.kind_vent");
        string here = " · " + Localize(session.Locale, "srv.ancient.here");
        for (int i = 0; i < candidates.Count && i < TeleportListMaxLines; i++)
        {
            var c = candidates[i];
            string where = c.Slot == UndergroundCitySlot ? city : vent;
            string mark = string.Equals(c.Body.Id, session.CurrentLocationId, StringComparison.Ordinal) ? here : string.Empty;
            Send(session, new ServerMessage
            {
                // netstandard2.1 (the server's second target) has no string.Create(IFormatProvider, …) — FormattableString does.
                Text = FormattableString.Invariant($"/ressaw {i + 1} · {c.Body.Name} · {StarMapSystemName(session, c.System)} · {where}{mark}"),
            });
        }

        if (candidates.Count > TeleportListMaxLines)
        {
            Send(session, new ServerMessage
            {
                Text = Localize(session.Locale, "srv.tp.list_more")
                    .Replace("{count}", (candidates.Count - TeleportListMaxLines).ToString(CultureInfo.InvariantCulture)),
            });
        }
    }

    /// <summary>Test seam: the <c>/ressaw</c> candidates of <paramref name="session"/> in their order (body, system, slot).</summary>
    internal IReadOnlyList<(string BodyId, string SystemId, int Slot)> AncientRuinCandidatesForTest(PlayerSession session)
    {
        Serve(session);
        return AncientRuinCandidates(session).Select(c => (c.Body.Id, c.System.Id, c.Slot)).ToList();
    }

    /// <summary>Test seam: the slots the seed alone gives body <paramref name="bodyId"/> a G.D.S. ruin in (city slot first).</summary>
    internal IReadOnlyList<int> PredictedAncientRuinSlotsForTest(string bodyId)
        => _galaxy?.FindBody(bodyId) is { } body ? PredictedAncientRuinSlots(body) : new List<int>();
}
