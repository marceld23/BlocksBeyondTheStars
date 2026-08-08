// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO.Compression;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>One persisted block edit as a serialization-friendly snapshot row.</summary>
public sealed class BlockEditRow
{
    public string Planet { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public ushort Block { get; set; }
    public int Tint { get; set; }
    public int Glow { get; set; }
    public int Shape { get; set; }
}

/// <summary>One scheduled flora regrowth as a snapshot row.</summary>
public sealed class FloraRegrowRow
{
    public string Planet { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public ushort Block { get; set; }
    public double Timer { get; set; }
}

/// <summary>One persisted in-space structure edit as a snapshot row.</summary>
public sealed class StructureEditRow
{
    public string StructureId { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public ushort Block { get; set; }
}

/// <summary>
/// The whole world state of a <see cref="MemoryWorldRepository"/> as one JSON-serializable document —
/// the browser singleplayer's save format (gzip'd JSON blob instead of a world.db file). Mirrors the
/// SQLite tables one-to-one so a later converter can copy row-by-row in either direction.
/// </summary>
public sealed class MemoryWorldSnapshot
{
    public int Version { get; set; } = 1;
    public Dictionary<ushort, string> BlockPalette { get; set; } = new();
    public string? MetadataJson { get; set; }
    public List<BlockEditRow> BlockEdits { get; set; } = new();
    public List<FloraRegrowRow> FloraRegrow { get; set; } = new();
    public Dictionary<string, PlayerSnapshot> Players { get; set; } = new();
    public Dictionary<string, ShipSnapshot> Ships { get; set; } = new();
    public List<StoredContainer> Containers { get; set; } = new();
    public List<StoredDoor> Doors { get; set; } = new();
    public List<StoredBeacon> Beacons { get; set; } = new();
    public List<StoredBeam> Beams { get; set; } = new();
    public List<StoredBase> Bases { get; set; } = new();
    public List<StoredPaintDesign> PaintDesigns { get; set; } = new();
    public List<StoredCustomShape> CustomShapes { get; set; } = new();
    public List<StoredPaintReport> PaintReports { get; set; } = new();
    public List<StoredAlliance> Alliances { get; set; } = new();
    public List<StoredStoryState> StoryStates { get; set; } = new();
    public List<StoredSpaceStructure> SpaceStructures { get; set; } = new();
    public List<StructureEditRow> StructureEdits { get; set; } = new();
    public Dictionary<string, string> LocationStatuses { get; set; } = new();
    public List<MissionDefinition> Missions { get; set; } = new();
}

/// <summary>
/// Fully managed <see cref="IWorldRepository"/> — no SQLite, no native code — for hosts that cannot
/// load native libraries: the in-browser (WebGL) singleplayer server. State lives in dictionaries and
/// round-trips through <see cref="ExportSnapshotBlob"/>/<see cref="ImportSnapshotBlob"/> as a gzip'd
/// JSON <see cref="MemoryWorldSnapshot"/> (the cloud/IndexedDB save payload). Semantics mirror
/// <c>SqliteWorldRepository</c>: JSON-blob player/ship rows, upsert-by-key stores, and the same
/// block-palette remap on content shifts. <see cref="Flush"/> raises <see cref="Flushed"/> so the
/// host can persist the blob exactly when the server would have committed to disk.
/// </summary>
public sealed class MemoryWorldRepository : IWorldRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly Lock _gate = new();
    private readonly SaveGamePaths _paths;

    // Keyed exactly like the SQLite tables. Complex mutable POCOs are JSON-cloned on write AND read so
    // no caller ever aliases repository state (the DB round-trip gave that isolation for free).
    private readonly Dictionary<ushort, string> _palette = new();
    private readonly Dictionary<(string Planet, int X, int Y, int Z), (ushort Block, int Tint, int Glow, int Shape, string Owner, DateTime? EditedUtc)> _blockEdits = new();
    private readonly Dictionary<(string Planet, int Cx, int Cy, int Cz), List<(string Planet, int X, int Y, int Z)>> _blockEditsByChunk = new();
    private readonly Dictionary<(string Planet, int X, int Y, int Z), (ushort Block, double Timer)> _flora = new();
    private readonly Dictionary<(string Planet, int X, int Y, int Z), (byte Level, bool Falling)> _fluidCells = new();
    private readonly Dictionary<(string Planet, int X, int Y, int Z), (double Remaining, int Generation)> _fireCells = new();
    private readonly Dictionary<string, string> _players = new();       // id → PlayerSnapshot JSON
    private readonly Dictionary<string, string> _ships = new();         // id → ShipSnapshot JSON
    private readonly Dictionary<string, string> _containers = new();    // id → StoredContainer JSON
    private readonly Dictionary<(string Planet, int X, int Y, int Z), StoredDoor> _doors = new();
    private readonly Dictionary<(string Planet, int X, int Y, int Z), StoredBeacon> _beacons = new();
    private readonly Dictionary<(string Planet, int X, int Y, int Z), StoredBeam> _beams = new();
    private readonly Dictionary<(string Planet, int X, int Y, int Z), StoredBase> _bases = new();
    private readonly Dictionary<int, StoredPaintDesign> _paintDesigns = new();
    private readonly Dictionary<int, StoredCustomShape> _customShapes = new();
    private readonly List<StoredPaintReport> _paintReports = new();
    private readonly Dictionary<(string A, string B), StoredAlliance> _alliances = new();
    private readonly Dictionary<string, string> _storyStates = new();   // storyId → JSON
    private readonly Dictionary<string, string> _spaceStructures = new(); // id → JSON
    private readonly Dictionary<(string StructureId, int X, int Y, int Z), ushort> _structureEdits = new();
    private readonly Dictionary<string, string> _locationStatuses = new();
    private readonly Dictionary<string, string> _missions = new();      // id → JSON

    private string? _metadataJson;

    public MemoryWorldRepository(SaveGamePaths paths) => _paths = paths;

    /// <summary>Raised by <see cref="Flush"/> — the moment the server would have committed durably.
    /// The browser host hooks this to serialize + store the snapshot blob (IndexedDB / cloud).</summary>
    public event Action? Flushed;

    public string WorldDirectory => _paths.WorldDirectory;

    public void Initialize() => _paths.EnsureDirectories();

    // ---------------- Snapshot blob (the save payload) ----------------

    /// <summary>Serializes the whole world state to a gzip'd JSON blob (the cloud/IndexedDB payload).</summary>
    public byte[] ExportSnapshotBlob()
    {
        lock (_gate)
        {
            var snapshot = BuildSnapshotLocked();
            using var buffer = new MemoryStream();
            using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                using var writer = new Utf8JsonWriter(gzip);
                JsonSerializer.Serialize(writer, snapshot, JsonOptions);
            }

            return buffer.ToArray();
        }
    }

    /// <summary>Replaces the whole world state from a blob produced by <see cref="ExportSnapshotBlob"/>.
    /// Call before <c>GameServer.Start()</c> — it rebuilds every table and index in place.</summary>
    public void ImportSnapshotBlob(byte[] blob)
    {
        using var input = new MemoryStream(blob);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var plain = new MemoryStream();
        gzip.CopyTo(plain);
        var snapshot = JsonSerializer.Deserialize<MemoryWorldSnapshot>(plain.ToArray(), JsonOptions)
            ?? throw new InvalidDataException("The save blob does not contain a world snapshot.");

        lock (_gate)
        {
            RestoreSnapshotLocked(snapshot);
        }
    }

    private MemoryWorldSnapshot BuildSnapshotLocked()
    {
        var snapshot = new MemoryWorldSnapshot
        {
            BlockPalette = new Dictionary<ushort, string>(_palette),
            MetadataJson = _metadataJson,
            Players = _players.ToDictionary(kv => kv.Key, kv => JsonSerializer.Deserialize<PlayerSnapshot>(kv.Value, JsonOptions)!),
            Ships = _ships.ToDictionary(kv => kv.Key, kv => JsonSerializer.Deserialize<ShipSnapshot>(kv.Value, JsonOptions)!),
            Containers = _containers.Values.Select(json => JsonSerializer.Deserialize<StoredContainer>(json, JsonOptions)!).ToList(),
            Doors = _doors.Values.Select(CloneDoor).ToList(),
            Beacons = _beacons.Values.Select(CloneBeacon).ToList(),
            Beams = _beams.Values.Select(CloneBeam).ToList(),
            Bases = _bases.Values.Select(CloneBase).ToList(),
            PaintDesigns = _paintDesigns.Values.Select(ClonePaintDesign).ToList(),
            CustomShapes = _customShapes.Values.Select(CloneCustomShape).ToList(),
            PaintReports = _paintReports.Select(ClonePaintReport).ToList(),
            Alliances = _alliances.Values.Select(a => new StoredAlliance { PlayerA = a.PlayerA, PlayerB = a.PlayerB, FormedUtc = a.FormedUtc }).ToList(),
            StoryStates = _storyStates.Values.Select(json => JsonSerializer.Deserialize<StoredStoryState>(json, JsonOptions)!).ToList(),
            SpaceStructures = _spaceStructures.Values.Select(json => JsonSerializer.Deserialize<StoredSpaceStructure>(json, JsonOptions)!).ToList(),
            LocationStatuses = new Dictionary<string, string>(_locationStatuses),
            Missions = _missions.Values.Select(json => JsonSerializer.Deserialize<MissionDefinition>(json, JsonOptions)!).ToList(),
        };

        foreach (var kv in _blockEdits)
        {
            snapshot.BlockEdits.Add(new BlockEditRow
            {
                Planet = kv.Key.Planet,
                X = kv.Key.X,
                Y = kv.Key.Y,
                Z = kv.Key.Z,
                Block = kv.Value.Block,
                Tint = kv.Value.Tint,
                Glow = kv.Value.Glow,
                Shape = kv.Value.Shape,
            });
        }

        foreach (var kv in _flora)
        {
            snapshot.FloraRegrow.Add(new FloraRegrowRow
            {
                Planet = kv.Key.Planet,
                X = kv.Key.X,
                Y = kv.Key.Y,
                Z = kv.Key.Z,
                Block = kv.Value.Block,
                Timer = kv.Value.Timer,
            });
        }

        foreach (var kv in _structureEdits)
        {
            snapshot.StructureEdits.Add(new StructureEditRow
            {
                StructureId = kv.Key.StructureId,
                X = kv.Key.X,
                Y = kv.Key.Y,
                Z = kv.Key.Z,
                Block = kv.Value,
            });
        }

        return snapshot;
    }

    private void RestoreSnapshotLocked(MemoryWorldSnapshot snapshot)
    {
        _palette.Clear();
        _blockEdits.Clear();
        _blockEditsByChunk.Clear();
        _flora.Clear();
        _players.Clear();
        _ships.Clear();
        _containers.Clear();
        _doors.Clear();
        _beacons.Clear();
        _beams.Clear();
        _bases.Clear();
        _paintDesigns.Clear();
        _paintReports.Clear();
        _alliances.Clear();
        _storyStates.Clear();
        _spaceStructures.Clear();
        _structureEdits.Clear();
        _locationStatuses.Clear();
        _missions.Clear();

        foreach (var kv in snapshot.BlockPalette)
        {
            _palette[kv.Key] = kv.Value;
        }

        _metadataJson = snapshot.MetadataJson;
        foreach (var row in snapshot.BlockEdits)
        {
            SetBlockLocked(row.Planet, new Vector3i(row.X, row.Y, row.Z), row.Block, row.Tint, row.Glow, row.Shape);
        }

        foreach (var row in snapshot.FloraRegrow)
        {
            _flora[(row.Planet, row.X, row.Y, row.Z)] = (row.Block, row.Timer);
        }

        foreach (var kv in snapshot.Players)
        {
            _players[kv.Key] = JsonSerializer.Serialize(kv.Value, JsonOptions);
        }

        foreach (var kv in snapshot.Ships)
        {
            _ships[kv.Key] = JsonSerializer.Serialize(kv.Value, JsonOptions);
        }

        foreach (var container in snapshot.Containers)
        {
            _containers[container.Id] = JsonSerializer.Serialize(container, JsonOptions);
        }

        foreach (var door in snapshot.Doors)
        {
            _doors[(door.Planet, door.X, door.Y, door.Z)] = CloneDoor(door);
        }

        foreach (var beacon in snapshot.Beacons)
        {
            _beacons[(beacon.Planet, beacon.X, beacon.Y, beacon.Z)] = CloneBeacon(beacon);
        }

        foreach (var beam in snapshot.Beams)
        {
            _beams[(beam.Planet, beam.X, beam.Y, beam.Z)] = CloneBeam(beam);
        }

        foreach (var basePoint in snapshot.Bases)
        {
            _bases[(basePoint.Planet, basePoint.X, basePoint.Y, basePoint.Z)] = CloneBase(basePoint);
        }

        foreach (var design in snapshot.PaintDesigns)
        {
            _paintDesigns[design.Id] = ClonePaintDesign(design);
        }

        foreach (var shape in snapshot.CustomShapes)
        {
            _customShapes[shape.Id] = CloneCustomShape(shape);
        }

        foreach (var report in snapshot.PaintReports)
        {
            _paintReports.Add(ClonePaintReport(report));
        }

        foreach (var alliance in snapshot.Alliances)
        {
            _alliances[(alliance.PlayerA, alliance.PlayerB)] = new StoredAlliance
            {
                PlayerA = alliance.PlayerA,
                PlayerB = alliance.PlayerB,
                FormedUtc = alliance.FormedUtc,
            };
        }

        foreach (var state in snapshot.StoryStates)
        {
            _storyStates[state.StoryId] = JsonSerializer.Serialize(state, JsonOptions);
        }

        foreach (var structure in snapshot.SpaceStructures)
        {
            _spaceStructures[structure.Id] = JsonSerializer.Serialize(structure, JsonOptions);
        }

        foreach (var row in snapshot.StructureEdits)
        {
            _structureEdits[(row.StructureId, row.X, row.Y, row.Z)] = row.Block;
        }

        foreach (var kv in snapshot.LocationStatuses)
        {
            _locationStatuses[kv.Key] = kv.Value;
        }

        foreach (var mission in snapshot.Missions)
        {
            _missions[mission.Id] = JsonSerializer.Serialize(mission, JsonOptions);
        }
    }

    // ---------------- Block-id palette (content-shift migration) ----------------

    public void EnsureBlockPalette(IReadOnlyDictionary<ushort, string> currentPalette)
    {
        lock (_gate)
        {
            if (_palette.Count == 0)
            {
                WritePaletteLocked(currentPalette);
                return;
            }

            var remap = BlockPaletteMigration.BuildRemap(_palette, currentPalette);
            if (remap.Count > 0)
            {
                RemapLocked(remap);
            }

            WritePaletteLocked(currentPalette);
        }
    }

    private void WritePaletteLocked(IReadOnlyDictionary<ushort, string> palette)
    {
        _palette.Clear();
        foreach (var kv in palette)
        {
            _palette[kv.Key] = kv.Value;
        }
    }

    private void RemapLocked(IReadOnlyDictionary<ushort, ushort> remap)
    {
        foreach (var key in _blockEdits.Keys.ToList())
        {
            var value = _blockEdits[key];
            if (remap.TryGetValue(value.Block, out ushort nb) && nb != value.Block)
            {
                _blockEdits[key] = (nb, value.Tint, value.Glow, value.Shape, value.Owner, value.EditedUtc);
            }
        }

        foreach (var key in _structureEdits.Keys.ToList())
        {
            if (remap.TryGetValue(_structureEdits[key], out ushort nb) && nb != _structureEdits[key])
            {
                _structureEdits[key] = nb;
            }
        }

        foreach (var key in _flora.Keys.ToList())
        {
            var value = _flora[key];
            if (remap.TryGetValue(value.Block, out ushort nb) && nb != value.Block)
            {
                _flora[key] = (nb, value.Timer);
            }
        }

        foreach (var id in _spaceStructures.Keys.ToList())
        {
            var structure = JsonSerializer.Deserialize<StoredSpaceStructure>(_spaceStructures[id], JsonOptions)!;
            string remapped = BlockPaletteMigration.RemapCellString(structure.Blocks, remap);
            if (remapped != structure.Blocks)
            {
                structure.Blocks = remapped;
                _spaceStructures[id] = JsonSerializer.Serialize(structure, JsonOptions);
            }
        }
    }

    // ---------------- Metadata ----------------

    public WorldMetadata? LoadMetadata()
    {
        lock (_gate)
        {
            return _metadataJson is null ? null : JsonSerializer.Deserialize<WorldMetadata>(_metadataJson, JsonOptions);
        }
    }

    public void SaveMetadata(WorldMetadata metadata)
    {
        lock (_gate)
        {
            _metadataJson = JsonSerializer.Serialize(metadata, JsonOptions);
        }
    }

    // ---------------- Block edits ----------------

    public void SetBlock(string planet, Vector3i worldPosition, ushort block, int tint = 0, int glow = 0, int shape = 0, string owner = "")
    {
        lock (_gate)
        {
            SetBlockLocked(planet, worldPosition, block, tint, glow, shape, owner);
        }
    }

    private void SetBlockLocked(string planet, Vector3i worldPosition, ushort block, int tint, int glow, int shape, string owner = "")
    {
        var key = (planet, worldPosition.X, worldPosition.Y, worldPosition.Z);
        if (!_blockEdits.ContainsKey(key))
        {
            var chunkKey = ChunkKeyOf(planet, worldPosition.X, worldPosition.Y, worldPosition.Z);
            if (!_blockEditsByChunk.TryGetValue(chunkKey, out var bucket))
            {
                _blockEditsByChunk[chunkKey] = bucket = new List<(string, int, int, int)>();
            }

            bucket.Add(key);
        }

        // Mirrors the SQL repositories: a server-internal write (no owner) must not erase an existing
        // attribution, so the previous editor is kept (issue #490).
        string keptOwner = owner;
        DateTime? stamp = string.IsNullOrEmpty(owner) ? null : DateTime.UtcNow;
        if (string.IsNullOrEmpty(owner) && _blockEdits.TryGetValue(key, out var prev))
        {
            keptOwner = prev.Owner;
            stamp = prev.EditedUtc;
        }

        _blockEdits[key] = (block, tint, glow, shape, keptOwner ?? string.Empty, stamp);
    }

    public (string Owner, DateTime? EditedUtc)? GetBlockAttribution(string planet, Vector3i worldPosition)
    {
        lock (_gate)
        {
            var key = (planet, worldPosition.X, worldPosition.Y, worldPosition.Z);
            return _blockEdits.TryGetValue(key, out var value) ? (value.Owner, value.EditedUtc) : null;
        }
    }

    public IReadOnlyList<BlockEdit> LoadChunkEdits(string planet, ChunkCoord chunk)
    {
        lock (_gate)
        {
            var origin = WorldConstants.ChunkOrigin(chunk);
            if (!_blockEditsByChunk.TryGetValue(ChunkKeyOf(planet, origin.X, origin.Y, origin.Z), out var bucket))
            {
                return Array.Empty<BlockEdit>();
            }

            var list = new List<BlockEdit>(bucket.Count);
            foreach (var key in bucket)
            {
                if (_blockEdits.TryGetValue(key, out var value))
                {
                    list.Add(new BlockEdit(new Vector3i(key.X, key.Y, key.Z), value.Block, value.Tint, value.Glow, value.Shape));
                }
            }

            return list;
        }
    }

    public bool HasPlayerBlockEdits(string planet, Vector3i min, Vector3i max)
    {
        lock (_gate)
        {
            foreach (var kv in _blockEdits)
            {
                var k = kv.Key;
                if (k.Planet == planet
                    && k.X >= min.X && k.X <= max.X
                    && k.Y >= min.Y && k.Y <= max.Y
                    && k.Z >= min.Z && k.Z <= max.Z
                    && !string.IsNullOrEmpty(kv.Value.Owner))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool HasAnyBlockEdits(string planet)
    {
        lock (_gate)
        {
            foreach (var kv in _blockEdits)
            {
                if (kv.Key.Planet == planet)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void DeleteBlockEdits(string planet, Vector3i min, Vector3i max)
    {
        lock (_gate)
        {
            var doomed = _blockEdits.Keys
                .Where(k => k.Planet == planet
                    && k.X >= min.X && k.X <= max.X
                    && k.Y >= min.Y && k.Y <= max.Y
                    && k.Z >= min.Z && k.Z <= max.Z)
                .ToList();
            foreach (var key in doomed)
            {
                _blockEdits.Remove(key);
                if (_blockEditsByChunk.TryGetValue(ChunkKeyOf(key.Planet, key.X, key.Y, key.Z), out var bucket))
                {
                    bucket.Remove(key);
                }
            }
        }
    }

    private static (string, int, int, int) ChunkKeyOf(string planet, int x, int y, int z)
    {
        var chunk = WorldConstants.WorldToChunk(new Vector3i(x, y, z));
        return (planet, chunk.X, chunk.Y, chunk.Z);
    }

    // ---------------- Flora regrowth ----------------

    public void SaveFloraRegrow(string planet, Vector3i worldPosition, ushort block, double timer)
    {
        lock (_gate)
        {
            _flora[(planet, worldPosition.X, worldPosition.Y, worldPosition.Z)] = (block, timer);
        }
    }

    public IReadOnlyList<StoredFloraRegrow> ListFloraRegrow(string planet)
    {
        lock (_gate)
        {
            return _flora
                .Where(kv => kv.Key.Planet == planet)
                .Select(kv => new StoredFloraRegrow(new Vector3i(kv.Key.X, kv.Key.Y, kv.Key.Z), kv.Value.Block, kv.Value.Timer))
                .ToList();
        }
    }

    public void DeleteFloraRegrow(string planet, Vector3i worldPosition)
    {
        lock (_gate)
        {
            _flora.Remove((planet, worldPosition.X, worldPosition.Y, worldPosition.Z));
        }
    }

    // ---------------- Flowing fluid cells (#657) ----------------

    public void SaveFluidCell(string planet, Vector3i worldPosition, byte level, bool falling)
    {
        lock (_gate)
        {
            _fluidCells[(planet, worldPosition.X, worldPosition.Y, worldPosition.Z)] = (level, falling);
        }
    }

    public IReadOnlyList<StoredFluidCell> ListFluidCells(string planet)
    {
        lock (_gate)
        {
            return _fluidCells
                .Where(kv => kv.Key.Planet == planet)
                .Select(kv => new StoredFluidCell(new Vector3i(kv.Key.X, kv.Key.Y, kv.Key.Z), kv.Value.Level, kv.Value.Falling))
                .ToList();
        }
    }

    public void DeleteFluidCell(string planet, Vector3i worldPosition)
    {
        lock (_gate)
        {
            _fluidCells.Remove((planet, worldPosition.X, worldPosition.Y, worldPosition.Z));
        }
    }

    // ---------------- Burning cells (#784) ----------------

    public void SaveFireCell(string planet, Vector3i worldPosition, double remaining, int generation)
    {
        lock (_gate)
        {
            _fireCells[(planet, worldPosition.X, worldPosition.Y, worldPosition.Z)] = (remaining, generation);
        }
    }

    public IReadOnlyList<StoredFireCell> ListFireCells(string planet)
    {
        lock (_gate)
        {
            return _fireCells
                .Where(kv => kv.Key.Planet == planet)
                .Select(kv => new StoredFireCell(new Vector3i(kv.Key.X, kv.Key.Y, kv.Key.Z), kv.Value.Remaining, kv.Value.Generation))
                .ToList();
        }
    }

    public void DeleteFireCell(string planet, Vector3i worldPosition)
    {
        lock (_gate)
        {
            _fireCells.Remove((planet, worldPosition.X, worldPosition.Y, worldPosition.Z));
        }
    }

    // ---------------- Players & ships (JSON rows, like the SQLite blob columns) ----------------

    public PlayerState? LoadPlayer(string playerId)
    {
        lock (_gate)
        {
            return _players.TryGetValue(playerId, out var json)
                ? StateMapper.FromSnapshot(JsonSerializer.Deserialize<PlayerSnapshot>(json, JsonOptions)!)
                : null;
        }
    }

    public void SavePlayer(PlayerState player)
    {
        lock (_gate)
        {
            _players[player.PlayerId] = JsonSerializer.Serialize(StateMapper.ToSnapshot(player), JsonOptions);
        }
    }

    public IReadOnlyList<string> ListPlayerIds()
    {
        lock (_gate)
        {
            return _players.Keys.ToList();
        }
    }

    public ShipState? LoadShip(string shipId)
    {
        lock (_gate)
        {
            return _ships.TryGetValue(shipId, out var json)
                ? StateMapper.FromSnapshot(JsonSerializer.Deserialize<ShipSnapshot>(json, JsonOptions)!)
                : null;
        }
    }

    public void SaveShip(string shipId, ShipState ship)
    {
        lock (_gate)
        {
            _ships[shipId] = JsonSerializer.Serialize(StateMapper.ToSnapshot(ship), JsonOptions);
        }
    }

    // ---------------- Containers ----------------

    public void SaveContainer(StoredContainer container)
    {
        lock (_gate)
        {
            _containers[container.Id] = JsonSerializer.Serialize(container, JsonOptions);
        }
    }

    public IReadOnlyList<StoredContainer> ListContainers(string planet)
    {
        lock (_gate)
        {
            return _containers.Values
                .Select(json => JsonSerializer.Deserialize<StoredContainer>(json, JsonOptions)!)
                .Where(c => c.Planet == planet)
                .ToList();
        }
    }

    public void DeleteContainer(string id)
    {
        lock (_gate)
        {
            _containers.Remove(id);
        }
    }

    // ---------------- Doors / beacons / beams / bases (flat keyed rows) ----------------

    private static StoredDoor CloneDoor(StoredDoor d)
        => new() { Planet = d.Planet, X = d.X, Y = d.Y, Z = d.Z, Kind = d.Kind, AxisX = d.AxisX };

    private static StoredBeacon CloneBeacon(StoredBeacon b)
        => new() { Planet = b.Planet, X = b.X, Y = b.Y, Z = b.Z, Label = b.Label, OwnerId = b.OwnerId };

    private static StoredBeam CloneBeam(StoredBeam b)
        => new() { Planet = b.Planet, X = b.X, Y = b.Y, Z = b.Z, Name = b.Name, OwnerId = b.OwnerId };

    private static StoredBase CloneBase(StoredBase b)
        => new() { Planet = b.Planet, X = b.X, Y = b.Y, Z = b.Z, Name = b.Name, OwnerId = b.OwnerId };

    private static StoredPaintDesign ClonePaintDesign(StoredPaintDesign d)
        => new() { Id = d.Id, OwnerId = d.OwnerId, OwnerName = d.OwnerName, Pixels = d.Pixels };

    private static StoredCustomShape CloneCustomShape(StoredCustomShape s)
        => new() { Id = s.Id, OwnerId = s.OwnerId, OwnerName = s.OwnerName, Name = s.Name, Voxels = s.Voxels };

    private static StoredPaintReport ClonePaintReport(StoredPaintReport r)
        => new()
        {
            ReporterId = r.ReporterId,
            OwnerId = r.OwnerId,
            DesignId = r.DesignId,
            Planet = r.Planet,
            X = r.X,
            Y = r.Y,
            Z = r.Z,
            CreatedUnix = r.CreatedUnix,
            Kind = r.Kind,
        };

    public void SaveDoor(StoredDoor door)
    {
        lock (_gate)
        {
            _doors[(door.Planet, door.X, door.Y, door.Z)] = CloneDoor(door);
        }
    }

    public IReadOnlyList<StoredDoor> ListDoors(string planet)
    {
        lock (_gate)
        {
            return _doors.Values.Where(d => d.Planet == planet).Select(CloneDoor).ToList();
        }
    }

    public void DeleteDoor(string planet, int x, int y, int z)
    {
        lock (_gate)
        {
            _doors.Remove((planet, x, y, z));
        }
    }

    public void SaveBeacon(StoredBeacon beacon)
    {
        lock (_gate)
        {
            _beacons[(beacon.Planet, beacon.X, beacon.Y, beacon.Z)] = CloneBeacon(beacon);
        }
    }

    public IReadOnlyList<StoredBeacon> ListBeacons(string planet)
    {
        lock (_gate)
        {
            return _beacons.Values.Where(b => b.Planet == planet).Select(CloneBeacon).ToList();
        }
    }

    public IReadOnlyList<StoredBeacon> ListAllBeacons()
    {
        lock (_gate)
        {
            return _beacons.Values.Select(CloneBeacon).ToList();
        }
    }

    public void DeleteBeacon(string planet, int x, int y, int z)
    {
        lock (_gate)
        {
            _beacons.Remove((planet, x, y, z));
        }
    }

    public void SavePaintDesign(StoredPaintDesign design)
    {
        lock (_gate)
        {
            _paintDesigns[design.Id] = ClonePaintDesign(design);
        }
    }

    public IReadOnlyList<StoredPaintDesign> ListPaintDesigns()
    {
        lock (_gate)
        {
            return _paintDesigns.Values.Select(ClonePaintDesign).ToList();
        }
    }

    public void DeletePaintDesign(int id)
    {
        lock (_gate)
        {
            _paintDesigns.Remove(id);
        }
    }

    public void SaveCustomShape(StoredCustomShape shape)
    {
        lock (_gate)
        {
            _customShapes[shape.Id] = CloneCustomShape(shape);
        }
    }

    public IReadOnlyList<StoredCustomShape> ListCustomShapes()
    {
        lock (_gate)
        {
            return _customShapes.Values.Select(CloneCustomShape).ToList();
        }
    }

    public void DeleteCustomShape(int id)
    {
        lock (_gate)
        {
            _customShapes.Remove(id);
        }
    }

    public void SavePaintReport(StoredPaintReport report)
    {
        lock (_gate)
        {
            _paintReports.Add(ClonePaintReport(report));
        }
    }

    public IReadOnlyList<StoredPaintReport> ListPaintReports()
    {
        lock (_gate)
        {
            return _paintReports.Select(ClonePaintReport).ToList();
        }
    }

    public void SaveBeam(StoredBeam beam)
    {
        lock (_gate)
        {
            _beams[(beam.Planet, beam.X, beam.Y, beam.Z)] = CloneBeam(beam);
        }
    }

    public IReadOnlyList<StoredBeam> ListBeams(string planet)
    {
        lock (_gate)
        {
            return _beams.Values.Where(b => b.Planet == planet).Select(CloneBeam).ToList();
        }
    }

    public IReadOnlyList<StoredBeam> ListAllBeams()
    {
        lock (_gate)
        {
            return _beams.Values.Select(CloneBeam).ToList();
        }
    }

    public void DeleteBeam(string planet, int x, int y, int z)
    {
        lock (_gate)
        {
            _beams.Remove((planet, x, y, z));
        }
    }

    public void SaveBase(StoredBase basePoint)
    {
        lock (_gate)
        {
            _bases[(basePoint.Planet, basePoint.X, basePoint.Y, basePoint.Z)] = CloneBase(basePoint);
        }
    }

    public IReadOnlyList<StoredBase> ListAllBases()
    {
        lock (_gate)
        {
            return _bases.Values.Select(CloneBase).ToList();
        }
    }

    public void DeleteBase(string planet, int x, int y, int z)
    {
        lock (_gate)
        {
            _bases.Remove((planet, x, y, z));
        }
    }

    // ---------------- Alliances / story / space structures / missions ----------------

    public void SaveAlliance(StoredAlliance alliance)
    {
        lock (_gate)
        {
            _alliances[(alliance.PlayerA, alliance.PlayerB)] = new StoredAlliance
            {
                PlayerA = alliance.PlayerA,
                PlayerB = alliance.PlayerB,
                FormedUtc = alliance.FormedUtc,
            };
        }
    }

    public IReadOnlyList<StoredAlliance> ListAlliances()
    {
        lock (_gate)
        {
            return _alliances.Values
                .Select(a => new StoredAlliance { PlayerA = a.PlayerA, PlayerB = a.PlayerB, FormedUtc = a.FormedUtc })
                .ToList();
        }
    }

    public void DeleteAlliance(string playerA, string playerB)
    {
        lock (_gate)
        {
            // Order-independent, mirroring the SQLite DELETE (the store key is already normalized).
            _alliances.Remove((playerA, playerB));
            _alliances.Remove((playerB, playerA));
        }
    }

    public void SaveStoryState(StoredStoryState state)
    {
        lock (_gate)
        {
            _storyStates[state.StoryId] = JsonSerializer.Serialize(state, JsonOptions);
        }
    }

    public IReadOnlyList<StoredStoryState> ListStoryStates()
    {
        lock (_gate)
        {
            return _storyStates.Values.Select(json => JsonSerializer.Deserialize<StoredStoryState>(json, JsonOptions)!).ToList();
        }
    }

    public void SaveSpaceStructure(StoredSpaceStructure structure)
    {
        lock (_gate)
        {
            _spaceStructures[structure.Id] = JsonSerializer.Serialize(structure, JsonOptions);
        }
    }

    public IReadOnlyList<StoredSpaceStructure> ListSpaceStructures()
    {
        lock (_gate)
        {
            return _spaceStructures.Values.Select(json => JsonSerializer.Deserialize<StoredSpaceStructure>(json, JsonOptions)!).ToList();
        }
    }

    public void DeleteSpaceStructure(string id)
    {
        lock (_gate)
        {
            _spaceStructures.Remove(id);
        }
    }

    public void SetStructureBlock(string structureId, Vector3i position, ushort block)
    {
        lock (_gate)
        {
            _structureEdits[(structureId, position.X, position.Y, position.Z)] = block;
        }
    }

    public IReadOnlyList<BlockEdit> LoadStructureEdits(string structureId)
    {
        lock (_gate)
        {
            return _structureEdits
                .Where(kv => kv.Key.StructureId == structureId)
                .Select(kv => new BlockEdit(new Vector3i(kv.Key.X, kv.Key.Y, kv.Key.Z), kv.Value))
                .ToList();
        }
    }

    public void DeleteStructureEdits(string structureId)
    {
        lock (_gate)
        {
            foreach (var key in _structureEdits.Keys.Where(k => k.StructureId == structureId).ToList())
            {
                _structureEdits.Remove(key);
            }
        }
    }

    public void SetLocationStatus(string locationId, string status)
    {
        lock (_gate)
        {
            _locationStatuses[locationId] = status;
        }
    }

    public IReadOnlyDictionary<string, string> LoadLocationStatuses()
    {
        lock (_gate)
        {
            return new Dictionary<string, string>(_locationStatuses);
        }
    }

    public void SaveMission(MissionDefinition mission)
    {
        lock (_gate)
        {
            _missions[mission.Id] = JsonSerializer.Serialize(mission, JsonOptions);
        }
    }

    public IReadOnlyList<MissionDefinition> ListMissions()
    {
        lock (_gate)
        {
            return _missions.Values.Select(json => JsonSerializer.Deserialize<MissionDefinition>(json, JsonOptions)!).ToList();
        }
    }

    public void DeleteMission(string id)
    {
        lock (_gate)
        {
            _missions.Remove(id);
        }
    }

    // ---------------- Transactions / flush / backup ----------------

    public void RunInTransaction(Action body)
    {
        // No WAL to batch — the dictionaries are the storage. The gate is a reentrant monitor, so a
        // nested call from inside the body behaves exactly like the SQLite reentrant transaction.
        lock (_gate)
        {
            body();
        }
    }

    public void Flush() => Flushed?.Invoke();

    public string CreateBackup(string label)
    {
        byte[] blob = ExportSnapshotBlob();
        Directory.CreateDirectory(_paths.BackupsDirectory);
        string safe = string.Concat(label.Where(char.IsLetterOrDigit));
        string path = Path.Combine(_paths.BackupsDirectory,
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{(safe.Length > 0 ? safe : "backup")}.world.json.gz");
        File.WriteAllBytes(path, blob);
        return path;
    }

    public void Dispose()
    {
        // Nothing unmanaged to release; the host owns blob persistence via Flushed/ExportSnapshotBlob.
    }
}
