// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Flowing fluids — water &amp; lava (World systems). A server-authoritative cellular automaton:
/// fluid cells flow **down** when there's air below, otherwise **spread sideways** with a
/// decreasing level (so pools settle at a bounded radius, Minecraft-style). Sources (full level)
/// keep feeding. Block changes are broadcast so clients render them via the normal chunk mesh.
/// Lava damages players standing in/on it. Per-tick work is capped.
///
/// Source vs flowing: a cell is a <b>source</b> (bottomless, never recedes) when it has no entry in
/// <c>_fluidLevel</c> — that's how worldgen seas and placed water/lava blocks behave. A <b>flowing</b>
/// cell is tracked in <c>_fluidLevel</c> with a level 1..8 and only persists while something still feeds
/// it (fluid above, or a stronger horizontal neighbour); when its feed is cut it <b>retracts</b> (dries
/// up) instead of hanging in the air. The <c>_fallingFluid</c> set marks cells filled by a downward flow
/// so a cell feeding a waterfall doesn't crawl sideways at its own elevation (which used to leave a sheet
/// of water floating over the drop). Levels are persisted alongside the fluid block edits (#657): the
/// block itself survives a restart as a block edit, so without its level row every flowing tongue would
/// reload as untracked — i.e. as a permanent full source that can never dry up.
/// </summary>
public sealed partial class GameServer
{
    private const double FluidInterval = 0.25; // ~4 Hz
    private const int FluidUpdatesPerTick = 400;
    private const byte FluidFull = 8;

    private Dictionary<Vector3i, byte> _fluidLevel => _worlds.Active.FluidLevel;
    private HashSet<Vector3i> _activeFluid => _worlds.Active.ActiveFluid;
    private HashSet<Vector3i> _fallingFluid => _worlds.Active.FallingFluid;
    private double _sinceFluid { get => _worlds.Active.SinceFluid; set => _worlds.Active.SinceFluid = value; }
    private ushort _waterId, _lavaId, _obsidianId, _basaltId;

    private void InitFluids()
    {
        _waterId = _content.GetBlock("water")?.NumericId.Value ?? 0;
        _lavaId = _content.GetBlock("lava")?.NumericId.Value ?? 0;
        _obsidianId = _content.GetBlock("obsidian")?.NumericId.Value ?? 0;
        _basaltId = _content.GetBlock("basalt")?.NumericId.Value ?? 0;
    }

    // --- Water meets lava (#477 decision 4, completed by #1284) -------------------------------------------
    // The contact rule used to live ONLY in FillFluid, i.e. it fired for a FLOWING fluid entering a cell next
    // to the other fluid. A placed water block (a source) beside or on lava, or a worldgen lake against a lava
    // pocket, never flowed anywhere — and fluids only ever enter AIR — so the two sat side by side forever
    // (Lyxette, 2026-08-26). Now the LAVA solidifies whenever it touches water, wherever the contact comes from:
    //   * a lava SOURCE (untracked) → obsidian — the glassy crust a bottomless pool grows,
    //   * a FLOWING lava cell (tracked level) → basalt — the cooled rock of a quenched flow.
    // Water stays water. Sources are checked when placed and whenever they are woken (mining nearby, a placed
    // block, a flow arriving), which is also what catches worldgen adjacencies.

    /// <summary>If the lava cell at <paramref name="pos"/> touches water, it hardens in place — obsidian for a
    /// source, basalt for a flowing cell — and the neighbours are woken. Returns the crust block id, 0 if the
    /// cell is not lava or touches no water.</summary>
    private ushort QuenchLava(Vector3i pos)
    {
        if (_world.GetBlock(pos).Value != _lavaId || !TouchesOtherFluid(pos, _lavaId))
        {
            return 0;
        }

        ushort crust = _fluidLevel.ContainsKey(pos) ? _basaltId : _obsidianId;
        if (crust == 0)
        {
            return 0;
        }

        _world.SetBlock(pos, new BlockId(crust));
        UntrackFluid(pos);
        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = crust });
        WakeNeighbors(pos);
        return crust;
    }

    /// <summary>Quenches every lava cell around a water cell. Returns the crust ids produced (0 = none) as a
    /// (obsidian, basalt) count pair so the caller can word its toast.</summary>
    private (int Obsidian, int Basalt) CrustLavaAround(Vector3i waterPos)
    {
        int obsidian = 0, basalt = 0;
        foreach (var n in new[]
        {
            new Vector3i(waterPos.X + 1, waterPos.Y, waterPos.Z), new Vector3i(waterPos.X - 1, waterPos.Y, waterPos.Z),
            new Vector3i(waterPos.X, waterPos.Y, waterPos.Z + 1), new Vector3i(waterPos.X, waterPos.Y, waterPos.Z - 1),
            new Vector3i(waterPos.X, waterPos.Y + 1, waterPos.Z), new Vector3i(waterPos.X, waterPos.Y - 1, waterPos.Z),
        })
        {
            ushort crust = QuenchLava(n);
            if (crust != 0 && crust == _basaltId)
            {
                basalt++;
            }
            else if (crust != 0)
            {
                obsidian++;
            }
        }

        return (obsidian, basalt);
    }

    /// <summary>A just-placed water/lava source meets the other fluid (#1284). Water placed INTO a lava cell (the
    /// #851 displace rule) quenches the pool it replaced: the placed cell itself becomes obsidian and no water
    /// remains. Water placed BESIDE lava stays water and hardens the lava around it; a placed lava source beside
    /// water hardens itself. The player is told what happened.</summary>
    private void QuenchPlacedFluid(PlayerSession session, Vector3i pos, ushort placedId, ushort displacedId)
    {
        ushort opposite = placedId == _waterId ? _lavaId : placedId == _lavaId ? _waterId : (ushort)0;
        if (opposite == 0 || _obsidianId == 0)
        {
            return;
        }

        if (displacedId == opposite)
        {
            _world.SetBlock(pos, new BlockId(_obsidianId));
            UntrackFluid(pos);
            BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = _obsidianId });
            WakeNeighbors(pos);
            Send(session, new ServerMessage { Text = "@srv.fluid.quench_obsidian" });
            return;
        }

        if (placedId == _waterId)
        {
            var (obsidian, basalt) = CrustLavaAround(pos);
            if (obsidian > 0)
            {
                Send(session, new ServerMessage { Text = "@srv.fluid.quench_obsidian" });
            }
            else if (basalt > 0)
            {
                Send(session, new ServerMessage { Text = "@srv.fluid.quench_basalt" });
            }
        }
        else if (QuenchLava(pos) != 0)
        {
            Send(session, new ServerMessage { Text = "@srv.fluid.quench_obsidian" });
        }
    }

    private bool IsFluid(ushort id) => id != 0 && (id == _waterId || id == _lavaId);

    /// <summary>Registers a full fluid source at the cell (the block must already be set). A source is an
    /// <i>untracked</i> cell — no level entry — so it is always full and never recedes, exactly like a
    /// worldgen sea. Flowing cells, by contrast, live in <c>_fluidLevel</c> and dry up when cut off.</summary>
    public void RegisterFluidSource(Vector3i pos)
    {
        UntrackFluid(pos);
        _activeFluid.Add(pos);
    }

    /// <summary>Records a flowing cell's level (memory + save). The persisted row is what stops a restart from
    /// promoting the cell to an untracked source (#657).</summary>
    private void TrackFluid(Vector3i pos, byte level, bool falling)
    {
        _fluidLevel[pos] = level;
        if (falling)
        {
            _fallingFluid.Add(pos);
        }
        else
        {
            _fallingFluid.Remove(pos); // a cell filled sideways rests on the surface it spread across
        }

        _repo.SaveFluidCell(_world.LocationId, pos, level, falling);
    }

    /// <summary>Drops a cell's flowing state (memory + save) — it dried up, became a source, or its block
    /// was replaced. Safe to call for cells that were never tracked.</summary>
    private void UntrackFluid(Vector3i pos)
    {
        if (_fluidLevel.Remove(pos))
        {
            _repo.DeleteFluidCell(_world.LocationId, pos);
        }

        _fallingFluid.Remove(pos);
    }

    /// <summary>Restores this world's persisted flowing-fluid cells (levels + falling flags) and wakes them,
    /// so streams keep flowing/retracting across a restart instead of fossilising into full sources (#657).
    /// Deliberately does not touch the world's blocks here (that would force chunk generation at load) — the
    /// tick's own stale-cell check drops any row whose block is no longer a fluid.</summary>
    private void LoadFluidState()
    {
        foreach (var cell in _repo.ListFluidCells(_world.LocationId))
        {
            _fluidLevel[cell.WorldPosition] = Math.Clamp(cell.Level, (byte)1, FluidFull);
            if (cell.Falling)
            {
                _fallingFluid.Add(cell.WorldPosition);
            }

            _activeFluid.Add(cell.WorldPosition); // wake: orphans retract, still-fed cells settle again
        }
    }

    /// <summary>Places a fluid source block and registers it (gameplay/admin/tests).</summary>
    public void PlaceFluidSource(string blockKey, int x, int y, int z)
    {
        if (_content.GetBlock(blockKey) is { } def && IsFluid(def.NumericId.Value))
        {
            var pos = new Vector3i(x, y, z);
            _world.SetBlock(pos, def.NumericId);
            BroadcastToWorld(new BlockChanged { X = x, Y = y, Z = z, Block = def.NumericId.Value });
            RegisterFluidSource(pos);
        }
    }

    private void TickFluids(double dt)
    {
        if (_activeFluid.Count == 0)
        {
            _sinceFluid = 0;
            return;
        }

        _sinceFluid += dt;
        if (_sinceFluid < FluidInterval)
        {
            return;
        }

        _sinceFluid = 0;
        _worlds.Active.FluidStep++;
        bool lavaRests = (_worlds.Active.FluidStep & 1) == 1; // #1316: lava moves on every second step only

        // #1505: one transaction per step. Every cell the step touches persists through SetBlock/SaveFluidCell/
        // DeleteFluidCell — as autocommits that was one commit per cell (measured 100–142 µs each on NVMe, far
        // more on SD cards), i.e. up to 400 commits per step and a breached lake ≈ 1600 commits/s on the tick
        // thread. Batched, the same writes cost ~24–35 µs each. A crash mid-step loses at most this one step.
        _repo.RunInTransaction(() => StepFluids(lavaRests));
    }

    /// <summary>One fluid step over the woken cells (the body of <see cref="TickFluids"/>; runs inside a
    /// repository transaction).</summary>
    private void StepFluids(bool lavaRests)
    {
        var todo = new List<Vector3i>(_activeFluid);
        _activeFluid.Clear();
        int budget = FluidUpdatesPerTick;

        foreach (var pos in todo)
        {
            if (budget-- <= 0)
            {
                _activeFluid.Add(pos); // defer leftover to the next step
                continue;
            }

            ushort id = _world.GetBlock(pos).Value;
            if (!IsFluid(id))
            {
                UntrackFluid(pos);
                continue;
            }

            if (id == _lavaId)
            {
                if (QuenchLava(pos) != 0)
                {
                    continue; // touched water: it is rock now (#1284) — contact fires on wake, never waits for the cadence
                }

                // Lava flows at half the water speed (#1316, maintainer decision — readability, not realism:
                // fast lava kills before you can react). The budget and the wake set stay shared; a lava cell
                // just sits out every other step and is re-queued untouched.
                if (lavaRests)
                {
                    _activeFluid.Add(pos);
                    continue;
                }

                IgniteFlammableNeighbors(pos); // active/flowing lava sets adjacent plants/wood alight (item 30)
            }
            else if (id == _waterId)
            {
                CrustLavaAround(pos); // a woken water cell hardens the lava it touches (#1284)
            }

            bool isSource = !_fluidLevel.ContainsKey(pos);
            byte level;
            bool changed = false;

            if (isSource)
            {
                level = FluidFull; // a source is bottomless and never recedes
            }
            else
            {
                // A flowing cell only lives while something still feeds it. With no feed it dries up — this is
                // what makes a dammed or cut-off stream recede a step at a time instead of hanging in the air.
                int supported = SupportedLevel(pos, id);
                if (supported <= 0)
                {
                    RetractFluid(pos);
                    continue;
                }

                byte old = _fluidLevel[pos];
                level = (byte)supported;
                if (level != old)
                {
                    TrackFluid(pos, level, _fallingFluid.Contains(pos));
                    changed = true;
                    WakeNeighbors(pos); // a level drop must ripple downstream so the whole tail recedes too
                }
            }

            var kind = new BlockId(id);
            var below = new Vector3i(pos.X, pos.Y - 1, pos.Z);
            if (FluidCanEnter(below))
            {
                FillFluid(below, kind, FluidFull, falling: true); // fluid falls full
                changed = true;
            }
            else if (level > 1)
            {
                // Don't crawl sideways while feeding a waterfall: a cell sitting on a *falling* column would
                // otherwise spread at its own (high) elevation and build a sheet of water hanging over the drop.
                ushort belowId = _world.GetBlock(below).Value;
                bool feedingFall = IsFluid(belowId) && _fallingFluid.Contains(below);
                if (!feedingFall)
                {
                    Spread(new Vector3i(pos.X + 1, pos.Y, pos.Z), kind, level, ref changed);
                    Spread(new Vector3i(pos.X - 1, pos.Y, pos.Z), kind, level, ref changed);
                    Spread(new Vector3i(pos.X, pos.Y, pos.Z + 1), kind, level, ref changed);
                    Spread(new Vector3i(pos.X, pos.Y, pos.Z - 1), kind, level, ref changed);
                }
            }

            // Keep a cell active while it still has somewhere to flow: it changed this step, or it's a full
            // source with an open neighbour. A settled full cell (a calm body of water) goes dormant, so a big
            // sea doesn't keep every cell active forever — mining wakes the frontier again via OnFluidRemoved.
            if (changed || (isSource && HasAirNeighbor(pos)))
            {
                _activeFluid.Add(pos);
            }
        }
    }

    /// <summary>The level a <i>flowing</i> cell can sustain from its surroundings: full if the same fluid sits
    /// directly above (a falling column feeds it), otherwise the strongest horizontal neighbour's level minus
    /// one (a source counts as full). 0 means nothing feeds it any more → it should dry up. Only the same kind
    /// of fluid feeds (water never sustains lava or vice-versa).</summary>
    private int SupportedLevel(Vector3i p, ushort id)
    {
        if (_world.GetBlock(new Vector3i(p.X, p.Y + 1, p.Z)).Value == id)
        {
            return FluidFull; // fed from directly above (a waterfall column)
        }

        int best = 0;
        best = Math.Max(best, NeighborFeed(new Vector3i(p.X + 1, p.Y, p.Z), id));
        best = Math.Max(best, NeighborFeed(new Vector3i(p.X - 1, p.Y, p.Z), id));
        best = Math.Max(best, NeighborFeed(new Vector3i(p.X, p.Y, p.Z + 1), id));
        best = Math.Max(best, NeighborFeed(new Vector3i(p.X, p.Y, p.Z - 1), id));
        return best;
    }

    /// <summary>How much a horizontal neighbour can feed this cell: a source feeds at FluidFull−1, a flowing
    /// neighbour at its own level−1, anything else (air / the other fluid) feeds nothing.</summary>
    private int NeighborFeed(Vector3i n, ushort id)
    {
        if (_world.GetBlock(n).Value != id)
        {
            return 0;
        }

        byte nl = _fluidLevel.TryGetValue(n, out var lv) ? lv : FluidFull; // untracked neighbour = full source
        return nl - 1;
    }

    private void Spread(Vector3i n, BlockId kind, byte level, ref bool changed)
    {
        if (FluidCanEnter(n))
        {
            FillFluid(n, kind, (byte)(level - 1), falling: false);
            changed = true;
        }
    }

    /// <summary>A fluid may enter a cell only if it's air AND not inside a ship interior — so a sea/lava body
    /// can never flow into (or refill) a landed ship's cabin, keeping a submerged ship watertight and dry.</summary>
    private bool FluidCanEnter(Vector3i p)
        => _world.GetBlock(p).IsAir && !InShipInterior(p);

    /// <summary>True if a cell lies inside any parked ship's bounds (cheap no-op when no ship is placed).</summary>
    private bool InShipInterior(Vector3i p)
        => _worlds.Active.LandedShips.Count > 0
        && ShipInteriorContains(new Vector3f(p.X + 0.5f, p.Y + 0.5f, p.Z + 0.5f));

    /// <summary>True if any of the 6 neighbours holds the OPPOSITE fluid (water vs lava).</summary>
    private bool TouchesOtherFluid(Vector3i p, ushort id)
    {
        ushort other = id == _waterId ? _lavaId : id == _lavaId ? _waterId : (ushort)0;
        if (other == 0)
        {
            return false;
        }

        return _world.GetBlock(new Vector3i(p.X + 1, p.Y, p.Z)).Value == other
            || _world.GetBlock(new Vector3i(p.X - 1, p.Y, p.Z)).Value == other
            || _world.GetBlock(new Vector3i(p.X, p.Y, p.Z + 1)).Value == other
            || _world.GetBlock(new Vector3i(p.X, p.Y, p.Z - 1)).Value == other
            || _world.GetBlock(new Vector3i(p.X, p.Y + 1, p.Z)).Value == other
            || _world.GetBlock(new Vector3i(p.X, p.Y - 1, p.Z)).Value == other;
    }

    private void FillFluid(Vector3i pos, BlockId kind, byte level, bool falling)
    {
        // Water meets lava (#477, decision #6): the entering flow solidifies at the contact face instead of
        // interleaving with the other fluid — dig a channel from a pond into a volcano crater and a glassy
        // crust grows where the two touch. Entering WATER chills to obsidian; an entering LAVA tongue is a
        // flowing cell and cools to basalt (#1284). Waking the neighbours lets the crust propagate.
        ushort crust = kind.Value == _lavaId && _basaltId != 0 ? _basaltId : _obsidianId;
        if (crust != 0 && TouchesOtherFluid(pos, kind.Value))
        {
            _world.SetBlock(pos, new BlockId(crust));
            UntrackFluid(pos);
            BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = crust });
            WakeNeighbors(pos);
            return;
        }

        _world.SetBlock(pos, kind);
        TrackFluid(pos, level, falling);
        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = kind.Value });
        _activeFluid.Add(pos);
    }

    /// <summary>Dries up a flowing cell that has lost its feed: clears the block, tells clients, and wakes the
    /// neighbours so the rest of the orphaned stream re-evaluates and recedes too (and any still-fed fluid
    /// above falls into the gap). Sources never reach here.</summary>
    private void RetractFluid(Vector3i pos)
    {
        _world.SetBlock(pos, BlockId.Air);
        UntrackFluid(pos);
        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = BlockId.AirValue });
        WakeNeighbors(pos);
        OnSupportRemoved(pos); // sand resting on a drying tongue drops with it (#1319)
    }


    /// <summary>True if any of the cell's 6 neighbours is a fluid — i.e. a hole opened here (e.g. by mining
    /// an underwater rock or kelp) would have a sea/lava body to refill it.</summary>
    private bool HasFluidNeighbor(Vector3i p)
        => IsFluid(_world.GetBlock(new Vector3i(p.X + 1, p.Y, p.Z)).Value)
        || IsFluid(_world.GetBlock(new Vector3i(p.X - 1, p.Y, p.Z)).Value)
        || IsFluid(_world.GetBlock(new Vector3i(p.X, p.Y, p.Z + 1)).Value)
        || IsFluid(_world.GetBlock(new Vector3i(p.X, p.Y, p.Z - 1)).Value)
        || IsFluid(_world.GetBlock(new Vector3i(p.X, p.Y + 1, p.Z)).Value)
        || IsFluid(_world.GetBlock(new Vector3i(p.X, p.Y - 1, p.Z)).Value);

    /// <summary>True if a cell has any neighbour it could flow into (sideways or down) — used to let settled
    /// full cells go dormant, so a big body of fluid doesn't keep every cell active forever. Ship-interior
    /// cells don't count (the fluid can't enter them), so a source against a submerged hull also goes dormant.</summary>
    private bool HasAirNeighbor(Vector3i p)
        => FluidCanEnter(new Vector3i(p.X + 1, p.Y, p.Z)) || FluidCanEnter(new Vector3i(p.X - 1, p.Y, p.Z))
        || FluidCanEnter(new Vector3i(p.X, p.Y, p.Z + 1)) || FluidCanEnter(new Vector3i(p.X, p.Y, p.Z - 1))
        || FluidCanEnter(new Vector3i(p.X, p.Y - 1, p.Z));

    /// <summary>When a fluid cell is removed (mined), wake the surrounding fluid so it flows back into the
    /// hole and any orphaned flowing tail recedes. Worldgen sea cells are untracked → treated as full sources,
    /// so digging into a body refills; a flowing stream cut off here dries up via <see cref="SupportedLevel"/>.
    /// Bounded by the per-tick budget + the settle guard in <see cref="TickFluids"/>.</summary>
    private void OnFluidRemoved(Vector3i pos) => WakeNeighbors(pos);

    private void WakeNeighbors(Vector3i p)
    {
        Wake(new Vector3i(p.X + 1, p.Y, p.Z));
        Wake(new Vector3i(p.X - 1, p.Y, p.Z));
        Wake(new Vector3i(p.X, p.Y, p.Z + 1));
        Wake(new Vector3i(p.X, p.Y, p.Z - 1));
        Wake(new Vector3i(p.X, p.Y + 1, p.Z)); // fluid directly above falls into / re-evaluates over the gap
        Wake(new Vector3i(p.X, p.Y - 1, p.Z));
    }

    private void Wake(Vector3i p)
    {
        if (IsFluid(_world.GetBlock(p).Value))
        {
            _activeFluid.Add(p); // untracked stays a source, tracked stays flowing — no promotion here
        }
    }

    /// <summary>Test seam: removes a block and wakes neighbouring fluid exactly as mining would (minus drops),
    /// so tests can exercise fluid retraction without driving the full mining path.</summary>
    public void RemoveBlockForTest(int x, int y, int z)
    {
        var pos = new Vector3i(x, y, z);
        bool wasFluid = IsFluid(_world.GetBlock(pos).Value);
        _world.SetBlock(pos, BlockId.Air);
        UntrackFluid(pos);
        BroadcastToWorld(new BlockChanged { X = x, Y = y, Z = z, Block = BlockId.AirValue });
        if (wasFluid || HasFluidNeighbor(pos))
        {
            OnFluidRemoved(pos);
        }

        OnSupportRemoved(pos); // #1319: the same wake mining gives a granular block above
    }

    /// <summary>True if the player is standing in or directly on lava (for contact damage).</summary>
    private bool InLava(Vector3f position)
    {
        if (_lavaId == 0)
        {
            return false;
        }

        var feet = position.ToBlock();
        return _world.GetBlock(feet).Value == _lavaId
               || _world.GetBlock(new Vector3i(feet.X, feet.Y - 1, feet.Z)).Value == _lavaId;
    }
}
