// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>The structure overlays (terrain generation 39) of the in-memory save: one blob per (planet, key), carried in the
/// snapshot blob as <see cref="MemoryWorldSnapshot.StructureOverlays"/> — the SQLite twin's rules.</summary>
public sealed partial class MemoryWorldRepository
{
    private readonly Dictionary<(string Planet, string Key), byte[]> _structureOverlays = new();

    public void SaveStructureOverlay(StoredStructureOverlay overlay)
    {
        lock (_gate)
        {
            _dirty = true;
            _structureOverlays[(overlay.Planet, overlay.Key)] = (byte[])overlay.Data.Clone();
        }
    }

    public IReadOnlyList<StoredStructureOverlay> ListStructureOverlays(string planet)
    {
        lock (_gate)
        {
            return _structureOverlays
                .Where(kv => kv.Key.Planet == planet)
                .OrderBy(kv => kv.Key.Key, StringComparer.Ordinal)
                .Select(kv => new StoredStructureOverlay { Planet = planet, Key = kv.Key.Key, Data = (byte[])kv.Value.Clone() })
                .ToList();
        }
    }

    public void DeleteStructureOverlay(string planet, string key)
    {
        lock (_gate)
        {
            _dirty |= _structureOverlays.Remove((planet, key));
        }
    }

    private bool HasStructureOverlayLocked(string planet)
    {
        foreach (var key in _structureOverlays.Keys)
        {
            if (key.Planet == planet)
            {
                return true;
            }
        }

        return false;
    }

    private List<StoredStructureOverlay> SnapshotStructureOverlaysLocked()
        => _structureOverlays
            .OrderBy(kv => kv.Key.Planet, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Key, StringComparer.Ordinal)
            .Select(kv => new StoredStructureOverlay { Planet = kv.Key.Planet, Key = kv.Key.Key, Data = kv.Value })
            .ToList();

    private void RestoreStructureOverlaysLocked(List<StoredStructureOverlay>? rows)
    {
        _structureOverlays.Clear();
        foreach (var row in rows ?? new List<StoredStructureOverlay>())
        {
            _structureOverlays[(row.Planet, row.Key)] = row.Data ?? Array.Empty<byte>();
        }
    }

    private void RemapStructureOverlaysLocked(IReadOnlyDictionary<ushort, ushort> remap)
    {
        foreach (var key in _structureOverlays.Keys.ToList())
        {
            _structureOverlays[key] = StructureOverlay.RemapBlocks(_structureOverlays[key], remap);
        }
    }
}
