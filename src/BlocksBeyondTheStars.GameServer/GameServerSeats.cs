// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// One seat, one sitter (#2122, "I can sit down on a chair that is occupied by a player or an entity!" — the camera
/// ended up inside a sitting station NPC):
/// <list type="bullet">
/// <item><b>Players.</b> Sitting down names the seat cell (<see cref="SetSeatedIntent.HasCell"/>). The server checks a
/// seat is there, within reach, and that no seated NPC and no other seated player is on it; otherwise it answers
/// <c>ActionRejected("seat", "@srv.seat.*")</c>, the client shows the toast and stands back up. An older client sends
/// no cell: its seat is looked for right around its position.</item>
/// <item><b>NPCs.</b> An NPC arriving at a taken seat rests standing beside it and looks again at every routine check
/// while its evening lasts (<see cref="ServerNpc.SeatWait"/>).</item>
/// <item><b>Spawners.</b> Settlement residents, professions and station crew get the next <i>free</i> seat — the
/// cursor used to wrap, so a tavern with fewer chairs than residents booked one chair for two people.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far from the player's reported position a seat may be: the client aims its 8 m interact ray from the
    /// camera, and the reported body position trails the 10 Hz move stream — a sanity bound, measured the short way
    /// round both seams (the block-edit reach check does not wrap latitude).</summary>
    private const float SeatReach = MaxReach + 2f;

    private void HandleSetSeated(PlayerSession session, SetSeatedIntent intent)
    {
        var p = session.State;
        if (!intent.Active || InSpace(p.PlayerId))
        {
            p.Seated = false;
            p.SeatCell = null;
            return;
        }

        Vector3i? cell = intent.HasCell ? new Vector3i(intent.X, intent.Y, intent.Z) : SeatAround(p.Position);
        if (cell is null)
        {
            // An older client with no cell on the wire whose position still trails its snap onto the chair: keep the
            // old unchecked pose — there is no seat to compare against.
            p.Seated = true;
            p.SeatCell = null;
            return;
        }

        var seat = WorldConstants.CanonicalBlock(cell.Value, _world.Circumference);
        string? refusal = !SeatStillThere(seat) ? "@srv.seat.none"
            : WrapDistSq(p.Position, new Vector3f(seat.X + 0.5f, seat.Y, seat.Z + 0.5f)) > SeatReach * SeatReach ? "@srv.seat.too_far"
            : SeatTaken(seat, exceptPlayer: p.PlayerId) ? "@srv.seat.taken"
            : null;
        if (refusal != null)
        {
            p.Seated = false;
            p.SeatCell = null;
            Reject(session, "seat", refusal);
            return;
        }

        p.Seated = true;
        p.SeatCell = seat;
    }

    /// <summary>The seat cell nearest <paramref name="pos"/> within one cell of it, or null — an older client's seat.</summary>
    private Vector3i? SeatAround(Vector3f pos)
    {
        var feet = pos.ToBlock();
        Vector3i? best = null;
        double bestSq = double.MaxValue;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var c = new Vector3i(feet.X + dx, feet.Y + dy, feet.Z + dz);
                    if (!SeatStillThere(c))
                    {
                        continue;
                    }

                    double d = WrapDistSq(pos, new Vector3f(c.X + 0.5f, c.Y, c.Z + 0.5f));
                    if (d < bestSq)
                    {
                        bestSq = d;
                        best = c;
                    }
                }

        return best;
    }

    /// <summary>Whether somebody sits on <paramref name="seat"/> right now: a seated NPC of this world (other than
    /// <paramref name="exceptNpc"/>), or a seated player here (other than <paramref name="exceptPlayer"/>).</summary>
    private bool SeatTaken(Vector3i seat, string? exceptPlayer = null, ServerNpc? exceptNpc = null)
    {
        int circ = _world.Circumference;
        seat = WorldConstants.CanonicalBlock(seat, circ);
        foreach (var n in _npcs)
        {
            if (n != exceptNpc && n.Pose == 1 && WorldConstants.CanonicalBlock(n.Pos.ToBlock(), circ) == seat)
            {
                return true;
            }
        }

        foreach (var s in JoinedInActiveWorld())
        {
            var st = s.State;
            if (st.Seated && st.SeatCell is { } c && c == seat && st.PlayerId != exceptPlayer)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether an NPC other than <paramref name="except"/> already has <paramref name="seat"/> as its seat.</summary>
    private bool SeatClaimed(Vector3i seat, ServerNpc? except = null)
    {
        int circ = _world.Circumference;
        seat = WorldConstants.CanonicalBlock(seat, circ);
        foreach (var n in _npcs)
        {
            if (n != except && n.Seat is { } s && WorldConstants.CanonicalBlock(s, circ) == seat)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The next seat of <paramref name="seats"/> from <paramref name="cursor"/> on that no other NPC has
    /// claimed (#2122), advancing the cursor past it; null once the list is used up — never a wrap-around.</summary>
    private Vector3i? NextFreeSeat(IReadOnlyList<Vector3i> seats, ref int cursor, ServerNpc? claimant = null)
    {
        while (cursor < seats.Count)
        {
            var seat = seats[cursor++];
            if (!SeatClaimed(seat, claimant))
            {
                return seat;
            }
        }

        return null;
    }

    /// <summary>Test seam (#2122): a player sits down on a cell (or stands up) exactly as the intent would.</summary>
    public void SetSeatedForTest(string playerId, bool active, Vector3i? cell = null)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleSetSeated(session, new SetSeatedIntent
            {
                Active = active,
                HasCell = cell.HasValue,
                X = cell?.X ?? 0,
                Y = cell?.Y ?? 0,
                Z = cell?.Z ?? 0,
            });
        }
    }

    /// <summary>Test seam (#2122): every NPC's claimed seat (the evening chair), by NPC id.</summary>
    public IReadOnlyList<(int Id, string Settlement, Vector3i Seat)> NpcSeatsForTest
        => _npcs.Where(n => n.Seat.HasValue).Select(n => (n.Id, n.Settlement, n.Seat!.Value)).ToList();

    /// <summary>Test seam (#2122): per inhabited settlement, how many tavern seats its evening regulars share.</summary>
    public IReadOnlyList<(string Name, int TavernSeats)> TavernSeatsForTest
        => _settlements.Where(s => !s.Ruined).Select(s => (s.Name, TavernSeats(s).Count)).ToList();
}
