// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What the save keeps of a DERIVED station (#3613) — see GameServerSpaceStructureDerived.cs:
/// <list type="bullet">
/// <item><b>The delta blob</b> (<c>structure_overlay</c>, location <see cref="DerivedDeltaLocation"/>, key
/// <c>delta:&lt;id&gt;</c>): every cell that differs from the base build. An edit only folds its cell into the in-memory
/// delta and marks the station dirty; the blob is re-encoded and written COALESCED — once the station has been dirty for
/// <see cref="DerivedFlushSeconds"/>, and on every save (<see cref="SaveAll"/>: the autosave, a checkpoint, the clean stop) —
/// so a hundred blocks mined in a second cost one write, not a hundred (a 50 000-change blob deflates for ~15 ms on a desktop
/// and several times that on a Raspberry Pi). The door records ride along with the same flush, only when the doors changed.</item>
/// <item><b>The record</b> (the named blob <c>derived-station:&lt;id&gt;</c>, a few bytes of JSON): the stamp origin and anchor
/// the interior world was laid at — once stored, never recomputed from a rebuilt base, so a later generator or content change
/// can never shift every later edit onto the wrong cell — and the caller's hash of the base build the stored changes were
/// made on (<see cref="DerivedStoredBaseHash"/>). Written with the first stored change or the first boarding, whichever comes
/// first; a station stored before the record existed gets it at its next registration, with the anchor it was made with
/// (the base build's minimum).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The <c>structure_overlay</c> location the derived stations' delta blobs live under — no body, never a world.</summary>
    internal const string DerivedDeltaLocation = "space";

    /// <summary>How long a derived station's changes may wait in memory before the blob is written.</summary>
    private const double DerivedFlushSeconds = 1.0;

    /// <summary>Derived stations whose changes (delta, doors, record) wait for the next flush.</summary>
    private readonly HashSet<string> _derivedDirty = new(System.StringComparer.Ordinal);

    /// <summary>Seconds since <see cref="_derivedDirty"/> became non-empty.</summary>
    private double _derivedDirtyAge;

    /// <summary>The key of a derived station's delta blob: <c>delta:&lt;id&gt;</c>.</summary>
    internal static string DerivedDeltaKey(string id) => "delta:" + id;

    /// <summary>The key of a derived station's interior overlay in its world: <c>derived:&lt;id&gt;</c>.</summary>
    internal static string DerivedOverlayKey(string id) => "derived:" + id;

    /// <summary>The named blob of a derived station's record: <c>derived-station:&lt;id&gt;</c>.</summary>
    internal static string DerivedRecordKey(string id) => "derived-station:" + id;

    /// <summary>A derived station's stored record: where its interior is laid, and which base its changes were made on.</summary>
    private sealed class DerivedRecord
    {
        public Vector3i Origin { get; init; }

        public Vector3i Anchor { get; init; }

        public string BaseHash { get; init; } = string.Empty;
    }

    // ---------------------------------------------------------------- the API for the caller

    /// <summary>The hash of the base build a derived station's stored changes were made on, as its caller registered it —
    /// "" when stored without one, null when the save holds no record (nothing of the station stored yet). Readable before
    /// the station is registered, so the caller can keep a wreck whose build changed away instead of laying old changes over
    /// a new build.</summary>
    internal string? DerivedStoredBaseHash(string id)
        => _derivedStations.TryGetValue(id, out var d) ? d.StoredBaseHash : LoadDerivedRecord(id)?.BaseHash;

    /// <summary>Whether the save holds anything of a derived station: its record, its delta blob, its interior overlay or its
    /// door records.</summary>
    internal bool DerivedHasStoredState(string id)
        => _repo.LoadNamedBlob(DerivedRecordKey(id)) is { Length: > 0 }
           || _repo.ListStructureOverlays(DerivedDeltaLocation).Any(o => o.Key == DerivedDeltaKey(id))
           || _repo.ListStructureOverlays(StationLocationIdPrefix + id).Any(o => o.Key == DerivedOverlayKey(id))
           || _repo.LoadStructureEdits(id).Count > 0;

    // ---------------------------------------------------------------- the record

    private DerivedRecord? LoadDerivedRecord(string id)
    {
        if (_repo.LoadNamedBlob(DerivedRecordKey(id)) is not { Length: > 0 } json)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new DerivedRecord
            {
                Origin = ReadCell(root.GetProperty("origin")),
                Anchor = ReadCell(root.GetProperty("anchor")),
                BaseHash = root.TryGetProperty("baseHash", out var h) ? h.GetString() ?? string.Empty : string.Empty,
            };
        }
        catch (System.Exception ex) when (ex is JsonException or KeyNotFoundException or System.InvalidOperationException
                                              or System.FormatException or System.IndexOutOfRangeException)
        {
            _log.Warn($"Derived station '{id}': its record cannot be read ({ex.Message}) — anchored at the base build.");
            return null;
        }
    }

    private static Vector3i ReadCell(JsonElement e) => new(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32());

    /// <summary>Writes the record of a derived station once — the anchor it is laid at now and the base hash it was registered
    /// with. No-op when the save already holds one.</summary>
    private void SaveDerivedRecordOnce(BoardableStation station, DerivedStation d)
    {
        if (d.StoredBaseHash is not null)
        {
            return;
        }

        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            WriteCell(w, "origin", station.Origin);
            WriteCell(w, "anchor", station.StampMin);
            w.WriteString("baseHash", d.BaseHash);
            w.WriteEndObject();
        }

        _repo.SaveNamedBlob(DerivedRecordKey(station.Id), Encoding.UTF8.GetString(buffer.ToArray()));
        d.StoredBaseHash = d.BaseHash;
    }

    private static void WriteCell(Utf8JsonWriter w, string name, Vector3i c)
    {
        w.WriteStartArray(name);
        w.WriteNumberValue(c.X);
        w.WriteNumberValue(c.Y);
        w.WriteNumberValue(c.Z);
        w.WriteEndArray();
    }

    /// <summary>At registration: a station stored before the record existed gets one now (its anchor was the base build's
    /// minimum, which it is registered with); a record naming another base than the caller's is reported — the stored anchor
    /// keeps the interior in place, and whether such a station should be shown at all is the caller's decision.</summary>
    private void AdoptDerivedRecord(BoardableStation station, DerivedStation d, bool storedState, DerivedRecord? record)
    {
        if (record is null)
        {
            if (storedState)
            {
                SaveDerivedRecordOnce(station, d);
                _log.Info($"Derived station '{station.Id}': recorded the anchor its stored changes were made with.");
            }

            return;
        }

        if (d.BaseHash.Length > 0 && record.BaseHash.Length > 0 && record.BaseHash != d.BaseHash)
        {
            _log.Warn($"Derived station '{station.Id}' is registered on a base build other than the one its stored changes " +
                      "were made on — they are laid over it at the stored anchor.");
        }
    }

    // ---------------------------------------------------------------- the delta

    /// <summary>Lays the stored delta blob over the base build. True when the save holds one (anything was ever stored).</summary>
    private bool ApplyStoredDerivedDelta(SpaceStructure s, DerivedStation d)
    {
        string key = DerivedDeltaKey(s.Id);
        var row = _repo.ListStructureOverlays(DerivedDeltaLocation).FirstOrDefault(o => o.Key == key);
        if (row is null)
        {
            return false;
        }

        d.DeltaStored = true;
        if (!StructureOverlay.TryDecode(row.Data, out var delta))
        {
            _log.Warn($"Derived station '{s.Id}': its stored changes cannot be read — it stands as built.");
            return true;
        }

        foreach (var (cell, block, tint, glow, shape) in delta.Cells())
        {
            s.Set(cell, new BlockId(block), tint, glow, shape);
            d.Delta[cell] = (block, tint, glow, shape);
        }

        return true;
    }

    /// <summary>A derived station changed (#3613): the cells touched since the last call are compared with the base (back to
    /// the base = no longer a change) and the station waits for the next coalesced flush. Only the very FIRST change of a
    /// station nothing was stored of is written at once — a few bytes, together with the record (the anchor) — so the save
    /// knows from the first block on that the station holds changes.</summary>
    private void PersistDerivedStation(SpaceStructure s)
    {
        if (!_derivedStations.TryGetValue(s.Id, out var d))
        {
            return;
        }

        FoldTouchedCells(s, d);
        if (!d.DeltaStored)
        {
            WriteDerivedStation(s, d);
            return;
        }

        if (_derivedDirty.Count == 0)
        {
            _derivedDirtyAge = 0;
        }

        _derivedDirty.Add(s.Id);
    }

    private static void FoldTouchedCells(SpaceStructure s, DerivedStation d)
    {
        if (s.Touched is not { Count: > 0 } touched)
        {
            return;
        }

        foreach (var cell in touched)
        {
            ushort live = s.Cells.TryGetValue(cell, out var b) ? b.Value : (ushort)0;
            var (tint, glow, shape) = live == 0 ? (0, 0, 0) : CellExtras(s, cell);
            var now = (live, tint, glow, shape);
            if (DerivedBaseAt(d, cell) == now)
            {
                d.DeltaDirty |= d.Delta.Remove(cell); // back to how it was built: no longer a change
            }
            else if (!d.Delta.TryGetValue(cell, out var had) || had != now)
            {
                d.Delta[cell] = now;
                d.DeltaDirty = true;
            }
        }

        touched.Clear();
    }

    /// <summary>Once per tick: writes the waiting derived stations once they have waited <see cref="DerivedFlushSeconds"/>.</summary>
    private void TickDerivedDeltas(double dt)
    {
        if (_derivedDirty.Count == 0)
        {
            return;
        }

        _derivedDirtyAge += dt;
        if (_derivedDirtyAge >= DerivedFlushSeconds)
        {
            FlushDerivedStations();
        }
    }

    /// <summary>Writes every waiting derived station — its delta blob (when it changed), its record (once) and its door records
    /// (when the doors changed) — in one transaction. Runs from the tick and from every <see cref="SaveAll"/>.</summary>
    private void FlushDerivedStations()
    {
        if (_derivedDirty.Count == 0)
        {
            return;
        }

        _repo.RunInTransaction(() =>
        {
            foreach (string id in _derivedDirty)
            {
                if (_derivedStations.TryGetValue(id, out var d) && _playerStationCells.TryGetValue(id, out var s))
                {
                    WriteDerivedStation(s, d);
                }
            }
        });
        _derivedDirty.Clear();
        _derivedDirtyAge = 0;
    }

    private void WriteDerivedStation(SpaceStructure s, DerivedStation d)
    {
        FoldTouchedCells(s, d);
        if (_stationsById.TryGetValue(s.Id, out var station))
        {
            SaveDerivedRecordOnce(station, d);
        }

        if (d.DeltaDirty || !d.DeltaStored)
        {
            var builder = new StructureOverlayBuilder();
            foreach (var (cell, v) in d.Delta)
            {
                builder.Set(cell, v.Block, v.Tint, v.Glow, v.Shape);
            }

            _repo.SaveStructureOverlay(new StoredStructureOverlay
            {
                Planet = DerivedDeltaLocation,
                Key = DerivedDeltaKey(s.Id),
                Data = builder.Build().Encode(),
            });
            d.DeltaStored = true;
            d.DeltaDirty = false;
            d.DeltaWrites++;
        }

        string doors = DoorSignatureOf(s);
        if (doors != d.DoorSignature)
        {
            d.DoorSignature = doors;
            PersistStationDoors(s);
        }
    }

    /// <summary>The door set of a structure as text (cell, axis, kind — in cell order), to tell whether it changed.</summary>
    private static string DoorSignatureOf(SpaceStructure s)
    {
        var sb = new StringBuilder();
        foreach (var (cell, axisX) in s.PlacedDoorAxes.OrderBy(d => d.Key.X).ThenBy(d => d.Key.Y).ThenBy(d => d.Key.Z))
        {
            sb.Append(cell.X).Append(',').Append(cell.Y).Append(',').Append(cell.Z).Append(axisX ? 'x' : 'z')
                .Append(s.DoorKinds.TryGetValue(cell, out var k) ? k : string.Empty).Append(';');
        }

        return sb.ToString();
    }
}
