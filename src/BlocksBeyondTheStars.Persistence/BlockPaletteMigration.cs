// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// Backend-agnostic helpers for the block-id palette migration shared by the SQLite and PostgreSQL
/// repositories. Numeric block ids are assigned by key sort order, so adding a block whose key sorts before
/// existing ones shifts every later id. Persisting the palette (numeric id → key) lets a save rebuild a
/// remap (old id → new id) by matching KEYS, then translate its stored ids to the current assignment.
/// </summary>
internal static class BlockPaletteMigration
{
    /// <summary>Builds old-id → new-id for every stored id whose current id (matched by key) differs. A stored
    /// key that no longer exists in content maps to air (0) — its cells decode to "removed" rather than
    /// mis-decoding to whatever block now holds the old id. Ids that don't change are omitted.</summary>
    public static Dictionary<ushort, ushort> BuildRemap(
        IReadOnlyDictionary<ushort, string> stored,
        IReadOnlyDictionary<ushort, string> current)
    {
        var currentByKey = new Dictionary<string, ushort>(current.Count);
        foreach (var kv in current)
        {
            currentByKey[kv.Value] = kv.Key;
        }

        var remap = new Dictionary<ushort, ushort>();
        foreach (var kv in stored)
        {
            ushort oldId = kv.Key;
            ushort newId = currentByKey.TryGetValue(kv.Value, out var id) ? id : (ushort)0;
            if (newId != oldId)
            {
                remap[oldId] = newId;
            }
        }

        return remap;
    }

    /// <summary>Remaps the block id inside a space-structure cell string ("x:y:z:block" cells joined by ';').
    /// The block is the LAST colon-separated field, so negative coordinates are handled correctly. Returns the
    /// original string unchanged when nothing needed remapping (so callers can skip a no-op DB write).</summary>
    public static string RemapCellString(string blocks, IReadOnlyDictionary<ushort, ushort> remap)
    {
        if (string.IsNullOrEmpty(blocks) || remap.Count == 0)
        {
            return blocks;
        }

        var cells = blocks.Split(';');
        bool changed = false;
        for (int i = 0; i < cells.Length; i++)
        {
            string cell = cells[i];
            if (cell.Length == 0)
            {
                continue;
            }

            int lastColon = cell.LastIndexOf(':');
            if (lastColon < 0)
            {
                continue;
            }

            if (ushort.TryParse(cell.AsSpan(lastColon + 1), out ushort b)
                && remap.TryGetValue(b, out ushort nb) && nb != b)
            {
                cells[i] = string.Concat(cell.AsSpan(0, lastColon + 1), nb.ToString());
                changed = true;
            }
        }

        return changed ? string.Join(";", cells) : blocks;
    }
}
