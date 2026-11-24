// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The terrain probes a creature step reads: whether a step is blocked by the ground, water and lava columns at a
/// creature's feet, the rest surface and ground feet under a column, and whether a body fits.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Feeds the world's column data (real ground heights + water depths) into the pure
    /// <see cref="CreatureBehaviour.TerrainStepBlocked"/> gate (#648, per motion class since #1331). Only
    /// consulted when the step actually crosses a column boundary, and only for the classes the gate cares
    /// about — so the extra world queries stay off the common same-column tick. Ground heights come from
    /// REAL blocks (#650), so player-built walls read as impassable steps and dug ramps as walkable ones.
    /// <paramref name="knownNextFeet"/> is the target column's probe when the caller already ran it (#1367).</summary>
    private bool StepBlockedByTerrain(CreatureSpecies sp, MotionClass motion, Vector3f cur, Vector3f next, int? knownNextFeet = null)
    {
        // #1862: a LAND hoverer (a gas-sac grazer floating 0.8 above its feet) is held to the walker's rules —
        // it drifted over two-block walls and across moats into a fortress. Air hoverers and fliers keep their freedom.
        bool groundRules = CreatureMotion.ObeysGroundRules(sp, motion);
        if (!groundRules && motion != MotionClass.Swimmer)
        {
            return false; // fliers and air hoverers keep their freedom
        }

        int cx = (int)System.Math.Floor(cur.X), cz = (int)System.Math.Floor(cur.Z);
        int nx = (int)System.Math.Floor(next.X), nz = (int)System.Math.Floor(next.Z);
        if (cx == nx && cz == nz)
        {
            return false; // same column — nothing to gate
        }

        int refY = (int)System.Math.Floor(cur.Y);
        int curFeet = GroundFeetFor(sp, cx, cz, refY);
        int nextFeet = knownNextFeet ?? GroundFeetFor(sp, nx, nz, refY);

        // A large body treats a filled column as a wall (#750): ground-height deltas alone made ruin
        // walls invisible to fauna (NPCs have PathBlockedByWorld; creatures had nothing), so titans
        // pathed straight through masonry and bit the player from inside rooms. Titan-scale only, so
        // the extra block reads stay off the common path. #3881: water is no masonry — the depth gate below decides on it;
        // a big animal in a deep pond read every neighbouring water column as a wall and was trapped.
        if (sp.Size >= LargeBodySize && groundRules
            && !LargeBodyColumnOpen(sp, nx, nextFeet, nz, waterPasses: true))
        {
            return true;
        }

        // #1367: only water was gated — a walker stepped down (≤ 3 blocks) onto the crust of a lava column
        // like onto any floor. The melt is a wall for everything that does not live in it.
        if (groundRules && sp.Habitat != CreatureHabitat.Lava && LavaUnderFeet(nx, nextFeet, nz))
        {
            return true;
        }

        // The creature's OWN column is read at its own feet (#1367), not at the probe's answer: in a water
        // column under an overhang the probe finds the ledge above the surface, which reads as dry ground
        // (depth 0) and let a swimmer steer out of its lake instead of along the shore.
        int curDepth = WaterDepthAtFeet(cx, cz, refY);
        int nextDepth = WaterDepthAtFeet(nx, nz, nextFeet);
        if (WaterDepthBand.StepTooShallow(sp, curDepth, nextDepth))
        {
            return true; // #3975: a whale keeps to the deep sea — never into shallower water than its band while it gets shallower
        }

        var gateClass = CreatureMotion.IsLandHoverer(sp, motion) ? MotionClass.Walker : motion; // #1862: a floating grazer is gated as a walker
        return CreatureBehaviour.TerrainStepBlocked(gateClass, CreatureMotion.IsGiant(sp), CreatureMotion.IsAmphibious(sp),
            curFeet, nextFeet, curDepth, nextDepth);
    }

    /// <summary>Whether feet at <paramref name="feetY"/> in a column would rest on lava (real block, no-load read).</summary>
    private bool LavaUnderFeet(int x, int feetY, int z)
        => _creatureLavaId != 0 && _world.GetBlockIfLoaded(new Vector3i(x, feetY - 1, z)).Value == _creatureLavaId;

    /// <summary>Test seam (#1711): the Y an airborne creature at <paramref name="refY"/> measures its band
    /// from in this column.</summary>
    public int RestSurfaceYForTest(int x, int z, int refY) => RestSurfaceYAt(x, z, refY);

    /// <summary>Test seam (#1367): the terrain gate's verdict for a creature stepping to <paramref name="next"/>
    /// from where it stands, in its current motion class.</summary>
    public bool TerrainStepBlockedForTest(string creatureId, Vector3f next)
    {
        var c = _creatures.First(x => x.Id == creatureId);
        var sp = _speciesById[c.SpeciesId];
        return StepBlockedByTerrain(sp, EffectiveMotion(c, sp), c.Position, next);
    }

    /// <summary>Test seam (#2553): the water standing on feet at <paramref name="feetY"/> as the wading gate reads it.</summary>
    public int WaterDepthAtFeetForTest(int x, int z, int feetY) => WaterDepthAtFeet(x, z, feetY);

    /// <summary>Test seam (#2552): the Y the spawner places an air animal of a species at over a column whose
    /// natural surface is <paramref name="surface"/> (the generator's answer in the spawner; a test passes the
    /// floor of the pond it built).</summary>
    public float AirSpawnYForTest(string speciesId, int x, int z, int surface) => AirSpawnY(_speciesById[speciesId], x, z, surface);

    /// <summary>Test seam: the generator's water body in a column of the active world (surface top, bed), or null.</summary>
    public (int Top, int Bed)? WaterSurfaceForTest(int x, int z)
        => _generator.TryGetWaterSurface(_world.Planet, x, z, out int top, out int bed) ? (top, bed) : null;

    /// <summary>Test seam (#3476): the one-off habitat height a species is set down at for <paramref name="p"/> — what a
    /// companion's spot and the taming scare bolt (<c>ShoveCreatureFrom</c>) place an animal at.</summary>
    public Vector3f HabitatHeightForTest(string speciesId, Vector3f p) => AdjustHabitatHeight(_speciesById[speciesId], p);

    /// <summary>How far up or down a column a fluid body is followed. Deeper than any hand-dug moat and past
    /// the wide ground scan, so a real body is measured whole rather than clipped.</summary>
    private const int FluidColumnScan = 32;

    /// <summary>The water standing on a creature's feet, read from REAL blocks (#1697): how many water cells
    /// are stacked from the feet cell upward. 0 = dry ground, or a floor built ABOVE a pond (a bridge, a
    /// floating platform, a filled-in shore) — that is dry ground, not a swim.
    ///
    /// <para>This used to ask the GENERATOR (<c>TryGetWaterSurface</c>), which knows only the water the world
    /// was born with. A moat the player digs and floods by hand answered depth 0, so the walker gate
    /// (<c>nextWaterDepth &gt; 1</c>) never fired and land animals strolled across a flooded trench — while
    /// the lava gate right beside it, reading real blocks since #1367, held them back. That asymmetry was the
    /// bug: ground heights moved onto real blocks in #650, water depth never followed.</para>
    ///
    /// <para>The generator remains the answer for a column whose chunk is not streamed in: there are no real
    /// blocks to read there, and the old behaviour is the safe one.</para>
    ///
    /// <para>#2553: a plant standing in the water counts as water (<see cref="SubmergedDepthAt"/>) — a kelp or
    /// seagrass stalk read as "depth 0" and land animals walked the pond floor between the stalks.</para></summary>
    private int WaterDepthAtFeet(int x, int z, int feetY)
    {
        if (_creatureWaterId != 0 && _world.IsChunkLoaded(WorldConstants.WorldToChunk(new Vector3i(x, feetY, z))))
        {
            return SubmergedDepthAt(x, z, feetY);
        }

        if (!_generator.TryGetWaterSurface(_world.Planet, x, z, out int top, out int bed))
        {
            return 0;
        }

        return top >= feetY - 1 ? top - bed : 0;
    }

    /// <summary>How many plant cells a stalk may stack before the water above it is looked for: a kelp forest grows
    /// twelve (#1757), and nothing else that grows in water is taller.</summary>
    private const int SubmergedPlantScan = 16;

    /// <summary>#2552/#2553: water, or a plant standing in it. A cell holds ONE block id, so a kelp or seagrass stalk,
    /// a coral clump or a reed deletes the water in its cell, and every probe that asked only "is this water?" read a
    /// planted pond floor as dry: land animals walked the bed between the stalks ("standable, depth 0") and a flying
    /// animal over a kelp bed measured its band from the sand under the water. A plant counts as water when water
    /// stands above it — directly or above the rest of its stalk; a plant with air above it (a tuft on the bank, a
    /// lily pad on the surface, a coral in water one cell deep) is just a plant. Real blocks, no-load reads (an unloaded
    /// cell is air, so not water). The one predicate every wading body reads: the creatures' depth gate and floor
    /// probes, and the walking robots.</summary>
    private bool IsWaterOrSubmergedPlant(int x, int y, int z)
    {
        if (_creatureWaterId == 0)
        {
            return false;
        }

        for (int dy = 0; dy <= SubmergedPlantScan; dy++)
        {
            ushort id = _world.GetBlockIfLoaded(new Vector3i(x, y + dy, z)).Value;
            if (id == _creatureWaterId)
            {
                return true;
            }

            if (!IsFlora(id))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>The water standing on feet at <paramref name="feetY"/>, from REAL blocks (#1697) with the plants in it
    /// counted as water (#2553): the cells from the feet up to the topmost water cell of the run of water and plants
    /// that starts there. 0 when the feet cell is neither (dry ground, a floor built over a pond) or when the plants
    /// at the feet have only air above them. No-load reads; <see cref="WaterDepthAtFeet"/> adds the generator's
    /// answer for an unloaded column.</summary>
    private int SubmergedDepthAt(int x, int z, int feetY)
    {
        int depth = 0;
        for (int d = 0; d < FluidColumnScan; d++)
        {
            ushort id = _world.GetBlockIfLoaded(new Vector3i(x, feetY + d, z)).Value;
            if (id == _creatureWaterId && id != 0)
            {
                depth = d + 1; // water up to here
            }
            else if (!IsFlora(id))
            {
                break; // a plant continues the run (it counts once water is found above it); anything else ends it
            }
        }

        return depth;
    }

    /// <summary>A fluid cell (water or lava), or a plant standing in water (<see cref="IsWaterOrSubmergedPlant"/>).</summary>
    private bool IsFluidOrSubmergedPlant(int x, int y, int z)
        => IsFluid(_world.GetBlockIfLoaded(new Vector3i(x, y, z)).Value) || IsWaterOrSubmergedPlant(x, y, z);

    /// <summary>The fluid body of one kind (water or lava) in a column, for the SPAWN probes (#1718): real
    /// blocks first, the generator only where nothing is streamed in — the #1697 rule applied to placement.
    /// A loaded column is scanned around its generator surface for the topmost cell of that fluid; the bed
    /// is the first non-fluid cell under it. A body the player drained reads dry, a pool the player built
    /// reads wet. Where the column's chunk is not loaded (or the fluid lies outside the scanned band) the
    /// generator answers as it always did.</summary>
    private bool TryGetFluidColumn(int x, int z, ushort fluidId, out int topY, out int bedY)
    {
        topY = 0;
        bedY = 0;
        if (fluidId == 0)
        {
            return false;
        }

        int surface = _generator.SurfaceHeight(_world.Planet, x, z);
        if (_world.IsChunkLoaded(WorldConstants.WorldToChunk(new Vector3i(x, surface, z))))
        {
            for (int y = surface + FluidColumnScan; y >= surface - FluidColumnScan; y--)
            {
                if (_world.GetBlockIfLoaded(new Vector3i(x, y, z)).Value != fluidId)
                {
                    continue;
                }

                topY = y;
                bedY = y - 1;
                while (y - bedY < FluidColumnScan && _world.GetBlockIfLoaded(new Vector3i(x, bedY, z)).Value == fluidId)
                {
                    bedY--;
                }

                return true;
            }
        }

        bool generated = fluidId == _creatureWaterId
            ? _generator.TryGetWaterSurface(_world.Planet, x, z, out topY, out bedY)
            : _generator.TryGetLavaSurface(_world.Planet, x, z, out topY, out bedY);
        if (!generated)
        {
            return false;
        }

        // The generator's body, where its top cell is streamed in and is NOT that fluid any more, was drained
        // or built over — offering it would only be rejected by HabitatSuitable one step later.
        var top = new Vector3i(x, topY, z);
        return !_world.IsChunkLoaded(WorldConstants.WorldToChunk(top)) || _world.GetBlockIfLoaded(top).Value == fluidId;
    }

    /// <summary>The top cell of the fluid body filling this column at <paramref name="fromY"/>, or
    /// <see cref="int.MinValue"/> when that cell holds no fluid. A plant standing in the water is part of the body
    /// (#2552): from a kelp stalk the body is followed up through the stalk to the water's top. Real blocks, no-load
    /// reads.</summary>
    private int FluidTopAt(int x, int z, int fromY)
    {
        if (!IsFluidOrSubmergedPlant(x, fromY, z))
        {
            return int.MinValue;
        }

        int top = fromY;
        while (top - fromY < FluidColumnScan && IsFluidOrSubmergedPlant(x, top + 1, z))
        {
            top++;
        }

        return top;
    }

    /// <summary>The feet cell on the BED of a fluid body — the submerged floor a body sinking through the
    /// column would come to rest on (#1697) — or <see cref="int.MinValue"/> when the column holds no fluid
    /// body standing on real ground within <paramref name="maxScan"/>.
    ///
    /// <para>Needed because <see cref="StandableAt"/> can never accept a submerged cell: a fluid counts as
    /// colliding for a body, so every cell inside the water fails the headroom test, the probe gave up, and
    /// the caller then answered with the generator's PRE-EXCAVATION surface — which for a moat dug into the
    /// terrain and filled flush is exactly the waterline. That is what let animals walk on water. The bed is
    /// the honest answer; whether the animal may go in at all is the water-depth gate's decision, not the
    /// probe's.</para>
    ///
    /// <para>#2552: the plants on a pond floor are part of the water. A kelp or seagrass stalk under the surface
    /// read as an air pocket, the probe gave up and the caller fell back to the noise surface — three cells under
    /// the water, which is where a flying animal over a kelp bed then measured its band from.</para></summary>
    private int SubmergedFeetYAt(int x, int z, int refY, int maxScan)
    {
        int start = refY;
        if (!IsFluidOrSubmergedPlant(x, start, z))
        {
            // The reference sits above the surface (a walker on the bank looking into the moat): find the
            // body below it, but never past real ground — a pond under a floor is not this column's answer.
            int found = int.MinValue;
            for (int r = 1; r <= maxScan; r++)
            {
                if (IsSupportCell(x, start - r, z))
                {
                    break;
                }

                if (IsFluidOrSubmergedPlant(x, start - r, z))
                {
                    found = start - r;
                    break;
                }
            }

            if (found == int.MinValue)
            {
                return int.MinValue;
            }

            start = found;
        }

        for (int y = start; y > start - maxScan; y--)
        {
            if (IsSupportCell(x, y - 1, z))
            {
                return y; // feet on the bed
            }

            if (!IsFluidOrSubmergedPlant(x, y - 1, z))
            {
                return int.MinValue; // an air pocket under the water — not a bed this probe can vouch for
            }
        }

        return int.MinValue;
    }

    /// <summary>The Y an AIRBORNE creature measures its altitude band from (#1697): the surface of the fluid
    /// filling the column when there is one, otherwise the ground feet cell. "The ground" under a pond is its
    /// bed, and an animal that measures its band from the bed floats INSIDE the water — a player found one of
    /// her flying creatures asleep below the surface of her moat. What a flier would come down onto is the
    /// water, so that is what the band is measured from. #2945: a floating LAND grazer passes
    /// <paramref name="canopyCarries"/> false — it rides over the ground, never over a crown.</summary>
    private int RestSurfaceYAt(int x, int z, int refY, bool canopyCarries = true)
    {
        if (TryGroundFeetYAt(x, z, refY, CreatureBodyMinHeight, CreatureGroundScan, out int feet, canopyCarries))
        {
            // Dry standable ground — a probe hit never lands inside a fluid. But a pond floor planted with kelp or
            // seagrass passes the probe (plants are walk-through), and that "ground" is under the water (#2552):
            // the band rides on the water's surface there too, not three cells down on the sand.
            int planted = FluidRestY(x, z, feet, openOnly: false);
            return planted != int.MinValue ? planted : feet;
        }

        // #2955: the water's REAL surface, or the ice frozen onto it. A sea deeper than the wide scan has no bed in
        // reach — its water (the reference's own cell, or the first one under it) is followed up all the same; the
        // noise surface below answered the seabed, and over a frozen ocean that flung a jellyfish under the ice.
        int bed = SubmergedFeetYAt(x, z, refY, CreatureWideGroundScan);
        int rest = bed != int.MinValue
            ? FluidRestY(x, z, bed, openOnly: false)
            : FluidRestY(x, z, FluidCellAtOrBelow(x, z, refY, CreatureWideGroundScan), openOnly: true);
        if (rest != int.MinValue)
        {
            return rest;
        }

        // #2552: the reference cell is IN THE FLOOR of a pond — the sand under the water, the rock under that. Water
        // is never standable, so the through-rock search below went on down to a cave under the pond (a flying
        // animal with an eleven-cell band then hovered inside the pond floor), and with no cave there the roof
        // guard held it at its depth. The pond is what the animal would come down onto: when the column above the
        // reference rises through solid ground into water that is open to the sky, the band rides on its surface.
        // A cave, an air pocket or a sealed flooded cavern above the reference leaves the rules below in charge.
        if (IsSupportCell(x, refY, z) && TryOpenWaterTopAbove(x, z, refY, out int pondRest, out _))
        {
            return pondRest;
        }

        // #1854: the reference cell is INSIDE a block — a gas-sac grazer that bobbed into a player's concrete
        // floor, a cave hoverer that eased into the rock. The narrow probe breaks at the first solid cell going
        // down and finds only masonry or hillside in the six above, and the answer used to be refY, the
        // creature's own sunk cell: refY + 0.8 is where the next tick eased it to, a stable fixed point 0.2
        // under the block top (the reported 6.6023 on a floor with its top at 7, −11.1965 in the rock). Look
        // THROUGH the rock for the nearest real floor in either direction, within the wide window a ground
        // mover's own probe uses (#1320), so the animal comes back onto its floor or down into its cave.
        if (IsSupportCell(x, refY, z)
            && TryNearestStandableThroughRock(x, z, refY, CreatureBodyMinHeight, CreatureWideGroundScan, out int through, canopyCarries))
        {
            return through;
        }

        // #1711: the last resort is the generator's noise surface — the height this column has where nothing
        // has been dug. For a creature under a ROOF that Y is on the far side of solid rock, and handing it
        // back parks the animal inside the ceiling: a player photographed one asleep halfway into the stone
        // above her cave, with water above that. Its sibling GroundFeetFor already refuses to answer the noise
        // surface for a cave dweller; this probe never got the same guard. Keyed on the rock rather than on
        // Habitat, because Habitat is a single value — the creature stuck in her ceiling was a hoverer, not a
        // cave species, and any animal below a ceiling has the same problem.
        int surface = _generator.SurfaceHeight(_world.Planet, x, z) + 1;
        return RoofBetween(x, z, refY, surface) ? refY : surface;
    }

    /// <summary>The nearest standable feet cell to <paramref name="refY"/> in either direction, looking THROUGH
    /// solid cells (#1854) — for a body that is already embedded, where <see cref="TryGroundFeetYAt(int, int, int, int, int, out int, bool)"/>
    /// stops at the first block under it. Distance decides, and at equal distance up wins (a creature 0.2 below
    /// a floor belongs on that floor, not in the cellar under it). Both directions reach <paramref name="maxScan"/>
    /// cells; a feet cell needs <paramref name="headroom"/> air cells, and a crown carries it only when
    /// <paramref name="canopyCarries"/> (#2945). No-load reads: an unloaded column has no support anywhere and answers
    /// false, so the caller keeps its noise-surface fallback for those.</summary>
    private bool TryNearestStandableThroughRock(int x, int z, int refY, int headroom, int maxScan, out int feetY,
        bool canopyCarries = true)
    {
        for (int r = 1; r <= maxScan; r++)
        {
            if (StandableAt(x, refY + r, z, headroom, canopyCarries))
            {
                feetY = refY + r;
                return true;
            }

            if (StandableAt(x, refY - r, z, headroom, canopyCarries))
            {
                feetY = refY - r;
                return true;
            }
        }

        feetY = refY;
        return false;
    }

    /// <summary>True when the column between a creature and a candidate rest Y above it is not open (#1711):
    /// solid ground in the way, or the candidate further off than the probe is willing to vouch for. Either
    /// way the noise surface is not a place this animal could come to rest, and the caller holds its depth
    /// instead. Cheap no-load reads, bounded by <see cref="FluidColumnScan"/>.</summary>
    private bool RoofBetween(int x, int z, int fromY, int toY)
    {
        if (toY <= fromY)
        {
            return false; // the candidate is at or below us — nothing to pass through
        }

        int scanTo = System.Math.Min(toY, fromY + FluidColumnScan);
        for (int y = fromY + 1; y <= scanTo; y++)
        {
            if (IsSupportCell(x, y, z))
            {
                return true;
            }
        }

        return toY > scanTo; // surface further up than we looked — do not teleport blind
    }

    /// <summary>#2552: the top of the water standing OPEN TO THE SKY above a cell in a pond's floor or in its water.
    /// From <paramref name="fromY"/> the scan rises through solid ground (at most <see cref="CreatureWideGroundScan"/>
    /// cells, the through-rock window; <paramref name="groundAbove"/> says how many it crossed), must then meet water
    /// (a plant standing in it counts, <see cref="IsWaterOrSubmergedPlant"/>) and follows that water to its top cell.
    /// Open to the sky means nothing a body collides with sits right on the top (a lily pad, a reed or a crown does
    /// not count) and no solid ground lies between it and the column's natural surface (<see cref="RoofBetween"/>,
    /// the #1711 guard) — so a flooded cave, a water pocket in the rock or a covered cistern never pulls a creature up
    /// through its ceiling. False when the first open cell above is not water (a cave, the open air). #2955:
    /// <paramref name="restY"/> is the cell a body rests on there — the one above the water's real top, or above the
    /// sheet of ice frozen onto it (<see cref="FluidRestY"/>). Real blocks, no-load reads.</summary>
    private bool TryOpenWaterTopAbove(int x, int z, int fromY, out int restY, out int groundAbove)
    {
        restY = int.MinValue;
        groundAbove = 0;
        while (groundAbove <= CreatureWideGroundScan && IsSupportCell(x, fromY + groundAbove, z))
        {
            groundAbove++;
        }

        int y = fromY + groundAbove;
        if (groundAbove > CreatureWideGroundScan || !IsWaterOrSubmergedPlant(x, y, z))
        {
            return false;
        }

        // #2955: the water's real top — a deep sea is deeper than the motion scan, which stopped mid-water and called
        // that the surface — and a sheet of ice frozen onto it is what the animal comes down onto.
        int rest = FluidRestY(x, z, y, openOnly: true);
        if (rest == int.MinValue
            || RoofBetween(x, z, rest, _generator.SurfaceHeight(_world.Planet, x, z) + 1))
        {
            return false;
        }

        restY = rest;
        return true;
    }

    /// <summary>The Y an AIR creature's band is measured from, for a body at <paramref name="bodyY"/> riding
    /// <paramref name="hover"/> over it: <see cref="RestSurfaceYAt"/> at the reference cell under the band — unless
    /// the body itself is under the surface of open water (#2552): in the water or among the plants on the floor, or
    /// in the pond's floor with the water nearer above it than any open cell below. The reference cell of such a body
    /// can lie in a cave under the pond, where the probe holds its depth (#1711), and the creature then sat in the
    /// pond's floor for good (reported at 62.3, the water's top at 65, a cave at 41–52 below). An air creature never
    /// belongs under water, so it rides up to its band over the surface. A body in a cave's ceiling, with the cave
    /// right under it, is left to the cave's rules.
    ///
    /// <para>#2583: nor in a gas giant's gas sea — whatever the probes answer, every path ends in
    /// <see cref="LiftOutOfGasSea"/> (they look through the gas to the heightfield buried under it).</para></summary>
    private int AirRestSurfaceYAt(int x, int z, float bodyY, float hover)
    {
        int body = (int)System.Math.Floor(bodyY);
        if (TryOpenWaterTopAbove(x, z, body, out int waterRest, out int groundAbove)
            && (groundAbove == 0 || SolidForCellsBelow(x, z, body, groundAbove)))
        {
            return LiftOutOfGasSea(x, z, bodyY, waterRest);
        }

        return LiftOutOfGasSea(x, z, bodyY, RestSurfaceYAt(x, z, (int)System.Math.Floor(bodyY - hover)));
    }

    /// <summary>True when the <paramref name="cells"/> cells under <paramref name="y"/> are all solid ground — the
    /// nearest open cell below lies further down than that.</summary>
    private bool SolidForCellsBelow(int x, int z, int y, int cells)
    {
        for (int d = 1; d <= cells; d++)
        {
            if (!IsSupportCell(x, y - d, z))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Where an AIR animal is placed when it spawns (#2552): at its own band over what it would come down
    /// onto — the surface of a pond, not the bed under it — exactly as <see cref="AdjustHabitatHeight"/> places it.
    /// It used to be the generator surface + 4, which over a pond is the pond floor + 4: in the water or in the sand,
    /// where the band probe then locked it in. #2583: on a gas giant the generator surface is the heightfield buried under
    /// the gas, so <paramref name="surface"/> is not asked there — the band rides over the real top of the column above
    /// the gas, or over the gas sea itself (<see cref="GasColumnAirRestY"/>).</summary>
    private float AirSpawnY(CreatureSpecies sp, int x, int z, int surface)
    {
        int gasTop = GasSeaTopY();
        int rest = gasTop != int.MinValue
            ? GasColumnAirRestY(x, z, gasTop)
            : RestSurfaceYAt(x, z, AirSpawnReferenceY(x, z, surface)); // #2955: over a sea, its water or ice
        return rest + HoverOf(sp);
    }

    /// <summary>The feet cell a ground mover of this species stands on in a column, nearest to
    /// <paramref name="refY"/>: real blocks first (#650), then the bed of a fluid body (#1697); a cave dweller
    /// with no standable cell near its depth holds that depth (it never pops up to the noise surface);
    /// a streamed-in column that is open all the way down the window is a drop to fall through; everyone
    /// else falls back to the generator surface for unloaded columns.</summary>
    private int GroundFeetFor(CreatureSpecies sp, int x, int z, int refY)
    {
        // Species-aware headroom + the wide real-ground scan (#1320), so a titan never "stands" in a two-cell
        // hollow and a fresh pit is found before the noise surface is trusted. #2945: a crown carries fliers only.
        bool canopy = CanopyCarries(sp);
        if (TryGroundFeetYAt(x, z, refY, CreatureHeadroom(sp), CreatureWideGroundScan, out int feet, canopy))
        {
            return feet;
        }

        // #1697: a flooded column has no DRY standable cell — every cell inside the water fails the headroom
        // test — but its bed is real ground. Answering the noise surface here handed back the height the
        // column had BEFORE it was dug out, i.e. the waterline of a hand-filled moat, and the animal walked
        // across it. The bed keeps the step deltas honest; the water-depth gate does the deciding.
        int bed = SubmergedFeetYAt(x, z, refY, CreatureWideGroundScan);
        if (bed != int.MinValue)
        {
            return bed;
        }

        // #2846: a player dug a moat deeper than the wide window right through the island an animal slept on: neither probe
        // reached the bottom, the noise surface below answered the height the island HAD, and the animal stood on
        // nothing for the rest of the night — an awake one also read the pit as level ground and walked out over it.
        // A column that is streamed in and holds no support anywhere in the window is a real drop: answer a cell at its
        // far end, so the body falls (and probes on from lower down next tick) and a step toward it reads as a cliff edge.
        int noise = _generator.SurfaceHeight(_world.Planet, x, z) + 1;
        if (OpenDropBelow(x, z, refY, CreatureWideGroundScan, canopy))
        {
            // Never deeper than a pit a player could have dug under the noise surface: below that (the void under an
            // island in a gas sea) the body holds where it is instead of sinking for ever.
            return System.Math.Max(refY - CreatureWideGroundScan, System.Math.Min(refY, noise - OpenDropMaxDepth));
        }

        return sp.Habitat == CreatureHabitat.Cave ? refY : noise;
    }

    /// <summary>How far under a column's noise surface <see cref="GroundFeetFor"/> lets a body fall through an open drop.</summary>
    private const int OpenDropMaxDepth = 192;

    /// <summary>Whether the <paramref name="maxScan"/> cells under <paramref name="refY"/> are a real, open drop: every one
    /// streamed in and none of them able to carry feet (air or fluid — no block, no canopy). An unloaded cell reads as air
    /// under the no-load reads, so it ends the claim: what is not streamed in keeps the noise-surface fallback. #2945: for a
    /// body a crown does not carry (<paramref name="canopyCarries"/> false) the leaves are part of the drop.</summary>
    private bool OpenDropBelow(int x, int z, int refY, int maxScan, bool canopyCarries = true)
    {
        for (int y = refY - 1; y >= refY - maxScan; y--)
        {
            if (!_world.IsChunkLoaded(WorldConstants.WorldToChunk(new Vector3i(x, y, z))) || IsSupportCell(x, y, z, canopyCarries))
            {
                return false;
            }
        }

        return true;
    }

    private const float CreatureSweepStep = 0.25f; // swept-step sampling distance (same as the NPC path check)
    private const int CreatureBodyMinHeight = 2;   // even a mouse-sized animal occupies feet + head
    private const int CreatureBodyMaxHeight = 8;   // ...and a titan is gated by its shoulders, not its full crown

    /// <summary>How many cells tall a creature's body is for collision purposes (its render height is
    /// <c>Size × 1.8</c>), clamped so tiny fauna still gets a head cell and a titan can still duck under an
    /// overhang instead of being walled in by its own crown.</summary>
    private static int CreatureBodyHeight(CreatureSpecies sp) => sp.BodyPlan switch
    {
        CreatureBodyPlan.Arachnid => ArachnidRules.BodyHeightCells(sp.Size, CreatureBodyMinHeight, CreatureBodyMaxHeight), // #2009: low and wide
        CreatureBodyPlan.Biped or CreatureBodyPlan.Robot // #3295: the robot stands on the biped's proportions
            => BipedRules.BodyHeightCells(sp.Size, sp.HeadRatio, CreatureBodyMinHeight, CreatureBodyMaxHeight), // #2081: its real height
        CreatureBodyPlan.Sounder => Shared.Sounders.SounderRules.BodyHeightCells(sp.Size, CreatureBodyMinHeight, CreatureBodyMaxHeight), // #3355
        CreatureBodyPlan.Whale => WhaleRules.BodyHeightCells(sp.Size, CreatureBodyMinHeight, CreatureBodyMaxHeight), // #3975: its girth
        _ => System.Math.Clamp((int)System.Math.Ceiling(sp.Size * 1.8f), CreatureBodyMinHeight, CreatureBodyMaxHeight),
    };

    /// <summary>Whether a creature's BODY would sit inside colliding blocks at a spot (#855). Creatures have no
    /// colliders and the server tracks a single point, so before this gate a wall was only ever seen indirectly —
    /// as a ground-height delta (<see cref="StepBlockedByTerrain"/>), which reads a 1–2 block player wall as a
    /// walkable step and lets the rendered body clip into it. Checks the creature's own column from the feet cell
    /// up through its body height; flying species pass through tree canopies (they hover inside them).</summary>
    private bool CreatureBodyBlocked(CreatureSpecies sp, Vector3f pos)
        => CreatureBodyBlocked(sp, pos, foliagePasses: sp.Habitat == CreatureHabitat.Air);

    /// <summary>Body check with an explicit canopy rule (#1332): a flier in the air weaves through tree crowns,
    /// but a flier coming down to PERCH treats the canopy as solid — it sits on a crown, never inside one.</summary>
    private bool CreatureBodyBlocked(CreatureSpecies sp, Vector3f pos, bool foliagePasses)
    {
        int x = (int)System.Math.Floor(pos.X);
        int y = (int)System.Math.Floor(pos.Y);
        int z = (int)System.Math.Floor(pos.Z);
        int height = CreatureBodyHeight(sp);
        for (int dy = 0; dy < height; dy++)
        {
            if (IsCollidingCellIfLoaded(x, y + dy, z, foliagePasses))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The swept version of <see cref="CreatureBodyBlocked"/> (#855): samples the whole step every
    /// <see cref="CreatureSweepStep"/> of a block, so a fast animal can't tunnel through a one-block-thin wall
    /// between two ticks. Mirrors <c>PathBlockedByWorld</c>, which fixed exactly this for NPCs.</summary>
    private bool CreaturePathBlocked(CreatureSpecies sp, Vector3f from, Vector3f to)
        => CreaturePathBlocked(sp, from, to, foliagePasses: sp.Habitat == CreatureHabitat.Air);

    private bool CreaturePathBlocked(CreatureSpecies sp, Vector3f from, Vector3f to, bool foliagePasses)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        int steps = System.Math.Max(1, (int)System.Math.Ceiling(dist / CreatureSweepStep));
        for (int s = 1; s <= steps; s++)
        {
            float f = s / (float)steps;
            if (CreatureBodyBlocked(sp, new Vector3f(from.X + dx * f, from.Y + dy * f, from.Z + dz * f), foliagePasses))
            {
                return true;
            }
        }

        return false;
    }

    private const float LargeBodySize = 3f; // species at/above this Size get the body-volume checks (#750)

    /// <summary>Whether a large creature's body volume fits at the spot (#750): every column in the
    /// footprint radius must be open from just above the feet through the body height. The scan starts
    /// two cells up so the ±1 ground tolerance <see cref="TitanGroundClear"/> allows isn't misread as a
    /// wall. Unloaded chunks read as air (permissive, matching <see cref="StandableAt"/>).</summary>
    private bool LargeBodyFits(CreatureSpecies sp, int x, int feetY, int z)
    {
        int radius = (int)System.Math.Ceiling(sp.Size * 0.5f);
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (!LargeBodyColumnOpen(sp, x + dx, feetY, z + dz))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Single-column variant of the body-volume check for the per-step gate (#750): the
    /// destination column must be open from just above the feet through the body height, so a titan
    /// treats a ruin wall as a barrier instead of a ground-height quirk. With <paramref name="waterPasses"/> (#3881, the
    /// step gate) water and a plant standing in it count as open — the water-depth gate decides on them, and a big land
    /// animal in a deep pond read every neighbouring water column as a wall and was trapped; the spawn check keeps air only.</summary>
    private bool LargeBodyColumnOpen(CreatureSpecies sp, int x, int feetY, int z, bool waterPasses = false)
    {
        int height = (int)System.Math.Ceiling(sp.Size * 1.8f);
        for (int dy = 2; dy < height; dy++)
        {
            int y = feetY + dy;
            if (!_world.GetBlockIfLoaded(new Vector3i(x, y, z)).IsAir && !(waterPasses && IsWaterOrSubmergedPlant(x, y, z)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The standable feet cell in a column nearest to <paramref name="refY"/> (#650), read from
    /// REAL blocks via <see cref="ServerWorld.GetBlockIfLoaded"/> (unloaded chunks read as air and fall
    /// through to the generator surface — today's behaviour). Scanned outward from the reference, downward
    /// first at equal distance, so a creature under a player bridge keeps the ground instead of snapping
    /// onto the deck. This is what makes fauna honour player builds: dug pits, ramps, walls, floors. A crown carries
    /// the feet only when <paramref name="canopyCarries"/> (#2945).</summary>
    private int GroundFeetYAt(int x, int z, int refY, bool canopyCarries = true)
        => TryGroundFeetYAt(x, z, refY, CreatureBodyMinHeight, CreatureGroundScan, out int feet, canopyCarries)
            ? feet
            : _generator.SurfaceHeight(_world.Planet, x, z) + 1;

    /// <summary>The species-aware feet snap for a creature's own column (#1320): a large body needs its
    /// whole height of headroom, not the two cells a mouse gets — the plain probe read a two-cell hollow
    /// under a floor as "standable" for a ten-block titan and held its body inside the floor above. Also
    /// looks further for REAL ground before trusting the noise surface: the ±6 window fell back to the
    /// generator's pre-excavation height, which floated a creature over a freshly dug pit.</summary>
    private int GroundFeetYAt(CreatureSpecies sp, int x, int z, int refY)
        => TryGroundFeetYAt(x, z, refY, CreatureHeadroom(sp), maxScan: CreatureWideGroundScan, out int feet, CanopyCarries(sp))
            ? feet
            : _generator.SurfaceHeight(_world.Planet, x, z) + 1;

    private const int CreatureGroundScan = 6;      // the legacy ±window every feet probe scans first
    private const int CreatureWideGroundScan = 24; // ...and how far a creature's own column keeps looking (#1320)

    /// <summary>Cells of headroom a species' feet cell must offer: its collision body for large land species
    /// (mirrors <see cref="LargeBodyColumnOpen"/>/<see cref="CreatureBodyBlocked"/>), feet + head otherwise.</summary>
    private static int CreatureHeadroom(CreatureSpecies sp)
        => sp.Size >= LargeBodySize && sp.Habitat == CreatureHabitat.Land ? CreatureBodyHeight(sp) : CreatureBodyMinHeight;

    /// <summary>Like <see cref="GroundFeetYAt(int, int, int, bool)"/> but reports whether a REAL standable cell was
    /// found, so callers that must never snap to the noise surface (settlement NPCs standing on stamped floors
    /// that the generator knows nothing about) can keep their current Y when the column is unloaded/blocked.</summary>
    private bool TryGroundFeetYAt(int x, int z, int refY, out int feetY)
        => TryGroundFeetYAt(x, z, refY, CreatureBodyMinHeight, CreatureGroundScan, out feetY);

    /// <summary>The feet cell a body at <paramref name="refY"/> comes to rest on (#1349): first DOWNWARD —
    /// the reference cell itself, then everything below it that can be reached by falling, i.e. until the
    /// scan meets the first supporting block — so a creature over a fresh pit finds the pit floor, never the
    /// storey above it (the old nearest-in-either-direction probe lifted a ground-floor animal through the
    /// ceiling when a deep pit opened under it). Only when nothing below is standable (entombed, the floor
    /// removed under a low ceiling) does it look UPWARD for the nearest standable cell — the one recovery a
    /// creature makes by being lifted. Both directions reach <paramref name="maxScan"/> cells; a feet cell
    /// needs <paramref name="headroom"/> air cells. #2945: with <paramref name="canopyCarries"/> false a crown is
    /// no floor — the downward scan falls through the leaves to the ground under them, and the lift never ends on
    /// a crown's top (a land animal climbed giant crowns that reached the ground, or was lifted onto one).</summary>
    private bool TryGroundFeetYAt(int x, int z, int refY, int headroom, int maxScan, out int feetY, bool canopyCarries = true)
    {
        for (int r = 0; r <= maxScan; r++)
        {
            int y = refY - r;
            if (StandableAt(x, y, z, headroom, canopyCarries))
            {
                feetY = y;
                return true;
            }

            if (IsSupportCell(x, y, z, canopyCarries))
            {
                break; // a supporting block — nothing deeper can be reached by falling
            }
        }

        for (int r = 1; r <= maxScan; r++)
        {
            if (StandableAt(x, refY + r, z, headroom, canopyCarries))
            {
                feetY = refY + r;
                return true;
            }
        }

        feetY = refY;
        return false;
    }

    /// <summary>Whether a cell carries feet: a colliding block or lava (a lava dweller's column is handled
    /// before the probe) — not air, not water, and since #1367 not a walk-through prop either (a tuft of grass
    /// carries nobody; the same rule <see cref="CreatureBodyBlocked"/> and the wall fill read). Canopy counts
    /// when <paramref name="canopyCarries"/> — a bird sits on a crown; #2945: a land animal's probes pass false
    /// (<see cref="CanopyCarries"/>). No-load read.</summary>
    private bool IsSupportCell(int x, int y, int z, bool canopyCarries = true)
    {
        var id = _world.GetBlockIfLoaded(new Vector3i(x, y, z));
        return id.Value != _creatureWaterId && IsCollidingBlock(id, fluidsPass: false, foliagePasses: !canopyCarries);
    }

    /// <summary>Whether feet placed at <paramref name="y"/> stand on something real: solid (non-water)
    /// support below, and nothing a body collides with from the feet up through <paramref name="headroom"/>
    /// cells (#1367: small flora, a torch or a ladder in the column passes — a titan's eight-cell headroom
    /// used to fail on one tuft of grass, and the wide scan then hauled it onto the nearest tree crown; a
    /// canopy or a fluid still blocks). A crown under the feet carries them only when <paramref name="canopyCarries"/>
    /// (#2945). Uses the no-load block read.</summary>
    private bool StandableAt(int x, int y, int z, int headroom = CreatureBodyMinHeight, bool canopyCarries = true)
    {
        if (!IsSupportCell(x, y - 1, z, canopyCarries))
        {
            return false;
        }

        for (int dy = 0; dy < headroom; dy++)
        {
            if (IsCollidingBlock(_world.GetBlockIfLoaded(new Vector3i(x, y + dy, z)), fluidsPass: false, foliagePasses: false))
            {
                return false;
            }
        }

        return true;
    }
}
