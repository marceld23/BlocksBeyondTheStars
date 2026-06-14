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
/// of water floating over the drop). Levels are in-memory (not persisted).
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
    private ushort _waterId, _lavaId;

    private void InitFluids()
    {
        _waterId = _content.GetBlock("water")?.NumericId.Value ?? 0;
        _lavaId = _content.GetBlock("lava")?.NumericId.Value ?? 0;
    }

    private bool IsFluid(ushort id) => id != 0 && (id == _waterId || id == _lavaId);

    /// <summary>Registers a full fluid source at the cell (the block must already be set). A source is an
    /// <i>untracked</i> cell — no level entry — so it is always full and never recedes, exactly like a
    /// worldgen sea. Flowing cells, by contrast, live in <c>_fluidLevel</c> and dry up when cut off.</summary>
    public void RegisterFluidSource(Vector3i pos)
    {
        _fluidLevel.Remove(pos);
        _fallingFluid.Remove(pos);
        _activeFluid.Add(pos);
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
                _fluidLevel.Remove(pos);
                _fallingFluid.Remove(pos);
                continue;
            }

            if (id == _lavaId)
            {
                IgniteFlammableNeighbors(pos); // active/flowing lava sets adjacent plants/wood alight (item 30)
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
                    _fluidLevel[pos] = level;
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

    private void FillFluid(Vector3i pos, BlockId kind, byte level, bool falling)
    {
        _world.SetBlock(pos, kind);
        _fluidLevel[pos] = level;
        if (falling)
        {
            _fallingFluid.Add(pos); // a cell filled from above feeds a waterfall column
        }
        else
        {
            _fallingFluid.Remove(pos); // a cell filled sideways rests on the surface it spread across
        }

        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = kind.Value });
        _activeFluid.Add(pos);
    }

    /// <summary>Dries up a flowing cell that has lost its feed: clears the block, tells clients, and wakes the
    /// neighbours so the rest of the orphaned stream re-evaluates and recedes too (and any still-fed fluid
    /// above falls into the gap). Sources never reach here.</summary>
    private void RetractFluid(Vector3i pos)
    {
        _world.SetBlock(pos, BlockId.Air);
        _fluidLevel.Remove(pos);
        _fallingFluid.Remove(pos);
        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = BlockId.AirValue });
        WakeNeighbors(pos);
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
        _fluidLevel.Remove(pos);
        _fallingFluid.Remove(pos);
        BroadcastToWorld(new BlockChanged { X = x, Y = y, Z = z, Block = BlockId.AirValue });
        if (wasFluid || HasFluidNeighbor(pos))
        {
            OnFluidRemoved(pos);
        }
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
