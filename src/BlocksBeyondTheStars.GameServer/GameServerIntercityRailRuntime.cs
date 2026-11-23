// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The intercity lines at runtime (#3984; the main file is GameServerIntercityRail.cs): the protection of their stations
/// and pylons, the footprints later stampers keep clear of, the stations on the map, the Crystal-Net graph (stops, nodes,
/// links) and the public train on every line, and the test seams.
/// </summary>
public sealed partial class GameServer
{
    // =====================================================================================================
    // Protection, reservations, the map
    // =====================================================================================================

    /// <summary>Whether a cell lies inside a station's protected volume (its hall and the plinth under it).</summary>
    private bool InIntercityStation(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var st in Intercity.Stations)
        {
            int lx = WorldConstants.WrapDeltaX(pos.X - st.Origin.X, circ);
            int lz = pos.Z - st.Origin.Z;
            if (lx >= 0 && lx < st.W && lz >= 0 && lz < st.L && pos.Y >= st.Origin.Y - 48 && pos.Y < st.Origin.Y + RailStationGenerator.Height)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a block belongs to an intercity line: a station's hall or plinth, or a generated pylon column.
    /// Protected like a settlement (#2125) — so nobody breaks the line by accident.</summary>
    internal bool IsIntercityRailBlock(Vector3i pos)
    {
        var ic = Intercity;
        if (!ic.Active)
        {
            return false;
        }

        return ic.ProtectedCells.Contains(WorldConstants.CanonicalBlock(pos, _world.Circumference)) || InIntercityStation(pos);
    }

    /// <summary>Whether a cell is one of the stations' stops.</summary>
    private bool IsIntercityStop(Vector3i cell) => Intercity.Active && Intercity.Stations.Any(s => s.Stop == cell);

    /// <summary>Whether a pylon top is one of a generated line's — its links are the line's, no player may change them.</summary>
    private bool IsGeneratedPylon(Vector3i top) => Intercity.Active && Intercity.PylonTops.Contains(top);

    /// <summary>Adds the stations and the routes' spans to a stamper's reserved footprints (#2125).</summary>
    private void AppendIntercityReservations(List<(int Cx, int Cz, int Hw, int Hl)> reserved)
    {
        if (Intercity.Active)
        {
            reserved.AddRange(Intercity.Reservations);
        }
    }

    /// <summary>Whether a point (or a small area around it) lies on the stations or a route (#2125) — the surface
    /// stampers that ask <see cref="OverlapsAnySettlement"/> keep clear of the lines the same way.</summary>
    private bool OverlapsIntercityRail(int x, int z, int halfExtent)
        => Intercity.Active && OverlapsFootprint(x, z, halfExtent, halfExtent, Intercity.Reservations, SettlementCollisionMargin);

    /// <summary>The stations on the world map (#2125): "Karth Town Station" / "Bahnhof Karth Town".</summary>
    private void AppendIntercityPois(PlayerSession session, List<NetPoi> pois)
    {
        foreach (var st in Intercity.Stations)
        {
            var c = st.Centre;
            pois.Add(new NetPoi
            {
                Type = "rail_station",
                Name = string.Format(CultureInfo.InvariantCulture, Localize(session.Locale, "poi.rail_station"), st.SettlementName),
                X = (float)WorldConstants.WrapX((double)c.X, _world.Circumference),
                Z = c.Z,
            });
        }
    }

    // =====================================================================================================
    // The runtime: the graph, the stops, the public trains
    // =====================================================================================================

    /// <summary>After the rail graph is read from the metadata: the stations' stops as owner-less Crystal-Net
    /// <c>RailStop</c> devices, every line's generated nodes and links. Idempotent — the first load writes them, every later
    /// load finds them. Returns whether anything was added.</summary>
    private bool EnsureIntercityGraph()
    {
        var ic = Intercity;
        if (!ic.Active)
        {
            return false;
        }

        foreach (var st in ic.Stations)
        {
            if (!CrystalNet.Cells.ContainsKey(st.Stop))
            {
                RegisterCrystalCell(st.Stop, CrystalDeviceKind.RailStop, RailRules.StopBlockKey, RailRules.PublicOwnerId, 0, string.Empty, string.Empty, 0, persist: true);
            }
        }

        bool changed = false;
        foreach (var line in ic.Lines)
        {
            changed |= EnsureIntercityLinks(line.Pylons);
        }

        return changed;
    }

    /// <summary>One line's nodes and links in the rail graph, when missing. Returns whether anything was added.</summary>
    private bool EnsureIntercityLinks(List<Vector3i> pylons)
    {
        var rails = Rails;
        bool changed = false;
        foreach (var p in pylons)
        {
            if (!rails.Nodes.ContainsKey(p))
            {
                rails.Nodes[p] = new RailNode { Cell = p };
                changed = true;
            }
        }

        for (int j = 0; j < pylons.Count - 1; j++)
        {
            var a = pylons[j];
            var b = pylons[j + 1];
            if (!rails.Nodes[a].Links.Contains(b))
            {
                rails.Nodes[a].Links.Add(b);
                rails.Nodes[b].Links.Add(a);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>The generated lines as the rail state knows them, in line order (a line that is not built, or not whole, is
    /// left out).</summary>
    private IEnumerable<RailLine> IntercityRailLines()
    {
        foreach (var state in Intercity.Lines)
        {
            if (state.Pylons.Count > 0 && Rails.Lines.Values.FirstOrDefault(l => l.Pylons.Contains(state.Pylons[0])) is { } line)
            {
                yield return line;
            }
        }
    }

    /// <summary>Whether a rail line is one of the generated lines (its own train, its own stops).</summary>
    private bool IsIntercityRailLine(RailLine? line) => line is not null && IntercityRailLines().Any(l => ReferenceEquals(l, line));

    /// <summary>The generated line a public train belongs to after the lines were renumbered: the one that shares a pylon with
    /// the line it ran on before, else the first one. Null when the world has none.</summary>
    private RailLine? IntercityRailLineFor(RailLine? ranOn)
    {
        RailLine? first = null;
        foreach (var line in IntercityRailLines())
        {
            first ??= line;
            if (ranOn is not null && line.Pylons.Any(ranOn.Pylons.Contains))
            {
                return line;
            }
        }

        return first;
    }

    /// <summary>After the lines are rebuilt: the public train on every generated line that has none — halted at the station
    /// with the lower arc as though it had just arrived there. Returns whether one was added.</summary>
    private bool EnsureIntercityTrain()
    {
        bool added = false;
        foreach (var line in IntercityRailLines().ToList())
        {
            if (!Rails.Trains.Any(t => RailRules.IsPublic(t.OwnerId) && t.LineId == line.Id))
            {
                Rails.Trains.Add(NewPublicTrain(line));
                added = true;
            }
        }

        return added;
    }

    /// <summary>The public train of a generated line, halted at its first stop.</summary>
    private ServerTrain NewPublicTrain(RailLine line)
    {
        var rails = Rails;
        var train = new ServerTrain
        {
            Id = "train-" + _world.LocationId + "-" + rails.NextTrainId++,
            OwnerId = RailRules.PublicOwnerId,
            LineId = line.Id,
            Speed = RailRules.PublicTrainSpeedFor(line.Spline.TotalArc), // #3632: a long line runs the fastest setting
            Direction = -1,
            Facing = -1, // #2741: the cab points down the line, its passenger wagon behind it toward the hall's exit
            Autopilot = true,
        };
        train.Wagons.AddRange(RailRules.PublicTrainWagons);
        if (line.Stops.Count > 0)
        {
            var (arc, cell) = line.Stops[0];
            train.Arc = arc;
            train.Halted = true;
            train.HaltUntil = _uptime + RailRules.PublicStopHaltSeconds;
            train.HaltStop = cell;
            train.LastStop = cell;

            // Like a train that halts there (#2741): a station near the line's end turns it round while it stands.
            var (lo, _) = RailRules.CabRange(line.Spline.TotalArc, RailRules.TrainSpan(train.Wagons.Count), train.Facing);
            if (!line.Closed && train.Arc - lo < RailRules.WagonLength)
            {
                train.Direction = 1;
            }
        }
        else
        {
            train.Arc = Math.Min(line.Spline.TotalArc, RailRules.WagonLength);
        }

        return train;
    }

    // =====================================================================================================
    // Test seams
    // =====================================================================================================

    /// <summary>Test seam (#2125): the active world's first intercity line — its stations (their town, heading, origin,
    /// footprint, stop cell and end pylon) and the pylon tops in line order; null when the world has none.</summary>
    public (IReadOnlyList<(string Settlement, int Heading, Vector3i Origin, int W, int L, Vector3i Stop, Vector3i EndPylon)> Stations, IReadOnlyList<Vector3i> Pylons)? IntercityRailForTest()
        => Intercity.Active
            ? (Intercity.Lines[0].Stations.Select(s => (s.SettlementName, s.Heading, s.Origin, s.W, s.L, s.Stop, s.EndPylon)).ToList(), Intercity.Lines[0].Pylons.ToList())
            : null;

    /// <summary>Test seam (#3984): every built line of the active world.</summary>
    internal IReadOnlyList<IntercityLineState> IntercityLinesForTest => Intercity.Lines;

    /// <summary>Test seam (#2125): the settlement names on this world, in stamp order (the order of the tier and box seams).</summary>
    public IReadOnlyList<string> SettlementNamesForTest => _settlements.Select(s => s.Name).ToList();

    /// <summary>Test seam (#2125): whether a cell is protected as part of the line.</summary>
    public bool IsIntercityRailBlockForTest(Vector3i pos) => IsIntercityRailBlock(pos);

    /// <summary>Test seam (#2125): the clearance check between two pylon tops, exactly as the player's links are checked.</summary>
    public bool RailLinkClearForTest(Vector3i a, Vector3i b) => LinkClear(a, b);

    /// <summary>Test seam (#2125): the train's remaining halt (seconds; 0 when it runs) and the stop it halts at.</summary>
    public (bool Halted, double Remaining, Vector3i? Stop) TrainHaltForTest(string trainId)
        => Rails.Trains.FirstOrDefault(t => t.Id == trainId) is { } t
            ? (t.Halted, t.Halted && t.HaltUntil > 0 ? Math.Max(0.0, t.HaltUntil - _uptime) : 0.0, t.HaltStop)
            : (false, 0.0, null);

    /// <summary>Test seam (#2125): uses the rail linker as a player on two pylon cells (pick, then couple/uncouple) and
    /// returns whether the second use was accepted.</summary>
    public bool RailLinkerForTest(string playerId, Vector3i a, Vector3i b)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return false;
        }

        Rails.LinkerPending.Remove(playerId);
        UseRailLinker(s, new Vector3f(a.X + 0.5f, a.Y + 0.5f, a.Z + 0.5f));
        return UseRailLinker(s, new Vector3f(b.X + 0.5f, b.Y + 0.5f, b.Z + 0.5f));
    }

    /// <summary>Test seam (#2125): couples a wagon as a player at a target point, returns whether it was accepted.</summary>
    public bool RailWagonForTest(string playerId, string wagonItem, Vector3f target)
        => FindSessionByPlayerId(playerId) is { } s && UseRailWagon(s, wagonItem, target);
}
