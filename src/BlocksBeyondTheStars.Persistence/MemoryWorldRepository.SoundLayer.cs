// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>The sound layer of the other side (#3907) in the in-memory save: one entry per sound block, carried in the
/// snapshot blob as <see cref="MemoryWorldSnapshot.SoundCells"/> — the SQLite twin's rules.</summary>
public sealed partial class MemoryWorldRepository
{
    private readonly Dictionary<(string Planet, int X, int Y, int Z), (int Glow, string Owner)> _soundCells = new();

    public void SaveSoundCell(string planet, Vector3i worldPosition, int glow, string owner)
    {
        lock (_gate)
        {
            _dirty = true;
            _soundCells[(planet, worldPosition.X, worldPosition.Y, worldPosition.Z)] = (glow, owner ?? string.Empty);
        }
    }

    public IReadOnlyList<StoredSoundCell> ListSoundCells(string planet)
    {
        lock (_gate)
        {
            return _soundCells
                .Where(kv => kv.Key.Planet == planet)
                .Select(kv => Row(kv.Key, kv.Value))
                .ToList();
        }
    }

    public void DeleteSoundCell(string planet, Vector3i worldPosition)
    {
        lock (_gate)
        {
            _dirty |= _soundCells.Remove((planet, worldPosition.X, worldPosition.Y, worldPosition.Z));
        }
    }

    public int CountSoundCells(string planet, string owner)
    {
        lock (_gate)
        {
            return _soundCells.Count(kv => kv.Key.Planet == planet && kv.Value.Owner == owner);
        }
    }

    private static StoredSoundCell Row((string Planet, int X, int Y, int Z) key, (int Glow, string Owner) value)
        => new() { Planet = key.Planet, X = key.X, Y = key.Y, Z = key.Z, Glow = value.Glow, Owner = value.Owner };

    private List<StoredSoundCell> SnapshotSoundCellsLocked() => _soundCells.Select(kv => Row(kv.Key, kv.Value)).ToList();

    private void RestoreSoundCellsLocked(List<StoredSoundCell>? rows)
    {
        _soundCells.Clear();
        foreach (var row in rows ?? new List<StoredSoundCell>())
        {
            _soundCells[(row.Planet, row.X, row.Y, row.Z)] = (row.Glow, row.Owner ?? string.Empty);
        }
    }
}
