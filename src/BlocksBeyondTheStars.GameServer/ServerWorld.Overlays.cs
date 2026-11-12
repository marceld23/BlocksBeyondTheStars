// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Structure overlays (terrain generation 39): the cells of a placed landmark live in ONE stored blob instead of a block edit
/// per cell. The world loads every overlay of its body the first time it stores a chunk; <see cref="Store"/> applies a chunk's
/// overlay cells to the freshly generated chunk and only then its block edits, so a player's change — a mined stone, a placed
/// block — is an ordinary edit that wins. An overlay cell counts as built (<see cref="ChunkData.MarkEditedLocal"/>) exactly
/// where a block edit would have made it built: where it differs from what the chunk held (the cell-by-cell stamp wrote no
/// row for a cell that already held its block). Applying cells wakes no fluid — neither did the stamp's <c>SetBlock</c>s:
/// fluids wake on a player's edit, never on a write by the server, and the overlay carries the stamp's seal stones.
/// </summary>
public sealed partial class ServerWorld
{
    private StructureOverlayIndex? _structureOverlays;

    /// <summary>This body's structure overlays, read from the store on first use.</summary>
    internal StructureOverlayIndex StructureOverlays => _structureOverlays ??= StructureOverlayIndex.Load(_repo, LocationId);

    /// <summary>Whether this body holds a structure overlay under <paramref name="key"/>.</summary>
    internal bool HasStructureOverlay(string key) => StructureOverlays.Contains(key);

    /// <summary>The structure overlay stored under <paramref name="key"/>, or null.</summary>
    internal StructureOverlay? StructureOverlayFor(string key) => StructureOverlays.Get(key);

    /// <summary>Takes a freshly stamped overlay (already stored by the caller): every chunk that loads from now on gets its
    /// cells, and a chunk that is resident gets them right away — with <see cref="BlockSet"/> and
    /// <see cref="ShapedBlockReplaced"/> for every cell that changed, as the stamp's <see cref="SetBlock"/>s raised them.
    /// Nothing is persisted here. Returns the resident chunks that changed (the caller re-streams them to the players who
    /// hold them).</summary>
    internal IReadOnlyList<ChunkCoord> AddStructureOverlay(string key, StructureOverlay overlay)
    {
        if (!StructureOverlays.Contains(key))
        {
            StructureOverlays.Add(key, overlay); // a first use reads the just-saved row already — never index it twice
        }

        var touched = new List<ChunkCoord>();
        var changes = new List<(int Local, BlockId Was, int WasShape)>();
        foreach (var cells in overlay.Chunks)
        {
            if (!_loaded.TryGetValue(cells.Coord, out var chunk))
            {
                continue;
            }

            changes.Clear();
            if (ApplyOverlayCells(chunk, cells, changes) == 0)
            {
                continue;
            }

            touched.Add(cells.Coord);
            RaiseOverlayEvents(chunk, cells.Coord, changes);
        }

        return touched;
    }

    private void RaiseOverlayEvents(ChunkData chunk, ChunkCoord coord, List<(int Local, BlockId Was, int WasShape)> changes)
    {
        var origin = WorldConstants.ChunkOrigin(coord);
        foreach (var (local, was, wasShape) in changes)
        {
            int x = StructureOverlay.LocalX(local), y = StructureOverlay.LocalY(local), z = StructureOverlay.LocalZ(local);
            var world = new Vector3i(origin.X + x, origin.Y + y, origin.Z + z);
            BlockSet?.Invoke(world);
            int shape = chunk.GetShapeLocal(local);
            if (wasShape != 0
                && (was.Value != chunk.Get(x, y, z).Value || ShapeCode.WithoutDesign(wasShape) != ShapeCode.WithoutDesign(shape)))
            {
                ShapedBlockReplaced?.Invoke(world, was, wasShape);
            }
        }
    }

    /// <summary>Writes one overlay chunk's cells into a chunk — <see cref="SetBlock"/>'s cell rule without the row: a cell that
    /// already holds exactly the block, dye, glow and shape is left alone (and not marked built), every other one is set and
    /// marked. Returns the cells that changed; <paramref name="changes"/> (when given) collects what each held before.</summary>
    private static int ApplyOverlayCells(ChunkData chunk, StructureOverlayChunk cells, List<(int Local, BlockId Was, int WasShape)>? changes)
    {
        int changed = 0;
        for (int r = 0; r < cells.RunCount; r++)
        {
            var block = new BlockId(cells.RunBlock(r));
            var (tint, glow, shape) = cells.RunExtras(r);
            int end = cells.RunStart(r) + cells.RunLength(r);
            for (int local = cells.RunStart(r); local < end; local++)
            {
                int x = StructureOverlay.LocalX(local), y = StructureOverlay.LocalY(local), z = StructureOverlay.LocalZ(local);
                var previous = chunk.Get(x, y, z);
                var modifier = chunk.GetModifierLocal(local);
                int previousShape = chunk.GetShapeLocal(local);
                if (previous.Value == block.Value && modifier.Tint == tint && modifier.Glow == glow && previousShape == shape)
                {
                    continue;
                }

                chunk.Set(x, y, z, block); // clears any old modifier/shape when set to air
                chunk.SetModifierLocal(local, tint, glow);
                chunk.SetShapeLocal(local, shape);
                chunk.MarkEditedLocal(local); // built, not natural (air is never marked)
                changes?.Add((local, previous, previousShape));
                changed++;
            }
        }

        return changed;
    }
}
