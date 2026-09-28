// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The monorail hover train (#2113, Justus' idea; Marcel's decisions 2026-09-27). Three parts:
/// <list type="bullet">
/// <item><b>The rail graph.</b> Pylons (<c>rail_pylon</c> blocks) are nodes keyed by their top cell; links between them are
/// data, not blocks. A pylon placed within 32 blocks and ≈30° of the builder's previously placed pylon links itself; the
/// <c>rail_linker</c> gadget couples or uncouples any two pylons explicitly (a loop's closing link, a link the auto-link
/// refused). Every link is <b>clearance-checked</b>: the wagon's box, swept along the spline between the two tops, must
/// not run through a solid block ("Hindernis"). A pylon carries at most two links, so a connected component is a path
/// or a loop — a <b>line</b>, with a <see cref="RailSpline"/> through its pylon tops.</item>
/// <item><b>The trains.</b> A world object (per world, persisted in the metadata): the cab placed on a line with the
/// <c>rail_cab</c> gadget, wagons coupled behind it, an arc position the server advances every tick by the speed setting
/// (autopilot, or a rider at the cab's panel), reversing at the ends of an open line, halting at every <c>rail_stop</c>
/// on autopilot (a signal's edge on the stop departs it). Stow returns the items.</item>
/// <item><b>The moving frame.</b> A rider's pose aboard is "frame + wagon-local offset" (<see cref="MoveIntent.FrameId"/>):
/// the server derives their world position from the wagon pose it drives every tick, so every other system keeps
/// its world coordinates; the presence carries the frame and the offset so every other client places the rider
/// relative to the wagon as it draws it. Walking out of the wagon's box (a halted train at a stop) is leaving.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    private const double TrainTickBroadcast = RailRules.BroadcastSeconds;

    /// <summary>Per-world rail state (lives on <see cref="LoadedWorld"/>).</summary>
    internal sealed class RailWorldState
    {
        public Dictionary<Vector3i, RailNode> Nodes { get; } = new();
        public List<ServerTrain> Trains { get; } = new();
        public Dictionary<int, RailLine> Lines { get; } = new();
        public int NextTrainId { get; set; } = 1;
        public bool LinesDirty { get; set; }
        public bool TrainsDirty { get; set; }
        public double NextBroadcast { get; set; }
        public bool Loaded { get; set; }

        /// <summary>The linker gadget's first pick, per player.</summary>
        public Dictionary<string, Vector3i> LinkerPending { get; } = new();

        /// <summary>The last pylon each builder placed (the auto-link's "previous pylon of the line").</summary>
        public Dictionary<string, Vector3i> LastPylon { get; } = new();

        public void Clear()
        {
            Nodes.Clear();
            Trains.Clear();
            Lines.Clear();
            NextTrainId = 1;
            LinesDirty = false;
            TrainsDirty = false;
            NextBroadcast = 0;
            Loaded = false;
            LinkerPending.Clear();
            LastPylon.Clear();
        }
    }

    internal sealed class RailNode
    {
        public Vector3i Cell;
        public HashSet<Vector3i> Links { get; } = new();
    }

    /// <summary>A line: its pylons in order, the spline through their tops, the stops on it.</summary>
    internal sealed class RailLine
    {
        public int Id;
        public List<Vector3i> Pylons { get; } = new();
        public bool Closed;
        public RailSpline Spline = null!;
        public List<(float Arc, Vector3i Cell)> Stops { get; } = new();
    }

    internal sealed class ServerTrain
    {
        public string Id = string.Empty;
        public string OwnerId = string.Empty;
        public int LineId;
        public float Arc;
        public int Speed = 1;
        public int Direction = 1;
        public bool Autopilot = true;
        public bool Halted;
        public double HaltUntil;
        public Vector3i? HaltStop;
        public Vector3i? LastStop;
        public List<string> Wagons { get; } = new();
        public Dictionary<string, RailRider> Riders { get; } = new();
    }

    internal sealed class RailRider
    {
        public int Wagon;
        public int Seat = -1;
        public Vector3f Local;
    }

    private RailWorldState Rails => _worlds.Active.Rails;

    // =====================================================================================================
    // The graph: pylons, links, lines
    // =====================================================================================================

    /// <summary>A placed block joins the rail graph: a pylon becomes a node (on top of another pylon it raises that node)
    /// and auto-links to the builder's previous pylon; a stop registers on the nearest line.</summary>
    private void OnRailBlockPlaced(PlayerSession session, Vector3i pos, BlockDefinition def)
    {
        if (def.Key == RailRules.PylonBlockKey)
        {
            var rails = Rails;
            var below = new Vector3i(pos.X, pos.Y - 1, pos.Z);
            if (rails.Nodes.TryGetValue(below, out var lower) && !IsGeneratedPylon(below))
            {
                // Stacking raises the line: the node moves to the new top, its links come along. (#2125: never the
                // generated intercity line's — a pylon on top of one is a pylon of its own.)
                rails.Nodes.Remove(below);
                var raised = new RailNode { Cell = pos };
                foreach (var l in lower.Links)
                {
                    raised.Links.Add(l);
                    if (rails.Nodes.TryGetValue(l, out var other))
                    {
                        other.Links.Remove(below);
                        other.Links.Add(pos);
                    }
                }

                rails.Nodes[pos] = raised;
                rails.LastPylon[session.State.PlayerId] = pos;
                RailGraphChanged();
                return;
            }

            rails.Nodes[pos] = new RailNode { Cell = pos };
            string who = session.State.PlayerId;
            if (rails.LastPylon.TryGetValue(who, out var prev) && rails.Nodes.TryGetValue(prev, out var prevNode) && prev != pos)
            {
                var prevDir = default(Vector3f);
                foreach (var l in prevNode.Links)
                {
                    prevDir = new Vector3f(prev.X - l.X, 0f, prev.Z - l.Z); // the line's direction into the previous pylon
                }

                string? why = LinkRefusal(prev, pos, prevDir, auto: true);
                if (why is null)
                {
                    Link(prev, pos);
                    Send(session, new ServerMessage { Text = "@srv.rail.linked" });
                }
                else if (why != "@srv.rail.too_far")
                {
                    Send(session, new ServerMessage { Text = why }); // a refused auto-link says why; out of range is simply a new line
                }
            }
            else if (rails.Nodes.Keys.Count(k => !IsGeneratedPylon(k)) == 1)
            {
                SendVegaLine(session, "vega.sys.rail_first", 3);
            }

            rails.LastPylon[who] = pos;
            RailGraphChanged();
        }
        else if (def.Key == RailRules.StopBlockKey)
        {
            RailGraphChanged(); // the lines pick up their stops on the rebuild
        }
    }

    /// <summary>A mined pylon leaves the graph (the node drops onto the pylon below it, if any, else its links go with it);
    /// a mined stop leaves its line.</summary>
    private void OnRailBlockRemoved(Vector3i pos, BlockDefinition def)
    {
        if (def.Key == RailRules.PylonBlockKey)
        {
            var rails = Rails;
            if (!rails.Nodes.TryGetValue(pos, out var node))
            {
                return;
            }

            rails.Nodes.Remove(pos);
            var below = new Vector3i(pos.X, pos.Y - 1, pos.Z);
            var pylonId = _content.GetBlock(RailRules.PylonBlockKey)?.NumericId ?? BlockId.Air;
            if (!pylonId.IsAir && _world.GetBlock(below) == pylonId && !rails.Nodes.ContainsKey(below))
            {
                var lowered = new RailNode { Cell = below };
                foreach (var l in node.Links)
                {
                    lowered.Links.Add(l);
                    if (rails.Nodes.TryGetValue(l, out var other))
                    {
                        other.Links.Remove(pos);
                        other.Links.Add(below);
                    }
                }

                rails.Nodes[below] = lowered;
            }
            else
            {
                foreach (var l in node.Links)
                {
                    if (rails.Nodes.TryGetValue(l, out var other))
                    {
                        other.Links.Remove(pos);
                    }
                }
            }

            foreach (var key in rails.LastPylon.Where(kv => kv.Value == pos).Select(kv => kv.Key).ToList())
            {
                rails.LastPylon.Remove(key);
            }

            RailGraphChanged();
        }
        else if (def.Key == RailRules.StopBlockKey)
        {
            RailGraphChanged();
        }
    }

    private void Link(Vector3i a, Vector3i b)
    {
        var rails = Rails;
        rails.Nodes[a].Links.Add(b);
        rails.Nodes[b].Links.Add(a);
    }

    private void Unlink(Vector3i a, Vector3i b)
    {
        var rails = Rails;
        if (rails.Nodes.TryGetValue(a, out var na))
        {
            na.Links.Remove(b);
        }

        if (rails.Nodes.TryGetValue(b, out var nb))
        {
            nb.Links.Remove(a);
        }
    }

    /// <summary>The top of a pylon in world space: the centre of the cell above it, plus the line's rise.</summary>
    private static Vector3f PylonTop(Vector3i cell) => new(cell.X + 0.5f, cell.Y + RailRules.LineRise, cell.Z + 0.5f);

    /// <summary>Why two pylons may not be linked — null when they may: range (the auto-link's or the linker's), the
    /// bend rule (auto-link only), a third link on either pylon, a line grown to its cap, or a solid block in the way.</summary>
    private string? LinkRefusal(Vector3i a, Vector3i b, Vector3f prevDir, bool auto)
    {
        var rails = Rails;
        if (!rails.Nodes.TryGetValue(a, out var na) || !rails.Nodes.TryGetValue(b, out var nb) || a == b)
        {
            return "@srv.rail.no_pylon";
        }

        if (IsGeneratedPylon(a) || IsGeneratedPylon(b))
        {
            return "@srv.rail.public_line"; // #2125: the intercity line's links are the line's
        }

        if (na.Links.Contains(b))
        {
            return "@srv.rail.already_linked";
        }

        var ta = PylonTop(a);
        var tb = PylonTop(b);
        float dx = (float)WorldConstants.WrapDeltaX((double)(tb.X - ta.X), _world.Circumference);
        float dy = tb.Y - ta.Y, dz = tb.Z - ta.Z;
        float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        float range = auto ? RailRules.AutoLinkRange : RailRules.LinkerRange;
        if (dist > range || dist < 2f)
        {
            return "@srv.rail.too_far";
        }

        if (auto && !RailRules.AutoLinkFits(ta, new Vector3f(ta.X + dx, tb.Y, tb.Z), prevDir))
        {
            return "@srv.rail.too_sharp";
        }

        if (na.Links.Count >= RailRules.MaxLinksPerPylon || nb.Links.Count >= RailRules.MaxLinksPerPylon)
        {
            return "@srv.rail.branch";
        }

        if (ComponentSize(a) + ComponentSize(b) > RailRules.MaxPylonsPerLine)
        {
            return "@srv.rail.line_full";
        }

        return LinkClear(a, b) ? null : "@srv.rail.obstacle";
    }

    /// <summary>The clearance check: the wagon's box swept along the straight run between the two tops (the spline stays
    /// close to it between two pylons), one sample per block — any solid block inside the box refuses the link. The
    /// pylons and stops themselves are never obstacles, nor are the loaded chunks' fluids and flora.</summary>
    private bool LinkClear(Vector3i a, Vector3i b)
    {
        foreach (var cell in LinkClearCells(a, b))
        {
            if (RailObstacle(cell))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The cells <see cref="LinkClear"/> samples between two pylon tops (canonical) — also the part of the
    /// generated intercity line's corridor its stamp carves first (#2125), so a generated link passes the same check.</summary>
    private IEnumerable<Vector3i> LinkClearCells(Vector3i a, Vector3i b)
    {
        var ta = PylonTop(a);
        var tb = PylonTop(b);
        float dx = (float)WorldConstants.WrapDeltaX((double)(tb.X - ta.X), _world.Circumference);
        float dy = tb.Y - ta.Y, dz = tb.Z - ta.Z;
        float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        int steps = Math.Max(1, (int)Math.Ceiling(len));
        float hl = (float)Math.Sqrt(dx * dx + dz * dz);
        float rx = hl < 1e-3f ? 1f : dz / hl, rz = hl < 1e-3f ? 0f : -dx / hl; // right of travel
        for (int i = 1; i < steps; i++)
        {
            float t = i / (float)steps;
            float cx = ta.X + dx * t, cy = ta.Y + dy * t + RailRules.HoverHeight, cz = ta.Z + dz * t;
            for (int side = -1; side <= 1; side++)
            {
                for (float up = 0.5f; up < RailRules.WagonHeight; up += 1f)
                {
                    yield return new Vector3i(
                        (int)Math.Floor(WorldConstants.WrapX((double)(cx + rx * side), _world.Circumference)),
                        (int)Math.Floor(cy + up),
                        (int)Math.Floor(WorldConstants.WrapZ((double)(cz + rz * side), _world.Circumference)));
                }
            }
        }
    }

    private bool RailObstacle(Vector3i cell)
    {
        var id = _world.GetBlock(cell);
        if (id.IsAir)
        {
            return false;
        }

        var def = _content.BlockById(id);
        if (def is null || !def.Solid || def.Liquid || IsFluid(id.Value))
        {
            return false;
        }

        return def.Key is not (RailRules.PylonBlockKey or RailRules.StopBlockKey) && !def.Key.StartsWith("flora_", StringComparison.Ordinal);
    }

    private int ComponentSize(Vector3i start)
    {
        var rails = Rails;
        var seen = new HashSet<Vector3i> { start };
        var stack = new Stack<Vector3i>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var c = stack.Pop();
            if (!rails.Nodes.TryGetValue(c, out var n))
            {
                continue;
            }

            foreach (var l in n.Links)
            {
                if (seen.Add(l))
                {
                    stack.Push(l);
                }
            }
        }

        return seen.Count;
    }

    private void RailGraphChanged()
    {
        Rails.LinesDirty = true;
        RebuildRailLines();
        SaveRails();
        BroadcastToWorld(RailListMessage());
        BroadcastTrains();
    }

    /// <summary>Rebuilds the lines from the graph: every component with at least two pylons, walked from an end (a loop
    /// from any pylon), its spline, and the stops within reach of it. Trains keep their line by id where the line's first
    /// pylon still leads it; a train whose line vanished is packed back to its owner.</summary>
    private void RebuildRailLines()
    {
        var rails = Rails;
        var old = rails.Lines.Values.ToList();
        rails.Lines.Clear();
        var seen = new HashSet<Vector3i>();
        int nextId = 1;
        foreach (var kv in rails.Nodes.OrderBy(k => k.Key.X).ThenBy(k => k.Key.Z).ThenBy(k => k.Key.Y))
        {
            if (seen.Contains(kv.Key))
            {
                continue;
            }

            // The component.
            var comp = new List<Vector3i>();
            var stack = new Stack<Vector3i>();
            stack.Push(kv.Key);
            seen.Add(kv.Key);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                comp.Add(c);
                foreach (var l in rails.Nodes[c].Links)
                {
                    if (rails.Nodes.ContainsKey(l) && seen.Add(l))
                    {
                        stack.Push(l);
                    }
                }
            }

            if (comp.Count < 2)
            {
                continue;
            }

            // Walk it: from an end (one link) for a path, from the first pylon for a loop.
            var start = comp.FirstOrDefault(c => rails.Nodes[c].Links.Count == 1);
            bool closed = rails.Nodes[start == default && !comp.Contains(start) ? comp[0] : start].Links.Count != 1 && comp.All(c => rails.Nodes[c].Links.Count == 2);
            if (!comp.Any(c => rails.Nodes[c].Links.Count == 1))
            {
                start = comp[0];
                closed = true;
            }

            var order = new List<Vector3i> { start };
            var prev = start;
            var cur = start;
            var walked = new HashSet<Vector3i> { start };
            while (true)
            {
                Vector3i? next = null;
                foreach (var l in rails.Nodes[cur].Links)
                {
                    if (l != prev && rails.Nodes.ContainsKey(l) && !walked.Contains(l))
                    {
                        next = l;
                        break;
                    }
                }

                if (next is null)
                {
                    break;
                }

                walked.Add(next.Value);
                order.Add(next.Value);
                prev = cur;
                cur = next.Value;
            }

            // A line keeps the id it had when its first pylon is unchanged (a train remembers its line by id).
            var previous = old.FirstOrDefault(l => l.Pylons.Count > 0 && l.Pylons[0] == order[0]);
            int id = previous?.Id ?? 0;
            if (id == 0 || rails.Lines.ContainsKey(id))
            {
                while (rails.Lines.ContainsKey(nextId) || old.Any(l => l.Id == nextId && !ReferenceEquals(l, previous)))
                {
                    nextId++;
                }

                id = nextId++;
            }

            var line = new RailLine { Id = id, Closed = closed };
            line.Pylons.AddRange(order);
            line.Spline = RailSpline.Build(order.Select(PylonTop).ToList(), closed, _world.Circumference);
            rails.Lines[id] = line;
        }

        // The stops: every stop block within reach of a line lands on it. #2125: the intercity line keeps exactly its two
        // stations — a stop a player sets beside it does not make the public train halt.
        var stopId = _content.GetBlock(RailRules.StopBlockKey)?.NumericId ?? BlockId.Air;
        if (!stopId.IsAir)
        {
            var intercity = IntercityLine();
            foreach (var c in CrystalNet.Cells.Values)
            {
                if (c.Kind != CrystalDeviceKind.RailStop)
                {
                    continue;
                }

                var at = new Vector3f(c.Cell.X + 0.5f, c.Cell.Y + 0.5f, c.Cell.Z + 0.5f);
                RailLine? best = null;
                float bestD = RailRules.StopReach + RailRules.LineRise;
                float bestArc = 0f;
                foreach (var line in rails.Lines.Values)
                {
                    var (arc, d) = line.Spline.Nearest(at);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = line;
                        bestArc = arc;
                    }
                }

                if (best is not null && ReferenceEquals(best, intercity) && !IsIntercityStop(c.Cell))
                {
                    continue;
                }

                best?.Stops.Add((bestArc, c.Cell));
            }

            foreach (var line in rails.Lines.Values)
            {
                line.Stops.Sort((x, y) => x.Arc.CompareTo(y.Arc));
            }
        }

        // Trains: keep or return.
        for (int i = rails.Trains.Count - 1; i >= 0; i--)
        {
            var t = rails.Trains[i];
            if (RailRules.IsPublic(t.OwnerId) && !rails.Lines.ContainsKey(t.LineId) && IntercityLine() is { } generated)
            {
                t.LineId = generated.Id; // #2125: the line was renumbered, not lost — the public train stays on it
            }

            if (!rails.Lines.TryGetValue(t.LineId, out var line) || line.Spline.TotalArc < RailRules.WagonLength)
            {
                ReturnTrain(t, "@srv.rail.line_gone");
                continue;
            }

            t.Arc = line.Spline.NormalizeArc(t.Arc);
        }

        rails.LinesDirty = false;
    }

    private RailList RailListMessage() => new()
    {
        Lines = Rails.Lines.Values.OrderBy(l => l.Id).Select(l => new NetRailLine
        {
            Id = l.Id,
            Closed = l.Closed,
            Points = l.Pylons.SelectMany(p => { var t = PylonTop(p); return new[] { t.X, t.Y, t.Z }; }).ToArray(),
            StopArcs = l.Stops.Select(s => s.Arc).ToArray(),
        }).ToArray(),
    };

    // =====================================================================================================
    // The linker gadget
    // =====================================================================================================

    /// <summary>The linker (#2113): the first use picks pylon A, the second couples A and B — or uncouples them when they
    /// were linked. Aiming at nothing, or at A again, drops the pick.</summary>
    private bool UseRailLinker(PlayerSession session, Vector3f target)
    {
        var rails = Rails;
        string who = session.State.PlayerId;
        var cell = new Vector3i((int)Math.Floor(target.X), (int)Math.Floor(target.Y), (int)Math.Floor(target.Z));
        cell = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        if (!rails.Nodes.ContainsKey(cell))
        {
            // The top of a stack is the node: aim at any pylon of the column and take its top.
            var pylonId = _content.GetBlock(RailRules.PylonBlockKey)?.NumericId ?? BlockId.Air;
            var probe = cell;
            for (int up = 0; up < 8 && !pylonId.IsAir && _world.GetBlock(probe) == pylonId; up++)
            {
                if (rails.Nodes.ContainsKey(probe))
                {
                    cell = probe;
                    break;
                }

                probe = new Vector3i(probe.X, probe.Y + 1, probe.Z);
            }
        }

        if (!rails.Nodes.ContainsKey(cell))
        {
            rails.LinkerPending.Remove(who);
            Reject(session, "rail", "@srv.rail.no_pylon");
            return false;
        }

        if (!rails.LinkerPending.TryGetValue(who, out var a))
        {
            rails.LinkerPending[who] = cell;
            Send(session, new ServerMessage { Text = "@srv.rail.linker_first" });
            return true;
        }

        rails.LinkerPending.Remove(who);
        if (a == cell || !rails.Nodes.ContainsKey(a))
        {
            Send(session, new ServerMessage { Text = "@srv.rail.linker_cleared" });
            return true;
        }

        if (rails.Nodes[a].Links.Contains(cell))
        {
            if (IsGeneratedPylon(a) || IsGeneratedPylon(cell))
            {
                Reject(session, "rail", "@srv.rail.public_line"); // #2125: nobody breaks the intercity line
                return false;
            }

            Unlink(a, cell);
            Send(session, new ServerMessage { Text = "@srv.rail.unlinked" });
            RailGraphChanged();
            return true;
        }

        string? why = LinkRefusal(a, cell, default, auto: false);
        if (why is not null)
        {
            Reject(session, "rail", why);
            return false;
        }

        Link(a, cell);
        Send(session, new ServerMessage { Text = "@srv.rail.linked" });
        RailGraphChanged();
        return true;
    }

    // =====================================================================================================
    // Trains: placing, coupling, stowing
    // =====================================================================================================

    /// <summary>The cab gadget: a train appears on the line nearest the aim point (within reach of it), the cab at that arc,
    /// heading along the line. The item is consumed.</summary>
    private bool UseRailCab(PlayerSession session, Vector3f target)
    {
        var rails = Rails;
        var p = session.State;
        RailLine? best = null;
        float bestD = RailRules.StopReach + 2f, bestArc = 0f;
        foreach (var line in rails.Lines.Values)
        {
            var (arc, d) = line.Spline.Nearest(target);
            if (d < bestD)
            {
                bestD = d;
                best = line;
                bestArc = arc;
            }
        }

        if (best is null || best.Spline.TotalArc < RailRules.WagonLength * 2f)
        {
            Reject(session, "rail", "@srv.rail.no_line");
            return false;
        }

        if (ReferenceEquals(best, IntercityLine()))
        {
            Reject(session, "rail", "@srv.rail.public_line"); // #2125: the intercity line runs its own train
            return false;
        }

        if (rails.Trains.Any(t => t.LineId == best.Id && Math.Abs(best.Spline.NormalizeArc(t.Arc) - bestArc) < RailRules.WagonLength * (t.Wagons.Count + 1)))
        {
            Reject(session, "rail", "@srv.rail.line_busy");
            return false;
        }

        if (!p.Inventory.Remove(RailRules.CabItemKey, 1))
        {
            Reject(session, "rail", "@srv.gadget.missing");
            return false;
        }

        var train = new ServerTrain
        {
            Id = "train-" + _world.LocationId + "-" + rails.NextTrainId++,
            OwnerId = p.PlayerId,
            LineId = best.Id,
            Arc = bestArc,
            Speed = 1,
            Direction = 1,
            Autopilot = true,
        };
        train.Wagons.Add(RailRules.CabItemKey);
        rails.Trains.Add(train);
        SaveRails();
        SendInventory(session);
        BroadcastTrains(force: true);
        Send(session, new ServerMessage { Text = "@srv.rail.placed" });
        return true;
    }

    /// <summary>A wagon gadget: the wagon couples behind the last wagon of the aim point's nearest train (within reach of
    /// its tail). The item is consumed.</summary>
    private bool UseRailWagon(PlayerSession session, string wagonItem, Vector3f target)
    {
        var rails = Rails;
        var p = session.State;
        ServerTrain? best = null;
        float bestD = RailRules.BoardRange * 2f;
        foreach (var t in rails.Trains)
        {
            if ((t.OwnerId != p.PlayerId && !RailRules.IsPublic(t.OwnerId)) || !rails.Lines.TryGetValue(t.LineId, out var line))
            {
                continue;
            }

            var (tail, _) = WagonPose(t, line, t.Wagons.Count); // where the next wagon would hang
            float d = (float)Math.Sqrt(WrapDistSq(target, tail));
            if (d < bestD)
            {
                bestD = d;
                best = t;
            }
        }

        if (best is null)
        {
            Reject(session, "rail", "@srv.rail.no_train");
            return false;
        }

        if (RailRules.IsPublic(best.OwnerId))
        {
            Reject(session, "rail", "@srv.rail.public_line"); // #2125: nobody couples onto the public train
            return false;
        }

        if (best.Wagons.Count >= 6)
        {
            Reject(session, "rail", "@srv.rail.train_full");
            return false;
        }

        if (!p.Inventory.Remove(wagonItem, 1))
        {
            Reject(session, "rail", "@srv.gadget.missing");
            return false;
        }

        best.Wagons.Add(wagonItem);
        SaveRails();
        SendInventory(session);
        BroadcastTrains(force: true);
        Send(session, new ServerMessage { Text = "@srv.rail.coupled" });
        return true;
    }

    private void HandleStowTrain(PlayerSession session, StowTrainIntent intent)
    {
        var rails = Rails;
        var p = session.State;
        var t = rails.Trains.FirstOrDefault(x => x.Id == intent.TrainId);
        if (t is not null && RailRules.IsPublic(t.OwnerId))
        {
            Reject(session, "rail", "@srv.rail.public_line"); // #2125: the public train is nobody's to pack up
            return;
        }

        if (t is null || t.OwnerId != p.PlayerId)
        {
            Reject(session, "rail", "@srv.rail.not_yours");
            return;
        }

        if (t.Riders.Keys.Any(id => id != p.PlayerId))
        {
            Reject(session, "rail", "@srv.rail.riders_aboard");
            return;
        }

        if (t.Riders.ContainsKey(p.PlayerId))
        {
            LeaveTrain(session, setDown: true);
        }

        if (!rails.Lines.TryGetValue(t.LineId, out var line) || (float)Math.Sqrt(WrapDistSq(p.Position, WagonPose(t, line, 0).Pos)) > RailRules.StowRange + RailRules.WagonLength * t.Wagons.Count)
        {
            Reject(session, "rail", "@srv.rail.closer_pack");
            return;
        }

        ReturnTrain(t, "@srv.rail.stowed", session);
    }

    /// <summary>Packs a train back into its items: to the owner's pack when they are here, else onto the ground at the cab.</summary>
    private void ReturnTrain(ServerTrain t, string message, PlayerSession? to = null)
    {
        var rails = Rails;
        foreach (var riderId in t.Riders.Keys.ToList())
        {
            if (FindSessionByPlayerId(riderId) is { } rs)
            {
                LeaveTrain(rs, setDown: true);
            }
        }

        rails.Trains.Remove(t);
        if (RailRules.IsPublic(t.OwnerId))
        {
            // #2125: the public train belongs to nobody — it returns to no pack and drops nothing, it is simply gone.
            SaveRails();
            BroadcastTrains(force: true);
            return;
        }

        var owner = to ?? FindSessionByPlayerId(t.OwnerId);
        if (owner is not null)
        {
            var pool = new MaterialPool(_content, owner.State, _ship);
            foreach (var w in t.Wagons)
            {
                pool.Add(w, 1);
            }

            SendInventory(owner);
            var cell = owner.State.Position.ToBlock();
            SpillPoolOverflow(owner, pool, cell);
            Send(owner, new ServerMessage { Text = message });
        }
        else if (rails.Lines.TryGetValue(t.LineId, out var line))
        {
            var (pos, _) = WagonPose(t, line, 0);
            SpillToGround(pos.ToBlock(), t.Wagons.Select(w => new ItemAmount(w, 1)));
        }

        SaveRails();
        BroadcastTrains(force: true);
    }

    // =====================================================================================================
    // Poses
    // =====================================================================================================

    /// <summary>Wagon <paramref name="index"/>'s pose (0 = the cab): its centre on the line and its heading in the train's
    /// direction of travel.</summary>
    private (Vector3f Pos, float Yaw) WagonPose(ServerTrain t, RailLine line, int index)
    {
        float arc = RailRules.WagonArc(t.Arc, index, t.Direction);
        var (pos, yaw) = line.Spline.PoseAt(line.Spline.NormalizeArc(arc), t.Direction);
        return (pos, yaw);
    }

    /// <summary>A rider's world position from their wagon and local offset.</summary>
    private Vector3f RiderWorld(ServerTrain t, RailLine line, RailRider r)
    {
        var (pos, yaw) = WagonPose(t, line, Math.Clamp(r.Wagon, 0, Math.Max(0, t.Wagons.Count - 1)));
        var w = RailRules.LocalToWorld(pos, yaw, r.Local);
        return new Vector3f((float)WorldConstants.WrapX((double)w.X, _world.Circumference), w.Y, (float)WorldConstants.WrapZ((double)w.Z, _world.Circumference));
    }

    // =====================================================================================================
    // Boarding, the frame, leaving
    // =====================================================================================================

    private void HandleEnterTrain(PlayerSession session, EnterTrainIntent intent)
    {
        var rails = Rails;
        var p = session.State;
        var t = rails.Trains.FirstOrDefault(x => x.Id == intent.TrainId);
        if (t is null || !rails.Lines.TryGetValue(t.LineId, out var line))
        {
            Reject(session, "rail", "@srv.rail.no_train");
            return;
        }

        if (!RailRules.IsPublic(t.OwnerId) && t.OwnerId != p.PlayerId && !AlliedWith(t.OwnerId, p.PlayerId))
        {
            Reject(session, "rail", "@srv.rail.not_yours"); // #2125: the public train takes anyone
            return;
        }

        int wagon = Math.Clamp(intent.Wagon, 0, Math.Max(0, t.Wagons.Count - 1));
        var (pos, yaw) = WagonPose(t, line, wagon);
        if (WrapDistSq(p.Position, pos) > (RailRules.BoardRange + RailRules.WagonLength) * (RailRules.BoardRange + RailRules.WagonLength))
        {
            Reject(session, "rail", "@srv.rail.closer_board");
            return;
        }

        if (!string.IsNullOrEmpty(p.InSpeeder))
        {
            HandleExitSpeeder(session);
        }

        var seats = RailRules.SeatOffsets(t.Wagons[wagon]);
        int seat = intent.Seat >= 0 && intent.Seat < seats.Count ? intent.Seat : -1;
        if (seat >= 0 && t.Riders.Values.Any(r => r.Wagon == wagon && r.Seat == seat))
        {
            Reject(session, "rail", "@srv.rail.seat_taken");
            return;
        }

        // Standing: where they are, brought into the wagon; seated: on the seat.
        var un = Unwrapped(pos, p.Position);
        var local = seat >= 0 ? seats[seat] : RailRules.ClampLocal(RailRules.WorldToLocal(pos, yaw, un));
        var rider = new RailRider { Wagon = wagon, Seat = seat, Local = local };
        t.Riders[p.PlayerId] = rider;
        p.InTrain = RailRules.FrameId(t.Id, wagon);
        p.TrainSeat = seat;
        p.TrainLocalX = local.X;
        p.TrainLocalY = local.Y;
        p.TrainLocalZ = local.Z;
        p.Seated = seat >= 0;
        p.Position = RiderWorld(t, line, rider);
        session.AwaitingSpawnAdopt = false;
        SendPlayerState(session);
        BroadcastTrains(force: true);
    }

    private void HandleExitTrain(PlayerSession session) => LeaveTrain(session, setDown: true);

    /// <summary>Takes a rider off their train: set down beside the wagon (a leaving rider), or simply dropped (a death,
    /// a disconnect, a stowed train — the caller places them).</summary>
    private void LeaveTrain(PlayerSession session, bool setDown)
    {
        var p = session.State;
        if (string.IsNullOrEmpty(p.InTrain))
        {
            return;
        }

        var rails = Rails;
        RailRules.TryParseFrame(p.InTrain, out string trainId, out _);
        var t = rails.Trains.FirstOrDefault(x => x.Id == trainId);
        if (t is not null && t.Riders.TryGetValue(p.PlayerId, out var rider) && rails.Lines.TryGetValue(t.LineId, out var line))
        {
            if (setDown)
            {
                var (pos, yaw) = WagonPose(t, line, rider.Wagon);
                // Beside the wagon, on the side the rider stood, at the floor.
                float side = rider.Local.X >= 0f ? RailRules.ExitSide : -RailRules.ExitSide;
                var w = RailRules.LocalToWorld(pos, yaw, new Vector3f(side, 0f, Math.Clamp(rider.Local.Z, -RailRules.RiderHalfLength, RailRules.RiderHalfLength)));
                p.Position = new Vector3f((float)WorldConstants.WrapX((double)w.X, _world.Circumference), w.Y, (float)WorldConstants.WrapZ((double)w.Z, _world.Circumference));
            }

            t.Riders.Remove(p.PlayerId);
        }

        p.InTrain = string.Empty;
        p.TrainSeat = -1;
        p.Seated = false;
        session.AwaitingSpawnAdopt = false;
        if (setDown)
        {
            SendPlayerState(session);
        }

        BroadcastTrains(force: true);
    }

    /// <summary>A rider's pose report (#2113): the client sends its wagon-local offset in the train's frame. The offset is
    /// clamped into the wagon; a rider who walked out of the box (a halted train at a stop, the open side) has left. A
    /// report in the world frame while aboard is a client that left on its own — it leaves. Returns true when the report
    /// was handled here (the caller skips its world-pose path).</summary>
    private bool HandleFramedMove(PlayerSession session, MoveIntent move)
    {
        var p = session.State;
        if (string.IsNullOrEmpty(p.InTrain))
        {
            return false;
        }

        p.Yaw = move.Yaw;
        p.Pitch = move.Pitch;
        if (string.IsNullOrEmpty(move.FrameId) || move.FrameId != p.InTrain)
        {
            if (!string.IsNullOrEmpty(move.FrameId))
            {
                return true; // a stale frame (the train list changed under the client): keep the last good offset
            }

            LeaveTrain(session, setDown: true);
            return true;
        }

        var rails = Rails;
        RailRules.TryParseFrame(p.InTrain, out string trainId, out _);
        var t = rails.Trains.FirstOrDefault(x => x.Id == trainId);
        if (t is null || !t.Riders.TryGetValue(p.PlayerId, out var rider) || !rails.Lines.TryGetValue(t.LineId, out var line))
        {
            LeaveTrain(session, setDown: true);
            return true;
        }

        var local = new Vector3f(move.X, move.Y, move.Z);
        if (!float.IsFinite(local.X) || !float.IsFinite(local.Y) || !float.IsFinite(local.Z))
        {
            return true;
        }

        if (rider.Seat >= 0)
        {
            return true; // a seated rider does not move; standing up is an exit or a new EnterTrain
        }

        if (RailRules.OutsideWagon(local))
        {
            // Walked out: on foot where they stepped off.
            var (pos, yaw) = WagonPose(t, line, rider.Wagon);
            var w = RailRules.LocalToWorld(pos, yaw, local);
            p.Position = new Vector3f((float)WorldConstants.WrapX((double)w.X, _world.Circumference), w.Y, (float)WorldConstants.WrapZ((double)w.Z, _world.Circumference));
            t.Riders.Remove(p.PlayerId);
            p.InTrain = string.Empty;
            p.TrainSeat = -1;
            p.Seated = false;
            SendPlayerState(session);
            BroadcastTrains(force: true);
            return true;
        }

        rider.Local = RailRules.ClampLocal(local);
        p.TrainLocalX = rider.Local.X;
        p.TrainLocalY = rider.Local.Y;
        p.TrainLocalZ = rider.Local.Z;
        p.Position = RiderWorld(t, line, rider);
        return true;
    }

    /// <summary>A death, a disconnect, a teleport: the rider simply leaves the train (no set-down — the caller places them).</summary>
    private void LeaveTrainSilently(PlayerState p)
    {
        if (string.IsNullOrEmpty(p.InTrain))
        {
            return;
        }

        RailRules.TryParseFrame(p.InTrain, out string trainId, out _);
        var t = Rails.Trains.FirstOrDefault(x => x.Id == trainId);
        t?.Riders.Remove(p.PlayerId);
        p.InTrain = string.Empty;
        p.TrainSeat = -1;
        BroadcastTrains(force: true);
    }

    /// <summary>Whether <paramref name="who"/> may ride <paramref name="ownerId"/>'s train: the owner, or an ally of theirs.</summary>
    private bool AlliedWith(string ownerId, string who) => AreAllied(ownerId, who);

    // =====================================================================================================
    // The cab panel
    // =====================================================================================================

    private void HandleSetTrain(PlayerSession session, SetTrainIntent intent)
    {
        var rails = Rails;
        var p = session.State;
        var t = rails.Trains.FirstOrDefault(x => x.Id == intent.TrainId);
        if (t is null)
        {
            Reject(session, "rail", "@srv.rail.no_train");
            return;
        }

        if (RailRules.IsPublic(t.OwnerId))
        {
            Reject(session, "rail", "@srv.rail.public_line"); // #2125: the public train keeps its own timetable
            return;
        }

        if (t.OwnerId != p.PlayerId && !AlliedWith(t.OwnerId, p.PlayerId))
        {
            Reject(session, "rail", "@srv.rail.not_yours");
            return;
        }

        if (intent.Speed is >= 1 and <= 3)
        {
            t.Speed = intent.Speed;
        }

        if (intent.Autopilot >= 0)
        {
            t.Autopilot = intent.Autopilot != 0;
        }

        if (intent.Halt >= 0)
        {
            t.Halted = intent.Halt != 0;
            t.HaltStop = null;
            t.HaltUntil = 0;
            if (!t.Halted)
            {
                t.LastStop = null; // "go" from a stop: the same stop does not catch it again until the next one
            }
        }

        SaveRails();
        BroadcastTrains(force: true);
    }

    /// <summary>A signal's rising edge on a stop (#2113): every train halted at this stop departs now.</summary>
    private void DepartTrainsAtStop(Vector3i stopCell)
    {
        bool any = false;
        foreach (var t in Rails.Trains)
        {
            if (t.Halted && t.HaltStop == stopCell && !RailRules.IsPublic(t.OwnerId)) // #2125: the public train keeps its timetable
            {
                t.Halted = false;
                t.HaltStop = null;
                t.HaltUntil = 0;
                any = true;
            }
        }

        if (any)
        {
            BroadcastTrains(force: true);
        }
    }

    // =====================================================================================================
    // The tick
    // =====================================================================================================

    private void TickTrains(double dt)
    {
        var rails = Rails;
        if (!rails.Loaded)
        {
            return;
        }

        bool moved = false;
        foreach (var t in rails.Trains)
        {
            if (!rails.Lines.TryGetValue(t.LineId, out var line))
            {
                continue;
            }

            float before = t.Arc;
            if (t.Halted)
            {
                if (t.HaltUntil > 0 && _uptime >= t.HaltUntil)
                {
                    t.Halted = false;
                    t.HaltStop = null;
                    t.HaltUntil = 0;
                    moved = true;
                }
            }
            else
            {
                // Autopilot, or a rider in the cab: the train runs. (Nobody aboard on manual = it waits.)
                bool runs = t.Autopilot || t.Riders.Values.Any(r => r.Wagon == 0);
                if (runs)
                {
                    float speed = RailRules.SpeedTable[Math.Clamp(t.Speed, 1, 3) - 1];
                    float next = t.Arc + t.Direction * speed * (float)dt;
                    if (!line.Closed)
                    {
                        // Reverse at the ends: the cab's arc runs between the train's own length and the line's end.
                        float minArc = Math.Max(0f, (t.Wagons.Count - 1) * (RailRules.WagonLength + RailRules.WagonGap));
                        float total = line.Spline.TotalArc;
                        if (t.Direction > 0 && next >= total)
                        {
                            next = total;
                            t.Direction = -1;
                            t.Arc = next;
                            // The train's arc is the CAB's: reversing swaps which end the cab leads from — shift the arc so the
                            // wagons stay where they are (the cab is now at the other end of the same span).
                            t.Arc = total - minArc;
                        }
                        else if (t.Direction < 0 && next <= 0f)
                        {
                            t.Direction = 1;
                            t.Arc = minArc;
                        }
                        else
                        {
                            t.Arc = next;
                        }
                    }
                    else
                    {
                        t.Arc = line.Spline.NormalizeArc(next);
                    }

                    // Autopilot: a stop the cab passed this tick halts the train (once per stop, the next stop resets).
                    if (t.Autopilot && line.Stops.Count > 0)
                    {
                        foreach (var (arc, cell) in line.Stops)
                        {
                            if (t.LastStop == cell)
                            {
                                continue;
                            }

                            if (Crossed(before, t.Arc, arc, line))
                            {
                                t.Halted = true;
                                t.HaltStop = cell;
                                t.LastStop = cell;
                                t.HaltUntil = _uptime + (RailRules.IsPublic(t.OwnerId) ? RailRules.PublicStopHaltSeconds : RailRules.StopHaltSeconds);
                                t.Arc = arc;
                                break;
                            }
                        }

                        if (t.LastStop is { } last && !line.Stops.Any(s => s.Cell == last && Math.Abs(NormDelta(t.Arc, s.Arc, line)) < RailRules.WagonLength))
                        {
                            t.LastStop = null; // clear of the last stop: it may catch the train again next lap
                        }
                    }

                    moved = true;
                }
            }

            // The riders ride: the server owns their world position while aboard.
            foreach (var kv in t.Riders)
            {
                if (FindSessionByPlayerId(kv.Key) is { } rs && rs.State.InTrain.Length > 0)
                {
                    rs.State.Position = RiderWorld(t, line, kv.Value);
                }
            }
        }

        if (moved)
        {
            rails.TrainsDirty = true;
        }

        BroadcastTrains();
    }

    private static float NormDelta(float a, float b, RailLine line)
    {
        float d = a - b;
        if (line.Closed && line.Spline.TotalArc > 0f)
        {
            float total = line.Spline.TotalArc;
            d %= total;
            if (d > total * 0.5f) d -= total;
            if (d < -total * 0.5f) d += total;
        }

        return d;
    }

    /// <summary>Whether the arc <paramref name="stop"/> lies between <paramref name="from"/> and <paramref name="to"/> (either way round; a loop's wrap included).</summary>
    private static bool Crossed(float from, float to, float stop, RailLine line)
    {
        float lo = Math.Min(from, to), hi = Math.Max(from, to);
        if (stop >= lo && stop <= hi)
        {
            return true;
        }

        if (line.Closed && Math.Abs(to - from) > line.Spline.TotalArc * 0.5f)
        {
            return stop >= hi || stop <= lo; // the wrap of a loop
        }

        return false;
    }

    private void BroadcastTrains(bool force = false)
    {
        var rails = Rails;
        if (!force && !rails.TrainsDirty)
        {
            return;
        }

        if (!force && _uptime < rails.NextBroadcast)
        {
            return;
        }

        rails.NextBroadcast = _uptime + TrainTickBroadcast;
        rails.TrainsDirty = false;
        BroadcastToWorld(TrainListMessage());
    }

    private TrainList TrainListMessage() => new()
    {
        Trains = Rails.Trains.Select(t => new NetTrain
        {
            Id = t.Id,
            OwnerId = t.OwnerId,
            LineId = t.LineId,
            Arc = t.Arc,
            Speed = t.Speed,
            Direction = t.Direction,
            Autopilot = t.Autopilot,
            Halted = t.Halted,
            HaltRemaining = t.Halted && t.HaltUntil > 0 ? (float)Math.Max(0.0, t.HaltUntil - _uptime) : 0f,
            Wagons = t.Wagons.ToArray(),
            Riders = t.Riders.Keys.ToArray(),
        }).ToArray(),
    };

    private void SendRails(PlayerSession session)
    {
        Send(session, RailListMessage());
        Send(session, TrainListMessage());
    }

    // =====================================================================================================
    // Persistence: the graph and the trains in the metadata (the pylons are blocks; the links and trains are not)
    // =====================================================================================================

    private void SaveRails()
    {
        var rails = Rails;
        var sb = new StringBuilder();
        foreach (var n in rails.Nodes.Values.OrderBy(n => n.Cell.X).ThenBy(n => n.Cell.Y).ThenBy(n => n.Cell.Z))
        {
            if (sb.Length > 0)
            {
                sb.Append('|');
            }

            sb.Append(n.Cell.X).Append(',').Append(n.Cell.Y).Append(',').Append(n.Cell.Z).Append(':');
            bool first = true;
            foreach (var l in n.Links.OrderBy(l => l.X).ThenBy(l => l.Y).ThenBy(l => l.Z))
            {
                if (!first)
                {
                    sb.Append(';');
                }

                first = false;
                sb.Append(l.X).Append(',').Append(l.Y).Append(',').Append(l.Z);
            }
        }

        _meta.RailGraphs[_world.LocationId] = sb.ToString();

        var tb = new StringBuilder();
        foreach (var t in rails.Trains)
        {
            if (tb.Length > 0)
            {
                tb.Append('|');
            }

            tb.Append(t.Id).Append(',').Append(t.OwnerId).Append(',').Append(t.LineId).Append(',')
              .Append(t.Arc.ToString("0.###", CultureInfo.InvariantCulture)).Append(',').Append(t.Speed).Append(',').Append(t.Direction).Append(',')
              .Append(t.Autopilot ? 1 : 0).Append(',').Append(string.Join("+", t.Wagons));
        }

        _meta.Trains[_world.LocationId] = tb.ToString();
        _repo.SaveMetadata(_meta);
    }

    /// <summary>Rebuilds the rail graph and the trains of the active world from the metadata (world load).</summary>
    private void LoadRails()
    {
        var rails = Rails;
        rails.Clear();
        if (_world.Planet is null)
        {
            rails.Loaded = true;
            return;
        }

        if (_meta.RailGraphs.TryGetValue(_world.LocationId, out string? graph) && graph.Length > 0)
        {
            foreach (string entry in graph.Split('|'))
            {
                int colon = entry.IndexOf(':');
                if (colon < 0 || !TryCell(entry.Substring(0, colon), out var cell))
                {
                    continue;
                }

                var node = new RailNode { Cell = cell };
                string links = entry.Substring(colon + 1);
                if (links.Length > 0)
                {
                    foreach (string l in links.Split(';'))
                    {
                        if (TryCell(l, out var lc))
                        {
                            node.Links.Add(lc);
                        }
                    }
                }

                rails.Nodes[cell] = node;
            }

            // Links must be mutual and point at nodes that exist.
            foreach (var n in rails.Nodes.Values)
            {
                n.Links.RemoveWhere(l => !rails.Nodes.ContainsKey(l));
                foreach (var l in n.Links)
                {
                    rails.Nodes[l].Links.Add(n.Cell);
                }
            }
        }

        if (_meta.Trains.TryGetValue(_world.LocationId, out string? trains) && trains.Length > 0)
        {
            foreach (string entry in trains.Split('|'))
            {
                var f = entry.Split(',');
                if (f.Length < 8)
                {
                    continue;
                }

                var t = new ServerTrain
                {
                    Id = f[0],
                    OwnerId = f[1],
                    LineId = int.TryParse(f[2], out int lid) ? lid : 0,
                    Arc = float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float arc) ? arc : 0f,
                    Speed = int.TryParse(f[4], out int sp) ? Math.Clamp(sp, 1, 3) : 1,
                    Direction = int.TryParse(f[5], out int dir) && dir < 0 ? -1 : 1,
                    Autopilot = f[6] == "1",
                };
                foreach (string w in f[7].Split('+', StringSplitOptions.RemoveEmptyEntries))
                {
                    t.Wagons.Add(w);
                }

                if (t.Wagons.Count > 0)
                {
                    rails.Trains.Add(t);
                    if (int.TryParse(t.Id.Substring(t.Id.LastIndexOf('-') + 1), out int n) && n >= rails.NextTrainId)
                    {
                        rails.NextTrainId = n + 1;
                    }
                }
            }
        }

        rails.Loaded = true;
        bool generated = EnsureIntercityGraph(); // #2125: the intercity line's stops, nodes and links, when missing
        RebuildRailLines();
        if (EnsureIntercityTrain() || generated)
        {
            SaveRails(); // #2125: the public train on the intercity line, when it has none
        }
    }

    private static bool TryCell(string s, out Vector3i cell)
    {
        cell = default;
        var f = s.Split(',');
        if (f.Length != 3 || !int.TryParse(f[0], out int x) || !int.TryParse(f[1], out int y) || !int.TryParse(f[2], out int z))
        {
            return false;
        }

        cell = new Vector3i(x, y, z);
        return true;
    }

    // =====================================================================================================
    // Test seams
    // =====================================================================================================

    /// <summary>Test seam (#2113): the lines of the active world (id, pylons in order, closed, total arc, stop count).</summary>
    public IReadOnlyList<(int Id, IReadOnlyList<Vector3i> Pylons, bool Closed, float TotalArc, int Stops)> RailLinesForTest()
        => Rails.Lines.Values.OrderBy(l => l.Id).Select(l => (l.Id, (IReadOnlyList<Vector3i>)l.Pylons, l.Closed, l.Spline.TotalArc, l.Stops.Count)).ToList();

    /// <summary>Test seam (#2113): the trains of the active world.</summary>
    public IReadOnlyList<(string Id, string OwnerId, int LineId, float Arc, int Speed, int Direction, bool Autopilot, bool Halted, IReadOnlyList<string> Wagons, IReadOnlyList<string> Riders)> TrainsForTest()
        => Rails.Trains.Select(t => (t.Id, t.OwnerId, t.LineId, t.Arc, t.Speed, t.Direction, t.Autopilot, t.Halted, (IReadOnlyList<string>)t.Wagons, (IReadOnlyList<string>)t.Riders.Keys.ToList())).ToList();

    /// <summary>Test seam (#2113): a wagon's pose.</summary>
    public (Vector3f Pos, float Yaw)? WagonPoseForTest(string trainId, int wagon)
        => Rails.Trains.FirstOrDefault(t => t.Id == trainId) is { } t && Rails.Lines.TryGetValue(t.LineId, out var line) ? WagonPose(t, line, wagon) : null;

    public void EnterTrainForTest(string playerId, string trainId, int wagon, int seat = -1)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleEnterTrain(s, new EnterTrainIntent { TrainId = trainId, Wagon = wagon, Seat = seat });
        }
    }

    public void ExitTrainForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleExitTrain(s);
        }
    }

    public void SetTrainForTest(string playerId, string trainId, int speed = -1, int halt = -1, int autopilot = -1)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleSetTrain(s, new SetTrainIntent { TrainId = trainId, Speed = speed, Halt = halt, Autopilot = autopilot });
        }
    }

    public void StowTrainForTest(string playerId, string trainId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleStowTrain(s, new StowTrainIntent { TrainId = trainId });
        }
    }

    /// <summary>Test seam (#2113): a rider's framed pose report — the wagon-local offset.</summary>
    public void FramedMoveForTest(string playerId, string frameId, Vector3f local)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleMove(s, new MoveIntent { FrameId = frameId, X = local.X, Y = local.Y, Z = local.Z, Yaw = s.State.Yaw, Pitch = s.State.Pitch });
        }
    }

    /// <summary>Test seam (#2113): the links of a pylon.</summary>
    public IReadOnlyList<Vector3i> RailLinksForTest(Vector3i pylon)
        => Rails.Nodes.TryGetValue(pylon, out var n) ? n.Links.ToList() : Array.Empty<Vector3i>();
}
