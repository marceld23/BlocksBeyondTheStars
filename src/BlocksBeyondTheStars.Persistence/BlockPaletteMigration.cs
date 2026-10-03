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
                // Substring instead of span Concat (absent from netstandard2.1, the Unity flavor) —
                // this is the rare palette-migration path, the allocation is irrelevant.
                cells[i] = cell.Substring(0, lastColon + 1) + nb;
                changed = true;
            }
        }

        return changed ? string.Join(";", cells) : blocks;
    }

#if NET10_0_OR_GREATER
    // Only the database repositories rewrite a raw row; the in-memory one (the netstandard2.1 flavor's only
    // repository) holds typed snapshots and remaps the hull string through them.

    /// <summary>Remaps the hull of a self-built ship inside its persisted JSON row (#2221). The hull is not a
    /// column: <see cref="ShipSnapshot.BuiltCells"/> holds it as "x:y:z:block" cells, the format a player station
    /// uses, so it needs the same translation — without it an old save decodes the hull with shifted ids (helm,
    /// engine and core are no longer recognised). Only that one property is rewritten; everything else in the row
    /// stays as it was written, fields this build does not know included. Returns the SAME string instance when
    /// nothing changed (a content ship has no hull string) so callers can skip a no-op write; a row that is not
    /// readable JSON is left alone for the normal load path to report.</summary>
    public static string RemapShipJson(string json, IReadOnlyDictionary<ushort, ushort> remap)
    {
        if (string.IsNullOrEmpty(json) || remap.Count == 0)
        {
            return json;
        }

        System.Text.Json.Nodes.JsonNode? root;
        try
        {
            root = System.Text.Json.Nodes.JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }

        if (root is not System.Text.Json.Nodes.JsonObject ship
            || ship[nameof(ShipSnapshot.BuiltCells)] is not System.Text.Json.Nodes.JsonValue value
            || !value.TryGetValue(out string? cells)
            || cells is null
            || cells.Length == 0)
        {
            return json;
        }

        string remapped = RemapCellString(cells, remap);
        if (ReferenceEquals(remapped, cells))
        {
            return json;
        }

        ship[nameof(ShipSnapshot.BuiltCells)] = remapped;
        return ship.ToJsonString();
    }
#endif
}
