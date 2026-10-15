// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The miner's work core (#2643, wilderness folk #2639), shared by the lodge residents (#2641) and the wanderers' prospector
/// (#2646). Natural terrain never grows back and every changed cell is a saved delta row, so an NPC that mined freely would
/// dig ever larger holes into the save. Instead every cell an NPC breaks fills up again: <see cref="NpcMineCell"/> breaks
/// it through the machine path (<see cref="BreakBlockCore"/> with no session and an EMPTY owner — never a player edit, so
/// bio samples, the drill rules and grief attribution stay as they were) and writes it into the world's refill registry
/// (<see cref="WorldMetadata.NpcRefills"/>, persisted); <see cref="TickNpcRestores"/> puts the block back once its time is
/// up, the cell is still air and no player stands within <see cref="NpcRefillPlayerClearance"/>.
/// <para>The core does ONE action per call and has no cadence of its own — the caller paces it (a lodge miner one cell
/// every 60–90 s). The drops go to the caller's <c>bank(itemKey, count)</c> (the resident's store crate).</para>
/// <para>Protection: the miner works INSIDE its lodge (the quarry face is the lodge's), so <see cref="NpcMineCell"/> asks the
/// narrow set — a settlement's box and the intercity line, never <see cref="IsSettlementBlock"/> (which also holds the
/// lodges) — plus ships, stations, the Guardian core, factories and bases. <see cref="FindNpcOutcrops"/> (a wanderer, who
/// must never mine a lodge's face) asks the full <see cref="IsSettlementBlock"/>.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Default refill delay of a mined cell (about ten minutes of play).</summary>
    private const double NpcMineRefillSeconds = 600.0;

    /// <summary>A due cell fills up only while no player stands within this many blocks — nobody sees stone appear.</summary>
    private const float NpcRefillPlayerClearance = 12f;

    /// <summary>The refill registry is stepped once a second per world.</summary>
    private const double NpcRestoreStepSeconds = 1.0;

    /// <summary>A changed refill registry is written at most this often (seconds of server time) — a mined cell or a
    /// restore step marks it dirty instead of serialising the whole metadata at once; every full save (autosave,
    /// checkpoint, shutdown) carries it anyway.</summary>
    private const double NpcRefillSaveSeconds = 60.0;

    /// <summary>How far above and below the centre <see cref="FindNpcOutcrops"/> looks for a column's surface.</summary>
    private const int NpcOutcropBand = 12;

    /// <summary>The widest search <see cref="FindNpcOutcrops"/> runs (blocks) — it walks every column of the disc.</summary>
    private const int NpcOutcropMaxRadius = 32;

    /// <summary>The highest tool tier an outcrop may need — a wanderer's pick, not a diamond drill.</summary>
    private const int NpcOutcropMaxTier = 1;

    /// <summary>Terrain an NPC never breaks: the floor of the world, a geyser, fire, and the gases and tars.</summary>
    private static readonly HashSet<string> NpcUnmineableTerrain = new(StringComparer.Ordinal)
    {
        "bedrock", "geyser_vent", "fire", "oil", "tar", "gas", "gas_dense",
    };

    // Per world (location id): the uptime of its next refill step (the worlds are ticked in one server tick, in which the
    // uptime stands still, so one shared gate would give every step to the world that came first).
    private readonly Dictionary<string, double> _npcRestoreAt = new();

    /// <summary>The most pending refills per world (<see cref="NpcRefillRecord.MaxPerWorld"/>; a test lowers it).</summary>
    private int _npcRefillCap = NpcRefillRecord.MaxPerWorld;

    // The registry changed since it was last written, and the uptime from which on the next coalesced write may run. The
    // metadata is one object for every world, so one gate serves them all.
    private bool _npcRefillsDirty;
    private double _npcRefillsSaveAt;

    /// <summary>Breaks one cell as an NPC (<c>BreakBlockCore(session: null, ownerId: "")</c> — never a player edit), banks
    /// the drops and schedules the cell to fill up again after <paramref name="refillSeconds"/> of play (persisted). False
    /// when the cell is air, in an unloaded chunk, protected, player-edited or nothing a miner breaks (only natural
    /// terrain and ore; no plant, tree, fluid, crate or machine), or when breaking it would open a fluid or bring down
    /// what rests on it (sand, a plant, a tree).</summary>
    private bool NpcMineCell(ServerNpc npc, Vector3i cell, System.Action<string, int> bank, double refillSeconds)
    {
        cell = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        if (!NpcCellLoaded(cell))
        {
            return false;
        }

        var id = _world.GetBlockIfLoaded(cell);
        if (id.IsAir || _content.BlockById(id) is not { } def || !NpcMinerBreaks(def, id)
            || NpcMinerProtected(cell) || NpcCellUnsafeToOpen(cell)
            || _repo.HasPlayerBlockEdits(_world.LocationId, cell, cell))
        {
            return false;
        }

        // Read the cell's dye and form BEFORE the break clears them — the refill brings them back.
        var (tint, glow) = _world.GetModifier(cell);
        int shape = _world.GetShape(cell);
        FaceTowards(npc, new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f));
        BreakBlockCore(null, string.Empty, cell, def, null, bank); // broadcasts the change: every client shows the break
        ScheduleNpcRefill(cell, def.Key, tint, glow, shape, refillSeconds);
        return true;
    }

    /// <summary>Natural surface stone/ore cells with air above around <paramref name="center"/> (the wanderers' prospector):
    /// stone or ore a drill of tier ≤ <see cref="NpcOutcropMaxTier"/> cuts (no soil, sand or bedrock), each the top of its
    /// column within <see cref="NpcOutcropBand"/> of the centre's height; never player-edited, never protected (the full
    /// settlement rule — a lodge's face too), never one whose breaking would open a fluid; loaded chunks only; at most
    /// <paramref name="max"/>, nearest first.</summary>
    private List<Vector3i> FindNpcOutcrops(Vector3f center, int radius, int max)
    {
        var result = new List<Vector3i>();
        radius = System.Math.Min(radius, NpcOutcropMaxRadius);
        if (max <= 0 || radius <= 0)
        {
            return result;
        }

        int cx = (int)System.Math.Floor(center.X), cy = (int)System.Math.Floor(center.Y), cz = (int)System.Math.Floor(center.Z);
        int band = System.Math.Min(radius, NpcOutcropBand);
        int radiusSq = radius * radius;
        var found = new List<(Vector3i Cell, int DistSq)>();
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (dx * dx + dz * dz > radiusSq)
                {
                    continue;
                }

                for (int y = cy + band; y >= cy - band; y--)
                {
                    var cell = WorldConstants.CanonicalBlock(new Vector3i(cx + dx, y, cz + dz), _world.Circumference);
                    if (!NpcCellLoaded(cell))
                    {
                        break; // an unloaded cell reads as air — nothing to trust in this column
                    }

                    var id = _world.GetBlockIfLoaded(cell);
                    if (id.IsAir)
                    {
                        continue;
                    }

                    // The first thing from above is the column's surface: an outcrop when it is open to the sky and rock.
                    var above = new Vector3i(cell.X, cell.Y + 1, cell.Z);
                    if (NpcCellLoaded(above) && _world.GetBlockIfLoaded(above).IsAir
                        && _content.BlockById(id) is { } def && NpcOutcropBlock(def, id))
                    {
                        int dy = y - cy;
                        found.Add((cell, dx * dx + dy * dy + dz * dz));
                    }

                    break;
                }
            }
        }

        // The cheap scan first, the protection and the store query only for the nearest few.
        found.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
        foreach (var (cell, _) in found)
        {
            if (NpcProspectorProtected(cell) || NpcCellUnsafeToOpen(cell)
                || _repo.HasPlayerBlockEdits(_world.LocationId, cell, cell))
            {
                continue;
            }

            result.Add(cell);
            if (result.Count >= max)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>Puts every given cell that is air back to its block at once (a lodge restoring its stamped quarry face on
    /// load) and drops their pending refills. A cell a player edited is left alone (its pending refill still goes); a block
    /// id of 0 only drops the refill. Loads the chunks it needs.</summary>
    private void RestoreNpcCellsNow(IEnumerable<(Vector3i Cell, ushort Block)> cells)
    {
        _meta.NpcRefills.TryGetValue(_world.LocationId, out var pending);
        bool registryChanged = false;
        foreach (var (raw, block) in cells)
        {
            var cell = WorldConstants.CanonicalBlock(raw, _world.Circumference);
            NpcRefillRecord? record = null;
            int at = pending?.FindIndex(r => r.X == cell.X && r.Y == cell.Y && r.Z == cell.Z) ?? -1;
            if (at >= 0)
            {
                record = pending![at];
                pending.RemoveAt(at);
                registryChanged = true;
            }

            if (block == 0 || !_world.GetBlock(cell).IsAir || _repo.HasPlayerBlockEdits(_world.LocationId, cell, cell))
            {
                continue;
            }

            // The pending refill knows what was mined there — block, dye and form; the stamp's layout is re-derived from the
            // content of today, so after an update that moved the planet's ore list it could name another ore. The record
            // wins; the layout is only the fallback for a cell whose record was lost (a crash between two saves).
            if (record is not null && _content.GetBlock(record.Block)?.NumericId is { } mined && mined.Value != 0)
            {
                PutNpcCellBack(cell, mined, record.Tint, record.Glow, record.Shape);
                continue;
            }

            PutNpcCellBack(cell, new BlockId(block), 0, 0, 0);
        }

        if (registryChanged)
        {
            MarkNpcRefillsChanged(pending!);
        }
    }

    /// <summary>Per-world step of the persisted refill registry (registered after <c>TickFlora</c>, runs once a second). A
    /// due cell (its time measured in played seconds, <see cref="WorldMetadata.CumulativePlaytimeSeconds"/>) that is still
    /// air gets its block back with a <see cref="BlockChanged"/> — when its chunk is loaded (an unloaded one waits for a
    /// player's streaming area; no chunk is loaded for it), no player stands within <see cref="NpcRefillPlayerClearance"/>
    /// and nobody stands in it (an NPC, an animal, a drop on the ground, a landed ship). A cell somebody built into, or a
    /// player dug again, is left as it is and leaves the registry. A changed registry is written coalesced
    /// (<see cref="FlushNpcRefillsIfDue"/>).</summary>
    private void TickNpcRestores(double dt)
    {
        FlushNpcRefillsIfDue();
        string world = _world.LocationId;
        if (!_meta.NpcRefills.TryGetValue(world, out var list) || list.Count == 0)
        {
            return;
        }

        if (_npcRestoreAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _npcRestoreAt[world] = _uptime + NpcRestoreStepSeconds;
        long now = _meta.CumulativePlaytimeSeconds;
        List<Vector3f>? players = null;
        bool changed = false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var r = list[i];
            if (r.DueAt > now)
            {
                continue;
            }

            var cell = new Vector3i(r.X, r.Y, r.Z);
            if (!NpcCellLoaded(cell))
            {
                continue;
            }

            if (!_world.GetBlockIfLoaded(cell).IsAir)
            {
                list.RemoveAt(i); // somebody built into it (or something filled it): it stays as it is
                changed = true;
                continue;
            }

            players ??= NpcRefillWitnesses();
            if (NpcRefillWatched(players, cell) || NpcRefillCellOccupied(cell))
            {
                continue; // later — nobody sees stone appear, nobody is buried in it
            }

            if (!_repo.HasPlayerBlockEdits(world, cell, cell) && _content.GetBlock(r.Block) is { } def)
            {
                PutNpcCellBack(cell, def.NumericId, r.Tint, r.Glow, r.Shape);
            }

            list.RemoveAt(i); // restored — or a player dug here since: their edit is never overwritten
            changed = true;
        }

        if (changed)
        {
            MarkNpcRefillsChanged(list);
        }
    }

    /// <summary>Writes a mined cell into the world's refill registry (one entry per cell) and marks the registry dirty —
    /// written within <see cref="NpcRefillSaveSeconds"/> (<see cref="FlushNpcRefillsIfDue"/>) and with every full save.
    /// A crash before that write leaves the cell an ordinary mined hole; a lodge restores its quarry face on load
    /// (<see cref="RestoreNpcCellsNow"/>). A full registry restores its oldest entry first (unless somebody stands in it,
    /// or its chunk is not loaded — then the entry is just dropped: no chunk is loaded for it).</summary>
    private void ScheduleNpcRefill(Vector3i cell, string blockKey, int tint, int glow, int shape, double refillSeconds)
    {
        string world = _world.LocationId;
        if (!_meta.NpcRefills.TryGetValue(world, out var list))
        {
            _meta.NpcRefills[world] = list = new List<NpcRefillRecord>();
        }

        list.RemoveAll(r => r.X == cell.X && r.Y == cell.Y && r.Z == cell.Z);
        while (list.Count > 0 && list.Count >= _npcRefillCap)
        {
            var oldest = list[0];
            list.RemoveAt(0);
            var oldCell = new Vector3i(oldest.X, oldest.Y, oldest.Z);
            if (NpcCellLoaded(oldCell) && _world.GetBlockIfLoaded(oldCell).IsAir && !NpcRefillCellOccupied(oldCell)
                && !NpcRefillPlayerInside(oldCell) && !_repo.HasPlayerBlockEdits(world, oldCell, oldCell)
                && _content.GetBlock(oldest.Block) is { } oldDef)
            {
                PutNpcCellBack(oldCell, oldDef.NumericId, oldest.Tint, oldest.Glow, oldest.Shape);
            }
        }

        list.Add(new NpcRefillRecord
        {
            X = cell.X,
            Y = cell.Y,
            Z = cell.Z,
            Block = blockKey,
            Tint = tint,
            Glow = glow,
            Shape = shape,
            DueAt = _meta.CumulativePlaytimeSeconds
                    + (long)System.Math.Ceiling(double.IsNaN(refillSeconds) ? NpcMineRefillSeconds : System.Math.Clamp(refillSeconds, 0.0, 86400.0)),
        });
        _npcRefillsDirty = true;
    }

    /// <summary>Notes a change of the active world's registry (an emptied world's entry goes); the write is coalesced.</summary>
    private void MarkNpcRefillsChanged(List<NpcRefillRecord> list)
    {
        if (list.Count == 0)
        {
            _meta.NpcRefills.Remove(_world.LocationId);
        }

        _npcRefillsDirty = true;
    }

    /// <summary>Writes the metadata when the refill registry changed and the last such write is at least
    /// <see cref="NpcRefillSaveSeconds"/> ago (the first change after a quiet spell is written on the next step). Every
    /// full save (<c>SaveAll</c>: the autosave, a checkpoint, the shutdown) writes the same metadata, the registry in it.</summary>
    private void FlushNpcRefillsIfDue()
    {
        if (!_npcRefillsDirty || _uptime < _npcRefillsSaveAt)
        {
            return;
        }

        _npcRefillsDirty = false;
        _npcRefillsSaveAt = _uptime + NpcRefillSaveSeconds;
        _repo.SaveMetadata(_meta);
    }

    /// <summary>Puts a block back into a mined cell — an ownerless write, like the stamp that made it — and tells every
    /// client.</summary>
    private void PutNpcCellBack(Vector3i cell, BlockId block, int tint, int glow, int shape)
    {
        _world.SetBlock(cell, block, tint, glow, shape);
        BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = block.Value, Tint = tint, Glow = glow, Shape = shape });
        NudgeCreatureBodyChecks(cell); // an animal whose body column holds the cell steps clear on its next tick
    }

    /// <summary>What a miner breaks: natural terrain and ore — a solid, mineable block that is no plant, tree, fluid, gas,
    /// crate or machine, no scrap with a random yield, and none of <see cref="NpcUnmineableTerrain"/>.</summary>
    private bool NpcMinerBreaks(BlockDefinition def, BlockId id)
        => def.Mineable && def.Solid && !def.Liquid && !def.Flammable
           && def.Category is "terrain" or "ore"
           && def.RandomDrops is not { Count: > 0 }
           && !IsFluid(id.Value) && !IsFlora(id.Value) && !IsSapling(id.Value)
           && !_treeSpeciesByBlock.ContainsKey(id.Value)
           && !NpcUnmineableTerrain.Contains(def.Key);

    /// <summary>An outcrop a prospector picks: what a miner breaks, and rock — stone or ore a drill of tier ≤
    /// <see cref="NpcOutcropMaxTier"/> cuts (soil, sand, snow and grass need no drill and are no outcrop).</summary>
    private bool NpcOutcropBlock(BlockDefinition def, BlockId id)
        => NpcMinerBreaks(def, id) && def.RequiredTool == ToolKind.Drill && def.MinToolTier <= NpcOutcropMaxTier;

    /// <summary>The narrow protection a working miner respects (#2639 contract): a settlement's box and the intercity line
    /// — NOT <see cref="IsSettlementBlock"/>, which also holds the lodges whose own quarry face the miner works — plus
    /// parked ships' pads, stations, the Guardian core, factories and bases (checked with an empty actor: protected
    /// against everyone, like fire).</summary>
    private bool NpcMinerProtected(Vector3i cell)
        => IsShipBlock(cell) || IsSettlementBoxBlock(cell) || IsIntercityRailBlock(cell) || IsStationBlock(cell)
           || IsGuardianCoreProtected(cell) || IsFactoryProtected(cell, string.Empty, false)
           || IsBaseProtected(cell, string.Empty, false);

    /// <summary>The prospector's protection: everything a player may not mine there — the full settlement rule, lodges
    /// included — plus the rest of <see cref="NpcMinerProtected"/>.</summary>
    private bool NpcProspectorProtected(Vector3i cell)
        => NpcMinerProtected(cell) || IsSettlementBlock(cell);

    /// <summary>True when breaking the cell would open a fluid (water or lava beside it — or a neighbour whose chunk is not
    /// loaded, which cannot be checked) or bring down what rests on it or hangs from it: sand or snow that would slide in,
    /// a plant or a sapling that would pop, a tree, a torch or a lantern standing on it (the props
    /// <c>PopIfUnsupported</c> drops — a wall beside it is not trusted to hold it), a hanging plant under it — and when a
    /// player edited the cell above (their build, their dig: an NPC never mines under a player's work).</summary>
    private bool NpcCellUnsafeToOpen(Vector3i cell)
    {
        foreach (var face in CrystalNetRules.Faces)
        {
            var n = WorldConstants.CanonicalBlock(cell + face, _world.Circumference);
            if (!NpcCellLoaded(n) || IsFluid(_world.GetBlockIfLoaded(n).Value))
            {
                return true;
            }
        }

        var aboveCell = WorldConstants.CanonicalBlock(new Vector3i(cell.X, cell.Y + 1, cell.Z), _world.Circumference);
        var above = _world.GetBlockIfLoaded(aboveCell);
        if (!above.IsAir
            && (IsGranular(above) || IsFlora(above.Value) || IsSapling(above.Value) || _treeSpeciesByBlock.ContainsKey(above.Value)
                || (_world.Definition(above) is { } aboveDef && IsStandingLightKey(aboveDef.Key))))
        {
            return true;
        }

        var below = _world.GetBlockIfLoaded(new Vector3i(cell.X, cell.Y - 1, cell.Z));
        return _floraHangingIds.Contains(below.Value) || _repo.HasPlayerBlockEdits(_world.LocationId, aboveCell, aboveCell);
    }

    /// <summary>Whether the chunk holding the cell is resident (never loads one).</summary>
    private bool NpcCellLoaded(Vector3i cell) => _world.IsChunkLoaded(WorldConstants.WorldToChunk(cell));

    /// <summary>Where the players of the active world stand (not those flying in space) — the refill's witnesses.</summary>
    private List<Vector3f> NpcRefillWitnesses()
    {
        var list = new List<Vector3f>();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!InSpace(s.State.PlayerId))
            {
                list.Add(s.State.Position);
            }
        }

        return list;
    }

    /// <summary>True when a player stands within <see cref="NpcRefillPlayerClearance"/> of the cell.</summary>
    private bool NpcRefillWatched(List<Vector3f> players, Vector3i cell)
    {
        var centre = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        double rangeSq = (double)NpcRefillPlayerClearance * NpcRefillPlayerClearance;
        foreach (var p in players)
        {
            if (WrapDistSq(p, centre) <= rangeSq)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a player's body (feet or head) is in the cell — the forced restore of a full registry checks it.</summary>
    private bool NpcRefillPlayerInside(Vector3i cell)
    {
        foreach (var p in NpcRefillWitnesses())
        {
            if (NpcRefillBodyIn(p, cell))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when something stands in the cell: an NPC's feet or head, an animal's body, a drop on the ground, a
    /// landed ship.</summary>
    private bool NpcRefillCellOccupied(Vector3i cell)
    {
        foreach (var n in _npcs)
        {
            if (NpcRefillBodyIn(n.Pos, cell))
            {
                return true;
            }
        }

        var centre = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        foreach (var c in _creatures)
        {
            if (WrapDistSq(c.Position, centre) <= 2.25) // within 1.5 blocks of the cell's centre
            {
                return true;
            }
        }

        foreach (var container in _containers)
        {
            if (container.Kind == DropPacketKind && container.Position == cell)
            {
                return true;
            }
        }

        return ShipInteriorContains(centre);
    }

    /// <summary>Whether a standing body (feet at <paramref name="feet"/>, two blocks tall) fills the cell — compared the
    /// short way round both seams (a body just across the longitude or the latitude seam stands in the canonical cell).</summary>
    private bool NpcRefillBodyIn(Vector3f feet, Vector3i cell)
    {
        int x = (int)System.Math.Floor(feet.X), y = (int)System.Math.Floor(feet.Y), z = (int)System.Math.Floor(feet.Z);
        int circ = _world.Circumference;
        return (y == cell.Y || y + 1 == cell.Y)
               && WorldConstants.WrapDeltaX(x - cell.X, circ) == 0 && WorldConstants.WrapDeltaZ(z - cell.Z, circ) == 0;
    }

    // ------------------------------------------------------------------------------------------------------
    // Test seams
    // ------------------------------------------------------------------------------------------------------

    /// <summary>Test seam (#2643): <see cref="NpcMineCell"/> as the NPC with this id; the drops land in <paramref name="banked"/>.</summary>
    internal bool NpcMineCellForTest(int npcId, Vector3i cell, List<ItemAmount> banked, double refillSeconds = NpcMineRefillSeconds)
        => NpcMineCell(_npcs.First(n => n.Id == npcId), cell, (item, count) => banked.Add(new ItemAmount(item, count)), refillSeconds);

    /// <summary>Test seam (#2643): <see cref="FindNpcOutcrops"/>.</summary>
    internal List<Vector3i> FindNpcOutcropsForTest(Vector3f center, int radius, int max) => FindNpcOutcrops(center, radius, max);

    /// <summary>Test seam (#2643): <see cref="RestoreNpcCellsNow"/>.</summary>
    internal void RestoreNpcCellsNowForTest(IEnumerable<(Vector3i Cell, ushort Block)> cells) => RestoreNpcCellsNow(cells);

    /// <summary>Test seam (#2643): the active world's pending refills, oldest first.</summary>
    internal IReadOnlyList<NpcRefillRecord> NpcRefillsForTest
        => _meta.NpcRefills.TryGetValue(_world.LocationId, out var list) ? list.ToList() : new List<NpcRefillRecord>();

    /// <summary>Test seam (#2643): one refill step of the active world now, ignoring the once-a-second gate.</summary>
    internal void RunNpcRestoresForTest()
    {
        _npcRestoreAt.Remove(_world.LocationId);
        TickNpcRestores(NpcRestoreStepSeconds);
    }

    /// <summary>Test seam (#2643): lowers the per-world registry cap.</summary>
    internal int NpcRefillCapForTest
    {
        set => _npcRefillCap = value;
    }
}
