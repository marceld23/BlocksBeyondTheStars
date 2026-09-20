// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Player-designed forms that span several blocks (#1961) — a wardrobe two cells high, a table two cells long.
/// ONE registry slot holds the whole form (see <see cref="CustomShape"/>); every placed block carries the form's
/// shape index and yaw, plus which cell it is in descriptor bits 27–30 (<see cref="ShapeCode.CellOf"/>). Cell 0
/// is the anchor: the block the player places. The others are stamped with it and fall with it.
///
/// The rules mirror the two-cell bed (#1846), generalised:
/// <list type="bullet">
/// <item>every footprint cell is put through the checks the anchor passed — free, in reach, not the player's
/// own head, no protected ground, inside the build band — BEFORE the item is consumed;</item>
/// <item>the form turns (yaw) but never tips, so its footprint is always upright;</item>
/// <item>taking ANY cell away takes the whole form away. That is hooked at <c>ServerWorld.SetBlock</c>, the one
/// place every write goes through — so it holds for the mining beam and equally for fire, fluids, falling
/// sand, bombs and stamped structures, none of which know what a form is;</item>
/// <item>the form is ONE item made from as many blocks as it has cells, and gives them back.</item>
/// </list>
/// On ships and stations a player form already becomes a plain cube (structure edits keep prop forms only), so
/// multi-cell forms are a planet/base feature in this version.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How many blocks of material an item carrying this shape index stands for: the cell count of a
    /// registered multi-cell form, 1 for everything else (cube, built-in forms, one-block player forms).</summary>
    internal int MaterialUnitsOfShape(int shapeIndex)
        => ShapeCode.IsCustomShape(shapeIndex) && _customShapes.TryGetValue(shapeIndex, out var stored)
            ? System.Math.Max(1, CustomShape.CellCount(stored.Voxels))
            : 1;

    /// <summary>The registered multi-cell payload behind a descriptor, or null for anything else.</summary>
    private string? MultiCellVoxelsOf(int descriptor)
    {
        int shape = ShapeCode.ShapeOf(descriptor);
        return ShapeCode.IsCustomShape(shape) && _customShapes.TryGetValue(shape, out var stored) && CustomShape.IsMulti(stored.Voxels)
            ? stored.Voxels
            : null;
    }

    /// <summary>
    /// The sibling cells (every cell but the anchor) a form placed at <paramref name="anchor"/> with
    /// <paramref name="descriptor"/> would take — or false when one of them cannot be taken. An ordinary
    /// one-block form succeeds with an empty list.
    /// </summary>
    private bool TryMultiCellSiblings(PlayerSession session, Vector3i anchor, int descriptor, out List<(Vector3i Pos, int Cell)> siblings)
    {
        siblings = new List<(Vector3i, int)>();
        string? voxels = MultiCellVoxelsOf(descriptor);
        if (voxels is null)
        {
            return true;
        }

        int yaw = ShapeCode.OrientationOf(descriptor);
        int cells = CustomShape.CellCount(voxels);
        for (int cell = 1; cell < cells; cell++)
        {
            var (dx, dy, dz) = CustomShape.CellOffset(voxels, cell, yaw);
            var pos = WorldConstants.CanonicalBlock(new Vector3i(anchor.X + dx, anchor.Y + dy, anchor.Z + dz), _world.Circumference);
            if (!IsFreePartnerCell(session, pos))
            {
                return false;
            }

            siblings.Add((pos, cell));
        }

        return true;
    }

    /// <summary>
    /// The checks a SECOND cell of a placement must pass — the ones the first cell went through in
    /// <c>HandlePlace</c>: free (air, or a fluid the block displaces), inside the build band and reach, not the
    /// player's own head cell, and on none of the protected ground. Shared by the bed's foot half and the
    /// sibling cells of a multi-cell form.
    /// </summary>
    private bool IsFreePartnerCell(PlayerSession session, Vector3i cell)
    {
        if (!WithinBuildHeight(cell.Y))
        {
            return false;
        }

        var existing = _world.GetBlock(cell);
        if (!existing.IsAir && !IsFluid(existing.Value))
        {
            return false;
        }

        var feet = session.State.Position;
        int fx = (int)System.Math.Floor(feet.X), fy = (int)System.Math.Floor(feet.Y), fz = (int)System.Math.Floor(feet.Z);
        if (cell.X == fx && cell.Z == fz && cell.Y == fy + 1)
        {
            return false;
        }

        if (!WithinReach(session.State, cell)
            || (!session.State.IsAdmin && IsOnLandingPad(cell))
            || IsStationBlock(cell)
            || IsFactoryProtected(cell, session.State.PlayerId, session.State.IsAdmin)
            || IsBaseProtected(cell, session.State.PlayerId, session.State.IsAdmin))
        {
            return false;
        }

        var cellF = new Vector3f(cell.X, cell.Y, cell.Z);
        return !ShipInteriorContains(cellF) && !ConstructionContains(cellF);
    }

    /// <summary>Stamps the sibling cells of a form whose anchor was just placed: the same block, colour, form,
    /// yaw and paint, each with its own cell index — written, mirrored and broadcast exactly like the anchor.</summary>
    private void StampMultiCellSiblings(PlayerSession session, BlockId block, int tint, int glow, int anchorDescriptor,
        List<(Vector3i Pos, int Cell)> siblings)
    {
        foreach (var (pos, cell) in siblings)
        {
            int descriptor = ShapeCode.WithCell(anchorDescriptor, cell);
            bool intoFluid = IsFluid(_world.GetBlock(pos).Value);
            _world.SetBlock(pos, block, tint, glow, descriptor, session.State.PlayerId);
            WriteBackStationCell(pos, block, tint, glow, descriptor);
            BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = block.Value, Tint = tint, Glow = glow, Shape = descriptor });
            NudgeCreatureBodyChecks(pos);
            if (intoFluid)
            {
                UntrackFluid(pos);
                OnFluidRemoved(pos);
            }
        }
    }

    private bool _clearingMultiCellForm; // re-entrancy guard: clearing a sibling raises the same hook again

    /// <summary>
    /// The <c>ServerWorld.ShapedBlockReplaced</c> subscriber: a cell that WAS part of a multi-cell form has been
    /// overwritten or cleared (by anyone — a miner, fire, a fluid, a bomb, a stamp). The rest of the form goes
    /// with it: every other footprint cell that still holds the same block and the same form is cleared,
    /// mirrored and broadcast. They yield nothing — the form is one item, and whoever mined it already got it.
    /// </summary>
    private void OnShapedBlockReplaced(ServerWorld world, Vector3i pos, BlockId previousBlock, int previousDescriptor)
    {
        if (_clearingMultiCellForm)
        {
            return;
        }

        string? voxels = MultiCellVoxelsOf(previousDescriptor);
        if (voxels is null)
        {
            return;
        }

        int yaw = ShapeCode.OrientationOf(previousDescriptor);
        int shape = ShapeCode.ShapeOf(previousDescriptor);
        int cells = CustomShape.CellCount(voxels);
        int goneCell = ShapeCode.CellOf(previousDescriptor);
        if (goneCell >= cells)
        {
            return; // a descriptor from a wiped-and-reused slot: not this form's cell
        }

        // The anchor is where cell 0 lies; every cell's place follows from it.
        var (gx, gy, gz) = CustomShape.CellOffset(voxels, goneCell, yaw);
        var anchor = new Vector3i(pos.X - gx, pos.Y - gy, pos.Z - gz);

        bool current = ReferenceEquals(world, _world); // broadcasts and fluid wake-ups act on the context world
        _clearingMultiCellForm = true;
        try
        {
            for (int cell = 0; cell < cells; cell++)
            {
                if (cell == goneCell)
                {
                    continue;
                }

                var (dx, dy, dz) = CustomShape.CellOffset(voxels, cell, yaw);
                var sibling = WorldConstants.CanonicalBlock(new Vector3i(anchor.X + dx, anchor.Y + dy, anchor.Z + dz), world.Circumference);
                int there = world.GetShape(sibling);
                if (world.GetBlock(sibling).Value != previousBlock.Value
                    || ShapeCode.ShapeOf(there) != shape
                    || ShapeCode.OrientationOf(there) != yaw
                    || ShapeCode.CellOf(there) != cell)
                {
                    continue; // not (or no longer) this form's cell — never touch a neighbour's block
                }

                world.SetBlock(sibling, BlockId.Air);
                if (!current)
                {
                    continue;
                }

                _miningProgress.Remove(sibling);
                BroadcastToWorld(new BlockChanged { X = sibling.X, Y = sibling.Y, Z = sibling.Z, Block = BlockId.AirValue });
                WriteBackStationCell(sibling, BlockId.Air);
                if (HasFluidNeighbor(sibling))
                {
                    OnFluidRemoved(sibling);
                }

                OnSupportRemoved(sibling);
            }
        }
        finally
        {
            _clearingMultiCellForm = false;
        }
    }
}
