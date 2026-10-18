// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The one-time repair of self-built ship hulls an earlier block-adding update left in old block ids (#2751).
/// <para>Before #2221 (v2026.10.4) the first-open palette remap rewrote every stored block id of a save EXCEPT the hull of
/// a self-built ship (<see cref="ShipState.BuiltCells"/>). A hull built in release <c>k</c> that went through one or more
/// block-adding updates therefore kept the ids of <c>k</c> while the save's palette moved on to the set of the last
/// release <c>s</c> that opened it; v2026.10.4 then remapped that hull as if it were written in <c>s</c>. Every cell now
/// holds a valid id of the WRONG block — helm, engine and doors are no longer recognised, and the ship reports "needs a
/// helm / engine / door" and cannot launch. The current key of a cell is <c>P_s[x]</c> for its original id <c>x</c>, so
/// the block it was is <c>P_k[id of that key in P_s]</c> (<see cref="HistoricalBlockSets"/> holds every <c>P</c>).</para>
/// <para>The pass runs once per save (<see cref="HullRepairMarker"/> in <see cref="WorldMetadata.StampedFeatures"/>) when
/// the save is opened. Only a self-built ship whose hull fails the basic invariants is looked at (commissioned: exactly
/// one helm, an engine and a door; under construction: a core or a helm). Every (built-in <c>k</c>, last-opened
/// <c>s</c>, <c>k &lt; s</c>) pair is tried; a pair counts only when every cell decodes, every decoded block is one an
/// item places, and the decoded hull passes the invariants. The hull is repaired only when every such pair gives the
/// SAME hull — anything else (no pair, several different readings, a hull mixed from two palettes) is left exactly as
/// it is and named in the log with the copy from before the remap. A backup is taken before the first write. No
/// save-version bump: the hull is written in the current ids like any other.</para>
/// <para>When the copy taken before the remap that misread the hull is still there, <c>s</c> is not guessed
/// (<see cref="PinnedReadAsPalette"/>): the hull remap, the <c>pre-remap_…</c> copies and the save version all came with
/// v2026.10.4, so a <c>pre-remap_…</c> copy whose save version is below <see cref="HullRemapSaveVersion"/> was last written
/// by a build WITHOUT the hull remap — the palette it has on record IS <c>P_s</c>. With <c>s</c> pinned only <c>k</c>
/// varies, which settles most hulls the open search finds two readings for. A palette that is none of the frozen sets,
/// copies that disagree, or no such copy (the browser save keeps none, rotation may have pruned it) fall back to the open
/// search. The copy is only ever read.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The <see cref="WorldMetadata.StampedFeatures"/> entry that marks the hull repair as done for this save.</summary>
    internal const string HullRepairMarker = "save|hull_repair_2751";

    /// <summary>The save version that came with the hull remap (#2221), the <c>pre-remap_…</c> copies and the save-version
    /// stamp itself (all v2026.10.4): a save carrying a lower one was last written by a build that left hulls alone.</summary>
    private const int HullRemapSaveVersion = 2;

    /// <summary>One hull the pass will rewrite.</summary>
    private sealed record HullRepair(string SaveKey, string Owner, string ShipId, ShipState Ship, string Cells, int Changed,
        string BuiltIn, string ReadAs);

    /// <summary>Runs the repair once per save (from <see cref="Start"/>, after the palette is current and the metadata is
    /// loaded; the caller persists the metadata with the marker). A backup that fails leaves the save untouched and the
    /// pass due again at the next start.</summary>
    private void RepairLegacyShipHulls()
    {
        if (_meta.StampedFeatures.Contains(HullRepairMarker))
        {
            return;
        }

        var repairs = new List<HullRepair>();
        HashSet<string>? placeable = null;
        int? pinned = null;
        foreach (var (saveKey, owner, shipId, ship) in SelfBuiltShipsOnRecord())
        {
            var cells = ParseCustomCells(ship.BuiltCells);
            if (HullInvariantsHold(cells.Values, ship.Commissioned))
            {
                continue; // a sound hull is never touched
            }

            placeable ??= new HashSet<string>(
                _content.Items.Values.Select(i => i.PlacesBlock).OfType<string>().Where(k => k.Length > 0), StringComparer.Ordinal);
            pinned ??= PinnedReadAsPalette(); // read once, and only when a hull needs it
            var (remap, builtIn, readAs, problem) = ReadHullThroughHistory(cells, ship.Commissioned, placeable, pinned.Value);
            if (remap is null)
            {
                _log.Warn($"Self-built ship '{shipId}' of '{owner}' cannot fly ({problem}), and its hull cannot be read safely in the block ids " +
                          "of an older version — it was left as it is. The copy taken before the block-id remap (a 'pre-remap_…' backup in " +
                          "the world's backups folder, if the world was updated with v2026.10.4 or later) still holds it as it was.");
                continue;
            }

            string repaired = RemapHullCells(ship.BuiltCells, remap, out int changed);
            repairs.Add(new HullRepair(saveKey, owner, shipId, ship, repaired, changed, builtIn, readAs));
        }

        if (repairs.Count > 0)
        {
            string path;
            try
            {
                path = _repo.CreateBackup(BackupRotation.Label(BackupRotation.PreHullRepairPrefix, BackupUtcNow));
            }
            catch (Exception ex)
            {
                _log.Warn($"{repairs.Count} self-built ship(s) of this world have their hull in the block ids of an older version, but the " +
                          $"backup before the repair failed ({ex.Message}) — nothing was changed; the repair runs again at the next start.");
                return;
            }

            int keep = _repo.SupportsAutomaticBackups ? _config.BackupKeepCount : UpdateCopiesWithoutRotation;
            BackupRotation.Prune(Path.GetDirectoryName(path) ?? string.Empty, BackupRotation.PreHullRepairPrefix, keep);
            foreach (var r in repairs)
            {
                r.Ship.BuiltCells = r.Cells;
                _repo.SaveShip(r.SaveKey, r.Ship);
                _log.Info($"Self-built ship '{r.ShipId}' of '{r.Owner}' was still in the block ids of {r.BuiltIn} (read as {r.ReadAs} by an " +
                          $"earlier update) — its hull was repaired ({r.Changed} cell(s)); backup written to '{path}' before.");
            }
        }

        _meta.StampedFeatures.Add(HullRepairMarker);
    }

    /// <summary>Every self-built ship on record with a hull: the fleet rows of every known player, plus the legacy
    /// single-ship row. A row that cannot be read is skipped (the normal load path reports it).</summary>
    private IEnumerable<(string SaveKey, string Owner, string ShipId, ShipState Ship)> SelfBuiltShipsOnRecord()
    {
        foreach (string playerId in _repo.ListPlayerIds())
        {
            PlayerState? player;
            try
            {
                player = _repo.LoadPlayer(playerId);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
            {
                continue;
            }

            string owner = player is { Name.Length: > 0 } ? player.Name : playerId;
            var rows = new List<(string Key, string ShipId)>();
            foreach (string shipId in player?.FleetShipIds ?? new List<string>())
            {
                rows.Add((FleetShipSaveKey(playerId, shipId), shipId));
            }

            rows.Add((ShipSaveKey(playerId), "ship"));
            foreach (var (key, shipId) in rows)
            {
                ShipState? ship;
                try
                {
                    ship = _repo.LoadShip(key);
                }
                catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
                {
                    continue;
                }

                if (ship is { IsCustom: true, BuiltCells.Length: > 0 })
                {
                    yield return (key, owner, shipId, ship);
                }
            }
        }
    }

    /// <summary>The basic invariants of a self-built hull: commissioned — exactly one helm, an engine and a door (what
    /// commissioning required); under construction — a core or a helm. Air entries are holes, not blocks.</summary>
    private bool HullInvariantsHold(IEnumerable<BlockId> blocks, bool commissioned)
    {
        int helms = 0;
        bool engine = false, door = false, core = false;
        foreach (var b in blocks)
        {
            if (b.IsAir || _content.BlockById(b) is not { } def)
            {
                continue;
            }

            switch (def.Key)
            {
                case ShipHelmBlock:
                    helms++;
                    break;
                case ShipEngineBlock:
                    engine = true;
                    break;
                case ShipCoreBlock:
                    core = true;
                    break;
                default:
                    door |= DoorBlocks.IsDoorBlock(def.Key);
                    break;
            }
        }

        return commissioned ? helms == 1 && engine && door : core || helms > 0;
    }

    /// <summary>Tries every (built-in, last-opened) pair of historical palettes on a hull (see the type summary) — with
    /// <paramref name="pinnedReadAs"/> (an index into <see cref="HistoricalBlockSets.All"/>, or -1) only the pairs with that
    /// last-opened palette. Returns the current-id → repaired-id map when every accepted pair agrees, else null with the
    /// reason for the log.</summary>
    private (Dictionary<ushort, ushort>? Remap, string BuiltIn, string ReadAs, string Problem) ReadHullThroughHistory(
        Dictionary<Vector3i, BlockId> cells, bool commissioned, HashSet<string> placeable, int pinnedReadAs = -1)
    {
        var ids = cells.Values.Where(b => !b.IsAir).Select(b => b.Value).Distinct().ToList();
        var palettes = HistoricalBlockSets.All;
        Dictionary<ushort, ushort>? accepted = null;
        string builtIn = string.Empty, readAs = string.Empty;
        // Pinned to the oldest set there is no older one to have been built in: no pair at all.
        int first = pinnedReadAs >= 0 ? Math.Max(1, pinnedReadAs) : 1;
        int last = pinnedReadAs >= 0 ? pinnedReadAs : palettes.Count - 1;
        for (int s = first; s <= last; s++)
        {
            for (int k = 0; k < s; k++)
            {
                if (DecodeThrough(ids, palettes[k], palettes[s], placeable) is not { } remap
                    || !HullInvariantsHold(cells.Values.Select(b => b.IsAir ? b : new BlockId(remap[b.Value])), commissioned))
                {
                    continue;
                }

                if (accepted is null)
                {
                    accepted = remap;
                    builtIn = palettes[k].Release;
                    readAs = palettes[s].Release;
                }
                else if (!remap.All(kv => accepted[kv.Key] == kv.Value))
                {
                    return (null, string.Empty, string.Empty, "more than one way to read its hull");
                }
            }
        }

        return accepted is null
            ? (null, string.Empty, string.Empty, "no older version's block ids make it whole")
            : (accepted, builtIn, readAs, string.Empty);
    }

    /// <summary>The last-opened palette <c>s</c>, pinned by the copy taken before the remap that misread the hulls (see the
    /// type summary): its index in <see cref="HistoricalBlockSets.All"/>, or -1 to search every pair — no such copy (or a
    /// repository without copies on disk), a copy whose palette is none of the frozen sets, or copies that disagree. Copies
    /// a hull-remapping build wrote (save version <see cref="HullRemapSaveVersion"/> or higher) say nothing about it and are
    /// passed over. Only the SQLite save keeps such copies; they are opened read-only.</summary>
    private int PinnedReadAsPalette()
    {
#if NET10_0_OR_GREATER
        if (_repo is not SqliteWorldRepository sqlite)
        {
            return -1;
        }

        var palettes = HistoricalBlockSets.All;
        int pinned = -1;
        foreach (var copy in sqlite.ReadPreRemapBackups())
        {
            if (copy.SaveVersion >= HullRemapSaveVersion)
            {
                continue;
            }

            int match = -1;
            for (int i = 0; i < palettes.Count && match < 0; i++)
            {
                if (palettes[i].Count == copy.Palette.Count
                    && copy.Palette.All(kv => kv.Key < palettes[i].Keys.Length && palettes[i].Keys[kv.Key] == kv.Value))
                {
                    match = i;
                }
            }

            if (match < 0 || (pinned >= 0 && pinned != match))
            {
                _log.Info($"The copy '{Path.GetFileName(copy.File)}' from before a block-id remap does not name one older version's " +
                          "block set — self-built hulls are read through every older version instead.");
                return -1;
            }

            pinned = match;
        }

        if (pinned >= 0)
        {
            _log.Info($"Self-built hulls are read as {palettes[pinned].Release} block ids — the block set on record in the copy taken " +
                      "before the update that remapped them.");
        }

        return pinned;
#else
        return -1; // the browser save keeps no copies on disk
#endif
    }

    /// <summary>Reads each current id as written in <paramref name="built"/> and remapped as if it were
    /// <paramref name="readAs"/>: current key → its id in <paramref name="readAs"/> → that id's key in
    /// <paramref name="built"/> → its current id. Null when a cell does not fit the pair (a key <paramref name="readAs"/>
    /// did not have, an id past <paramref name="built"/>'s end) or decodes to a block no item places.</summary>
    private Dictionary<ushort, ushort>? DecodeThrough(List<ushort> ids, HistoricalBlockSets.Palette built,
        HistoricalBlockSets.Palette readAs, HashSet<string> placeable)
    {
        var remap = new Dictionary<ushort, ushort>(ids.Count);
        foreach (ushort id in ids)
        {
            if (_content.BlockById(new BlockId(id)) is not { } now
                || !readAs.IdOf.TryGetValue(now.Key, out ushort written)
                || written > built.Count
                || !placeable.Contains(built.Keys[written])
                || _content.GetBlock(built.Keys[written]) is not { } truth)
            {
                return null;
            }

            remap[id] = truth.NumericId.Value;
        }

        return remap;
    }

    /// <summary>The hull string with every cell's block id remapped, cell order and coordinates kept.</summary>
    private static string RemapHullCells(string blob, Dictionary<ushort, ushort> remap, out int changed)
    {
        changed = 0;
        var sb = new StringBuilder(blob.Length);
        string[] cells = blob.Split(';');
        for (int i = 0; i < cells.Length; i++)
        {
            string cell = cells[i];
            if (i > 0)
            {
                sb.Append(';');
            }

            int colon = cell.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(cell.AsSpan(colon + 1), out ushort id) && remap.TryGetValue(id, out ushort to) && to != id)
            {
                sb.Append(cell, 0, colon + 1).Append(to);
                changed++;
            }
            else
            {
                sb.Append(cell);
            }
        }

        return sb.ToString();
    }

    /// <summary>Test seam (#2751): the launch problem of a ship's hull (a locale key), or null when it may fly.</summary>
    internal string? CustomShipLaunchProblemForTest(ShipState ship) => CustomShipLaunchProblem(ship);
}
