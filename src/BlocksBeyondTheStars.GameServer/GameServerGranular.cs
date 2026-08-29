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
        StepGranular();
    }

    /// <summary>One settle step over the woken cells (the fluid cadence, its own budget).</summary>
    private void StepGranular()
    {
        var todo = new List<Vector3i>(_activeGranular);
        _activeGranular.Clear();
        int budget = GranularUpdatesPerStep;

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
            if (belowId.IsAir)
            {
                if (!TryLandingCell(pos, out target))
                {
                    _activeGranular.Add(pos); // a player stands in the way — wait for them to move
                    continue;
                }
            }
            else if (IsFluid(belowId.Value))
            {
                if (CellOccupiedByPlayer(below))
                {
                    _activeGranular.Add(pos);
                    continue;
                }

                target = below; // one cell per step through water/lava
            }
            else
            {
                continue; // supported
            }

            SettleGranular(pos, target, id);
        }
    }

    /// <summary>The cell a block at <paramref name="pos"/> lands in: straight down through air until the next
    /// cell is not air (solid or fluid), the build floor, or a ship interior. False when a player occupies a
    /// cell on the way — the block waits rather than landing on someone's head.</summary>
    private bool TryLandingCell(Vector3i pos, out Vector3i landing)
    {
        landing = pos;
        for (int i = 0; i < GranularFallScan; i++)
        {
            var next = new Vector3i(landing.X, landing.Y - 1, landing.Z);
            if (!WithinBuildHeight(next.Y) || !_world.GetBlock(next).IsAir || InShipInterior(next))
            {
                break;
            }

            if (CellOccupiedByPlayer(next))
            {
                return false;
            }

            landing = next;
        }

        return true;
    }

    /// <summary>Moves a granular block from <paramref name="from"/> to <paramref name="to"/>, carrying its dye,
    /// glow and paint design (read before clearing — the <c>BreakBlockAt</c> pattern). A fluid at the target
    /// is displaced like a placed block displaces it (#851). Wakes what depended on the vacated cell — the
    /// granular column above (cascade) and any fluid around it — and keeps the landed block active so it can
    /// keep sinking when it came to rest on fluid.</summary>
    private void SettleGranular(Vector3i from, Vector3i to, BlockId id)
    {
        var (tint, glow) = _world.GetModifier(from);
        int design = ShapeCode.DesignOf(_world.GetShape(from));
        int shape = design != 0 ? ShapeCode.WithDesign(0, design) : 0;
        bool intoFluid = IsFluid(_world.GetBlock(to).Value);

        _world.SetBlock(from, BlockId.Air);
        _miningProgress.Remove(from);
        BroadcastToWorld(new BlockChanged { X = from.X, Y = from.Y, Z = from.Z, Block = BlockId.AirValue });

        _world.SetBlock(to, id, tint, glow, shape);
        BroadcastToWorld(new BlockChanged { X = to.X, Y = to.Y, Z = to.Z, Block = id.Value, Tint = tint, Glow = glow, Shape = shape });

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

    /// <summary>Test seam: wakes a granular cell as a mutation beside it would (a direct <c>World.SetBlock</c>
    /// in a test stands in for generated terrain and wakes nothing — which is the point of that discipline).</summary>
    public void WakeGranularForTest(int x, int y, int z) => ActivateGranular(new Vector3i(x, y, z));
}
