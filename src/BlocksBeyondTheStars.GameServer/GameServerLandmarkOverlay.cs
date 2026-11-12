// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// A landmark's cells as a STRUCTURE OVERLAY (terrain generation 39, <see cref="WorldDescription.SchoolClubLandmarksGeneration"/>).
/// A giant landmark has up to 600 000 cells, and as a block edit per cell it was the bulk of a save and of every chunk load
/// in its box. On a world created at generation 39 the record's pin says <c>storage=overlay</c>, and the stamp:
/// <list type="number">
/// <item>runs the very cell list of the cell-by-cell stamp (GameServerLandmarkStamp.cs — the rows from the ground row up with
/// the plinth or the piles from the real terrain, the rows under it with the cavities and the seal stones, the chest crates and
/// their hidden stones, every cell with its dye, glow and shape) into a collecting sink that reads through to the world, so a
/// seal or a hidden stone decides on exactly what the cell-by-cell stamp saw;</item>
/// <item>stores the result as ONE compressed blob (<see cref="StructureOverlay"/>, table <c>structure_overlay</c>, key = the
/// stamped-feature key <c>landmark:&lt;index&gt;</c>) — no block edit row;</item>
/// <item>hands it to the world, which applies it to every chunk of the box when it loads (under the edits) and right away to
/// the chunks already resident, which are re-streamed to the players who hold them;</item>
/// <item>gives a cell that already carried a block edit (an admin's stamp over a player's build) a row with the landmark's
/// block, as the cell-by-cell stamp overwrote it — on a fresh body there is none.</item>
/// </list>
/// What players do afterwards — mining a stone, building inside — is an ordinary block edit on top. The chests, their
/// <c>GeneratedLoot</c> keys and every other marker are re-derived from the record exactly as before. The far silhouette and
/// the streaming lift read the overlay. A record without <c>storage=overlay</c> (every older world) keeps the cell-by-cell
/// stamp, byte-identical.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The pin key that says where a landmark's cells are stored …</summary>
    internal const string LandmarkStoragePin = "storage";

    /// <summary>… and its value for the structure overlay (absent = block edits).</summary>
    internal const string LandmarkOverlayStorage = "overlay";

    /// <summary>Whether a landmark stamped on the active world now stores its cells as a structure overlay.</summary>
    private bool LandmarkOverlayWorld => _meta.Description.TerrainGeneration >= WorldDescription.SchoolClubLandmarksGeneration;

    internal sealed partial class LandmarkInstance
    {
        /// <summary>Whether <see cref="OverlayKey"/> was looked up.</summary>
        public bool OverlayResolved { get; set; }

        /// <summary>The key of the landmark's structure overlay, null for a landmark stored as block edits.</summary>
        public string? OverlayKey { get; set; }
    }

    /// <summary>Collects the stamp's cells (canonical, a later write replacing an earlier one); a read answers the stamp's own
    /// last write of the cell, else the world as it is — terrain, edits and every overlay stamped before.</summary>
    private sealed class OverlayCellSink : IStructureCellSink
    {
        private readonly ServerWorld _world;

        public OverlayCellSink(ServerWorld world) => _world = world;

        public StructureOverlayBuilder Cells { get; } = new();

        public void Set(int x, int y, int z, BlockId block, int tint, int glow, int shape)
            => Cells.Set(WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference), block.Value, tint, glow, shape);

        public BlockId Get(int x, int y, int z)
        {
            var cell = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);
            return Cells.TryGet(cell, out ushort block) ? new BlockId(block) : _world.GetBlock(cell);
        }

        public void Mark(string type, int x, int y, int z, string data)
        {
            // the landmark's markers are re-derived from its record (GameServerLandmarkLife.cs), never from the stamp
        }
    }

    /// <summary>The landmark's placement record: this body, the landmark kind, the template, the seat and the seed of the
    /// pin — null when none matches (a landmark nothing recorded).</summary>
    private StructurePlacementRecord? LandmarkRecordOf(LandmarkInstance lm)
    {
        foreach (var rec in _meta.Placements)
        {
            if (rec.Placed && rec.Kind == LandmarkRules.RecordKind && rec.LocationId == _world.LocationId
                && rec.Template == lm.Key && rec.X == lm.Origin.X && rec.Z == lm.Origin.Z && rec.GroundY == lm.GroundY
                && long.TryParse(MarkerSettings.Parse(rec.KitLayout).Get("seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seed)
                && seed == lm.Seed)
            {
                return rec;
            }
        }

        return null;
    }

    /// <summary>The structure-overlay key of a landmark whose record pins <c>storage=overlay</c>, else null (once per
    /// instance).</summary>
    private string? LandmarkOverlayKey(LandmarkInstance lm)
    {
        if (!lm.OverlayResolved)
        {
            lm.OverlayResolved = true;
            lm.OverlayKey = LandmarkRecordOf(lm) is { } rec
                            && MarkerSettings.Parse(rec.KitLayout).Get(LandmarkStoragePin) == LandmarkOverlayStorage
                ? LandmarkFeature(rec.Index)
                : null;
        }

        return lm.OverlayKey;
    }

    /// <summary>The landmark's structure overlay as the world holds it — null for a landmark stored as block edits, and for
    /// one whose stored blob is gone or cannot be read (warned: its blocks are not in the world then).</summary>
    private StructureOverlay? LandmarkOverlayOf(LandmarkInstance lm)
    {
        if (LandmarkOverlayKey(lm) is not { } key)
        {
            return null;
        }

        var overlay = _world.StructureOverlayFor(key);
        if (overlay is null && !_warnedOverlays.Contains(_world.LocationId + "|" + key))
        {
            _warnedOverlays.Add(_world.LocationId + "|" + key);
            _log.Warn($"Landmark '{lm.Key}' on '{_world.LocationId}' is pinned to the structure overlay '{key}', which " +
                      $"{(_world.StructureOverlays.Broken.Contains(key) ? "cannot be read" : "the save does not hold")} — its blocks are missing.");
        }

        return overlay;
    }

    private readonly HashSet<string> _warnedOverlays = new(System.StringComparer.Ordinal);

    /// <summary>Stamps a landmark as its structure overlay (inside the caller's transaction): collect, store, apply, then the
    /// rows for the cells that carried an edit. Returns the overlay's cells — 0 when a blob is already stored under the key
    /// (stored before a crash cut the feature mark short: its cells already load with every chunk).</summary>
    private int StampLandmarkOverlay(LandmarkInstance lm, string seat, string key)
    {
        if (_world.HasStructureOverlay(key))
        {
            return 0;
        }

        var sink = new OverlayCellSink(_world);
        WriteLandmarkCells(sink, lm, seat);
        var overlay = sink.Cells.Build();
        _repo.SaveStructureOverlay(new StoredStructureOverlay { Planet = _world.LocationId, Key = key, Data = overlay.Encode() });
        var resident = _world.AddStructureOverlay(key, overlay);
        OverrideEditsUnderOverlay(overlay);
        foreach (var session in JoinedInActiveWorld())
        {
            foreach (var coord in resident)
            {
                session.Streaming.SentChunks.Remove(coord); // re-streamed whole on the next pass
            }
        }

        return overlay.CellCount;
    }

    /// <summary>A cell of the overlay that already carries a block edit — an admin's stamp over something built — gets a row
    /// with the overlay's cell: the edits apply over the overlay, and the cell-by-cell stamp overwrote that edit. A row that
    /// already holds the cell's block, dye, glow and shape is left as it is, like <see cref="ServerWorld.SetBlock"/> leaves
    /// it. One edit query per chunk of the overlay; on a fresh body every one comes back empty.</summary>
    private void OverrideEditsUnderOverlay(StructureOverlay overlay)
    {
        foreach (var chunk in overlay.Chunks)
        {
            foreach (var edit in _repo.LoadChunkEdits(_world.LocationId, chunk.Coord))
            {
                var l = WorldConstants.WorldToLocal(edit.WorldPosition);
                int run = chunk.FindRun(WorldConstants.LocalIndex(l.X, l.Y, l.Z));
                if (run < 0)
                {
                    continue; // not a cell of the landmark
                }

                ushort block = chunk.RunBlock(run);
                var (tint, glow, shape) = chunk.RunExtras(run);
                if (edit.Block != block || (edit.Tint & 0xFFFFFF) != tint || (edit.Glow & 0xFFFFFF) != glow || edit.Shape != shape)
                {
                    _repo.SetBlock(_world.LocationId, edit.WorldPosition, block, tint, glow, shape);
                }
            }
        }
    }

    // ---------------------------------------------------------------- what is read from the overlay

    /// <summary>The far silhouette of a landmark (#3497): sampled from its structure overlay — the cells as they stand,
    /// crates and hidden stones included — or, stored as block edits, from its layout as before.</summary>
    private LandmarkSilhouette LandmarkSilhouetteOf(LandmarkInstance lm)
    {
        if (LandmarkOverlayOf(lm) is not { } overlay)
        {
            return LandmarkSilhouette.Build(lm.Layout);
        }

        var s = lm.Structure;
        int g = lm.Layout.GroundRow;
        return LandmarkSilhouette.FromCells(s.Width, s.Height - g, s.Length, OverlayCellsOf(lm, overlay, fromRow: g));
    }

    /// <summary>The overlay's non-air cells inside the landmark's box from template row <paramref name="fromRow"/> up, in
    /// template coordinates relative to that row (X and Z the short way round the seams).</summary>
    private IEnumerable<(int X, int Y, int Z, ushort Block, int Tint)> OverlayCellsOf(LandmarkInstance lm, StructureOverlay overlay, int fromRow)
    {
        var s = lm.Structure;
        int circ = _world.Circumference;
        foreach (var (cell, block, tint, _, _) in overlay.Cells())
        {
            if (block == BlockId.AirValue)
            {
                continue;
            }

            int x = WorldConstants.WrapDeltaX(cell.X - lm.Origin.X, circ);
            int z = (int)WorldConstants.WrapDeltaZ(cell.Z - lm.Origin.Z, circ);
            int y = cell.Y - lm.Origin.Y;
            if (x >= 0 && x < s.Width && z >= 0 && z < s.Length && y >= fromRow && y < s.Height)
            {
                yield return (x, y - fromRow, z, block, tint);
            }
        }
    }

    /// <summary>Hands an overlay landmark the column tops of its overlay for the streaming lift (#3489) — once per instance.</summary>
    private void ResolveLandmarkColumns(LandmarkInstance lm)
    {
        if (lm.ColumnsResolved)
        {
            return;
        }

        lm.ColumnsResolved = true;
        if (LandmarkOverlayOf(lm) is { } overlay)
        {
            var s = lm.Structure;
            var tops = new int[s.Width * s.Length];
            System.Array.Fill(tops, -1);
            foreach (var (x, y, z, _, _) in OverlayCellsOf(lm, overlay, fromRow: 0))
            {
                int i = (x * s.Length) + z;
                tops[i] = System.Math.Max(tops[i], y);
            }

            lm.UseColumnTops(tops);
        }
    }
}
