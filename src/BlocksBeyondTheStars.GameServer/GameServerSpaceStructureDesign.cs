// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3612 — how a space structure travels to the pilots outside:
/// <list type="bullet">
/// <item><b>In parts.</b> A design whose cell arrays would pass <see cref="DesignPartJsonBudget"/> bytes as JSON (the
/// browser's envelope, the larger of the two encodings — MessagePack never writes a number longer than its decimal text) is
/// cut into parts of one <see cref="SpaceShipDesign.Serial"/>; <c>Client.Core.SpaceDesignAssembler</c> joins them. Every
/// design that fitted one message before still goes as one (<c>PartCount</c> 1), so older clients see no change.</item>
/// <item><b>The shell only</b> for a <see cref="SpaceStructure.ShellOnly"/> structure (a giant wreck): the cells a pilot can
/// see (<see cref="HullShell"/>), never the decks inside.</item>
/// <item><b>One cell at a time.</b> A block mined or placed on a station structure — from inside or on a spacewalk — reaches
/// the pilots as <see cref="StructureBlockChanged"/> with its dye, glow and shape, plus the cells it laid open; the whole
/// design only goes again when the change moved the structure's cell box (the flight view centres the hull on it), when
/// nobody knows what changed, or when that many cells changed that one design is cheaper.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The most JSON bytes one design message's cell arrays may take (the browser's 1 MB envelope cap,
    /// <c>NetCodec.MaxJsonPayloadBytes</c>, minus room for the header, doors and envelope — "well under 1 MB").</summary>
    internal const int DesignPartJsonBudget = 600_000;

    /// <summary>The fewest changed cells that may still go one by one, whatever the structure's size.</summary>
    private const int SingleCellChangeFloor = 64;

    /// <summary>Numbers the designs sent (one per design, every part of it the same): a later design of a structure
    /// always carries a larger serial, so the client drops the parts of one it already replaced.</summary>
    private int _designSerial;

    private HashSet<ushort>? _hullSeeThrough;

    /// <summary>Cells of a station structure that changed since its pilots last heard (#3612), and whether the cell box
    /// moved with them — kept by <see cref="MirrorStationCell"/>, sent by <see cref="PublishStationStructureChanges"/>.</summary>
    private sealed class PendingStationCells
    {
        public HashSet<Vector3i> Cells { get; } = new();

        public bool BoxChanged { get; set; }
    }

    private readonly Dictionary<string, PendingStationCells> _pendingStationCells = new(System.StringComparer.Ordinal);

    // ---------------------------------------------------------------- the design in parts

    /// <summary>The design messages of <paramref name="s"/>: one when it fits <see cref="DesignPartJsonBudget"/>, else its
    /// cells cut into parts of one serial (part 0 also carries the doors and the hatch).</summary>
    private List<SpaceShipDesign> ShipDesignParts(SpaceStructure s, string? kindOverride)
    {
        var cells = DesignCells(s);

        // Modifier arrays are emitted only when the design actually carries dye/glow/shape (authored ships),
        // so plain hulls stay as compact as before.
        bool anyMods = s.Mods.Count > 0 || s.Shapes.Count > 0;
        var cuts = DesignPartCuts(s, cells, anyMods);
        int serial = ++_designSerial;
        var parts = new List<SpaceShipDesign>(cuts.Count);
        int start = 0;
        for (int p = 0; p < cuts.Count; p++)
        {
            var part = DesignHeader(s, kindOverride, withDoors: p == 0);
            FillDesignCells(part, s, cells, start, cuts[p], anyMods);
            part.Serial = serial;
            part.PartIndex = p;
            part.PartCount = cuts.Count;
            parts.Add(part);
            start = cuts[p];
        }

        return parts;
    }

    /// <summary>The cells a design carries: every cell, or the visible shell of a <see cref="SpaceStructure.ShellOnly"/> one.</summary>
    private List<KeyValuePair<Vector3i, BlockId>> DesignCells(SpaceStructure s)
    {
        var shell = s.ShellOnly ? EnsureShell(s) : null;
        var cells = new List<KeyValuePair<Vector3i, BlockId>>(shell is null ? s.Cells.Count : 0);
        foreach (var kv in s.Cells)
        {
            if (shell is null || shell.IsVisible(kv.Key))
            {
                cells.Add(kv);
            }
        }

        return cells;
    }

    /// <summary>The end index of every part: a part closes before the cell that would take its arrays past the budget.</summary>
    private static List<int> DesignPartCuts(SpaceStructure s, List<KeyValuePair<Vector3i, BlockId>> cells, bool anyMods)
    {
        var cuts = new List<int>();
        long used = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            int bytes = CellJsonBytes(s, cells[i], anyMods);
            if (used > 0 && used + bytes > DesignPartJsonBudget)
            {
                cuts.Add(i);
                used = 0;
            }

            used += bytes;
        }

        cuts.Add(cells.Count);
        return cuts;
    }

    /// <summary>What one cell adds to the JSON of a design: its numbers and their separating commas.</summary>
    private static int CellJsonBytes(SpaceStructure s, KeyValuePair<Vector3i, BlockId> kv, bool anyMods)
    {
        int n = JsonDigits(kv.Key.X) + JsonDigits(kv.Key.Y) + JsonDigits(kv.Key.Z) + JsonDigits(kv.Value.Value) + 4;
        if (anyMods)
        {
            var (tint, glow) = s.Mods.TryGetValue(kv.Key, out var m) ? m : (0, 0);
            int shape = s.Shapes.TryGetValue(kv.Key, out int sh) ? sh : 0;
            n += JsonDigits(tint) + JsonDigits(glow) + JsonDigits(shape) + 3;
        }

        return n;
    }

    /// <summary>The length of a number's decimal text, its sign included.</summary>
    internal static int JsonDigits(long v)
    {
        int n = v < 0 ? 2 : 1;
        for (long u = System.Math.Abs(v); u >= 10; u /= 10)
        {
            n++;
        }

        return n;
    }

    private SpaceShipDesign DesignHeader(SpaceStructure s, string? kindOverride, bool withDoors)
    {
        var design = new SpaceShipDesign
        {
            HatchKnown = s.Kind == "ship", // #2587: an empty list from this server means "no hatch", not "guess"
            OwnerId = s.OwnerId,
            Id = s.Id,
            Kind = kindOverride ?? s.Kind,
            PosX = s.Position.X,
            PosY = s.Position.Y,
            PosZ = s.Position.Z,
            Width = s.Width,
            Height = s.Height,
            Length = s.Length,
            HasDock = s.HasDock,
            DockX = s.DockX,
            DockY = s.DockY,
            DockZ = s.DockZ,
            DockOutX = s.DockOutX,
            DockOutZ = s.DockOutZ,
        };

        if (withDoors)
        {
            // #2119: the doors the owner built, drawn closed by the flight view (their cells are air) — #2607: a player
            // station's doors too.
            var doors = s.PlacedDoorAxes.OrderBy(d => d.Key.X).ThenBy(d => d.Key.Y).ThenBy(d => d.Key.Z).ToList();
            design.DoorX = doors.Select(d => d.Key.X).ToArray();
            design.DoorY = doors.Select(d => d.Key.Y).ToArray();
            design.DoorZ = doors.Select(d => d.Key.Z).ToArray();
            design.DoorKind = doors.Select(d => s.DoorKinds.TryGetValue(d.Key, out var k) ? k : DoorBlocks.Energy).ToArray();
            design.DoorAxisX = doors.Select(d => d.Value).ToArray();

            // #2587: the design's own doorways to space (the rear hatch), so the flight view caps exactly those.
            var hatch = ShipHatchCells(s);
            design.HatchX = hatch.Select(c => c.X).ToArray();
            design.HatchY = hatch.Select(c => c.Y).ToArray();
            design.HatchZ = hatch.Select(c => c.Z).ToArray();
        }

        return design;
    }

    private static void FillDesignCells(SpaceShipDesign d, SpaceStructure s, List<KeyValuePair<Vector3i, BlockId>> cells,
        int from, int to, bool anyMods)
    {
        int n = to - from;
        d.X = new int[n];
        d.Y = new int[n];
        d.Z = new int[n];
        d.Block = new ushort[n];
        d.Tint = anyMods ? new int[n] : System.Array.Empty<int>();
        d.Glow = anyMods ? new int[n] : System.Array.Empty<int>();
        d.Shape = anyMods ? new int[n] : System.Array.Empty<int>();
        for (int i = 0; i < n; i++)
        {
            var kv = cells[from + i];
            d.X[i] = kv.Key.X;
            d.Y[i] = kv.Key.Y;
            d.Z[i] = kv.Key.Z;
            d.Block[i] = kv.Value.Value;
            if (anyMods)
            {
                if (s.Mods.TryGetValue(kv.Key, out var m))
                {
                    d.Tint[i] = m.Tint;
                    d.Glow[i] = m.Glow;
                }

                if (s.Shapes.TryGetValue(kv.Key, out int sh))
                {
                    d.Shape[i] = sh;
                }
            }
        }
    }

    /// <summary>The visible shell of a <see cref="SpaceStructure.ShellOnly"/> structure, built on first need (one pass over
    /// its box) and from then on kept by every cell write.</summary>
    private HullShell EnsureShell(SpaceStructure s)
    {
        if (s.Shell is { } shell)
        {
            return shell;
        }

        var (min, max) = s.CellBox();
        _hullSeeThrough ??= StationHull.SeeThroughIds(_content);
        s.Shell = HullShell.Build(min, max,
            s.Cells.Select(kv => new KeyValuePair<Vector3i, ushort>(kv.Key, kv.Value.Value)),
            p => s.Get(p).Value,
            p => s.Shapes.TryGetValue(p, out int v) ? v : 0,
            _hullSeeThrough);
        s.LaidOpen.Clear(); // the design built from the shell carries them all
        s.LaidOpenOverflow = false;
        return s.Shell;
    }

    // ---------------------------------------------------------------- one cell at a time

    /// <summary>Notes a station cell the interior just changed (<see cref="MirrorStationCell"/>), for the next publish.</summary>
    private void NotePendingStationCell(string stationId, Vector3i cell, bool boxChanged)
    {
        if (!_pendingStationCells.TryGetValue(stationId, out var pending))
        {
            pending = _pendingStationCells[stationId] = new PendingStationCells();
        }

        pending.Cells.Add(cell);
        pending.BoxChanged |= boxChanged;
    }

    /// <summary>Tells the pilots of every orbit that holds <paramref name="s"/> what changed on it since the last publish:
    /// the changed (and laid-open) cells one by one, or — the box moved, nothing was noted, too many cells — the design.</summary>
    private void PublishStationStructureChanges(SpaceStructure s)
    {
        _pendingStationCells.Remove(s.Id, out var pending);
        var changes = pending is null || pending.BoxChanged ? null : StructureCellChanges(s, pending.Cells);
        SendStructureChanges(PilotsHolding(s), s, changes);
        if (changes is null)
        {
            s.LaidOpen.Clear(); // the design carries them
            s.LaidOpenOverflow = false;
        }
    }

    /// <summary>After a spacewalk edit (its own cell already broadcast): the cells it laid open, to every orbit holding the
    /// structure. No-op when it laid nothing open.</summary>
    private void PublishLaidOpenCells(SpaceStructure s)
    {
        if (s.LaidOpen.Count == 0 && !s.LaidOpenOverflow)
        {
            return;
        }

        var changes = StructureCellChanges(s, System.Array.Empty<Vector3i>());
        SendStructureChanges(PilotsHolding(s), s, changes);
        s.LaidOpen.Clear();
        s.LaidOpenOverflow = false;
    }

    /// <summary>The per-cell messages for <paramref name="changed"/> (only those a pilot holds or sees, for a shell) and the
    /// laid-open cells — or null when the whole design should go instead.</summary>
    private List<StructureBlockChanged>? StructureCellChanges(SpaceStructure s, IEnumerable<Vector3i> changed)
    {
        if (s.LaidOpenOverflow || (s.ShellOnly && s.Shell is null))
        {
            return null;
        }

        var cells = new HashSet<Vector3i>();
        foreach (var c in changed)
        {
            if (s.Shell is null || s.Shell.IsVisible(c))
            {
                cells.Add(c);
            }
        }

        cells.UnionWith(s.LaidOpen);
        s.LaidOpen.Clear();
        int limit = System.Math.Max(SingleCellChangeFloor, System.Math.Min(SpaceStructure.LaidOpenLimit, s.Cells.Count / 4));
        if (cells.Count > limit)
        {
            return null;
        }

        return cells.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z).Select(c => StructureCellChange(s, c)).ToList();
    }

    /// <summary>The changes — or, with none, the whole design — to <paramref name="pilots"/>: every message built and encoded
    /// ONCE for all of them (a giant's design is a few hundred thousand cells; ten pilots used to cost ten builds).</summary>
    private void SendStructureChanges(List<PlayerSession> pilots, SpaceStructure s, List<StructureBlockChanged>? changes)
    {
        if (changes is null)
        {
            SendShipDesignTo(pilots, s);
            return;
        }

        foreach (var change in changes)
        {
            SendToSessions(pilots, change);
        }
    }

    /// <summary>One design to many pilots (#3612): its parts are built once and each part is encoded once.</summary>
    private void SendShipDesignTo(List<PlayerSession> pilots, SpaceStructure s)
    {
        if (pilots.Count == 0)
        {
            return;
        }

        foreach (var part in ShipDesignParts(s, null))
        {
            SendToSessions(pilots, part);
        }
    }

    /// <summary>One message to many sessions, encoded once (the loopback hands the object itself to each, as everywhere).</summary>
    private void SendToSessions(List<PlayerSession> sessions, object message)
    {
        if (_objectTransport != null)
        {
            foreach (var session in sessions)
            {
                _objectTransport.SendMessage(session.ConnectionId, message, DeliveryMode.ReliableOrdered);
            }

            return;
        }

        byte[] payload = NetCodec.Encode(message);
        foreach (var session in sessions)
        {
            _transport.Send(session.ConnectionId, payload, DeliveryMode.ReliableOrdered);
        }
    }

    /// <summary>The joined pilots of every orbit that holds <paramref name="s"/> (a derelict floats in all orbits of its
    /// system), each once.</summary>
    private List<PlayerSession> PilotsHolding(SpaceStructure s)
    {
        var pilots = new List<PlayerSession>();
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var instance in _spaceInstances.Values)
        {
            if (instance.Structures.ContainsKey(s.Id))
            {
                AddPilots(instance, pilots, seen);
            }
        }

        return pilots;
    }

    private void AddPilots(SpaceInstance instance, List<PlayerSession> pilots, HashSet<string> seen)
    {
        foreach (var pid in instance.Players)
        {
            if (seen.Add(pid) && FindSessionByPlayerId(pid) is { } session)
            {
                pilots.Add(session);
            }
        }
    }

    /// <summary>One cell as the pilots get it: block, dye, glow and shape (all 0 for air).</summary>
    private static StructureBlockChanged StructureCellChange(SpaceStructure s, Vector3i c)
    {
        var block = s.Get(c);
        var (tint, glow) = !block.IsAir && s.Mods.TryGetValue(c, out var m) ? m : (0, 0);
        int shape = !block.IsAir && s.Shapes.TryGetValue(c, out int sh) ? sh : 0;
        return new StructureBlockChanged { StructureId = s.Id, X = c.X, Y = c.Y, Z = c.Z, Block = block.Value, Tint = tint, Glow = glow, Shape = shape };
    }

    // ---------------------------------------------------------------- the edit range

    /// <summary>#3612: whether a spacewalk edit on a static structure is within <paramref name="range"/> of the suit. It is
    /// measured to the EDITED CELL where the flight view draws it (the structure floats with its cell box centred on its
    /// position): a giant hull is worked along its whole length, and from its middle its far end is out of reach like any
    /// far-off body. A structure whose whole box lies within the range of its centre (half its box diagonal ≤ the range — an
    /// asteroid, a debris piece, an ordinary player station) keeps the centre measure it always had: every one of its cells is
    /// near the centre anyway, and a builder extending it from beside the hull is never refused for the new cell's offset.</summary>
    private static bool InStructureEditRange(SpaceStructure s, Vector3f suit, Vector3i cell, float range)
    {
        var (min, max) = s.CellBox();
        float w = max.X - min.X + 1, h = max.Y - min.Y + 1, l = max.Z - min.Z + 1;
        float ex = suit.X - s.Position.X, ey = suit.Y - s.Position.Y, ez = suit.Z - s.Position.Z;
        if ((w * w + h * h + l * l) * 0.25f <= range * range && ex * ex + ey * ey + ez * ez <= range * range)
        {
            return true; // a small structure: measured to its centre, as before #3612
        }

        float dx = suit.X - (s.Position.X + cell.X + 0.5f - (min.X + max.X + 1) * 0.5f);
        float dy = suit.Y - (s.Position.Y + cell.Y + 0.5f - (min.Y + max.Y + 1) * 0.5f);
        float dz = suit.Z - (s.Position.Z + cell.Z + 0.5f - (min.Z + max.Z + 1) * 0.5f);
        return dx * dx + dy * dy + dz * dz <= range * range;
    }

    // ---------------------------------------------------------------- test seams

    /// <summary>Test seam (#3612): the design messages a structure of the server would go out as right now.</summary>
    public IReadOnlyList<SpaceShipDesign> ShipDesignPartsForTest(SpaceStructure s) => ShipDesignParts(s, null);
}
