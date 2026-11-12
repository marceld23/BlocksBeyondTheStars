// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Writing a landmark into the world (#3486, #3490) — see GameServerLandmarks.cs. One repo transaction, once:
/// <list type="bullet">
/// <item><b>Above the ground row</b> the monument rule, not the settlement's: NO foundation plate. Only the columns that
/// carry something are touched — cleared from the ground row to their top (so an arch is open and a hall is hollow even
/// where a hill ran through it) and seated on a plinth down to the natural ground. A landmark standing in water gets a
/// pile every fourth column instead of a solid block.</item>
/// <item><b>Below the ground row</b> (a mine's tunnels and caverns) only what the template says: its blocks, and its
/// <i>cavity</i> cells carved to air. An empty cell keeps the planet's own rock. Wherever a cavity meets something that
/// would run in — air, a liquid, loose ground — the cell beside it is sealed with stone, the buried ruin's rule (#3334).</item>
/// <item><b>The chests</b> (#3488): a crate block in every chest marker's cell that exists by its chance.</item>
/// </list>
/// The cells go through an <see cref="IStructureCellSink"/> in this order: straight into the world, a <c>SetBlock</c> per
/// cell (every landmark of a world older than terrain generation 39 — byte-identical to the writer before the sink), or —
/// for a record pinned <c>storage=overlay</c> — into the landmark's structure overlay (GameServerLandmarkOverlay.cs).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far a plinth reaches down to the natural ground under a landmark's foot.</summary>
    private const int LandmarkPlinthDepth = 24;

    /// <summary>How far a pile reaches down through water to the bed.</summary>
    private const int LandmarkPileDepth = 48;

    private static readonly (int X, int Y, int Z)[] LandmarkFaces =
    {
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
    };

    /// <summary>Writes the whole landmark (inside the caller's transaction). Returns the cells written.</summary>
    private int WriteLandmark(LandmarkInstance lm, string seat)
    {
        if (LandmarkOverlayKey(lm) is { } key)
        {
            return StampLandmarkOverlay(lm, seat, key); // generation 39: one blob, no block edit per cell
        }

        return WriteLandmarkCells(new WorldCellSink(_world, canonical: true), lm, seat);
    }

    /// <summary>Every cell of the landmark, in the stamp's order: the rows from the ground row up, the rows under it, the
    /// chests. Returns the cells written.</summary>
    private int WriteLandmarkCells(IStructureCellSink sink, LandmarkInstance lm, string seat)
    {
        bool span = seat == LandmarkRules.SeatSpan; // generation 39: a bridge (GameServerLandmarkSeats.cs)
        int cells = WriteLandmarkAboveGround(sink, lm, seat == "stilts", span);
        if (lm.Layout.GroundRow > 0)
        {
            cells += WriteLandmarkUnderground(sink, lm);
            cells += span ? WriteLandmarkSpanPiles(sink, lm) : 0;
        }

        return cells + WriteLandmarkChests(sink, lm);
    }

    private static void SetLandmarkCell(IStructureCellSink sink, LandmarkInstance lm, int x, int y, int z)
    {
        var s = lm.Structure;
        var (tint, glow) = s.GetModifier(x, y, z);
        var cell = lm.Cell(new Vector3i(x, y, z));
        sink.Set(cell.X, cell.Y, cell.Z, new BlockId(s.Get(x, y, z)), tint, glow, s.GetShape(x, y, z));
    }

    /// <summary>The rows from the ground row up: per column, everything from the ground row to the column's top (its
    /// highest block or cavity), air included, then the plinth under its foot — on a <paramref name="span"/> only where its
    /// foot rests on a bank, never down into the gap it spans.</summary>
    private int WriteLandmarkAboveGround(IStructureCellSink sink, LandmarkInstance lm, bool onWater, bool span = false)
    {
        var s = lm.Structure;
        var layout = lm.Layout;
        int g = layout.GroundRow, n = 0;
        for (int x = 0; x < s.Width; x++)
        {
            for (int z = 0; z < s.Length; z++)
            {
                int top = -1;
                for (int y = s.Height - 1; y >= g && top < 0; y--)
                {
                    if (s.Get(x, y, z) != 0 || layout.IsCavity(x, y, z))
                    {
                        top = y;
                    }
                }

                if (top < 0)
                {
                    continue; // nothing stands in this column — the terrain stays exactly as it is
                }

                for (int y = g; y <= top; y++)
                {
                    SetLandmarkCell(sink, lm, x, y, z);
                }

                n += top - g + 1;
                if (s.Get(x, g, z) != 0 && (!onWater || ((x & 3) == 0 && (z & 3) == 0)) && !(span && SpanGapUnder(lm, x, z)))
                {
                    n += WriteLandmarkPlinth(sink, lm, x, z, onWater ? LandmarkPileDepth : LandmarkPlinthDepth);
                }
            }
        }

        return n;
    }

    /// <summary>The foot block of a column carried down to the natural ground, so nothing hangs in the air on a slope.</summary>
    private int WriteLandmarkPlinth(IStructureCellSink sink, LandmarkInstance lm, int x, int z, int depth)
    {
        var foot = new BlockId(lm.Structure.Get(x, lm.Layout.GroundRow, z));
        int wx = lm.Origin.X + x, wz = lm.Origin.Z + z;
        int floorY = System.Math.Max(_generator.SurfaceHeight(_world.Planet, wx, wz) + 1, lm.GroundY - depth);
        int n = 0;
        for (int y = lm.GroundY - 1; y >= floorY; y--)
        {
            sink.Put(wx, y, wz, foot);
            n++;
        }

        return n;
    }

    /// <summary>The rows under the ground row: the template's blocks, its cavities carved to air, and a stone seal beside
    /// every cavity cell that touches something which is neither part of the landmark nor solid rock.</summary>
    private int WriteLandmarkUnderground(IStructureCellSink sink, LandmarkInstance lm)
    {
        var s = lm.Structure;
        var layout = lm.Layout;
        var shell = AncientRuinBlock("deepslate");
        int n = 0;
        for (int x = 0; x < s.Width; x++)
        {
            for (int y = 0; y < layout.GroundRow; y++)
            {
                for (int z = 0; z < s.Length; z++)
                {
                    bool cavity = layout.IsCavity(x, y, z);
                    if (s.Get(x, y, z) == 0 && !cavity)
                    {
                        continue; // the planet's own rock
                    }

                    SetLandmarkCell(sink, lm, x, y, z);
                    n++;
                    if (cavity)
                    {
                        n += SealLandmarkCavity(sink, lm, x, y, z, shell);
                    }
                }
            }
        }

        return n;
    }

    private int SealLandmarkCavity(IStructureCellSink sink, LandmarkInstance lm, int x, int y, int z, BlockId shell)
    {
        var s = lm.Structure;
        int n = 0;
        foreach (var (dx, dy, dz) in LandmarkFaces)
        {
            int nx = x + dx, ny = y + dy, nz = z + dz;
            if (s.InBounds(nx, ny, nz) && (s.Get(nx, ny, nz) != 0 || lm.Layout.IsCavity(nx, ny, nz)))
            {
                continue; // the landmark's own cell
            }

            n += SealLandmarkCell(sink, lm.Cell(new Vector3i(nx, ny, nz)), shell);
        }

        return n;
    }

    /// <summary>The buried ruin's seal (<see cref="SealAncientRuinCell"/>) through a sink: <paramref name="shell"/> into a cell
    /// that would let something in — air, a liquid, loose (granular) ground or anything not solid. Returns 1 when it wrote.</summary>
    private int SealLandmarkCell(IStructureCellSink sink, Vector3i cell, BlockId shell)
    {
        if (_content.BlockById(sink.Get(cell.X, cell.Y, cell.Z)) is { Solid: true, Liquid: false, Granular: false })
        {
            return 0;
        }

        sink.Put(cell.X, cell.Y, cell.Z, shell);
        return 1;
    }

    // ---------------------------------------------------------------- chests

    /// <summary>Whether a chest marker's chest exists (settings <c>chance</c>, default 1) — a hash of the world seed, the
    /// body and the marker's cell, so the stamp and every later load agree.</summary>
    private bool LandmarkChestExists(Vector3i cell, MarkerSettings settings) => MarkerChestExists(LandmarkChestLane, cell, settings);

    /// <summary>Whether a chest marker's chest exists (#3488; shared with the settlements and stations since #3519): the
    /// <c>chance</c> setting against a hash of the world seed, the body, the marker's cell and the owner's lane.</summary>
    private bool MarkerChestExists(string lane, Vector3i cell, MarkerSettings settings)
    {
        double chance = settings.GetDouble("chance", 1.0);
        return chance >= 1.0
               || StableHashing.Unit($"{lane}:chest:exists:{_world.LocationId}:{cell.X}:{cell.Y}:{cell.Z}", _meta.Seed) < chance;
    }

    /// <summary>A crate block in every existing chest's cell (settings <c>block</c>, default the wooden crate), and — with
    /// <c>hidden=1</c> — a stone on top of it when the cell above is free.</summary>
    private int WriteLandmarkChests(IStructureCellSink sink, LandmarkInstance lm)
    {
        int n = 0;
        foreach (var m in lm.Structure.Markers)
        {
            if (m.Type != LandmarkRules.ChestMarker)
            {
                continue;
            }

            var settings = MarkerSettings.Parse(m.Data);
            var cell = WorldConstants.CanonicalBlock(lm.Cell(m.LocalPos), _world.Circumference);
            if (!LandmarkChestExists(cell, settings))
            {
                continue;
            }

            n += WriteMarkerChestBlock(sink, cell, settings);
        }

        return n;
    }

    /// <summary>One chest's crate block (settings <c>block</c>, default the wooden crate) and — with <c>hidden=1</c> — a stone
    /// on top of it when the cell above is free (#3488; shared with the settlements and stations since #3519). Returns the
    /// cells written.</summary>
    private int WriteMarkerChestBlock(Vector3i cell, MarkerSettings settings)
        => WriteMarkerChestBlock(new WorldCellSink(_world, canonical: true), cell, settings);

    /// <summary><see cref="WriteMarkerChestBlock(Vector3i, MarkerSettings)"/> through a sink.</summary>
    private int WriteMarkerChestBlock(IStructureCellSink sink, Vector3i cell, MarkerSettings settings)
    {
        string key = settings.Get("block", LandmarkChestBlock);
        var block = _content.GetBlock(ContainerSystem.IsContainerBlock(key) ? key : LandmarkChestBlock) ?? _content.GetBlock("crate");
        if (block is null)
        {
            return 0;
        }

        sink.Put(cell.X, cell.Y, cell.Z, block.NumericId);
        var above = new Vector3i(cell.X, cell.Y + 1, cell.Z);
        if (settings.GetBool("hidden") && sink.Get(above.X, above.Y, above.Z).IsAir)
        {
            sink.Put(above.X, above.Y, above.Z, AncientRuinBlock("stone"));
            return 2;
        }

        return 1;
    }
}
