using System;
using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;

namespace Spacecraft.GameServer;

/// <summary>
/// Fire (item 30) — a server-authoritative cellular automaton, sibling of <see cref="GameServerFluids"/>.
/// Lava ignites adjacent flammable blocks (flora, wood, leaves); a burning block becomes a transient
/// <c>fire</c> block for a short while, spreading to its flammable neighbours, then collapses to <c>ash</c>.
/// A <c>water</c> neighbour douses it (back to air). Fire burns a player standing in it. Block changes are
/// broadcast (so clients render fire/ash via the normal chunk mesh) and per-tick work is capped; existing
/// lava seas stay dormant (only active/flowing lava ignites), so a lava world doesn't sweep into flame.
/// </summary>
public sealed partial class GameServer
{
    private const double FireInterval = 0.16;   // ~6 Hz
    private const int FireUpdatesPerTick = 300;  // bound the burning frontier per tick
    private const float FireBurnTime = 3.5f;     // how long a cell burns before turning to ash

    private Dictionary<Vector3i, float> _fireTimer => _worlds.Active.FireTimer;
    private HashSet<Vector3i> _activeFire => _worlds.Active.ActiveFire;
    private double _sinceFire { get => _worlds.Active.SinceFire; set => _worlds.Active.SinceFire = value; }
    private ushort _fireId, _ashId;

    private void InitFire()
    {
        _fireId = _content.GetBlock("fire")?.NumericId.Value ?? 0;
        _ashId = _content.GetBlock("ash")?.NumericId.Value ?? 0;
    }

    /// <summary>Flammable blocks that catch fire: the plants + trees (not the grassy ground, so a brush fire
    /// can't run away across a whole biome). Fire/ash themselves are never flammable.</summary>
    private bool IsFlammable(ushort id)
    {
        if (id == 0 || id == _fireId || id == _ashId)
        {
            return false;
        }

        var key = _content.BlockById(new BlockId(id))?.Key;
        return key != null && (key.StartsWith("flora_", StringComparison.Ordinal) || key == "wood_log" || key == "tree_leaves");
    }

    /// <summary>Sets a flammable cell alight: it becomes a fire block that will spread + burn down to ash.</summary>
    private void Ignite(Vector3i pos)
    {
        if (_fireId == 0 || _fireTimer.ContainsKey(pos) || !IsFlammable(_world.GetBlock(pos).Value))
        {
            return;
        }

        _world.SetBlock(pos, new BlockId(_fireId));
        _fireTimer[pos] = FireBurnTime;
        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = _fireId });
        _activeFire.Add(pos);
    }

    /// <summary>Called from the fluid tick for a (placed/flowing) lava cell: set its flammable neighbours alight.</summary>
    private void IgniteFlammableNeighbors(Vector3i pos)
    {
        if (_fireId == 0)
        {
            return;
        }

        Ignite(new Vector3i(pos.X + 1, pos.Y, pos.Z));
        Ignite(new Vector3i(pos.X - 1, pos.Y, pos.Z));
        Ignite(new Vector3i(pos.X, pos.Y, pos.Z + 1));
        Ignite(new Vector3i(pos.X, pos.Y, pos.Z - 1));
        Ignite(new Vector3i(pos.X, pos.Y + 1, pos.Z));
        Ignite(new Vector3i(pos.X, pos.Y - 1, pos.Z));
    }

    private void TickFire(double dt)
    {
        if (_activeFire.Count == 0)
        {
            _sinceFire = 0;
            return;
        }

        _sinceFire += dt;
        if (_sinceFire < FireInterval)
        {
            return;
        }

        float step = (float)_sinceFire;
        _sinceFire = 0;

        var todo = new List<Vector3i>(_activeFire);
        _activeFire.Clear();
        int budget = FireUpdatesPerTick;

        foreach (var pos in todo)
        {
            if (budget-- <= 0)
            {
                _activeFire.Add(pos); // defer leftover work to the next step
                continue;
            }

            if (_world.GetBlock(pos).Value != _fireId)
            {
                _fireTimer.Remove(pos); // already extinguished/mined elsewhere
                continue;
            }

            // Water touching the fire douses it (back to air — quenched, not charred).
            if (HasWaterNeighbor(pos))
            {
                SetCell(pos, 0);
                _fireTimer.Remove(pos);
                continue;
            }

            // Spread to flammable neighbours, then burn down toward ash.
            IgniteFlammableNeighbors(pos);

            float t = (_fireTimer.TryGetValue(pos, out var rem) ? rem : FireBurnTime) - step;
            if (t <= 0f)
            {
                SetCell(pos, _ashId); // burned out → charred ash
                _fireTimer.Remove(pos);
                continue;
            }

            _fireTimer[pos] = t;
            _activeFire.Add(pos); // still burning
        }
    }

    private void SetCell(Vector3i pos, ushort block)
    {
        _world.SetBlock(pos, new BlockId(block));
        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = block });
    }

    private bool HasWaterNeighbor(Vector3i p)
        => _world.GetBlock(new Vector3i(p.X + 1, p.Y, p.Z)).Value == _waterId
        || _world.GetBlock(new Vector3i(p.X - 1, p.Y, p.Z)).Value == _waterId
        || _world.GetBlock(new Vector3i(p.X, p.Y, p.Z + 1)).Value == _waterId
        || _world.GetBlock(new Vector3i(p.X, p.Y, p.Z - 1)).Value == _waterId
        || _world.GetBlock(new Vector3i(p.X, p.Y + 1, p.Z)).Value == _waterId
        || _world.GetBlock(new Vector3i(p.X, p.Y - 1, p.Z)).Value == _waterId;

    /// <summary>True if the player is standing in fire (feet or head cell) — for contact burn damage.</summary>
    private bool InFire(Vector3f position)
    {
        if (_fireId == 0)
        {
            return false;
        }

        var feet = position.ToBlock();
        return _world.GetBlock(feet).Value == _fireId
               || _world.GetBlock(new Vector3i(feet.X, feet.Y + 1, feet.Z)).Value == _fireId;
    }

    /// <summary>Test/diagnostic: how many cells are currently on fire in the active world.</summary>
    public int BurningCellCount => _worlds.Active.FireTimer.Count;

    /// <summary>Test hook: set a flammable cell alight directly.</summary>
    public void IgniteForTest(int x, int y, int z) => Ignite(new Vector3i(x, y, z));
}
