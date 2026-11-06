// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The monorail's auto-link (#2113, reworked in #3256 after a player's straight line refused to link): a freshly placed
/// pylon takes <b>one</b> link by itself.
/// <list type="number">
/// <item>The builder's <b>last placed pylon</b> (<see cref="RailWorldState.LastPylon"/>) first — within
/// <see cref="RailRules.AutoLinkRange"/>, bending at most <see cref="RailRules.AutoLinkMaxAngleDeg"/> against that pylon's
/// line, with room for the wagons. That is how a row of pylons becomes a line.</item>
/// <item>When it does not take the new pylon — there is none (mined, a world load forgets it), it stands out of range, or
/// it refuses — the <b>free line ends</b> in range compete: every pylon with at most one link (a lone pylon or a line's
/// end), measured by the same rules against its own line, cheapest first by <see cref="RailRules.AutoLinkCost"/>
/// (distance plus bend). Filling a gap, extending a line's other end or re-placing a mined pylon then just works, and a
/// parallel line's end does not steal the pylon while a straighter continuation is in range. A pylon never gets a third
/// link, and the generated intercity line is never a candidate (#2125).</item>
/// <item>When nothing links, the player hears why: the last pylon's refusal ("too sharp", "obstacle", "no forks", …), or
/// — out of range or none — a hint to couple it by hand with the rail linker. The very first pylon of a world keeps
/// VEGA's welcome line instead.</item>
/// </list>
/// Every delta runs the short way round both seams (X at the circumference, Z at the latitude period).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Links a freshly placed pylon (already a node of the graph, not yet the builder's last pylon) to the
    /// builder's last placed pylon, else to the best free line end, and tells the builder when nothing linked.</summary>
    private void AutoLinkPlacedPylon(PlayerSession session, Vector3i pos)
    {
        var rails = Rails;
        string? lastRefusal = null;
        Vector3i? last = rails.LastPylon.TryGetValue(session.State.PlayerId, out var lp) && lp != pos && rails.Nodes.ContainsKey(lp)
            ? lp
            : null;
        if (last is { } prev)
        {
            lastRefusal = LinkRefusal(prev, pos, LineDirectionInto(prev), auto: true);
            if (lastRefusal is null)
            {
                Link(prev, pos);
                Send(session, new ServerMessage { Text = "@srv.rail.linked" });
                return;
            }
        }

        if (BestFreeLineEnd(pos, last) is { } end)
        {
            Link(end, pos);
            Send(session, new ServerMessage { Text = "@srv.rail.linked" });
            return;
        }

        if (lastRefusal is not null && lastRefusal != "@srv.rail.too_far")
        {
            Send(session, new ServerMessage { Text = lastRefusal }); // the last pylon refused: say why
        }
        else if (rails.Nodes.Keys.Count(k => !IsGeneratedPylon(k)) == 1)
        {
            SendVegaLine(session, "vega.sys.rail_first", 3); // the world's first pylon: VEGA explains the line
        }
        else
        {
            Send(session, new ServerMessage { Text = "@srv.rail.no_link_hint" }); // nothing in range: the linker does it
        }
    }

    /// <summary>The free line end (a pylon with at most one link) the new pylon at <paramref name="pos"/> links to best,
    /// or null: in auto-link range, within the bend against the end's own line, cheapest by distance plus bend, and
    /// passing every rule of <see cref="LinkRefusal"/> (clearance, the line cap). <paramref name="skip"/> (the builder's
    /// last pylon, already asked) and the generated intercity line's pylons are no candidates.</summary>
    private Vector3i? BestFreeLineEnd(Vector3i pos, Vector3i? skip)
    {
        var rails = Rails;
        int circ = _world.Circumference;
        var top = PylonTop(pos);
        var candidates = new List<(Vector3i Cell, Vector3f Dir, float Cost)>();
        foreach (var kv in rails.Nodes)
        {
            var cell = kv.Key;
            if (cell == pos || cell == skip || kv.Value.Links.Count >= RailRules.MaxLinksPerPylon || IsGeneratedPylon(cell))
            {
                continue;
            }

            var from = PylonTop(cell);
            var to = new Vector3f(
                from.X + (float)WorldConstants.WrapDeltaX((double)(top.X - from.X), circ),
                top.Y,
                from.Z + (float)WorldConstants.WrapDeltaZ((double)(top.Z - from.Z), circ));
            float dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
            float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            var dir = LineDirectionInto(cell);
            if (dist < 2f || dist > RailRules.AutoLinkRange || !RailRules.AutoLinkFits(from, to, dir))
            {
                continue;
            }

            candidates.Add((cell, dir, RailRules.AutoLinkCost(dist, RailRules.AutoLinkBendDegrees(from, to, dir))));
        }

        foreach (var c in candidates.OrderBy(c => c.Cost).ThenBy(c => c.Cell.X).ThenBy(c => c.Cell.Z).ThenBy(c => c.Cell.Y))
        {
            if (LinkRefusal(c.Cell, pos, c.Dir, auto: true) is null)
            {
                return c.Cell;
            }
        }

        return null;
    }

    /// <summary>The horizontal direction a line arrives at its end pylon <paramref name="end"/> — from its one neighbour
    /// toward it, the short way round both seams — or zero for a lone pylon (any direction fits the first link) and for
    /// a pylon with two links (it takes no third anyway).</summary>
    private Vector3f LineDirectionInto(Vector3i end)
    {
        if (!Rails.Nodes.TryGetValue(end, out var node) || node.Links.Count != 1)
        {
            return default;
        }

        var from = node.Links.First();
        int circ = _world.Circumference;
        return new Vector3f(WorldConstants.WrapDeltaX(end.X - from.X, circ), 0f, WorldConstants.WrapDeltaZ(end.Z - from.Z, circ));
    }
}
