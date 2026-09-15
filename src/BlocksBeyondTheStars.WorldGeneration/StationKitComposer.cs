// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>One module of a composed station: its key, its origin inside the station and its quarter turns.</summary>
public sealed class PlacedKitModule
{
    public string Key = string.Empty;
    public int X, Y, Z, Turns;

    public PlacedKitModule()
    {
    }

    public PlacedKitModule(string key, int x, int y, int z, int turns)
    {
        Key = key;
        X = x;
        Y = y;
        Z = z;
        Turns = turns;
    }
}

/// <summary>What a station was composed from (#1874) — pinned per station so a replay bakes the same station
/// whatever happens to the kit or the pool afterwards.</summary>
public sealed class StationComposition
{
    public string KitKey = string.Empty;
    public long Seed;
    public readonly List<PlacedKitModule> Modules = new();
}

/// <summary>
/// Composes a station from a kit (#1874): modules dock port to port. The start module sits at the origin; every
/// further module — the required ones first (their minimum copies, in kit order), then random draws by weight up
/// to the target count — tries every open port of the station against every port of its own, in every allowed
/// rotation, in a hash-shuffled order, and takes the first fit: compatible ports (same tag, same rectangle,
/// opposite faces), walls adjacent, no bounding-box overlap, inside the extent. A required module that never
/// fits restarts the attempt with the next hash lane (<see cref="Attempts"/> tries), then the composer reports
/// failure — the caller falls back to the procedural generator, so a broken kit never breaks a world.
/// <para>Baking: every joint's two port walls become air (a two-deep doorway) with a door marker per the port's
/// door option; a vertical joint becomes a shaft with a ladder column; ports that happen to coincide after
/// placement are joined too; unmatched ports stay walls. Rooms under <c>room</c> markers are furnished by the
/// module's function; a canteen's or bar's rooms also get a <c>lounge</c> marker (the crew's evening seats). Every
/// door keeps its lane clear and ends the room it opens (<see cref="RoomFurnisher.DoorLaneAt"/>, #1901).
/// Every joint is found from geometry alone, so <see cref="Replay"/> of a pinned composition bakes byte for byte
/// what <see cref="Compose"/> built.</para>
/// </summary>
public static class StationKitComposer
{
    public const int DefaultMaxExtent = 128;
    public const int Attempts = 8;

    /// <summary>The most floor cells one <c>room</c> marker flood-fills.</summary>
    private const int RoomCap = 256;

    private sealed class Placed
    {
        public string Key = string.Empty;
        public StructureTemplate Template = null!;
        public int Turns;
        public Vector3i Origin;
        public List<PortRect> Ports = new();
        public readonly HashSet<int> Used = new();

        public string Function => Template.FunctionOrRole;

        public Vector3i Max => new(Origin.X + Template.Width - 1, Origin.Y + Template.Height - 1, Origin.Z + Template.Length - 1);
    }

    private sealed class Joint
    {
        public Placed A = null!;
        public int PortA;
        public Placed B = null!;
        public int PortB;
    }

    private sealed class ModuleCache
    {
        private readonly Func<string, StructureTemplate?> _byKey;
        private readonly Dictionary<(string Key, int Turns), (StructureTemplate T, List<PortRect> Ports)?> _cache = new();

        public ModuleCache(Func<string, StructureTemplate?> byKey) => _byKey = byKey;

        public (StructureTemplate T, List<PortRect> Ports)? Get(string key, int turns)
        {
            turns = ((turns % 4) + 4) % 4;
            if (_cache.TryGetValue((key, turns), out var hit))
            {
                return hit;
            }

            var t = _byKey(key);
            (StructureTemplate, List<PortRect>)? result = null;
            if (t != null && t.Width > 0 && t.Height > 0 && t.Length > 0)
            {
                var rotated = TemplateTransform.RotateY(t, turns);
                result = (rotated, StructurePorts.Collect(rotated));
            }

            _cache[(key, turns)] = result;
            return result;
        }
    }

    /// <summary>Composes a station; null (with <paramref name="failure"/> set) when the kit cannot be assembled.</summary>
    public static StationStructure? Compose(StructureKit kit, Func<string, StructureTemplate?> moduleByKey, long seed, GameContent content,
        out StationComposition composition, out string? failure)
    {
        composition = new StationComposition { KitKey = kit.Key, Seed = seed };
        failure = null;
        if (kit.Entries.Count == 0)
        {
            failure = $"kit '{kit.Key}' has no entries";
            return null;
        }

        var cache = new ModuleCache(moduleByKey);
        var (min, max) = kit.EffectiveBounds();
        int maxExtent = kit.MaxExtent > 0 ? kit.MaxExtent : DefaultMaxExtent;
        string startKey = kit.Start.Length > 0 ? kit.Start : FirstRequiredKey(kit);
        var start = cache.Get(startKey, 0);
        if (start is null)
        {
            failure = $"kit '{kit.Key}': start module '{startKey}' is missing";
            return null;
        }

        string lastFailure = string.Empty;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            var rng = new Random(unchecked((int)WorldGenerator.StableHash($"kit:{kit.Key}:{seed}:{attempt}")));
            var placed = new List<Placed>
            {
                new Placed { Key = startKey, Template = start.Value.T, Turns = 0, Origin = new Vector3i(0, 0, 0), Ports = start.Value.Ports },
            };
            var counts = new int[kit.Entries.Count];
            int startEntry = IndexOfEntry(kit, startKey);
            if (startEntry >= 0)
            {
                counts[startEntry]++;
            }

            int target = rng.Next(min, max + 1);
            bool ok = true;
            for (int e = 0; e < kit.Entries.Count && ok; e++)
            {
                var entry = kit.Entries[e];
                while (counts[e] < entry.MinOrRequired)
                {
                    if (TryPlace(entry, placed, rng, maxExtent, cache))
                    {
                        counts[e]++;
                    }
                    else
                    {
                        ok = false;
                        lastFailure = $"kit '{kit.Key}': required module '{entry.Module}' found no port to dock on (attempt {attempt + 1})";
                        break;
                    }
                }
            }

            if (!ok)
            {
                continue;
            }

            var blocked = new bool[kit.Entries.Count];
            while (placed.Count < target)
            {
                int total = 0;
                for (int e = 0; e < kit.Entries.Count; e++)
                {
                    if (!blocked[e] && counts[e] < Math.Max(kit.Entries[e].Max, kit.Entries[e].MinOrRequired))
                    {
                        total += Math.Max(1, kit.Entries[e].Weight);
                    }
                }

                if (total == 0)
                {
                    break; // every entry is at its maximum (or cannot dock): the station is as big as it gets
                }

                int roll = rng.Next(total);
                int pick = -1;
                for (int e = 0; e < kit.Entries.Count; e++)
                {
                    if (blocked[e] || counts[e] >= Math.Max(kit.Entries[e].Max, kit.Entries[e].MinOrRequired))
                    {
                        continue;
                    }

                    roll -= Math.Max(1, kit.Entries[e].Weight);
                    if (roll < 0)
                    {
                        pick = e;
                        break;
                    }
                }

                if (pick < 0)
                {
                    break;
                }

                if (TryPlace(kit.Entries[pick], placed, rng, maxExtent, cache))
                {
                    counts[pick]++;
                }
                else
                {
                    blocked[pick] = true; // this module docks nowhere any more — draw the others
                }
            }

            return Bake(kit.Key, kit.Tier, seed, placed, content, composition);
        }

        failure = lastFailure;
        return null;
    }

    /// <summary>Bakes a pinned composition again — the same modules at the same places, the joints found from
    /// geometry, the same furniture — or null when a pinned module no longer exists.</summary>
    public static StationStructure? Replay(StationComposition composition, Func<string, StructureTemplate?> moduleByKey, GameContent content,
        string tier, out string? failure)
    {
        failure = null;
        var cache = new ModuleCache(moduleByKey);
        var placed = new List<Placed>();
        foreach (var m in composition.Modules)
        {
            var res = cache.Get(m.Key, m.Turns);
            if (res is null)
            {
                failure = $"pinned station module '{m.Key}' is missing from the pool";
                return null;
            }

            placed.Add(new Placed { Key = m.Key, Template = res.Value.T, Turns = ((m.Turns % 4) + 4) % 4, Origin = new Vector3i(m.X, m.Y, m.Z), Ports = res.Value.Ports });
        }

        if (placed.Count == 0)
        {
            failure = "pinned station composition is empty";
            return null;
        }

        var again = new StationComposition { KitKey = composition.KitKey, Seed = composition.Seed };
        return Bake(composition.KitKey, tier, composition.Seed, placed, content, again);
    }

    private static string FirstRequiredKey(StructureKit kit)
    {
        foreach (var e in kit.Entries)
        {
            if (e.Required)
            {
                return e.Module;
            }
        }

        return kit.Entries[0].Module;
    }

    private static int IndexOfEntry(StructureKit kit, string key)
    {
        for (int i = 0; i < kit.Entries.Count; i++)
        {
            if (kit.Entries[i].Module == key)
            {
                return i;
            }
        }

        return -1;
    }

    private static Vector3i MinCell(List<Vector3i> cells)
    {
        int x = int.MaxValue, y = int.MaxValue, z = int.MaxValue;
        foreach (var c in cells)
        {
            x = Math.Min(x, c.X);
            y = Math.Min(y, c.Y);
            z = Math.Min(z, c.Z);
        }

        return new Vector3i(x, y, z);
    }

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static bool Overlaps(Vector3i origin, StructureTemplate t, List<Placed> placed)
    {
        var max = new Vector3i(origin.X + t.Width - 1, origin.Y + t.Height - 1, origin.Z + t.Length - 1);
        foreach (var p in placed)
        {
            var pMax = p.Max;
            bool apart = max.X < p.Origin.X || origin.X > pMax.X
                || max.Y < p.Origin.Y || origin.Y > pMax.Y
                || max.Z < p.Origin.Z || origin.Z > pMax.Z;
            if (!apart)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExceedsExtent(Vector3i origin, StructureTemplate t, List<Placed> placed, int maxExtent)
    {
        int minX = origin.X, minY = origin.Y, minZ = origin.Z;
        int maxX = origin.X + t.Width - 1, maxY = origin.Y + t.Height - 1, maxZ = origin.Z + t.Length - 1;
        foreach (var p in placed)
        {
            var pMax = p.Max;
            minX = Math.Min(minX, p.Origin.X); minY = Math.Min(minY, p.Origin.Y); minZ = Math.Min(minZ, p.Origin.Z);
            maxX = Math.Max(maxX, pMax.X); maxY = Math.Max(maxY, pMax.Y); maxZ = Math.Max(maxZ, pMax.Z);
        }

        return maxX - minX + 1 > maxExtent || maxY - minY + 1 > maxExtent || maxZ - minZ + 1 > maxExtent;
    }

    private static bool TryPlace(KitEntry entry, List<Placed> placed, Random rng, int maxExtent, ModuleCache cache)
    {
        var open = new List<(Placed P, int I)>();
        foreach (var p in placed)
        {
            for (int i = 0; i < p.Ports.Count; i++)
            {
                if (!p.Used.Contains(i))
                {
                    open.Add((p, i));
                }
            }
        }

        if (open.Count == 0)
        {
            return false;
        }

        Shuffle(open, rng);
        var turnsList = entry.Rotate ? new List<int> { 0, 1, 2, 3 } : new List<int> { 0 };
        Shuffle(turnsList, rng);

        foreach (var (a, ai) in open)
        {
            var pa = a.Ports[ai];
            foreach (int turns in turnsList)
            {
                var res = cache.Get(entry.Module, turns);
                if (res is null)
                {
                    return false; // the module is gone from the pool
                }

                var (t, ports) = res.Value;
                for (int qi = 0; qi < ports.Count; qi++)
                {
                    var q = ports[qi];
                    if (!pa.Compatible(q))
                    {
                        continue;
                    }

                    var origin = MinCell(pa.Cells) + a.Origin + pa.Outward - MinCell(q.Cells);
                    if (Overlaps(origin, t, placed) || ExceedsExtent(origin, t, placed, maxExtent))
                    {
                        continue;
                    }

                    var b = new Placed { Key = entry.Module, Template = t, Turns = turns, Origin = origin, Ports = ports };
                    a.Used.Add(ai);
                    b.Used.Add(qi);
                    placed.Add(b);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Every pair of compatible ports that coincide wall to wall, each port in at most one joint —
    /// found from geometry alone, so a replay agrees with the composition that placed the modules.</summary>
    private static List<Joint> JointsOf(List<Placed> placed)
    {
        var joints = new List<Joint>();
        var taken = new HashSet<(int Module, int Port)>();
        for (int i = 0; i < placed.Count; i++)
        {
            var a = placed[i];
            for (int ai = 0; ai < a.Ports.Count; ai++)
            {
                if (taken.Contains((i, ai)))
                {
                    continue;
                }

                var pa = a.Ports[ai];
                var want = MinCell(pa.Cells) + a.Origin + pa.Outward;
                bool joined = false;
                for (int j = 0; j < placed.Count && !joined; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    var b = placed[j];
                    for (int bi = 0; bi < b.Ports.Count; bi++)
                    {
                        if (taken.Contains((j, bi)))
                        {
                            continue;
                        }

                        var pb = b.Ports[bi];
                        if (!pa.Compatible(pb) || !(MinCell(pb.Cells) + b.Origin).Equals(want))
                        {
                            continue;
                        }

                        taken.Add((i, ai));
                        taken.Add((j, bi));
                        joints.Add(new Joint { A = i < j ? a : b, PortA = i < j ? ai : bi, B = i < j ? b : a, PortB = i < j ? bi : ai });
                        joined = true;
                        break;
                    }
                }
            }
        }

        return joints;
    }

    /// <summary>The furnisher role for a module function; a cabin that already holds a bed gets no second one.</summary>
    private static RoomFurnisher.RoomRole RoleFor(string function, bool hasBed) => function switch
    {
        StructureRoles.Cabins => hasBed ? RoomFurnisher.RoomRole.CabinNoBed : RoomFurnisher.RoomRole.Cabin,
        StructureRoles.Canteen or StructureRoles.Bar => RoomFurnisher.RoomRole.Lounge,
        StructureRoles.Storage => RoomFurnisher.RoomRole.Storage,
        StructureRoles.Medbay => RoomFurnisher.RoomRole.Medbay,
        StructureRoles.Mission => RoomFurnisher.RoomRole.Board,
        StructureRoles.Market => RoomFurnisher.RoomRole.Market,
        StructureRoles.Room => hasBed ? RoomFurnisher.RoomRole.CabinNoBed : RoomFurnisher.RoomRole.House,
        _ => RoomFurnisher.RoomRole.Hall,
    };

    private static StationStructure Bake(string kitKey, string tier, long seed, List<Placed> placed, GameContent content, StationComposition composition)
    {
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        foreach (var p in placed)
        {
            var pMax = p.Max;
            minX = Math.Min(minX, p.Origin.X); minY = Math.Min(minY, p.Origin.Y); minZ = Math.Min(minZ, p.Origin.Z);
            maxX = Math.Max(maxX, pMax.X); maxY = Math.Max(maxY, pMax.Y); maxZ = Math.Max(maxZ, pMax.Z);
        }

        int w = maxX - minX + 1, h = maxY - minY + 1, l = maxZ - minZ + 1;
        var shift = new Vector3i(-minX, -minY, -minZ);
        var blocks = new ushort[w * h * l];
        var mods = new Dictionary<int, (int Tint, int Glow)>();
        var shapes = new Dictionary<int, int>();
        var markers = new List<StationMarker>();
        var modules = new List<StationModule>();
        var markerCells = new HashSet<Vector3i>();

        ushort B(string key) => content.GetBlock(key)?.NumericId.Value ?? 0;
        int Idx(int x, int y, int z) => (x * h + y) * l + z;
        bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l;
        ushort Get(int x, int y, int z) => In(x, y, z) ? blocks[Idx(x, y, z)] : (ushort)0;

        void SetCell(int x, int y, int z, ushort block, int shape, int tint, int glow)
        {
            if (!In(x, y, z))
            {
                return;
            }

            int idx = Idx(x, y, z);
            blocks[idx] = block;
            if (block != 0 && (tint != 0 || glow != 0)) mods[idx] = (tint, glow); else mods.Remove(idx);
            if (block != 0 && shape != 0) shapes[idx] = shape; else shapes.Remove(idx);
        }

        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            var o = p.Origin + shift;
            foreach (var c in p.Template.Cells)
            {
                var pos = new Vector3i(o.X + c.X, o.Y + c.Y, o.Z + c.Z);
                if (c.Kind == "marker")
                {
                    markers.Add(new StationMarker(c.Id, pos));
                    markerCells.Add(pos);
                    if (c.Id == "room" && StructureRoles.IsLoungeFunction(p.Function))
                    {
                        markers.Add(new StationMarker("lounge", pos)); // the crew's evening seats are in here
                    }
                }
                else
                {
                    ushort id = B(c.Id);
                    if (id != 0)
                    {
                        SetCell(pos.X, pos.Y, pos.Z, id, c.Shape, c.Tint, c.Glow);
                    }
                }
            }

            modules.Add(new StationModule(p.Function, new Vector3i(i, 0, 0), o));
            composition.Modules.Add(new PlacedKitModule(p.Key, o.X, o.Y, o.Z, p.Turns));
        }

        // Joints: both port walls open; a door marker (or a ladder column for a vertical joint).
        ushort ladder = B("ladder");
        var opened = new HashSet<Vector3i>();
        foreach (var j in JointsOf(placed))
        {
            var pa = j.A.Ports[j.PortA];
            var pb = j.B.Ports[j.PortB];
            foreach (var c in pa.Cells)
            {
                var n = c + j.A.Origin + shift;
                SetCell(n.X, n.Y, n.Z, 0, 0, 0, 0);
                opened.Add(n);
            }

            foreach (var c in pb.Cells)
            {
                var n = c + j.B.Origin + shift;
                SetCell(n.X, n.Y, n.Z, 0, 0, 0, 0);
                opened.Add(n);
            }

            bool vertical = pa.Face == PortFace.PlusY || pa.Face == PortFace.MinusY;
            if (vertical)
            {
                var lower = pa.Face == PortFace.PlusY ? j.A : j.B;
                var upper = pa.Face == PortFace.PlusY ? j.B : j.A;
                var corner = MinCell(pa.Cells) + j.A.Origin + shift;
                int from = lower.Origin.Y + shift.Y + 1, to = upper.Origin.Y + shift.Y;
                for (int y = from; y <= to && ladder != 0; y++)
                {
                    SetCell(corner.X, y, corner.Z, ladder, 0, 0, 0); // the climb from the lower floor onto the upper one
                }
            }
            else if (pa.Door != StructurePorts.DoorOpen)
            {
                Vector3i best = pa.Cells[0];
                foreach (var c in pa.Cells)
                {
                    if (c.Y < best.Y || (c.Y == best.Y && (c.X < best.X || (c.X == best.X && c.Z < best.Z))))
                    {
                        best = c;
                    }
                }

                markers.Add(new StationMarker("door_" + pa.Door, best + j.A.Origin + shift));
            }
        }

        // Arrival: the start module's own spawn marker, else its floor centre.
        var startModule = placed[0];
        if (!markers.Exists(m => m.Type == "spawn"))
        {
            var so = startModule.Origin + shift;
            markers.Add(new StationMarker("spawn", new Vector3i(so.X + startModule.Template.Width / 2, so.Y + 1, so.Z + startModule.Template.Length / 2)));
        }

        // Door lanes (#1901): every door — a joint's and a module's own (a cabin's) — keeps the lane through it clear, and
        // a room ends at its doorway. Measured once on the bare modules, before anything is furnished.
        var lanes = new List<RoomFurnisher.DoorLane>();
        var doorGaps = new HashSet<Vector3i>();
        var laneCells = new HashSet<Vector3i>();
        foreach (var m in markers)
        {
            if (RoomFurnisher.IsDoorMarker(m.Type) && RoomFurnisher.DoorLaneAt(Get, w, h, l, m.LocalPos.X, m.LocalPos.Y, m.LocalPos.Z) is { } lane)
            {
                lanes.Add(lane);
                foreach (var (x, z) in lane.Gap)
                {
                    doorGaps.Add(new Vector3i(x, lane.FootY, z));
                }

                foreach (var (x, z) in lane.Keep)
                {
                    laneCells.Add(new Vector3i(x, lane.FootY, z));
                }
            }
        }

        // The essentials every station has (like FromTemplate): a vendor and a mission board, in the start module — never
        // in a doorway.
        foreach (var essential in new[] { "vendor", "mission_board" })
        {
            if (!markers.Exists(m => m.Type == essential) && FreeFloorCell(startModule, shift, Get, markerCells, laneCells) is { } spot)
            {
                markers.Add(new StationMarker(essential, spot));
                markerCells.Add(spot);
            }
        }

        // Furnishing (#1828 style): every room marker's floor, by the module's function; doorways stay clear.
        var palette = RoomFurnisher.PaletteFor(RoomFurnisher.Style.Station, content);
        palette.CeilingLit = true; // kit modules light their rooms from the ceiling
        ushort bed = B("bed");
        var reservedLanes = new HashSet<(int X, int Z)>();
        foreach (var c in opened)
        {
            reservedLanes.Add((c.X, c.Z)); // around every opened cell — the ladder shafts of vertical joints too
            reservedLanes.Add((c.X + 1, c.Z));
            reservedLanes.Add((c.X - 1, c.Z));
            reservedLanes.Add((c.X, c.Z + 1));
            reservedLanes.Add((c.X, c.Z - 1));
        }

        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            var o = p.Origin + shift;
            int n = 0;
            foreach (var c in p.Template.Cells)
            {
                if (c.Kind != "marker" || (c.Id != "room" && c.Id != "cabin"))
                {
                    continue; // a cabin marker is a room of its own: one resident's quarters
                }

                n++;
                var pos = new Vector3i(o.X + c.X, o.Y + c.Y, o.Z + c.Z);
                // The flood fill must not walk through an opened joint into the next module, nor through a doorway into
                // the next room (#1901) — every room is furnished on its own (a hub's hall and a cabin, or two cabins off
                // one corridor, are not one region).
                ushort GetSealed(int x, int y, int z)
                {
                    var cell = new Vector3i(x, y, z);
                    return opened.Contains(cell) || doorGaps.Contains(cell) ? (ushort)1 : Get(x, y, z);
                }

                var region = RoomFurnisher.FloodRoom(GetSealed, w, h, l, pos.X, pos.Y, pos.Z, RoomCap, out int clearance);
                if (region.Count == 0)
                {
                    continue;
                }

                var reserved = new HashSet<(int X, int Z)>(reservedLanes);
                foreach (var lane in lanes)
                {
                    if (lane.FootY == pos.Y)
                    {
                        reserved.UnionWith(lane.Keep);
                    }
                }

                foreach (var m in markerCells)
                {
                    if (m.Y == pos.Y)
                    {
                        reserved.Add((m.X, m.Z));
                    }
                }

                // An authored bed stands ON the floor (a block, so not a region cell): look over the room's box.
                bool hasBed = false;
                if (bed != 0)
                {
                    int rx0 = int.MaxValue, rx1 = int.MinValue, rz0 = int.MaxValue, rz1 = int.MinValue;
                    foreach (var (x, z) in region)
                    {
                        rx0 = Math.Min(rx0, x); rx1 = Math.Max(rx1, x);
                        rz0 = Math.Min(rz0, z); rz1 = Math.Max(rz1, z);
                    }

                    for (int x = rx0 - 1; x <= rx1 + 1 && !hasBed; x++)
                        for (int z = rz0 - 1; z <= rz1 + 1 && !hasBed; z++)
                        {
                            hasBed = Get(x, pos.Y, z) == bed;
                        }
                }

                var rng = new Random(unchecked((int)WorldGenerator.StableHash($"kitfurnish:{kitKey}:{seed}:{i}:{n}")));
                RoomFurnisher.Furnish(SetCell, region, pos.Y, clearance, palette, RoleFor(p.Function, hasBed), reserved, rng);
            }
        }

        return new StationStructure(w, h, l, tier, startModule.Template.Width, startModule.Template.Height, startModule.Template.Length,
            blocks, markers, modules, mods, shapes);
    }

    /// <summary>The first free floor cell of a module (air over a block, no marker there, not in a door lane), scanning its
    /// interior.</summary>
    private static Vector3i? FreeFloorCell(Placed p, Vector3i shift, Func<int, int, int, ushort> get, HashSet<Vector3i> markerCells, HashSet<Vector3i> laneCells)
    {
        var o = p.Origin + shift;
        for (int y = 1; y < p.Template.Height - 1; y++)
            for (int x = 1; x < p.Template.Width - 1; x++)
                for (int z = 1; z < p.Template.Length - 1; z++)
                {
                    var c = new Vector3i(o.X + x, o.Y + y, o.Z + z);
                    if (get(c.X, c.Y, c.Z) == 0 && get(c.X, c.Y - 1, c.Z) != 0 && get(c.X, c.Y + 1, c.Z) == 0 && !markerCells.Contains(c) && !laneCells.Contains(c))
                    {
                        return c;
                    }
                }

        return null;
    }
}
