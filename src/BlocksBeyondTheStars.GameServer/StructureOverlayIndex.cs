// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The structure overlays of one world (terrain generation 39, <see cref="StructureOverlay"/>), decoded once and indexed by
/// chunk: <see cref="ServerWorld"/> applies the cells of a chunk whenever it stores that chunk, before the players' block
/// edits. Overlays apply in stamp order — a later one over an earlier one where they meet, as the cell-by-cell stamp wrote
/// them — which for the record keys (<c>landmark:&lt;index&gt;</c>) is the order of their numeric suffix.
/// </summary>
internal sealed class StructureOverlayIndex
{
    private readonly List<(string Key, StructureOverlay Overlay)> _overlays = new();
    private readonly Dictionary<ChunkCoord, List<StructureOverlayChunk>> _byChunk = new();
    private readonly HashSet<string> _broken = new(System.StringComparer.Ordinal);

    /// <summary>The overlays held, in the order they apply.</summary>
    public int Count => _overlays.Count;

    /// <summary>The keys whose stored blob could not be read (a damaged row): their cells are not applied.</summary>
    public IReadOnlyCollection<string> Broken => _broken;

    /// <summary>Every overlay a body has in the store, in stamp order.</summary>
    public static StructureOverlayIndex Load(IWorldRepository repo, string location)
    {
        var index = new StructureOverlayIndex();
        var rows = new List<StoredStructureOverlay>(repo.ListStructureOverlays(location));
        rows.Sort((a, b) => CompareKeys(a.Key, b.Key));
        foreach (var row in rows)
        {
            if (StructureOverlay.TryDecode(row.Data, out var overlay))
            {
                index.Add(row.Key, overlay);
            }
            else
            {
                index._broken.Add(row.Key);
            }
        }

        return index;
    }

    public bool Contains(string key) => Get(key) is not null;

    public StructureOverlay? Get(string key)
    {
        foreach (var (k, overlay) in _overlays)
        {
            if (string.Equals(k, key, System.StringComparison.Ordinal))
            {
                return overlay;
            }
        }

        return null;
    }

    /// <summary>Adds an overlay after every one held (a fresh stamp applies over the older ones).</summary>
    public void Add(string key, StructureOverlay overlay)
    {
        _overlays.Add((key, overlay));
        foreach (var chunk in overlay.Chunks)
        {
            if (!_byChunk.TryGetValue(chunk.Coord, out var list))
            {
                list = new List<StructureOverlayChunk>(1);
                _byChunk[chunk.Coord] = list;
            }

            list.Add(chunk);
        }
    }

    /// <summary>The overlay cells of a canonical chunk in the order they apply, or null when none touches it.</summary>
    public List<StructureOverlayChunk>? At(ChunkCoord canonical) => _byChunk.TryGetValue(canonical, out var list) ? list : null;

    /// <summary>Stamp order of two keys: by the text before the last ':' and then by the number after it (so
    /// <c>landmark:10</c> comes after <c>landmark:9</c>); keys without a number compare as text.</summary>
    internal static int CompareKeys(string a, string b)
    {
        int ia = a.LastIndexOf(':'), ib = b.LastIndexOf(':');
        if (ia >= 0 && ib >= 0
            && string.CompareOrdinal(a, 0, b, 0, System.Math.Max(ia, ib)) == 0 && ia == ib
            && long.TryParse(a.Substring(ia + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long na)
            && long.TryParse(b.Substring(ib + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long nb))
        {
            return na.CompareTo(nb);
        }

        return string.CompareOrdinal(a, b);
    }
}
