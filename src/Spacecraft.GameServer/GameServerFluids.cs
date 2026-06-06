using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;

namespace Spacecraft.GameServer;

/// <summary>
/// Flowing fluids — water &amp; lava (World systems). A server-authoritative cellular automaton:
/// fluid cells flow **down** when there's air below, otherwise **spread sideways** with a
/// decreasing level (so pools settle at a bounded radius, Minecraft-style). Sources (full level)
/// keep feeding. Block changes are broadcast so clients render them via the normal chunk mesh.
/// Lava damages players standing in/on it. Per-tick work is capped.
///
/// Slice scope: fluid levels are in-memory (not persisted); worldgen lakes/lava pools and
/// buckets/swimming come later — for now place a source (water/lava item) and watch it flow.
/// </summary>
public sealed partial class GameServer
{
    private const double FluidInterval = 0.25; // ~4 Hz
    private const int FluidUpdatesPerTick = 400;
    private const byte FluidFull = 8;

    private Dictionary<Vector3i, byte> _fluidLevel => _worlds.Active.FluidLevel;
    private HashSet<Vector3i> _activeFluid => _worlds.Active.ActiveFluid;
    private double _sinceFluid { get => _worlds.Active.SinceFluid; set => _worlds.Active.SinceFluid = value; }
    private ushort _waterId, _lavaId;

    private void InitFluids()
    {
        _waterId = _content.GetBlock("water")?.NumericId.Value ?? 0;
        _lavaId = _content.GetBlock("lava")?.NumericId.Value ?? 0;
    }

    private bool IsFluid(ushort id) => id != 0 && (id == _waterId || id == _lavaId);

    /// <summary>Registers a full fluid source at the cell (the block must already be set).</summary>
    public void RegisterFluidSource(Vector3i pos)
    {
        _fluidLevel[pos] = FluidFull;
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
                continue;
            }

            byte level = _fluidLevel.TryGetValue(pos, out var lv) ? lv : FluidFull;
            var kind = new BlockId(id);
            bool changed = false;

            var below = new Vector3i(pos.X, pos.Y - 1, pos.Z);
            if (_world.GetBlock(below).IsAir)
            {
                FillFluid(below, kind, FluidFull); // fluid falls full
                changed = true;
            }
            else if (level > 1)
            {
                Spread(new Vector3i(pos.X + 1, pos.Y, pos.Z), kind, level, ref changed);
                Spread(new Vector3i(pos.X - 1, pos.Y, pos.Z), kind, level, ref changed);
                Spread(new Vector3i(pos.X, pos.Y, pos.Z + 1), kind, level, ref changed);
                Spread(new Vector3i(pos.X, pos.Y, pos.Z - 1), kind, level, ref changed);
            }

            // Keep a cell active while it still has somewhere to flow: it changed this step, or it's a full
            // source with an open neighbour. A settled full cell (a calm body of water) goes dormant, so a big
            // sea doesn't keep every cell active forever — mining wakes the frontier again via OnFluidRemoved.
            if (changed || (level >= FluidFull && HasAirNeighbor(pos)))
            {
                _activeFluid.Add(pos);
            }
        }
    }

    private void Spread(Vector3i n, BlockId kind, byte level, ref bool changed)
    {
        if (_world.GetBlock(n).IsAir)
        {
            FillFluid(n, kind, (byte)(level - 1));
            changed = true;
        }
    }

    private void FillFluid(Vector3i pos, BlockId kind, byte level)
    {
        _world.SetBlock(pos, kind);
        _fluidLevel[pos] = level;
        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = kind.Value });
        _activeFluid.Add(pos);
    }

    private bool IsAirAt(int x, int y, int z) => _world.GetBlock(new Vector3i(x, y, z)).IsAir;

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
    /// full cells go dormant, so a big body of fluid doesn't keep every cell active forever.</summary>
    private bool HasAirNeighbor(Vector3i p)
        => IsAirAt(p.X + 1, p.Y, p.Z) || IsAirAt(p.X - 1, p.Y, p.Z)
        || IsAirAt(p.X, p.Y, p.Z + 1) || IsAirAt(p.X, p.Y, p.Z - 1)
        || IsAirAt(p.X, p.Y - 1, p.Z);

    /// <summary>When a fluid cell is removed (mined), wake the surrounding fluid so it flows back into the
    /// hole. Worldgen sea cells are untracked → treated as full sources, so digging into a body refills; a
    /// finite pool only stops once its last feeding cells are gone. Bounded by the per-tick budget + the
    /// settle guard in <see cref="TickFluids"/>.</summary>
    private void OnFluidRemoved(Vector3i pos)
    {
        Wake(new Vector3i(pos.X + 1, pos.Y, pos.Z));
        Wake(new Vector3i(pos.X - 1, pos.Y, pos.Z));
        Wake(new Vector3i(pos.X, pos.Y, pos.Z + 1));
        Wake(new Vector3i(pos.X, pos.Y, pos.Z - 1));
        Wake(new Vector3i(pos.X, pos.Y + 1, pos.Z)); // fluid directly above falls into the hole
    }

    private void Wake(Vector3i p)
    {
        if (IsFluid(_world.GetBlock(p).Value))
        {
            if (!_fluidLevel.ContainsKey(p))
            {
                _fluidLevel[p] = FluidFull; // a worldgen sea cell acts as a full source
            }

            _activeFluid.Add(p);
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
