// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The gatherer's work core (#2642, wilderness folk #2639): an NPC picks a plant the way the base gardener does — the
/// cell becomes air, every client hears the <c>BlockChanged</c>, the regrow is scheduled exactly as after a player's
/// harvest (a fruit in its tree kind's colour) — and the drops go to the caller's <c>bank</c>. Shared by the lodge
/// residents (#2641) and the wanderers (#2646); the caller picks the site, walks there, works a few seconds with the
/// basket and paces itself (one harvest per call, no cadence here).
/// <list type="bullet">
/// <item><b>What counts</b>: a crop (always edible, the same on every world) and a wild species this world did NOT roll
/// toxic — land plants only (no seabed or cave plant, no hanging plant except a tree's fruit). Never a sapling (the
/// woodcutter's replanting must not be picked) and never a bred plant (a player's species).</item>
/// <item><b>Never</b> a plant on a cell a player edited (a block-edit row with an owner: a player planted it, or ever
/// picked it — the attribution store cannot tell the two apart, so a picked plant is left to the player), never inside
/// an intact settlement's box, the intercity line, a station, a factory, the Guardian core, a player base's zone or a
/// landed ship. The lodge box is deliberately NOT asked (<see cref="GatherCellProtected"/>): a lodge's own plot lies
/// inside it.</item>
/// <item><b>Loaded chunks only</b>: every read is <see cref="ServerWorld.GetBlockIfLoaded"/>, so a search never drags a
/// chunk into the cache.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Largest search radius a gatherer may ask for (the scan is a column walk over the disc).</summary>
    private const int NpcGatherMaxRadius = 32;

    /// <summary>Most sites one search returns.</summary>
    private const int NpcGatherMaxSites = 64;

    /// <summary>How far above and below the centre's feet a column is walked for plants and their ground.</summary>
    private const int NpcGatherScanUp = 6;
    private const int NpcGatherScanDown = 6;

    /// <summary>How high above its ground a plant may stand and still be picked from below — a ground plant stands one
    /// above, a tree's fruit hangs under the lowest leaves.</summary>
    private const int NpcGatherReachAboveGround = 4;

    private HashSet<ushort>? _npcTreeLogIds;
    private HashSet<ushort>? _npcTreeLeafIds;

    /// <summary>The ordinary trunk of this content's trees (the wood every world's ordinary tree species is made of —
    /// see <c>InitFlora</c>'s <c>MapTreeBlocks</c>); the giant trees' own wood is not in it.</summary>
    private HashSet<ushort> NpcTreeLogIds => _npcTreeLogIds ??= HostIds("wood_log");

    /// <summary>The ordinary crowns: broadleaf leaves, conifer needles, palm fronds (the giant trees' leaves excluded).</summary>
    private HashSet<ushort> NpcTreeLeafIds => _npcTreeLeafIds ??= HostIds("tree_leaves", "pine_needles", "palm_frond");

    /// <summary>Harvestable cells around <paramref name="center"/> within <paramref name="radius"/> (at most
    /// <paramref name="max"/>, nearest first): crops and edible wild flora on loaded chunks, never a player-planted plant,
    /// never one in a protected settlement/station/base, never a species this world rolled toxic. Each column of the
    /// disc is walked down from a few blocks above the centre's feet: air, leaves, logs and plants are looked through
    /// (a fruit hangs under a crown), the first other block is the ground and ends the column — so a plant in a cave
    /// below is never offered, and one on a column whose ground is not loaded neither. Canonical cells.</summary>
    private List<Vector3i> FindNpcGatherSites(Vector3f center, int radius, int max)
    {
        var result = new List<Vector3i>();
        radius = System.Math.Clamp(radius, 0, NpcGatherMaxRadius);
        max = System.Math.Min(max, NpcGatherMaxSites);
        if (max <= 0)
        {
            return result;
        }

        var gatherable = GatherableFloraIds();
        if (gatherable.Count == 0)
        {
            return result;
        }

        int circ = _world.Circumference;
        int cx = (int)System.Math.Floor(center.X), cy = (int)System.Math.Floor(center.Y), cz = (int)System.Math.Floor(center.Z);
        var candidates = new List<(Vector3i Cell, int DistSq)>();
        var column = new List<int>(4);
        for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (dx * dx + dz * dz > radius * radius)
                {
                    continue;
                }

                column.Clear();
                int? groundY = null;
                for (int y = cy + NpcGatherScanUp; y >= cy - NpcGatherScanDown; y--)
                {
                    ushort id = _world.GetBlockIfLoaded(new Vector3i(cx + dx, y, cz + dz)).Value;
                    if (id == 0)
                    {
                        continue;
                    }

                    if (gatherable.Contains(id))
                    {
                        column.Add(y);
                        continue;
                    }

                    if (IsFlora(id) || NpcTreeLogIds.Contains(id) || NpcTreeLeafIds.Contains(id))
                    {
                        continue; // looked through: a crown over the fruit, a trunk, a plant nobody picks
                    }

                    groundY = y;
                    break;
                }

                if (groundY is not { } ground)
                {
                    continue; // no ground in reach (a cliff, a gap or an unloaded chunk): nothing to stand on
                }

                foreach (int y in column)
                {
                    if (y - ground <= NpcGatherReachAboveGround)
                    {
                        int dy = y - cy;
                        candidates.Add((WorldConstants.CanonicalBlock(new Vector3i(cx + dx, y, cz + dz), circ), dx * dx + dy * dy + dz * dz));
                    }
                }
            }

        if (candidates.Count == 0)
        {
            return result;
        }

        candidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));

        // One aggregate query answers "no player touched anything here" for the whole disc (the common case in the open
        // land); only when it finds something does each candidate ask for its own cell.
        bool anyEdits = NpcAreaHasPlayerEdits(
            new Vector3i(cx - radius, cy - NpcGatherScanDown, cz - radius),
            new Vector3i(cx + radius, cy + NpcGatherScanUp, cz + radius));
        foreach (var (cell, _) in candidates)
        {
            if (_floraRegrow.ContainsKey(cell) || GatherCellProtected(cell) || (anyEdits && NpcCellPlayerEdited(cell)))
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

    /// <summary>Harvests one flora cell the gardener's way (air + <c>BlockChanged</c> + regrow), banking the drops. False
    /// when the cell no longer holds harvestable flora.</summary>
    /// <remarks>The break itself is <see cref="BreakBlockCore"/> without a player (owner "" — an NPC's harvest is no
    /// player edit): the drops are composed exactly as for a player (a spore bloom's bonus included), the fruit's tint is
    /// read before the cell is cleared and rides into its regrow, the station grid and the support/fluid hooks see the
    /// change, and the player-only hooks (missions, VEGA, samples) stay out. The site is checked again here — the plant
    /// may have been picked, or the cell edited, while the NPC walked over.</remarks>
    private bool NpcHarvestFlora(ServerNpc npc, Vector3i cell, System.Action<string, int> bank)
    {
        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        var id = _world.GetBlockIfLoaded(c);
        if (id.IsAir || !IsGatherableFlora(id.Value) || _floraRegrow.ContainsKey(c) || GatherCellProtected(c) || NpcCellPlayerEdited(c))
        {
            return false;
        }

        var def = _world.Definition(id);
        if (def is null)
        {
            return false;
        }

        BreakBlockCore(null, string.Empty, c, def, null, bank);
        return true;
    }

    /// <summary>The flora block ids a gatherer picks on the ACTIVE world (its roster decides what is toxic, #2226).</summary>
    private HashSet<ushort> GatherableFloraIds()
    {
        var set = new HashSet<ushort>();
        foreach (ushort id in _floraIds)
        {
            if (IsGatherableFlora(id))
            {
                set.Add(id);
            }
        }

        return set;
    }

    /// <summary>Whether a gatherer picks this flora on the active world: a crop always; a wild land species only when this
    /// world's roster did not roll it toxic; never a sapling, a bred plant, an authored plant, a seabed or cave plant, nor
    /// a hanging plant other than a tree's fruit.</summary>
    private bool IsGatherableFlora(ushort id)
    {
        if (!IsFlora(id) || IsSapling(id) || IsBredPlant(id) || _content.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(id)) is not { } def
            || FloraCatalog.Find(def.Key) is not { } sp)
        {
            return false;
        }

        if (sp.Cultivated)
        {
            return true; // a crop is edible on every world (FloraCatalog: never rolled toxic)
        }

        if (sp.Authored || sp.Aquatic || sp.Habitat == FloraHabitat.Cave || (sp.Hanging && !sp.Fruit))
        {
            return false;
        }

        return _floraSpeciesByBlock.TryGetValue(id, out var species) && !species.Toxic;
    }

    /// <summary>
    /// The protection a gatherer's or woodcutter's work cell must stay clear of (#2642/#2644): an intact settlement's box
    /// and the intercity line, a station, the Guardian core, a factory, a player base's zone (no NPC is anybody's ally),
    /// a landed ship's pad and interior. Deliberately NOT <see cref="IsSettlementBlock"/>: the lodges hook their own box
    /// in there (#2640), and a lodge's plot lies inside that box — a lodge resident picks its own garden.
    /// </summary>
    private bool GatherCellProtected(Vector3i c)
        => IsSettlementBoxBlock(c) || IsIntercityRailBlock(c) || IsStationBlock(c) || IsGuardianCoreProtected(c)
           || IsFactoryProtected(c, string.Empty, false) || IsBaseProtected(c, string.Empty, false)
           || IsShipBlock(c) || InShipInterior(c);

    /// <summary>Whether a player edited this one cell (a block-edit row with an owner — a build, a dig, a planting, a
    /// harvest). Structure stamps, regrowth and NPC work write no owner and do not count.</summary>
    private bool NpcCellPlayerEdited(Vector3i c)
    {
        var p = WorldConstants.CanonicalBlock(c, _world.Circumference);
        return _repo.HasPlayerBlockEdits(_world.LocationId, p, p);
    }

    /// <summary>Whether any player edit lies in the box (inclusive). One aggregate query when the box's canonical corners
    /// keep their order; across the longitude or latitude seam it answers true (callers then ask cell by cell), so it
    /// never misses an edit.</summary>
    private bool NpcAreaHasPlayerEdits(Vector3i min, Vector3i max)
    {
        int circ = _world.Circumference;
        var lo = WorldConstants.CanonicalBlock(min, circ);
        var hi = WorldConstants.CanonicalBlock(max, circ);
        if (lo.X > hi.X || lo.Z > hi.Z || hi.X - lo.X != max.X - min.X || hi.Z - lo.Z != max.Z - min.Z)
        {
            return true; // the box wraps: no single canonical range covers it
        }

        return _repo.HasPlayerBlockEdits(_world.LocationId, lo, hi);
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam (#2642): <see cref="FindNpcGatherSites"/> on the active world.</summary>
    public IReadOnlyList<Vector3i> FindNpcGatherSitesForTest(Vector3f center, int radius, int max) => FindNpcGatherSites(center, radius, max);

    /// <summary>Test seam (#2642): <see cref="NpcHarvestFlora"/> by the NPC with this id; the banked drops are appended to
    /// <paramref name="banked"/>. False when there is no such NPC or nothing to harvest.</summary>
    public bool NpcHarvestFloraForTest(int npcId, Vector3i cell, List<(string Item, int Count)> banked)
        => _npcs.FirstOrDefault(n => n.Id == npcId) is { } npc && NpcHarvestFlora(npc, cell, (item, count) => banked.Add((item, count)));

    /// <summary>Test seam (#2642): whether a player edited this cell (a block-edit row with an owner).</summary>
    public bool IsBlockPlayerEditedForTest(Vector3i cell) => NpcCellPlayerEdited(cell);

    /// <summary>Test seam (#2642): the colour a regrowing cell comes back in (its persisted tint), or -1 when nothing
    /// regrows there.</summary>
    public int FloraRegrowTintForTest(Vector3i cell)
        => _floraRegrow.TryGetValue(WorldConstants.CanonicalBlock(cell, _world.Circumference), out var r) ? r.Tint : -1;
}
