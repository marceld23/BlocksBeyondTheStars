// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Fire (item 30) — a server-authoritative cellular automaton, sibling of <see cref="GameServerFluids"/>.
/// A burning block becomes a transient <c>fire</c> block for a short while, spreads to its flammable
/// neighbours, then collapses to <c>ash</c>. Fire burns a player standing in it. Block changes are broadcast
/// (so clients render fire/ash via the normal chunk mesh) and per-tick work is capped.
///
/// <para><b>Ignition sources.</b> Active/flowing lava (existing lava seas stay dormant, so a lava world doesn't
/// sweep into flame), a torch swung at a flammable block (#786), and a shot from an igniting energy weapon
/// (#788). Player-triggered ignition is why <see cref="Ignite"/> now runs the same protection chain as the
/// terrain blaster: ships, settlements, stations, factories and claimed bases never catch fire, so a village
/// greenhouse (wood + crops) can't be torched.</para>
///
/// <para><b>Why it stops.</b> Fuel is consumed (a burnt cell is ash and never re-ignites) and ground cover is
/// deliberately non-flammable, so a fire dies in the vegetation it started in. On top of that (#791): spread is
/// a per-step roll rather than a certainty, each cell records how many hops it is from the ignition point and
/// stops propagating past <see cref="FireMaxSpreadHops"/>, and a world-wide cap bounds the burning frontier.</para>
///
/// <para><b>How it goes out.</b> A water neighbour douses it, rain or a storm douses sky-exposed cells (#789),
/// and a player can simply hit the flame to stamp it out (#790).</para>
///
/// <para>Burn timers are persisted per cell (#784): the <c>fire</c> block itself survives a restart as a block
/// edit, so without its timer row a burning cell would reload untracked — a permanent, inert flame that never
/// becomes ash yet still burns anyone standing in it. Same failure mode the fluid levels had in #657.</para>
/// </summary>
public sealed partial class GameServer
{
    private const double FireInterval = 0.16;   // ~6 Hz
    private const int FireUpdatesPerTick = 300;  // bound the burning frontier per tick
    private const float FireBurnTime = 3.5f;     // how long a cell burns before turning to ash

    /// <summary>Chance per fire step that a burning cell sets one flammable neighbour alight (#791). Below 1
    /// so a fire creeps and frays instead of advancing as a solid deterministic wave — over a cell's ~21-step
    /// burn, spreading to an adjacent block is still all but certain, so a forest still goes up.</summary>
    private const double FireSpreadChance = 0.5;

    /// <summary>How far fire propagates from the cell that was ignited first (#791). Cells past this many hops
    /// still burn down, they just stop passing the flame on — the radius of any one arson event is bounded
    /// even in an unbroken canopy.</summary>
    private const int FireMaxSpreadHops = 16;

    /// <summary>Ceiling on simultaneously burning cells per world (#791). Above it, spread stops (burn-down
    /// continues) — a backstop for pathological worlds, not a limit play should ever reach.</summary>
    private const int FireMaxActiveCells = 2000;

    /// <summary>Chance per fire step that precipitation douses a sky-exposed burning cell, at full storm
    /// intensity (#789). Scaled by the biome's weather intensity, so a storm smothers a fire in about a
    /// second while light rain takes several.</summary>
    private const double FireRainDouseChance = 0.6;

    /// <summary>Blocks scanned upward for the open-sky test (#789) — mirrors the shelter scan the temperature
    /// hazard uses, so "under a roof" means the same thing in both systems.</summary>
    private const int FireSkyScanHeight = 50;

    private Dictionary<Vector3i, float> _fireTimer => _worlds.Active.FireTimer;
    private HashSet<Vector3i> _activeFire => _worlds.Active.ActiveFire;
    private Dictionary<Vector3i, int> _fireGeneration => _worlds.Active.FireGeneration;
    private System.Random _fireRng { get => _worlds.Active.FireRng; set => _worlds.Active.FireRng = value; }
    private double _sinceFire { get => _worlds.Active.SinceFire; set => _worlds.Active.SinceFire = value; }
    private ushort _fireId, _ashId;
    private readonly HashSet<ushort> _flammableIds = new();

    private void InitFire()
    {
        _fireId = _content.GetBlock("fire")?.NumericId.Value ?? 0;
        _ashId = _content.GetBlock("ash")?.NumericId.Value ?? 0;

        // Cache the flammable ids once instead of a per-neighbour dictionary + string lookup inside the tick.
        _flammableIds.Clear();
        foreach (var def in _content.Blocks.Values)
        {
            if (def.Flammable && def.NumericId.Value != 0)
            {
                _flammableIds.Add(def.NumericId.Value);
            }
        }

        _fireRng = new System.Random(unchecked((int)_meta.Seed ^ 0xF12E));
    }

    /// <summary>Blocks that catch fire — data-driven via <see cref="BlockDefinition.Flammable"/> (#785).
    /// Fire and ash themselves never burn, so a burnt-out cell can't re-ignite.</summary>
    private bool IsFlammable(ushort id)
        => id != 0 && id != _fireId && id != _ashId && _flammableIds.Contains(id);

    /// <summary>Structures never catch fire (#787): the same protection chain the terrain blaster runs. Fire is
    /// the one block change with no actor to attribute it to — lava or a spreading flame has no owner — so
    /// claimed bases and factories are checked with an empty actor, i.e. as protected against everyone. Without
    /// this a village greenhouse (wood frame + crop beds) burns down, and player-triggered ignition would make
    /// torching someone's base trivial.</summary>
    private bool IsFireProtected(Vector3i pos)
        => IsShipBlock(pos)
           || IsSettlementBlock(pos)
           || IsStationBlock(pos)
           || IsFactoryProtected(pos, string.Empty, false)
           || IsBaseProtected(pos, string.Empty, false);

    /// <summary>Sets a flammable cell alight: it becomes a fire block that will spread + burn down to ash.
    /// <paramref name="generation"/> is how many hops the flame has travelled from the cell that started it.</summary>
    private void Ignite(Vector3i pos, int generation = 0)
    {
        if (_fireId == 0 || _fireTimer.ContainsKey(pos) || !IsFlammable(_world.GetBlock(pos).Value))
        {
            return;
        }

        if (IsFireProtected(pos))
        {
            return;
        }

        // Soaked vegetation doesn't catch (#789) — while rain falls on an open cell, nothing lights there.
        if (PrecipitationDouses(pos))
        {
            return;
        }

        _world.SetBlock(pos, new BlockId(_fireId));
        TrackFire(pos, FireBurnTime, generation);
        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = _fireId });
        _activeFire.Add(pos);
    }

    /// <summary>Records a burning cell's remaining time + hop count (memory and save). The persisted row is
    /// what stops a restart from stranding the cell as a permanent inert flame (#784).
    ///
    /// <para>Only ignition writes the row. The countdown itself stays in memory: persisting it would mean a
    /// database write per burning cell per step — up to 300 every 0.16 s — to save at most a few seconds of
    /// burn progress. A restart therefore resumes a cell with the burn time it was lit with, which is well
    /// within what the automaton has to tolerate anyway (a world nobody is on doesn't tick at all).</para></summary>
    private void TrackFire(Vector3i pos, float remaining, int generation, bool persist = true)
    {
        _fireTimer[pos] = remaining;
        _fireGeneration[pos] = generation;
        if (persist)
        {
            _repo.SaveFireCell(_world.LocationId, pos, remaining, generation);
        }
    }

    /// <summary>Drops a cell's burning state (memory and save) — it burned out, was doused, or its block was
    /// replaced. Safe to call for cells that were never burning.</summary>
    private void UntrackFire(Vector3i pos)
    {
        _fireGeneration.Remove(pos);
        if (_fireTimer.Remove(pos))
        {
            _repo.DeleteFireCell(_world.LocationId, pos);
        }
    }

    /// <summary>Restores this world's persisted burning cells and wakes them, so a fire keeps burning down
    /// across a restart instead of fossilising (#784). Like the fluid loader this deliberately doesn't read
    /// blocks here (that would force chunk generation at load) — the tick's own stale-cell check drops any row
    /// whose block is no longer fire.</summary>
    private void LoadFireState()
    {
        foreach (var cell in _repo.ListFireCells(_world.LocationId))
        {
            _fireTimer[cell.WorldPosition] = Math.Clamp((float)cell.Remaining, 0.1f, FireBurnTime);
            _fireGeneration[cell.WorldPosition] = Math.Max(0, cell.Generation);
            _activeFire.Add(cell.WorldPosition);
        }
    }

    /// <summary>Called from the fluid tick for a (placed/flowing) lava cell: set its flammable neighbours
    /// alight. Lava is the origin of a fire, so its neighbours start at generation 0.</summary>
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

    /// <summary>Spread from a burning cell: each flammable neighbour catches on a roll, one hop further out
    /// (#791). Stops once the frontier is at the world cap or this branch has travelled far enough.</summary>
    private void SpreadFire(Vector3i pos)
    {
        if (_fireId == 0 || _fireTimer.Count >= FireMaxActiveCells)
        {
            return;
        }

        int generation = _fireGeneration.TryGetValue(pos, out var g) ? g : 0;
        if (generation >= FireMaxSpreadHops)
        {
            return; // this branch burns out where it stands
        }

        SpreadTo(new Vector3i(pos.X + 1, pos.Y, pos.Z), generation + 1);
        SpreadTo(new Vector3i(pos.X - 1, pos.Y, pos.Z), generation + 1);
        SpreadTo(new Vector3i(pos.X, pos.Y, pos.Z + 1), generation + 1);
        SpreadTo(new Vector3i(pos.X, pos.Y, pos.Z - 1), generation + 1);
        SpreadTo(new Vector3i(pos.X, pos.Y + 1, pos.Z), generation + 1);
        SpreadTo(new Vector3i(pos.X, pos.Y - 1, pos.Z), generation + 1);
    }

    private void SpreadTo(Vector3i pos, int generation)
    {
        if (_fireRng.NextDouble() < FireSpreadChance)
        {
            Ignite(pos, generation);
        }
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
                UntrackFire(pos); // already extinguished/mined elsewhere
                continue;
            }

            // Water touching the fire douses it, and so does rain or a storm falling on it (back to air —
            // quenched, not charred).
            if (HasWaterNeighbor(pos) || RainDousedThisStep(pos))
            {
                Extinguish(pos);
                continue;
            }

            // Spread to flammable neighbours, then burn down toward ash.
            SpreadFire(pos);

            float t = (_fireTimer.TryGetValue(pos, out var rem) ? rem : FireBurnTime) - step;
            if (t <= 0f)
            {
                UntrackFire(pos);
                SetCell(pos, _ashId); // burned out → charred ash
                continue;
            }

            TrackFire(pos, t, _fireGeneration.TryGetValue(pos, out var g) ? g : 0, persist: false);
            _activeFire.Add(pos); // still burning
        }
    }

    /// <summary>Puts a burning cell out: the flame is gone and the cell is air (quenched, not charred).</summary>
    private void Extinguish(Vector3i pos)
    {
        UntrackFire(pos);
        SetCell(pos, 0);
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

    // ---------------- Weather (#789) ----------------

    /// <summary>Whether water is currently falling on this cell (#789): the world must actually be raining or
    /// storming, the biome must not have shrugged it off, the precipitation must be water (a lava world's
    /// ash-rain and a desert's sandstorm put nothing out), and the cell must see the sky.
    ///
    /// <para>The world-level gate comes first on purpose. <see cref="BiomeWeatherAt"/> shifts the world's
    /// weather by a persistent per-biome offset of up to +2 steps, so a wet biome reads as "rain" even while
    /// the world is clear — that is fine for a temperature nudge, but keying ignition off it alone would make
    /// fire permanently impossible in roughly a quarter of every world's biomes. Asking the world first keeps
    /// the biome offset as what it was meant to be: some biomes catch a passing storm harder than others.</para></summary>
    private bool PrecipitationDouses(Vector3i pos) => PrecipitationDouses(pos, out _);

    private bool PrecipitationDouses(Vector3i pos, out float intensity)
    {
        intensity = 0f;
        // Anything that comes down wet can douse — the ladder's rain/storm and the wet events (#900).
        if (_weatherState is not ("rain" or "storm" or "drizzle" or "blizzard" or "acid_rain"))
        {
            return false; // the world is dry — skip the whole probe
        }

        var at = new Vector3f(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
        var (state, biomeIntensity) = BiomeWeatherAt(at);
        if (state is not ("rain" or "storm" or "drizzle" or "blizzard" or "acid_rain"))
        {
            return false; // a drier biome sits this one out
        }

        string precipitation = PrecipitationFor(state, CurrentTemperature(state, _dayFraction, at));
        if (precipitation is not ("rain" or "drizzle" or "sleet" or "snow" or "hail" or "acid"))
        {
            return false; // ash-rain on a lava world feeds the fire's mood, not a bucket of water
        }

        intensity = biomeIntensity;
        return SkyExposed(pos);
    }

    /// <summary>The douse roll for one fire step: heavier weather smothers a flame faster.</summary>
    private bool RainDousedThisStep(Vector3i pos)
        => PrecipitationDouses(pos, out float intensity)
           && _fireRng.NextDouble() < FireRainDouseChance * Math.Clamp(intensity, 0.2f, 1f);

    /// <summary>Server-side open-sky test for a cell: any block within <see cref="FireSkyScanHeight"/> above it
    /// is a roof, so a campfire indoors or in a cave keeps burning through a storm. Reads only already-loaded
    /// chunks (never generates): a burning cell is by definition somewhere a player has been, so its column is
    /// resident, and an unknown column reads as open sky exactly like the shelter scan.</summary>
    private bool SkyExposed(Vector3i pos)
    {
        for (int y = pos.Y + 1; y <= pos.Y + FireSkyScanHeight; y++)
        {
            if (!_world.GetBlockIfLoaded(new Vector3i(pos.X, y, pos.Z)).IsAir)
            {
                return false;
            }
        }

        return true;
    }

    // ---------------- Player interaction (#786, #788, #790) ----------------

    /// <summary>Swinging a torch at a flammable block sets it alight (#786) — the first ignition source the
    /// player carries. Returns true when the swing was consumed as an ignition, so the mine path stops there.
    /// The torch is not spent: it is a burning stick, not a match.</summary>
    private bool TryTorchIgnite(PlayerSession session, Vector3i pos, ushort block)
    {
        if (_fireId == 0 || !IsFlammable(block) || HeldItemKey(session.State) != "torch")
        {
            return false;
        }

        if (!WithinReach(session.State, pos))
        {
            Reject(session, "mine", "@out_of_reach");
            return true;
        }

        if (IsFireProtected(pos))
        {
            Reject(session, "mine", "@fire_protected");
            return true;
        }

        Ignite(pos);
        return true;
    }

    /// <summary>Hitting a flame stamps it out (#790). Fire is not mineable, so without this the swing was just
    /// rejected and water was the only counter-play.</summary>
    private bool TryStampOutFire(PlayerSession session, Vector3i pos, ushort block)
    {
        if (_fireId == 0 || block != _fireId)
        {
            return false;
        }

        if (!WithinReach(session.State, pos))
        {
            Reject(session, "mine", "@out_of_reach");
            return true;
        }

        Extinguish(pos);
        _miningProgress.Remove(pos);
        return true;
    }

    /// <summary>A shot from an igniting energy weapon that hit terrain rather than an entity (#788). The client
    /// only sends this when its shot found no target, so the server re-validates everything that matters:
    /// the held weapon really ignites, the cell is within the weapon's range, and the shot costs suit energy
    /// exactly like a shot at a creature. Ignition itself runs the normal guards (flammable + unprotected),
    /// so the worst a spoofed cell can do is light a plant the player could have walked up to and torched.</summary>
    private void HandleShootBlock(PlayerSession session, ShootBlockIntent intent)
    {
        var p = session.State;
        var tool = ActiveTool(p);
        if (tool.Kind != ToolKind.Weapon || !tool.Ignites || _fireId == 0)
        {
            return;
        }

        var pos = WorldConstants.CanonicalBlock(new Vector3i(intent.X, intent.Y, intent.Z), _world.Circumference);
        if (!WithinBuildHeight(pos.Y) || !IsFlammable(_world.GetBlock(pos).Value))
        {
            return; // nothing burnable there — the shot was just a scorch mark
        }

        float range = Math.Max(tool.Range, EnemyAttackReach);
        if (WrapDistSq(p.Position, pos) > range * range)
        {
            Reject(session, "attack", "@srv.attack.out_of_reach");
            return;
        }

        if (tool.CooldownSeconds > 0f)
        {
            if (_meleeReadyAt.TryGetValue(p.PlayerId, out var readyAt) && _uptime < readyAt)
            {
                return; // still on cooldown — ignore the shot (no reject spam)
            }

            _meleeReadyAt[p.PlayerId] = _uptime + tool.CooldownSeconds;
        }

        if (tool.EnergyPerUse > 0f)
        {
            if (p.SuitEnergy < tool.EnergyPerUse)
            {
                Reject(session, "attack", "@srv.attack.no_energy");
                return;
            }

            p.SuitEnergy -= tool.EnergyPerUse;
            SendPlayerState(session);
        }

        Ignite(pos);
    }

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

    /// <summary>Test hook: run a terrain shot as if the intent arrived from the client.</summary>
    public void ShootBlockForTest(string playerId, int x, int y, int z)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleShootBlock(s, new ShootBlockIntent { X = x, Y = y, Z = z });
        }
    }
}
