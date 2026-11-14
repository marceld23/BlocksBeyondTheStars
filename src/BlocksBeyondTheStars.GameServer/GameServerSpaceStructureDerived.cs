// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// DERIVED station structures (#3613): a station structure whose cells are a pure function of a template and a seed — a
/// giant wreck drifting in space. It rides the player-station machinery like every derelict (<see cref="_playerStationCells"/>,
/// a <see cref="BoardableStation"/>, the interior world <c>station:&lt;id&gt;</c>), but it is far too big for a cell row:
/// <list type="bullet">
/// <item><b>Storage = the players' changes only</b> (GameServerSpaceStructureDerivedStore.cs): the cells that differ from the
/// base build are ONE compact blob — a <see cref="StructureOverlay"/> in the <c>structure_overlay</c> table under location
/// <see cref="DerivedDeltaLocation"/>, key <c>delta:&lt;id&gt;</c> — written COALESCED (at most once a second, and on every
/// save and the clean stop), never a <see cref="StoredSpaceStructure"/> row. A small record beside it (the named blob
/// <c>derived-station:&lt;id&gt;</c>) keeps what the interior mapping depends on — the stamp origin and anchor — and the
/// base hash the changes were made on; once stored, the anchor is never recomputed from a rebuilt base.</item>
/// <item><b>The interior is one structure overlay.</b> The first boarding stores the build as it stands as ONE overlay of the
/// interior world (key <c>derived:&lt;id&gt;</c>, location <c>station:&lt;id&gt;</c>) — applied to every chunk as it loads,
/// under the block edits — instead of a block edit per cell; later boardings stamp nothing and top nothing up (every change
/// since reached the interior as an ordinary edit: aboard as the player's own, from a spacewalk through
/// <see cref="MirrorStationCellOutside"/>). The interior counts as materialised exactly when its overlay is stored.</item>
/// <item><b>Restart</b> = the base build (the caller regenerates it from template + seed) + the delta blob, mapped into the
/// interior through the STORED anchor; the interior world = its overlay + its block edits.</item>
/// <item><b>Arrival</b> is searched around the entrance hint the caller names (a bounded box, widening twice), never over
/// the whole volume — only a hull with no standable deck near its entrance falls back to the full search.</item>
/// </list>
/// The flight view gets the shell only (<see cref="SpaceStructure.ShellOnly"/>) and single-cell updates; see
/// GameServerSpaceStructureDesign.cs.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The reaches (blocks, each way) the arrival search around a derived station's entrance tries, in order.</summary>
    private static readonly int[] DerivedArrivalReach = { 8, 24 };

    /// <summary>A registered derived station: its base build (compact, for "is this cell a change?"), the entrance hint, the
    /// players' changes and what the save holds of them.</summary>
    private sealed class DerivedStation
    {
        public Dictionary<ChunkCoord, StructureOverlayChunk> Base { get; init; } = new();

        /// <summary>The build cell the arrival search starts from (a deck cell just inside the way in).</summary>
        public Vector3i Entrance { get; init; }

        /// <summary>The caller's hash of the base build it registered (empty = none given).</summary>
        public string BaseHash { get; init; } = string.Empty;

        /// <summary>The base hash the stored record names (the build the stored changes were made on); null = no record.</summary>
        public string? StoredBaseHash { get; set; }

        /// <summary>Every cell that differs from the base: its block (0 = mined out), dye, glow and shape.</summary>
        public Dictionary<Vector3i, (ushort Block, int Tint, int Glow, int Shape)> Delta { get; } = new();

        public bool DeltaStored { get; set; }

        public bool DeltaDirty { get; set; }

        /// <summary>The door set the save holds (<see cref="DoorSignatureOf"/>) — the records are rewritten only when it changes.</summary>
        public string DoorSignature { get; set; } = string.Empty;

        /// <summary>How often the delta blob was written this run (the coalescing test reads it).</summary>
        public int DeltaWrites { get; set; }
    }

    private readonly Dictionary<string, DerivedStation> _derivedStations = new(System.StringComparer.Ordinal);

    /// <summary>True for the interior world of a registered derived station.</summary>
    private bool IsDerivedStationWorld(string locationId)
        => locationId.StartsWith(StationLocationIdPrefix, System.StringComparison.Ordinal)
           && _derivedStations.ContainsKey(locationId.Substring(StationLocationIdPrefix.Length));

    /// <summary>
    /// Registers a DERIVED station structure (#3613) — the call a giant wreck's builder makes once per server run, when the
    /// wreck is first needed. <paramref name="baseBuild"/> is the intact-and-ruined build as the template and the seed make it
    /// (cells, dyes, shapes, the door records, <see cref="SpaceStructure.Name"/> and the flight <see cref="SpaceStructure.Position"/>
    /// set by the caller); this method takes it over as THE structure: it becomes a boardable <c>station</c>,
    /// <see cref="SpaceStructure.Derived"/> and <see cref="SpaceStructure.ShellOnly"/>, the players' stored changes are laid over
    /// it (the delta blob; the stored door records replace the base's once anything was stored), and it is registered in
    /// <see cref="_playerStationCells"/> and <see cref="_stationsById"/> — materialised when its interior overlay is stored, its
    /// interior anchored where the stored record says (else at <see cref="StationStampOrigin"/> and the base build's minimum,
    /// recorded with the first stored change or boarding). <paramref name="entranceHint"/> is a build cell on a deck just inside
    /// the way in (the template's <c>entrance</c> marker): the arrival search starts there. <paramref name="baseHash"/> (optional)
    /// names the base build; it is stored with the first change, and <see cref="DerivedStoredBaseHash"/> tells a later run which
    /// build the stored changes belong to. The caller puts the structure into the orbits that show it
    /// (<c>instance.Structures</c>) and adds its contact, as for any derelict.
    /// </summary>
    private BoardableStation RegisterDerivedStation(string id, SpaceStructure baseBuild, Vector3i entranceHint, string sizeTier = "large",
        string baseHash = "")
    {
        if (_derivedDirty.Contains(id))
        {
            FlushDerivedStations(); // registered again in the same run: what the old registration still holds goes first
        }

        var s = baseBuild;
        s.Id = id;
        s.Kind = "station";
        s.Boardable = true;
        s.Derived = true;
        s.ShellOnly = true;
        s.Shell = null;
        s.Touched = null;
        var (baseMin, _) = s.CellBox();
        var record = LoadDerivedRecord(id);
        var d = new DerivedStation
        {
            Base = SnapshotDerivedBase(s),
            Entrance = entranceHint,
            BaseHash = baseHash ?? string.Empty,
            StoredBaseHash = record?.BaseHash,
        };
        bool stored = ApplyStoredDerivedDelta(s, d);
        ApplyStoredDerivedDoors(s, d, stored);
        var (min, max) = s.CellBox();
        s.Width = max.X - min.X + 1;
        s.Height = max.Y - min.Y + 1;
        s.Length = max.Z - min.Z + 1;
        s.Touched = new HashSet<Vector3i>();
        _derivedStations[id] = d;
        _playerStationCells[id] = s;
        bool materialised = _repo.ListStructureOverlays(StationLocationIdPrefix + id).Any(o => o.Key == DerivedOverlayKey(id));
        var station = new BoardableStation
        {
            Id = id,
            Name = s.Name,
            SizeTier = sizeTier,
            SpacePosition = s.Position,
            Origin = record?.Origin ?? StationStampOrigin,
            Materialised = materialised,
            StampMin = record?.Anchor ?? baseMin, // #3613: the stored anchor wins — a rebuilt base must never move the interior
        };
        _stationsById[id] = station;
        AdoptDerivedRecord(station, d, stored || materialised, record);
        return station;
    }

    /// <summary>The stored door records replace the base's doors once anything of the station was stored (as a stored
    /// derelict's do).</summary>
    private void ApplyStoredDerivedDoors(SpaceStructure s, DerivedStation d, bool deltaStored)
    {
        if (!deltaStored && _repo.LoadStructureEdits(s.Id).Count == 0)
        {
            return;
        }

        foreach (var door in s.PlacedDoorAxes.Keys.ToList())
        {
            RemovePlacedShipDoor(s, door);
        }

        ApplyPersistedStationDoors(s);
        d.DoorSignature = DoorSignatureOf(s);
    }

    /// <summary>The base build as an overlay in build-cell coordinates, by chunk — a fraction of a byte per cell.</summary>
    private static Dictionary<ChunkCoord, StructureOverlayChunk> SnapshotDerivedBase(SpaceStructure s)
    {
        var builder = new StructureOverlayBuilder();
        foreach (var kv in s.Cells)
        {
            var (tint, glow, shape) = CellExtras(s, kv.Key);
            builder.Set(kv.Key, kv.Value.Value, tint, glow, shape);
        }

        return builder.Build().Chunks.ToDictionary(c => c.Coord);
    }

    private static (int Tint, int Glow, int Shape) CellExtras(SpaceStructure s, Vector3i cell)
    {
        var (tint, glow) = s.Mods.TryGetValue(cell, out var m) ? m : (0, 0);
        return (tint & 0xFFFFFF, glow & 0xFFFFFF, s.Shapes.TryGetValue(cell, out int sh) ? sh : 0);
    }

    /// <summary>The base build's cell (block 0 = air).</summary>
    private static (ushort Block, int Tint, int Glow, int Shape) DerivedBaseAt(DerivedStation d, Vector3i cell)
    {
        if (!d.Base.TryGetValue(WorldConstants.WorldToChunk(cell), out var chunk))
        {
            return default;
        }

        var l = WorldConstants.WorldToLocal(cell);
        int run = chunk.FindRun(WorldConstants.LocalIndex(l.X, l.Y, l.Z));
        if (run < 0)
        {
            return default;
        }

        var (tint, glow, shape) = chunk.RunExtras(run);
        return (chunk.RunBlock(run), tint, glow, shape);
    }

    // ---------------------------------------------------------------- the interior

    /// <summary>The boarding stamp of a derived station (the station world is active): its doors are hung, its bounds set,
    /// and — the first time, or should the stored overlay be gone — the build as it stands goes into the world as ONE
    /// structure overlay; then the boarder's arrival is found near the entrance. Nothing is written cell by cell.</summary>
    private void StampDerivedStation(BoardableStation station, SpaceStructure src)
    {
        bool refresh = station.Stamped;
        station.Materialised = true;
        if (_derivedStations.TryGetValue(station.Id, out var d))
        {
            SaveDerivedRecordOnce(station, d); // the anchor the interior is laid at is stored before the interior is
        }

        SyncStationDoors(station, src);
        var (min, max) = CellBox(src);
        RefreshStationBounds(station, min, max);
        string key = DerivedOverlayKey(station.Id);
        if (!_world.HasStructureOverlay(key))
        {
            int cells = StampDerivedOverlay(station, src, key);
            PersistDerivedStation(src); // the save holds this station's changes (an empty delta at worst) and its doors with the next flush
            _log.Info($"Derived station '{station.Name}' laid into its void world as one structure overlay ({cells} cells).");
        }

        station.Markers.Clear();
        PlaceDerivedStationArrival(station, src);
        station.Structure = null; // no procedural structure bounds
        station.Stamped = true;
        if (refresh)
        {
            _log.Info($"Derived station '{station.Name}' boarded again (nothing re-stamped).");
        }
    }

    /// <summary>Stores the build as the interior world's structure overlay and hands it to the world (resident chunks get it
    /// at once and are re-streamed). A plant that would open onto the void is left out, as the cell stamp leaves it out.</summary>
    private int StampDerivedOverlay(BoardableStation station, SpaceStructure src, string key)
    {
        var builder = new StructureOverlayBuilder();
        int circ = _world.Circumference;
        ushort GetCell(Vector3i p) => src.Get(p).Value;
        foreach (var kv in src.Cells)
        {
            if (IsFlora(kv.Value.Value) && FloraCellOpensToVoid(GetCell, kv.Key))
            {
                continue;
            }

            var (tint, glow, shape) = CellExtras(src, kv.Key);
            builder.Set(WorldConstants.CanonicalBlock(StationCellToWorld(station, kv.Key), circ), kv.Value.Value, tint, glow, shape);
        }

        var overlay = builder.Build();
        _repo.SaveStructureOverlay(new StoredStructureOverlay { Planet = _world.LocationId, Key = key, Data = overlay.Encode() });
        var resident = _world.AddStructureOverlay(key, overlay);
        foreach (var session in JoinedInActiveWorld())
        {
            foreach (var coord in resident)
            {
                session.Streaming.SentChunks.Remove(coord); // re-streamed whole on the next pass
            }
        }

        return overlay.CellCount;
    }

    /// <summary>Where a boarder of a derived station arrives: the standable deck nearest to the entrance hint, searched in a
    /// box around it (widening once) — under the hull's roof first for a derelict — loading only the chunks of that box. Only
    /// when nothing stands there the whole-volume search of the player stations runs.</summary>
    private void PlaceDerivedStationArrival(BoardableStation station, SpaceStructure src)
    {
        var hint = _derivedStations.TryGetValue(station.Id, out var d)
            ? StationCellToWorld(station, d.Entrance)
            : StationCellToWorld(station, src.CellBox().Min);
        _stationAir.Remove(station.Id); // judged on what the world holds now
        bool roofed = IsGeneratedDerelictId(station.Id);
        Vector3i? feet = null;
        foreach (int reach in DerivedArrivalReach)
        {
            LoadChunksAround(station, hint, reach);
            if ((roofed && TryFindStandableNear(station, hint, reach, roofedOnly: true, out var spot))
                || TryFindStandableNear(station, hint, reach, roofedOnly: false, out spot))
            {
                feet = spot;
                break;
            }
        }

        if (feet is null)
        {
            LoadStationBoxChunks(station);
            if (TryFindStandableInStation(station, hint, null, out var any))
            {
                feet = any;
            }
            else
            {
                CutStationArrivalPad(hint);
                feet = hint;
            }
        }

        var f = feet.Value;
        station.Spawn = new Vector3f(f.X + 0.5f, f.Y, f.Z + 0.5f);
        station.Markers.Insert(0, ("spawn", station.Spawn));
    }

    /// <summary>Loads the chunks the local arrival search reads: the box around <paramref name="hint"/>, up to the hull's top
    /// (the roof check looks up).</summary>
    private void LoadChunksAround(BoardableStation station, Vector3i hint, int reach)
    {
        int top = System.Math.Max(hint.Y + reach, station.BoundsMax.Y) + 3;
        var cMin = WorldConstants.WorldToChunk(new Vector3i(hint.X - reach, hint.Y - reach - 1, hint.Z - reach));
        var cMax = WorldConstants.WorldToChunk(new Vector3i(hint.X + reach, top, hint.Z + reach));
        for (int cx = cMin.X; cx <= cMax.X; cx++)
            for (int cy = cMin.Y; cy <= cMax.Y; cy++)
                for (int cz = cMin.Z; cz <= cMax.Z; cz++)
                {
                    _world.GetOrLoadChunk(new ChunkCoord(cx, cy, cz));
                }
    }

    /// <summary>The standable cell (two free cells above, a block below, never a doorway) within <paramref name="reach"/> of
    /// <paramref name="hint"/> and inside the station's box that lies nearest to it — the same deck preferred.</summary>
    private bool TryFindStandableNear(BoardableStation station, Vector3i hint, int reach, bool roofedOnly, out Vector3i spot)
    {
        spot = default;
        long best = long.MaxValue;
        int x0 = System.Math.Max(station.BoundsMin.X, hint.X - reach), x1 = System.Math.Min(station.BoundsMax.X, hint.X + reach);
        int z0 = System.Math.Max(station.BoundsMin.Z, hint.Z - reach), z1 = System.Math.Min(station.BoundsMax.Z, hint.Z + reach);
        int y0 = System.Math.Max(station.BoundsMin.Y + 1, hint.Y - reach), y1 = System.Math.Min(station.BoundsMax.Y + 1, hint.Y + reach);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                {
                    if (!StandableAt(x, y, z) || InDoorway(x, y, z) || (roofedOnly && !RoofedInStation(station, x, y, z)))
                    {
                        continue;
                    }

                    long dx = x - hint.X, dy = y - hint.Y, dz = z - hint.Z;
                    long dist = dx * dx + dy * dy * 4 + dz * dz;
                    if (dist < best)
                    {
                        best = dist;
                        spot = new Vector3i(x, y, z);
                    }
                }

        return best != long.MaxValue;
    }

    // ---------------------------------------------------------------- test seams

    /// <summary>Test seam (#3613): registers a derived station from a plain cell grid (the stand-in for a generated giant
    /// wreck) at <paramref name="position"/> in the flight frame.</summary>
    public void RegisterDerivedStationForTest(string id, string name, IReadOnlyDictionary<Vector3i, BlockId> cells,
        Vector3i entrance, Vector3f position, string baseHash = "")
    {
        var s = new SpaceStructure { Name = name, Position = position };
        foreach (var kv in cells)
        {
            s.Set(kv.Key, kv.Value);
        }

        RegisterDerivedStation(id, s, entrance, baseHash: baseHash);
    }

    /// <summary>Test seam (#3613): where a derived station's build cell (0,0,0) lies in its interior world — origin minus anchor.</summary>
    public Vector3i DerivedInteriorOffsetForTest(string id)
        => _stationsById.TryGetValue(id, out var st) ? st.Origin - st.StampMin : default;

    /// <summary>Test seam (#3613): how often the delta blob of a derived station was written this run.</summary>
    public int DerivedDeltaWritesForTest(string id) => _derivedStations.TryGetValue(id, out var d) ? d.DeltaWrites : -1;

    /// <summary>Test seam (#3613): the base hash the stored changes of a derived station were made on (null = none stored).</summary>
    public string? DerivedStoredBaseHashForTest(string id) => DerivedStoredBaseHash(id);

    /// <summary>Test seam (#3613): puts a registered derived station into the player's current flight, with its contact.</summary>
    public void ShowDerivedStationForTest(string playerId, string id)
    {
        if (!_playerInstance.TryGetValue(playerId, out var iid) || !_spaceInstances.TryGetValue(iid, out var instance)
            || !_playerStationCells.TryGetValue(id, out var s))
        {
            return;
        }

        instance.Structures[id] = s;
        if (!instance.Entities.Any(e => e.Id == id))
        {
            instance.Entities.Add(new CombatEntity
            {
                Id = id,
                Kind = CombatEntityKind.SpaceStation,
                Name = s.Name,
                Hostile = false,
                Hull = 1f,
                HullMax = 1f,
                Position = s.Position,
            });
        }

        if (FindSessionByPlayerId(playerId) is { } session)
        {
            SendShipDesign(session, s);
        }
    }

    /// <summary>Test seam (#3613): the players' changes a derived station holds in memory.</summary>
    public int DerivedDeltaCountForTest(string id) => _derivedStations.TryGetValue(id, out var d) ? d.Delta.Count : -1;

    /// <summary>Test seam (#3613): whether a structure is shown as its shell, and the cell is part of it.</summary>
    public bool DerivedShellShowsForTest(string id, Vector3i cell)
        => _playerStationCells.TryGetValue(id, out var s) && s.Shell is { } shell && shell.IsVisible(cell);
}
