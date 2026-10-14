// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

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
///
/// Every structure here — levels, falling flags, the woken and the parked set, the persisted rows — is keyed
/// by the <b>canonical</b> cell (<see cref="FluidCell"/>, #2447). The automaton walks neighbours as
/// <c>pos ± 1</c>, so without that one cell beside the world's X seam had two spellings, and a flowing cell
/// read under the spelling without a level row passed for a source.
///
/// The water model of #2515 (ADR 0017) bounds every flow: <b>a fall keeps its level</b> — a falling cell carries
/// the level of the cell above it instead of refilling to full, so no flow travels more than about seven cells from
/// the source that feeds it, however many steps, shafts and cave floors lie below. And not every untracked water cell
/// is a source any more: only <b>endless</b> water is — at or below the sea level, routed rivers, generated waterfall
/// columns (which fall but never spread) and water a player placed. Still water above the sea level is finite and
/// settles as a whole (<c>GameServerFluidSettle.cs</c>). Lava sources stay endless; the fall rule applies to lava too.
///
/// A doorway is a wall to both fluids (#2561, <see cref="FluidDoorCells"/>): a door keeps water and lava out, open or shut.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>One fluid step every 0.125 s (#2449: twice the old 0.25 s, so a seven-cell spread takes about a
    /// second; on the default 15 Hz server tick that is every second tick). Lava sits most steps out
    /// (<see cref="LavaStepEvery"/>) and keeps its old pace.</summary>
    private const double FluidInterval = 0.125;

    /// <summary>Lava advances on every fourth fluid step only — one cell per 0.5 s, the pace it had before water
    /// doubled (#1316: fast lava kills before you can react; #2449).</summary>
    private const int LavaStepEvery = 4;

    /// <summary>How often (in fluid steps) the parked cells are re-checked without a chunk load (#1824): about
    /// once a second.</summary>
    private const int UnparkEverySteps = 8;

    private const int FluidUpdatesPerTick = 400;
    private const byte FluidFull = 8;

    private Dictionary<Vector3i, byte> _fluidLevel => _worlds.Active.FluidLevel;
    private HashSet<Vector3i> _activeFluid => _worlds.Active.ActiveFluid;
    private HashSet<Vector3i> _fallingFluid => _worlds.Active.FallingFluid;

    /// <summary>#2447: the one spelling of a cell that every fluid structure is keyed by. The world wraps X (and Z)
    /// on each read and write, but the automaton reaches neighbours as <c>pos ± 1</c>: water flowing east from a
    /// block placed at x = −3 (stored as circumference − 3) reached the seam as <c>circumference</c>, water flowing
    /// west from x = +3 as <c>−1</c> — one cell, two keys. Read under the key without a level row, a flowing cell
    /// looked like an untracked source, and those phantom sources flooded the seam without end. Every first spawn
    /// sits at or near that seam (landing pad 0 is the prime-meridian touchdown).</summary>
    private Vector3i FluidCell(Vector3i p) => WorldConstants.CanonicalBlock(p, _world.Circumference);

    /// <summary>True if the cell is a tracked FLOWING cell (has a level row); false for a source or no fluid.</summary>
    private bool IsFlowingCell(Vector3i p) => _fluidLevel.ContainsKey(FluidCell(p));

    /// <summary>True if the cell was filled by a downward flow (it feeds a waterfall).</summary>
    private bool IsFallingCell(Vector3i p) => _fallingFluid.Contains(FluidCell(p));

    /// <summary>Puts the cell into the woken set for the next fluid step.</summary>
    private void WakeFluidCell(Vector3i p) => _activeFluid.Add(FluidCell(p));
    private double _sinceFluid { get => _worlds.Active.SinceFluid; set => _worlds.Active.SinceFluid = value; }
    private ushort _waterId, _lavaId, _obsidianId, _basaltId;

    /// <summary>The waterfall block (#1726): a placed block — solid, not a fluid — whose underside pours a column
    /// of ordinary water straight down, and that column never spreads sideways, not even where it lands. A builder
    /// wanted a waterfall on a levelled ~80×80 spaceport and got a flood instead: a falling cell is refilled at
    /// <see cref="FluidFull"/>, so every step of stepped ground re-arms a seven-cell spread, and on a big flat
    /// build "one water block" meant "the whole place". Changing that rule would change every existing body of
    /// water in every save; a source that only ever falls is the tool she actually asked for (Marcel's call,
    /// option C of the issue).</summary>
    private const string WaterSpoutBlockKey = "water_spout";
    private ushort _spoutId;

    /// <summary>How close a player must be to a lava cell hardened by a FLOWING quench to be told about it
    /// (#1727), and how long that explanation stays quiet afterwards. A flood over a trench hardens hundreds
    /// of cells in a few ticks; the point is one sentence per episode, not one per block.</summary>
    private const double FlowQuenchTellRange = 40.0;
    private const double FlowQuenchTellCooldown = 60.0;

    private void InitFluids()
    {
        _waterId = _content.GetBlock("water")?.NumericId.Value ?? 0;
        _lavaId = _content.GetBlock("lava")?.NumericId.Value ?? 0;
        _obsidianId = _content.GetBlock("obsidian")?.NumericId.Value ?? 0;
        _basaltId = _content.GetBlock("basalt")?.NumericId.Value ?? 0;
        _spoutId = _content.GetBlock(WaterSpoutBlockKey)?.NumericId.Value ?? 0;
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

        ushort crust = IsFlowingCell(pos) ? _basaltId : _obsidianId;
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

    /// <summary>
    /// Tells nearby players, at most once a minute each, what a FLOWING quench actually did (#1727).
    /// <para>
    /// Placing a fluid by hand already explains itself (<see cref="QuenchPlacedFluid"/>), but a flood that
    /// reaches a lava trench on its own said nothing at all. A player watched her whole trench go dark, saw
    /// glowing lava still in the walls beneath the new rock, and reasonably concluded the game had laid a
    /// block ON TOP of the lava. It had not — <see cref="QuenchLava"/> replaces the cell it hardens — but only
    /// the cells the water actually touched are quenched, so the molten core stays. That is the sentence the
    /// game owed her.
    /// </para>
    /// </summary>
    private void TellAboutFlowQuench(Vector3i waterPos)
    {
        foreach (var s in JoinedInActiveWorld())
        {
            if (_uptime - s.LastFlowQuenchTold < FlowQuenchTellCooldown)
            {
                continue;
            }

            double dx = WorldConstants.WrapDeltaX(s.State.Position.X - waterPos.X, _world.Circumference);
            double dy = s.State.Position.Y - waterPos.Y;
            double dz = s.State.Position.Z - waterPos.Z;
            if (dx * dx + dy * dy + dz * dz > FlowQuenchTellRange * FlowQuenchTellRange)
            {
                continue;
            }

            s.LastFlowQuenchTold = _uptime;
            Send(s, new ServerMessage { Text = "@srv.fluid.quench_surface_only" });
        }
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

    /// <summary>A still liquid by its block data (#2106: oil) — NOT one of the automaton's two fluids: it never flows,
    /// a pumped cell stays air, and only the pump, the scanner, the oxygen drain and the aim treat it as liquid.</summary>
    private bool IsLiquid(BlockId id) => !id.IsAir && (_world.Definition(id)?.Liquid ?? false);

    /// <summary>Registers a full fluid source at the cell (the block must already be set). A source is an
    /// <i>untracked</i> cell — no level entry — so it is always full and never recedes, exactly like a
    /// worldgen sea. Flowing cells, by contrast, live in <c>_fluidLevel</c> and dry up when cut off.
    /// <para>
    /// The neighbours are woken too (#2448). A source only ever spreads into AIR, so one placed into a pond —
    /// the client puts the block into the water cell under the crosshair (#851) — changed nothing around it: the
    /// flowing cells beside it kept their old, lower level until something else happened to wake them, and a pond
    /// that had already come to rest never grew to its new size. They are queued without reading them: a source at
    /// the edge of the loaded world must not generate the chunk beyond (#1824) — the step parks what it cannot read
    /// yet and drops what is no fluid.
    /// </para>
    /// <para>
    /// Placed water is marked as a source (#2515, W1): generated water above the sea level is finite, but a block the
    /// player places stays bottomless — the fall rule alone keeps it to about seven cells of travel.
    /// </para></summary>
    public void RegisterFluidSource(Vector3i pos)
    {
        UntrackFluid(pos);
        if (_waterId != 0 && _world.GetBlock(pos).Value == _waterId)
        {
            MarkPlacedSource(pos);
        }

        WakeFluidCell(pos);
        WakeFluidCell(new Vector3i(pos.X + 1, pos.Y, pos.Z));
        WakeFluidCell(new Vector3i(pos.X - 1, pos.Y, pos.Z));
        WakeFluidCell(new Vector3i(pos.X, pos.Y, pos.Z + 1));
        WakeFluidCell(new Vector3i(pos.X, pos.Y, pos.Z - 1));
        WakeFluidCell(new Vector3i(pos.X, pos.Y + 1, pos.Z));
        WakeFluidCell(new Vector3i(pos.X, pos.Y - 1, pos.Z));
    }

    /// <summary>Records a flowing cell's level (memory + save). The persisted row is what stops a restart from
    /// promoting the cell to an untracked source (#657).</summary>
    private void TrackFluid(Vector3i pos, byte level, bool falling)
    {
        var cell = FluidCell(pos);
        if (_placedSources.Count > 0)
        {
            ForgetPlacedSource(cell); // a flowing cell is never a placed source
        }

        _fluidLevel[cell] = level;
        if (falling)
        {
            _fallingFluid.Add(cell);
        }
        else
        {
            _fallingFluid.Remove(cell); // a cell filled sideways rests on the surface it spread across
        }

        _repo.SaveFluidCell(_world.LocationId, cell, level, falling);
    }

    /// <summary>Drops a cell's flowing state (memory + save) — it dried up, became a source, or its block
    /// was replaced — and its placed-source mark (#2515). Safe to call for cells that were never tracked.</summary>
    private void UntrackFluid(Vector3i pos)
    {
        var cell = FluidCell(pos);
        if (_fluidLevel.Remove(cell))
        {
            _repo.DeleteFluidCell(_world.LocationId, cell);
        }

        _fallingFluid.Remove(cell);
        ForgetPlacedSource(cell);
    }

    /// <summary>Restores this world's persisted flowing-fluid cells (levels + falling flags) and wakes them,
    /// so streams keep flowing/retracting across a restart instead of fossilising into full sources (#657).
    /// Deliberately does not touch the world's blocks here (that would force chunk generation at load) — the
    /// tick's own stale-cell check drops any row whose block is no longer a fluid.
    /// <para>
    /// A row written before the keys were canonical (#2447) is moved to the canonical cell; a cell that ended up
    /// with a row under each spelling keeps the higher level. Water a seam flood already left in a save stays as
    /// it is — a stored flood cannot be told apart from water placed on purpose.
    /// </para>
    /// <para>
    /// The placed-source marks come back first (#2515). Waking the rows is also what heals a save flooded under the
    /// old rules: re-evaluated with "a fall keeps its level", a long tongue loses its feed and recedes.
    /// </para></summary>
    private void LoadFluidState()
    {
        _ = _placedSources; // the marks load on first use; reading them here runs the one-time migration at boot
        var rows = new Dictionary<Vector3i, (byte Level, bool Falling)>();
        List<(Vector3i Stored, Vector3i Cell)>? moved = null;
        foreach (var row in _repo.ListFluidCells(_world.LocationId))
        {
            var cell = FluidCell(row.WorldPosition);
            byte level = Math.Clamp(row.Level, (byte)1, FluidFull);
            if (!cell.Equals(row.WorldPosition))
            {
                (moved ??= new List<(Vector3i, Vector3i)>()).Add((row.WorldPosition, cell));
            }

            if (!rows.TryGetValue(cell, out var have) || level > have.Level)
            {
                rows[cell] = (level, row.Falling);
            }
        }

        foreach (var (cell, state) in rows)
        {
            _fluidLevel[cell] = state.Level;
            if (state.Falling)
            {
                _fallingFluid.Add(cell);
            }

            _activeFluid.Add(cell); // wake: orphans retract, still-fed cells settle again
        }

        if (moved != null)
        {
            _repo.RunInTransaction(() =>
            {
                foreach (var (stored, cell) in moved)
                {
                    _repo.DeleteFluidCell(_world.LocationId, stored);
                    var state = rows[cell];
                    _repo.SaveFluidCell(_world.LocationId, cell, state.Level, state.Falling);
                }
            });
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

    private bool IsSpout(ushort id) => id != 0 && id == _spoutId;

    /// <summary>A just-placed waterfall block starts pouring (#1726). Placed on solid ground it can do nothing at
    /// all, and a block that silently does nothing is a bug report waiting to happen — so the player is told.</summary>
    private void StartSpout(PlayerSession session, Vector3i pos)
    {
        WakeFluidCell(pos);
        if (!FluidCanEnter(new Vector3i(pos.X, pos.Y - 1, pos.Z)))
        {
            Send(session, new ServerMessage { Text = "@srv.fluid.spout_needs_drop" });
        }
    }

    /// <summary>One step of a waterfall block: fill the cell beneath with full, falling water if it is open. The
    /// block then sleeps — it is woken again like any fluid when a neighbour changes (the column dries up, the cell
    /// below is mined), so a spout on a wall keeps pouring for as long as it stands there.</summary>
    private void PourFromSpout(Vector3i pos)
    {
        if (_waterId == 0 || CrystalSpoutClosed(pos)) // #2053: a conduit can shut a spout
        {
            return;
        }

        var below = new Vector3i(pos.X, pos.Y - 1, pos.Z);
        if (FluidCanEnter(below))
        {
            FillFluid(below, new BlockId(_waterId), FluidFull, falling: true);
        }
    }

    /// <summary>Whether a falling water cell hangs from a waterfall block: walks up the falling column above it and
    /// answers true if the block at its top is a spout. Only evaluated for the cell at the foot of a fall — the one
    /// place an ordinary column would start to spread — so the walk costs the column's height, once per landing.
    /// A generated waterfall column (#2515) counts like a spout: water poured from it into a dug-out foot stays a
    /// confined fall too.</summary>
    private bool FedByASpout(Vector3i p, ushort id)
    {
        if (id != _waterId)
        {
            return false;
        }

        var c = p;
        for (int guard = 0; guard < 256; guard++) // a column is at most a world's height tall
        {
            var up = new Vector3i(c.X, c.Y + 1, c.Z);
            if (!_world.IsNeighbourhoodLoaded(up, 0, _fluidMayLoad))
            {
                return true; // #1824: the column runs into an unloaded chunk — stay confined rather than generate it
            }

            ushort u = _world.GetBlock(up).Value;
            if (IsSpout(u))
            {
                return true;
            }

            if (u != id)
            {
                return false;
            }

            if (!IsFallingCell(up))
            {
                var upCell = FluidCell(up);
                return !_fluidLevel.ContainsKey(upCell) && ClassifyWater(upCell) == WaterClass.ConfinedFall;
            }

            c = up;
        }

        return false;
    }

    /// <summary>Test seam: places a waterfall block and wakes it exactly as the placement path does.</summary>
    public void PlaceWaterSpoutForTest(int x, int y, int z)
    {
        if (_spoutId == 0)
        {
            return;
        }

        var pos = new Vector3i(x, y, z);
        _world.SetBlock(pos, new BlockId(_spoutId));
        BroadcastToWorld(new BlockChanged { X = x, Y = y, Z = z, Block = _spoutId });
        WakeFluidCell(pos);
    }

    private void TickFluids(double dt)
    {
        _ = FluidDoorCells; // #2561: a changed door registry rebuilds the doorway set and wakes water standing in one

        // Parked cells (#1824) still need their re-check; a settle (#2515) its steps, and a held-back wake its turn.
        if (_activeFluid.Count == 0 && _worlds.Active.ParkedFluid.Count == 0 && _settles.Count == 0 && _worlds.Active.SettleDeferred.Count == 0)
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
        bool lavaRests = _worlds.Active.FluidStep % LavaStepEvery != 0; // #1316/#2449: lava keeps one cell per 0.5 s

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
        CollectFluidAnchors();
        UnparkFluid();
        StepSettles(); // #2515: still water that is settling or draining moves first; what it wakes is handled below
        var todo = new List<Vector3i>(_activeFluid);
        _activeFluid.Clear();
        int budget = FluidUpdatesPerTick;
        var doorCells = FluidDoorCells; // #2561

        foreach (var queued in todo)
        {
            var pos = FluidCell(queued); // #2447: every wake is canonical already; this keeps a stray spelling harmless
            if (budget-- <= 0)
            {
                _activeFluid.Add(pos); // defer leftover to the next step
                continue;
            }

            // #1824: the automaton reads (and wakes) cells up to two away. If that neighbourhood reaches into a
            // chunk that is not resident and lies outside every player's keep range, each GetBlock there would
            // GENERATE terrain inside the simulation step — outside the streaming budget, for nobody's view, only for
            // the next sweep to evict it again. Park the cell instead (Minecraft freezes simulation at the loaded
            // edge the same way); it resumes once the chunk is resident or a player comes within range.
            if (!_world.IsNeighbourhoodLoaded(pos, FluidReach, _fluidMayLoad))
            {
                _worlds.Active.ParkedFluid.Add(pos);
                continue;
            }

            ushort id = _world.GetBlock(pos).Value;
            if (IsSpout(id))
            {
                PourFromSpout(pos); // #1726: a waterfall block only ever feeds the cell beneath it
                continue;
            }

            if (!IsFluid(id))
            {
                UntrackFluid(pos);
                continue;
            }

            if (doorCells.Count > 0 && doorCells.Contains(pos))
            {
                // #2561: a doorway holds no fluid. What an older save left in one — the tongue that ran through a shut
                // door — dries up here, and whatever it fed downstream loses its feed and recedes with it.
                RetractFluid(pos);
                continue;
            }

            if (id == _lavaId)
            {
                if (QuenchLava(pos) != 0)
                {
                    continue; // touched water: it is rock now (#1284) — contact fires on wake, never waits for the cadence
                }

                // Lava keeps one cell per 0.5 s (#1316, maintainer decision — readability, not realism: fast lava
                // kills before you can react). Since water doubled its pace (#2449) that is a quarter of the water
                // speed. The budget and the wake set stay shared; a lava cell sits out three steps of four and is
                // re-queued untouched.
                if (lavaRests)
                {
                    _activeFluid.Add(pos);
                    continue;
                }

                IgniteFlammableNeighbors(pos); // active/flowing lava sets adjacent plants/wood alight (item 30)
            }
            else if (id == _waterId)
            {
                var (crustedObsidian, crustedBasalt) = CrustLavaAround(pos); // a woken water cell hardens the lava it touches (#1284)
                if (crustedObsidian + crustedBasalt > 0)
                {
                    TellAboutFlowQuench(pos); // #1727: say what happened, once per episode
                }
            }

            bool isSource = !_fluidLevel.ContainsKey(pos);
            byte level;
            bool changed = false;
            bool confinedFall = false;

            if (isSource && id == _waterId)
            {
                // #2515: only endless water is a source. A cell a running settle owns waits for it (a draining
                // outlet keeps its stream going); finite still water never spreads as a flow — with somewhere to go,
                // the whole body settles; at rest it sleeps. A body too big to settle is a reservoir and falls through.
                if (_settling.Contains(pos))
                {
                    FeedDrainStream(pos);
                    continue;
                }

                var waterClass = ClassifyWater(pos);
                if (waterClass == WaterClass.Finite && (!HasAirNeighbor(pos) || TrySettle(pos)))
                {
                    continue;
                }

                confinedFall = waterClass == WaterClass.ConfinedFall; // a generated waterfall column: it falls, it never spreads
            }

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
                // #2515: a fall keeps its level. It used to refill to FluidFull, so every step down re-armed a fresh
                // seven-cell spread and a flood grew with the terrain — stepped ground, cave shafts and cave floors.
                FillFluid(below, kind, level, falling: true);
                changed = true;
            }
            else if (level > 1)
            {
                // Don't crawl sideways while feeding a waterfall: a cell sitting on a *falling* column would
                // otherwise spread at its own (high) elevation and build a sheet of water hanging over the drop.
                ushort belowId = _world.GetBlock(below).Value;
                bool feedingFall = IsFluid(belowId) && IsFallingCell(below);
                // ...and a column poured by a waterfall block (#1726) never spreads at all, not even where it
                // lands: the whole point of that block is a fall that ends where it hits, not a sheet across the floor.
                bool confined = confinedFall || (!feedingFall && _fallingFluid.Contains(pos) && FedByASpout(pos, id));
                if (!feedingFall && !confined)
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
            if (changed || (isSource && !confinedFall && HasAirNeighbor(pos)))
            {
                _activeFluid.Add(pos);
            }
        }
    }

    /// <summary>#1824: how far (cells) one fluid step reads around a woken cell — its neighbours, and the neighbours
    /// of a filled neighbour through <see cref="WakeNeighbors"/>.</summary>
    private const int FluidReach = 2;

    /// <summary>#1824: the canonical chunks of the players on the active world and the sweep's keep radius — a chunk
    /// the fluid step would have to load is fine inside that range (the stream would load it anyway).</summary>
    private readonly List<ChunkCoord> _fluidAnchors = new();
    private int _fluidKeepSq;
    private System.Func<ChunkCoord, bool>? _fluidMayLoadCache;

    private System.Func<ChunkCoord, bool> _fluidMayLoad => _fluidMayLoadCache ??= FluidMayLoad;

    private void CollectFluidAnchors()
    {
        _fluidAnchors.Clear();
        int maxView = 1;
        foreach (var session in JoinedInActiveWorld())
        {
            _fluidAnchors.Add(WorldConstants.CanonicalChunk(WorldConstants.WorldToChunk(session.State.Position.ToBlock()), _world.Circumference));
            maxView = Math.Max(maxView, EffectiveViewRadius(session));
        }

        int keep = maxView + 4; // SweepFarChunks' keep radius
        _fluidKeepSq = keep * keep;
    }

    /// <summary>No player on the world (tests, a server-side stamp) keeps the old behaviour: load what is read.</summary>
    private bool FluidMayLoad(ChunkCoord coord)
    {
        if (_fluidAnchors.Count == 0)
        {
            return true;
        }

        foreach (var anchor in _fluidAnchors)
        {
            if (WorldConstants.WrappedChunkDistanceSquared(coord, anchor, _world.Circumference) <= _fluidKeepSq)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>#1824: moves parked cells back into the woken set once their neighbourhood may be read again — after
    /// a chunk load, and once a second anyway (a player walking closer changes the answer without a load).</summary>
    private void UnparkFluid()
    {
        var parked = _worlds.Active.ParkedFluid;
        if (parked.Count == 0
            || (_worlds.Active.ParkedFluidCheckedAt == _world.ChunkLoads && _worlds.Active.FluidStep % UnparkEverySteps != 0))
        {
            return;
        }

        _worlds.Active.ParkedFluidCheckedAt = _world.ChunkLoads;
        parked.RemoveWhere(p =>
        {
            if (!_world.IsNeighbourhoodLoaded(p, FluidReach, _fluidMayLoad))
            {
                return false;
            }

            _activeFluid.Add(p);
            return true;
        });
    }

    /// <summary>Test seam (#1824): how many fluid cells currently wait at the loaded edge.</summary>
    public int ParkedFluidCountForTest => _worlds.Active.ParkedFluid.Count;

    /// <summary>Test seam (#2447): how many fluid cells wait for the next step — 0 once every body has come to rest.</summary>
    public int ActiveFluidCountForTest => _worlds.Active.ActiveFluid.Count;

    /// <summary>The level a <i>flowing</i> cell can sustain from its surroundings: the level of the same fluid directly
    /// above (a falling column carries its level down — #2515; a feeding source or the waterfall block counts as full),
    /// or the strongest horizontal neighbour's level minus one, whichever is higher. 0 means nothing feeds it any more →
    /// it should dry up. Only the same kind of fluid feeds (water never sustains lava or vice-versa), and finite still
    /// water feeds nothing (<see cref="FeedsAsSource"/>).</summary>
    private int SupportedLevel(Vector3i p, ushort id)
    {
        var up = new Vector3i(p.X, p.Y + 1, p.Z);
        ushort above = _world.GetBlock(up).Value;
        if (id == _waterId && IsSpout(above))
        {
            return FluidFull; // fed by the waterfall block itself (#1726)
        }

        int best = 0;
        if (above == id)
        {
            var upCell = FluidCell(up);
            best = _fluidLevel.TryGetValue(upCell, out var upLevel) ? upLevel : FeedsAsSource(upCell, id) ? FluidFull : 0;
        }

        best = Math.Max(best, NeighborFeed(new Vector3i(p.X + 1, p.Y, p.Z), id));
        best = Math.Max(best, NeighborFeed(new Vector3i(p.X - 1, p.Y, p.Z), id));
        best = Math.Max(best, NeighborFeed(new Vector3i(p.X, p.Y, p.Z + 1), id));
        best = Math.Max(best, NeighborFeed(new Vector3i(p.X, p.Y, p.Z - 1), id));
        return best;
    }

    /// <summary>How much a horizontal neighbour can feed this cell: an endless source feeds at FluidFull−1, a flowing
    /// neighbour at its own level−1, anything else (air / the other fluid / finite still water) feeds nothing.</summary>
    private int NeighborFeed(Vector3i n, ushort id)
    {
        if (_world.GetBlock(n).Value != id)
        {
            return 0;
        }

        var cell = FluidCell(n);
        if (_fluidLevel.TryGetValue(cell, out var lv))
        {
            return lv - 1;
        }

        return FeedsAsSource(cell, id) ? FluidFull - 1 : 0;
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
    /// can never flow into (or refill) a landed ship's cabin, keeping a submerged ship watertight and dry. A cell a
    /// running drain has just emptied is closed too (#2515), so a draining pond's own stream does not run back in.
    /// A doorway is closed as well (#2561, <see cref="FluidDoorCells"/>): a door keeps water and lava out.</summary>
    private bool FluidCanEnter(Vector3i p)
    {
        if (!_world.GetBlock(p).IsAir || InShipInterior(p))
        {
            return false;
        }

        var doorCells = FluidDoorCells;
        if (_drained.Count == 0 && doorCells.Count == 0)
        {
            return true;
        }

        var cell = FluidCell(p);
        return !_drained.Contains(cell) && !doorCells.Contains(cell);
    }

    /// <summary>
    /// #2561: every cell a door fills on the active world — open or shut, any kind, hung by a player or stamped with a
    /// structure (the door's width along its wall × the three-high opening, as <see cref="DoorCellsWhere"/> lays it out).
    /// <para>
    /// A door is an entity standing in an AIR cell, and the automaton only ever asks "is it air?" — so water used to flow
    /// straight through a shut door (a tester's base flooded through its wooden door), and the player could not plug the
    /// gap, because nothing may be built into a doorway. Now a doorway is a wall to both fluids: the flow, the settle of
    /// still water and the drain stream all refuse it. An open door is no drain either — one rule, whatever the door's
    /// state, so opening a door never floods a room. Lava shares the rule (it enters cells through the same check).
    /// </para>
    /// <para>
    /// Kept as a set per world, rebuilt only when the door registry changes (the version every door add, removal and
    /// rebuild bumps), so the check in the hot <see cref="FluidCanEnter"/> stays one hash lookup without allocation.
    /// </para>
    /// </summary>
    private HashSet<Vector3i> FluidDoorCells
    {
        get
        {
            var world = _worlds.Active;
            if (world.FluidDoorCellsVersion != _baseAirDoorsVersion)
            {
                RebuildFluidDoorCells(world);
            }

            return world.FluidDoorCells;
        }
    }

    /// <summary>#2561: rebuilds the active world's doorway set from the door registry. A doorway that is gone (a door was
    /// taken down) wakes the fluid around it, so the water it held back flows into the opening; fluid already standing in a
    /// doorway — an older save, or the upper cells of a door just hung — is woken so <see cref="StepFluids"/> clears it.
    /// Only resident chunks are read; a doorway in an unloaded chunk is cleared on its next wake.</summary>
    private void RebuildFluidDoorCells(LoadedWorld world)
    {
        world.FluidDoorCellsVersion = _baseAirDoorsVersion;
        var cells = world.FluidDoorCells;
        var fresh = _doors.Count > 0 ? DoorCellsWhere(_ => true) : null;
        foreach (var gone in cells)
        {
            if (fresh is null || !fresh.Contains(gone))
            {
                foreach (var d in SettleDirs)
                {
                    WakeFluidIfLoaded(new Vector3i(gone.X + d.X, gone.Y + d.Y, gone.Z + d.Z));
                }
            }
        }

        cells.Clear();
        if (fresh is null)
        {
            return;
        }

        cells.UnionWith(fresh);
        foreach (var c in cells)
        {
            WakeFluidIfLoaded(c);
        }
    }

    /// <summary>Wakes the cell if it holds a fluid or a waterfall block — read without ever loading its chunk.</summary>
    private void WakeFluidIfLoaded(Vector3i p)
    {
        ushort id = _world.GetBlockIfLoaded(p).Value;
        if (IsFluid(id) || IsSpout(id))
        {
            WakeFluidCell(p);
        }
    }

    /// <summary>Test seam (#2561): whether fluids treat the cell as a doorway.</summary>
    internal bool IsFluidDoorCellForTest(int x, int y, int z) => FluidDoorCells.Contains(FluidCell(new Vector3i(x, y, z)));

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
        pos = FluidCell(pos); // #2447: a neighbour across the seam arrives as pos ± 1 — store and wake the one spelling

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
        WakeFluidCell(pos);
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
        || IsFluid(_world.GetBlock(new Vector3i(p.X, p.Y - 1, p.Z)).Value)
        || IsSpout(_world.GetBlock(new Vector3i(p.X, p.Y + 1, p.Z)).Value); // a waterfall block overhead pours into the hole (#1726)

    /// <summary>True if a cell has any neighbour it could flow into (sideways or down) — used to let settled
    /// full cells go dormant, so a big body of fluid doesn't keep every cell active forever. Ship-interior
    /// cells don't count (the fluid can't enter them), so a source against a submerged hull also goes dormant.</summary>
    private bool HasAirNeighbor(Vector3i p)
        => FluidCanEnter(new Vector3i(p.X + 1, p.Y, p.Z)) || FluidCanEnter(new Vector3i(p.X - 1, p.Y, p.Z))
        || FluidCanEnter(new Vector3i(p.X, p.Y, p.Z + 1)) || FluidCanEnter(new Vector3i(p.X, p.Y, p.Z - 1))
        || FluidCanEnter(new Vector3i(p.X, p.Y - 1, p.Z));

    /// <summary>When a fluid cell is removed (mined), wake the surrounding fluid so it flows back into the
    /// hole and any orphaned flowing tail recedes. Endless water refills the hole as a flow; finite still water
    /// (#2515) settles into it from the top of its body; a flowing stream cut off here dries up via
    /// <see cref="SupportedLevel"/>. The removed cell's placed-source mark goes with it.
    /// Bounded by the per-tick budget + the settle guard in <see cref="TickFluids"/>.</summary>
    private void OnFluidRemoved(Vector3i pos)
    {
        ForgetPlacedSource(pos);
        WakeNeighbors(pos);
    }

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
        ushort id = _world.GetBlock(p).Value;
        if (IsFluid(id) || IsSpout(id))
        {
            WakeFluidCell(p); // untracked stays a source, tracked stays flowing — no promotion here
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
