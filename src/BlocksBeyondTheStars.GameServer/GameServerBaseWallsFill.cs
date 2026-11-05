// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Diagnostics;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The walking outside-in fill of a base's wall ring (#1315, #1347, #1862) as a <b>resumable job</b> (#3209): the same flood
/// <c>GameServerBaseWalls</c> describes, in the same order, but its state — the scratch classification, the bitset of reached
/// cells and the frontier — lives on the job, so <see cref="WalledFillJob.Step"/> can stop after a time budget and go on in the
/// next tick. A fill stepped to the end in one call is the old synchronous fill, cell for cell.
/// </summary>
public sealed partial class GameServer
{
    // Scratch cell classes: bits 0–1 = free (unknown / free / solid), bits 2–3 = carries feet (unknown / yes / no),
    // bits 4–5 = the cell is a fluid (unknown / yes / no) — read for the cell UNDER a candidate (#1862).
    private const byte WallFreeMask = 0x03, WallFree = 0x01, WallSolid = 0x02;
    private const byte WallSupportMask = 0x0C, WallSupports = 0x04, WallNoSupport = 0x08;
    private const byte WallFluidMask = 0x30, WallFluid = 0x10, WallNotFluid = 0x20;

    private static readonly (int Dx, int Dz)[] WallFillDirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>Per block id, what <see cref="IsCollidingBlock"/> answers with fluids colliding: bit 0 with foliage colliding,
    /// bit 1 with foliage passing (#3209: a fill classifies up to millions of cells, and the predicate looks every non-air
    /// block up by its key string). Built once — the content does not change while the server runs.</summary>
    private byte[]? _wallCollideTable;

    private byte[] WallCollideTable()
    {
        if (_wallCollideTable is null)
        {
            int max = 0;
            foreach (var def in _content.Blocks.Values)
            {
                max = System.Math.Max(max, def.NumericId.Value);
            }

            var table = new byte[max + 1];
            for (int i = 1; i <= max; i++)
            {
                var id = new BlocksBeyondTheStars.Shared.Primitives.BlockId((ushort)i);
                table[i] = (byte)((IsCollidingBlock(id, fluidsPass: false, foliagePasses: false) ? 1 : 0)
                    | (IsCollidingBlock(id, fluidsPass: false, foliagePasses: true) ? 2 : 0));
            }

            _wallCollideTable = table;
        }

        return _wallCollideTable;
    }

    /// <summary>
    /// One outside-in fill of one base at one feet level (#3209). A cell is <i>free</i> when it is neither a colliding block
    /// nor a fluid (walk-through props pass, as the creature body gate reads them) and not covered by a door that counts as
    /// wall; the fill may stand in a free cell that is <i>supported</i> (a colliding block under it, or ONE cell of fluid over
    /// something that is not fluid — a pond is waded on its surface, never flooded through its volume, while fluid over fluid
    /// is a moat and carries no feet, #1862; grass, props and tree canopies carry no feet either, which also keeps the fill out
    /// of every tree crown) or that lies on the queried level itself (the original horizontal slice — a moat directly under
    /// that level is refused there too). From a cell it steps to the four neighbour columns at the same height, one up (onto
    /// a supported cell — a step, never a levitation) or one down (through a free cell above the landing). Seeds: every
    /// boundary column's free cells on the level, plus its supported free cells within <see cref="WalledFillBand"/>, so the
    /// fill can come down a slope from higher ground. The band also bounds the flood, so a deep cave system under the base
    /// cannot eat the budget. All boundary columns are seeded before the flood starts — the order of the synchronous fill.
    /// </summary>
    private sealed class WalledFillJob
    {
        private readonly GameServer _s;
        private readonly HashSet<Vector3i> _wallDoors;
        private readonly Queue<int> _frontier = new(); // reached-bitset indices, in visit order
        private readonly byte[] _scratch;
        private readonly byte[] _collides;
        private readonly int _circ;
        private int _seedNext;   // the next boundary round (0..Side-1) to seed; Side once seeding is done
        private int _budgetLeft;
        private ChunkData? _chunk;      // the chunk of the last read (null: not loaded) — valid while _chunkCached
        private ChunkCoord _chunkCoord;
        private bool _chunkCached;      // reset at each step

        public WalledFillJob(GameServer server, ServerBase b, int feetY, int reach, byte[] scratch, double now)
        {
            _s = server;
            _collides = server.WallCollideTable();
            _circ = server._world.Circumference;
            Body = server._world.LocationId;
            Center = b.Cell;
            FeetY = feetY;
            Reach = reach;
            Side = 2 * reach + 1;
            YMin = System.Math.Max(feetY - WalledFillBand, b.Cell.Y - reach);
            YMax = System.Math.Min(feetY + WalledFillBand, b.Cell.Y + reach);
            _scratch = scratch;
            System.Array.Clear(scratch, 0, ScratchCells(Side, YMin, YMax));
            Reached = new ulong[(((long)Side * Side * Rows) + 63) / 64];
            Budget = WalledFillBudgetFor(Side);
            _budgetLeft = Budget;
            _wallDoors = server.DoorCellsWhere(d => !d.Open || !DoorBlocks.IsHandOperated(d.Kind)); // #1358: proximity doors are walls in any state
            StartedAt = now;
            LastStepped = now;
        }

        public string Body { get; }
        public Vector3i Center { get; }
        public int FeetY { get; }
        public int Reach { get; }
        public int Side { get; }
        public int YMin { get; }
        public int YMax { get; }

        /// <summary>Band rows the bitset holds.</summary>
        public int Rows => YMax - YMin + 1;

        /// <summary>The reached cells: box-relative ((y − YMin) · Side + dx) · Side + dz.</summary>
        public ulong[] Reached { get; }

        /// <summary>Cells the fill may visit (the /basewalls report).</summary>
        public int Budget { get; }

        public int Count { get; private set; }
        public bool Done { get; private set; }

        /// <summary>The fill ran out of budget — nothing at this level may read as fenced in.</summary>
        public bool BudgetOut { get; private set; }

        /// <summary>Calls to <see cref="Step"/> that did work — more than one means the fill was sliced over ticks.</summary>
        public int Slices { get; private set; }

        public double StartedAt { get; }
        public double LastStepped { get; set; }

        /// <summary>The scratch array (returned to the pool when the job ends).</summary>
        public byte[] Scratch => _scratch;

        /// <summary>Scratch cells a box needs: the band plus the support row under it and the head row above it.</summary>
        public static int ScratchCells(int side, int yMin, int yMax) => side * side * (yMax - yMin + 3);

        /// <summary>The scratch cells a fill of this base at this level will need (asked before one is started).</summary>
        public static int ScratchCellsFor(ServerBase b, int feetY, int reach)
            => ScratchCells(2 * reach + 1, System.Math.Max(feetY - WalledFillBand, b.Cell.Y - reach),
                System.Math.Min(feetY + WalledFillBand, b.Cell.Y + reach));

        /// <summary>
        /// Works on the fill until it is done or <paramref name="budgetTicks"/> (Stopwatch ticks; negative = no limit) have
        /// passed — but always for at least <paramref name="minWork"/> units of work (a cell taken off the frontier, or a
        /// boundary column seeded), so a starved clock still moves it. Returns the work done.
        /// </summary>
        public int Step(long budgetTicks, int minWork)
        {
            long start = budgetTicks >= 0 ? Stopwatch.GetTimestamp() : 0;
            _chunkCached = false; // chunks may have loaded or gone since the last step
            int work = 0;
            while (!Done)
            {
                if (_seedNext < Side)
                {
                    SeedRound(_seedNext++);
                    work += 4;
                }
                else if (_frontier.Count == 0)
                {
                    Done = true;
                }
                else if (_budgetLeft-- <= 0)
                {
                    BudgetOut = true; // too much open ground to walk — nothing at this level may read as fenced in
                    Done = true;
                }
                else
                {
                    Expand(_frontier.Dequeue());
                    work++;
                }

                if (budgetTicks >= 0 && work >= minWork && (work & 63) == 0 && Stopwatch.GetTimestamp() - start >= budgetTicks)
                {
                    break;
                }
            }

            if (work > 0)
            {
                Slices++;
            }

            if (Done)
            {
                _frontier.Clear();
                _frontier.TrimExcess();
            }

            return work;
        }

        // dx/dz are box-relative (0..Side-1); y is absolute and may run one row past the band on each side.
        private Vector3i World(int dx, int y, int dz)
            => WorldConstants.CanonicalBlock(new Vector3i(Center.X - Reach + dx, y, Center.Z - Reach + dz), _circ);

        private int ScratchIndex(int dx, int y, int dz) => ((y - (YMin - 1)) * Side + dx) * Side + dz;

        // ServerWorld.GetBlockIfLoaded for a canonical cell, remembering the last chunk asked: the fill reads its neighbours,
        // which share a chunk most of the time, and the chunk lookup was most of a read's cost. Reset at each step, since
        // chunks load and unload between ticks; an unloaded chunk reads as air.
        private BlocksBeyondTheStars.Shared.Primitives.BlockId Read(Vector3i canonical)
        {
            var coord = WorldConstants.WorldToChunk(canonical);
            if (!_chunkCached || !coord.Equals(_chunkCoord))
            {
                _chunkCoord = coord;
                _chunk = _s._world.LoadedChunkOrNull(coord);
                _chunkCached = true;
            }

            if (_chunk is null)
            {
                return BlocksBeyondTheStars.Shared.Primitives.BlockId.Air;
            }

            var local = WorldConstants.WorldToLocal(canonical);
            return _chunk.Get(local.X, local.Y, local.Z);
        }

        // IsCollidingBlock with fluids colliding, from the table (an id the content does not know asks the predicate).
        private bool Collides(BlocksBeyondTheStars.Shared.Primitives.BlockId id, bool foliagePasses)
        {
            int v = id.Value;
            return v < _collides.Length
                ? (_collides[v] & (foliagePasses ? 2 : 1)) != 0
                : _s.IsCollidingBlock(id, fluidsPass: false, foliagePasses);
        }

        private bool Free(int dx, int y, int dz)
        {
            if (y < YMin - 1 || y > YMax + 1)
            {
                return false; // outside the band (and its one-row margins) — nothing is walked there
            }

            int i = ScratchIndex(dx, y, dz);
            byte v = (byte)(_scratch[i] & WallFreeMask);
            if (v == 0)
            {
                var c = World(dx, y, dz);
                var id = Read(c);
                // #3148: an energy gate's membrane is no solid block, but no animal passes it — a fence ring with its gate
                // is a closed yard (a gate or fence a conduit holds open is a gap, as it is for the animals).
                bool free = !Collides(id, foliagePasses: false)
                    && (_wallDoors.Count == 0 || !_wallDoors.Contains(c))
                    && !_s.IsClosedFenceBlock(c, id.Value);
                v = free ? WallFree : WallSolid;
                _scratch[i] |= v;
            }

            return v == WallFree;
        }

        // Whether the cell (dx, y, dz) is a fluid (any y: a cell below the scratch rows is read from the world directly).
        private bool Fluid(int dx, int y, int dz)
        {
            if (y < YMin - 1 || y > YMax + 1)
            {
                return _s.IsFluid(Read(World(dx, y, dz)).Value);
            }

            int i = ScratchIndex(dx, y, dz);
            byte v = (byte)(_scratch[i] & WallFluidMask);
            if (v == 0)
            {
                v = _s.IsFluid(Read(World(dx, y, dz)).Value) ? WallFluid : WallNotFluid;
                _scratch[i] |= v;
            }

            return v == WallFluid;
        }

        // Whether the cell UNDER (dx, y, dz) carries feet: a colliding block, or one cell of fluid over something that
        // is not fluid (#1862: a pond is waded, a moat two or more deep is not) — not air, grass, a prop or a canopy.
        private bool Supported(int dx, int y, int dz)
        {
            int by = y - 1;
            if (by < YMin - 1)
            {
                return false;
            }

            int i = ScratchIndex(dx, by, dz);
            byte v = (byte)(_scratch[i] & WallSupportMask);
            if (v == 0)
            {
                bool supports = Collides(Read(World(dx, by, dz)), foliagePasses: true)
                    && !(Fluid(dx, by, dz) && Fluid(dx, by - 1, dz));
                v = supports ? WallSupports : WallNoSupport;
                _scratch[i] |= v;
            }

            return v == WallSupports;
        }

        // Where the fill may stand: a free cell in the band that has something under it, or that is on the queried
        // level and not over a moat (the level slice passes over air and a one-deep pond, never fluid over fluid).
        private bool Standable(int dx, int y, int dz)
            => y >= YMin && y <= YMax && Free(dx, y, dz)
               && (Supported(dx, y, dz) || (y == FeetY && !Fluid(dx, y - 1, dz)));

        private void Visit(int dx, int y, int dz)
        {
            int i = ((y - YMin) * Side + dx) * Side + dz;
            ulong bit = 1UL << (i & 63);
            if ((Reached[i >> 6] & bit) == 0)
            {
                Reached[i >> 6] |= bit;
                Count++;
                _frontier.Enqueue(i);
            }
        }

        private void Seed(int dx, int dz)
        {
            for (int y = YMin; y <= YMax; y++)
            {
                if (Standable(dx, y, dz))
                {
                    Visit(dx, y, dz);
                }
            }
        }

        // One round of the boundary: the d-th column of each of the four box edges, in the synchronous fill's order.
        private void SeedRound(int d)
        {
            Seed(d, 0);
            Seed(d, Side - 1);
            Seed(0, d);
            Seed(Side - 1, d);
        }

        private void Expand(int index)
        {
            int dz = index % Side;
            int rest = index / Side;
            int dx = rest % Side;
            int y = (rest / Side) + YMin;
            foreach (var (ddx, ddz) in WallFillDirs)
            {
                int nx = dx + ddx, nz = dz + ddz;
                if (nx < 0 || nx >= Side || nz < 0 || nz >= Side)
                {
                    continue;
                }

                if (Standable(nx, y, nz))
                {
                    Visit(nx, y, nz); // a level step
                }

                if (Standable(nx, y + 1, nz))
                {
                    Visit(nx, y + 1, nz); // one step up onto something (a supported cell, or the level slice)
                }

                if (Free(nx, y, nz) && Standable(nx, y - 1, nz))
                {
                    Visit(nx, y - 1, nz); // one step down, through the free cell above the landing
                }
            }
        }
    }
}
