// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Granular blocks (#1319): sand, ash and snow settle when their support goes — the way Lyxette knew it from
/// Luanti ("Kies oder Sand rutschten runter … SEHR hilfreich, um von Höhlendecken in die Tiefe zu kommen",
/// and to fill a lava lake block by block). Built as <b>instant settle</b>, mirroring the fluid automaton:
/// <list type="bullet">
/// <item><b>Through air</b> a woken block jumps straight to its landing cell in one step — one
/// <c>SetBlock</c> pair, two <c>BlockChanged</c>. No falling entity, no animation, no new message; a settled
/// block IS its own state, so nothing mid-fall ever needs saving.</item>
/// <item><b>Through fluid</b> it sinks one cell per step, replacing the fluid cell exactly as a placed block
/// displaces one (#851) — sand dropped on lava eats the lava, one cell at a time.</item>
/// <item><b>Only mutations wake it</b>: mining, placing, the terrain blaster, fire, a retracting fluid, and the
/// cascade above a vacated cell. Generated terrain is never scanned, so a dune overhang stands until touched
/// (Minecraft's rule) and worldgen stays deterministic. The active set is transient — after a restart nothing
/// moves until something touches it, the same lazy discipline as <c>LoadFluidState</c>.</item>
/// <item>A <b>carved</b> form of a granular block is a built thing and stays; dye/glow/paint travel with the
/// fall; a parked ship's interior is never entered and never disturbed; a player standing in the landing cell
/// makes the block wait.</item>
/// <item><b>Landing follows the colliding rule</b> (#1367): a walk-through prop on the way — small flora, a
/// torch, a lantern, a ladder, a flame — is crushed (dropped as items where it has a drop, put out when it is
/// fire) and the block falls on; a doorway cell, an NPC and a player all hold the block up like a player does;
/// a creature the block lands in is nudged to step aside on its next tick (#1357). The settled block keeps its
/// owner (grief attribution, #490) and, when it is weather snow, its melt-tracking entry.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    private const int GranularUpdatesPerStep = 200; // woken cells settled per step (a whole dune face is a few dozen)
    private const int GranularFallScan = 256;        // how far a block may drop in one step (the build-height span)

    private HashSet<Vector3i> _activeGranular => _worlds.Active.ActiveGranular;
    private double _sinceGranular { get => _worlds.Active.SinceGranular; set => _worlds.Active.SinceGranular = value; }

    private bool IsGranular(BlockId id) => !id.IsAir && _world.Definition(id)?.Granular == true;

    /// <summary>Wakes the cell above a vacated <paramref name="pos"/> when it is loose material. Every path that
    /// turns a cell into air calls this (mining, blasting, fire, fluid retraction, a settling block's origin).</summary>
    private void OnSupportRemoved(Vector3i pos) => ActivateGranular(new Vector3i(pos.X, pos.Y + 1, pos.Z));

    /// <summary>Queues a cell for the next settle step if it holds a granular block (placement, a burn-out's
    /// ash, a block that just landed on fluid and may keep sinking).</summary>
    private void ActivateGranular(Vector3i pos)
    {
        if (IsGranular(_world.GetBlock(pos)))
        {
            _activeGranular.Add(pos);
        }
    }

    private void TickGranular(double dt)
    {
        if (_activeGranular.Count == 0)
        {
            _sinceGranular = 0;
            return;
        }

        _sinceGranular += dt;
        if (_sinceGranular < FluidInterval)
        {
            return;
        }

        _sinceGranular = 0;
        _repo.RunInTransaction(StepGranular); // #1505: one commit per step, not per settled cell
    }

    /// <summary>One settle step over the woken cells (the fluid cadence, its own budget).</summary>
    private void StepGranular()
    {
        var todo = new List<Vector3i>(_activeGranular);
        _activeGranular.Clear();
        int budget = GranularUpdatesPerStep;
        HashSet<Vector3i>? doorways = null; // the doorway cells of this world, built once per step and only when needed
        var crushed = new List<Vector3i>();

        foreach (var pos in todo)
        {
            if (budget-- <= 0)
            {
                _activeGranular.Add(pos); // defer leftover to the next step
                continue;
            }

            var id = _world.GetBlock(pos);
            if (!IsGranular(id) || ShapeCode.ShapeOf(_world.GetShape(pos)) != 0 || InShipInterior(pos))
            {
                continue; // gone, carved into a form (a built thing), or part of a parked ship's furnishing
            }

            var below = new Vector3i(pos.X, pos.Y - 1, pos.Z);
            if (!WithinBuildHeight(below.Y) || InShipInterior(below))
            {
                continue;
            }

            var belowId = _world.GetBlock(below);
            Vector3i target;
            if (GranularPassable(belowId))
            {
                doorways ??= _doors.Count > 0 ? DoorCellsWhere(_ => true) : new HashSet<Vector3i>();
                crushed.Clear();
                if (!TryLandingCell(pos, doorways, crushed, out target))
                {
                    _activeGranular.Add(pos); // a player / NPC / doorway is in the way — wait for it to clear
                    continue;
                }
            }
            else if (IsFluid(belowId.Value))
            {
                if (CellOccupiedByPlayer(below) || CellOccupiedByNpc(below))
                {
                    _activeGranular.Add(pos);
                    continue;
                }

                crushed.Clear();
                target = below; // one cell per step through water/lava
            }
            else
            {
                continue; // supported
            }

            SettleGranular(pos, target, id, crushed);
        }
    }

    /// <summary>Whether a falling block passes through a cell (#1367): air, or a block with no collider — the
    /// same rule a walking body uses (<see cref="IsCollidingBlock"/>): small flora, torch, lantern, ladder, a
    /// flame. Fluids and every real block stop it (a canopy too — sand rests on a tree crown).</summary>
    private bool GranularPassable(BlockId id) => !IsCollidingBlock(id, fluidsPass: false, foliagePasses: false);

    /// <summary>The cell a block at <paramref name="pos"/> lands in: straight down through air and walk-through
    /// props (collected in <paramref name="crushed"/> — they are destroyed when the block settles) until the next
    /// cell is a real block or a fluid, the build floor, or a ship interior. False when a player, an NPC or a
    /// doorway occupies a cell on the way — the block waits rather than landing on someone's head or in a gate.</summary>
    private bool TryLandingCell(Vector3i pos, HashSet<Vector3i> doorways, List<Vector3i> crushed, out Vector3i landing)
    {
        landing = pos;
        for (int i = 0; i < GranularFallScan; i++)
        {
            var next = new Vector3i(landing.X, landing.Y - 1, landing.Z);
            if (!WithinBuildHeight(next.Y) || InShipInterior(next))
            {
                break;
            }

            var id = _world.GetBlock(next);
            if (!GranularPassable(id))
            {
                break;
            }

            if (CellOccupiedByPlayer(next) || CellOccupiedByNpc(next)
                || doorways.Contains(WorldConstants.CanonicalBlock(next, _world.Circumference)))
            {
                return false;
            }

            if (!id.IsAir)
            {
                crushed.Add(next);
            }

            landing = next;
        }

        return true;
    }

    /// <summary>Destroys a walk-through prop a settling block fell through (#1367): a flame is put out, anything
    /// else is cleared and its drops (if any) spill onto the ground — the same yield mining it would give, minus
    /// the regrow. <paramref name="overwritten"/> skips the clear for the landing cell itself, which the settled
    /// block has already replaced (<paramref name="id"/> is what stood there). Runs AFTER the block has landed,
    /// so the spill settles on top of it rather than inside it.</summary>
    private void CrushProp(Vector3i cell, BlockId id, bool overwritten)
    {
        if (id.IsAir)
        {
            return;
        }

        if (id.Value == _fireId)
        {
            if (overwritten)
            {
                UntrackFire(cell);
            }
            else
            {
                Extinguish(cell);
            }

            return;
        }

        var def = _world.Definition(id);
        if (!overwritten)
        {
            _world.SetBlock(cell, BlockId.Air);
            BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = BlockId.AirValue });
        }

        if (def is { Drops.Count: > 0 })
        {
            SpillToGround(cell, def.Drops.Select(d => new ItemAmount(d.Item, d.Count)));
        }
    }

    /// <summary>Moves a granular block from <paramref name="from"/> to <paramref name="to"/>, carrying its dye,
    /// glow and paint design (read before clearing — the <c>BreakBlockAt</c> pattern). A fluid at the target
    /// is displaced like a placed block displaces it (#851). Wakes what depended on the vacated cell — the
    /// granular column above (cascade) and any fluid around it — and keeps the landed block active so it can
    /// keep sinking when it came to rest on fluid. The block's owner (#490) and a weather-snow deposit's melt
    /// entry travel with it; the props it fell through (<paramref name="crushed"/>) are destroyed once it has
    /// landed (their drops settle on top of it), and a creature standing in the landing cell is told to step
    /// aside (#1357).</summary>
    private void SettleGranular(Vector3i from, Vector3i to, BlockId id, List<Vector3i> crushed)
    {
        var (tint, glow) = _world.GetModifier(from);
        int design = ShapeCode.DesignOf(_world.GetShape(from));
        int shape = design != 0 ? ShapeCode.WithDesign(0, design) : 0;
        bool intoFluid = IsFluid(_world.GetBlock(to).Value);
        // #1367: a painted/built sand block stays attributed to its builder after the fall — the grief report
        // must still name whoever put it there, not "the server".
        string owner = _repo.GetBlockAttribution(_world.LocationId, WorldConstants.CanonicalBlock(from, _world.Circumference))?.Owner ?? string.Empty;

        _world.SetBlock(from, BlockId.Air);
        _miningProgress.Remove(from);
        BroadcastToWorld(new BlockChanged { X = from.X, Y = from.Y, Z = from.Z, Block = BlockId.AirValue });

        var crushedIds = new List<(Vector3i Cell, BlockId Id)>(crushed.Count);
        foreach (var cell in crushed)
        {
            crushedIds.Add((cell, _world.GetBlock(cell))); // read before the landing cell is overwritten
        }

        _world.SetBlock(to, id, tint, glow, shape, owner);
        BroadcastToWorld(new BlockChanged { X = to.X, Y = to.Y, Z = to.Z, Block = id.Value, Tint = tint, Glow = glow, Shape = shape });
        NudgeCreatureBodyChecks(to); // #1357/#1367: an animal the block landed on steps aside on its next tick
        MoveWeatherDeposit(from, to, id.Value); // fallen weather snow still melts (#1367)
        foreach (var (cell, crushedId) in crushedIds)
        {
            CrushProp(cell, crushedId, overwritten: cell == to);
        }

        if (intoFluid)
        {
            UntrackFluid(to);   // the cell's flowing state (memory + saved level row) goes with the fluid
            OnFluidRemoved(to); // the body around it settles again / flows over the sand
        }

        if (HasFluidNeighbor(from))
        {
            OnFluidRemoved(from); // fluid beside the vacated cell refills it
        }

        OnSupportRemoved(from);    // the next block of the column follows on the next step
        _activeGranular.Add(to);   // landed on fluid → keeps sinking; on solid → next step finds it supported
    }

    /// <summary>True if a joined player on this world stands in the cell (feet or head).</summary>
    private bool CellOccupiedByPlayer(Vector3i cell)
    {
        foreach (var s in JoinedInActiveWorld())
        {
            var feet = s.State.Position.ToBlock();
            if (feet == cell || new Vector3i(feet.X, feet.Y + 1, feet.Z) == cell)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if a settlement / trader / camp NPC on this world stands in the cell (feet or head). NPCs
    /// have no displacement of their own (#1367), so they hold a falling block up exactly like a player.</summary>
    private bool CellOccupiedByNpc(Vector3i cell)
    {
        foreach (var n in _npcs)
        {
            var feet = n.Pos.ToBlock();
            if (feet == cell || new Vector3i(feet.X, feet.Y + 1, feet.Z) == cell)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Test seam: wakes a granular cell as a mutation beside it would (a direct <c>World.SetBlock</c>
    /// in a test stands in for generated terrain and wakes nothing — which is the point of that discipline).</summary>
    public void WakeGranularForTest(int x, int y, int z) => ActivateGranular(new Vector3i(x, y, z));
}
