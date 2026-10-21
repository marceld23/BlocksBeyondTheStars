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

    /// <summary>The ordinary trunks of this content's trees (the wood every world's ordinary tree species is made of —
    /// see <c>InitFlora</c>'s <c>MapTreeBlocks</c>; since #2851 also the pale bark of birches and bottle trees); the giant
    /// trees' own wood is not in it.</summary>
    private HashSet<ushort> NpcTreeLogIds => _npcTreeLogIds ??= HostIds("wood_log", "wood_log_b");

    /// <summary>The ordinary crowns: broadleaf leaves, conifer needles, palm fronds and (#2851) the two more leaf slots and
    /// the blossom crown (the giant trees' leaves excluded).</summary>
    private HashSet<ushort> NpcTreeLeafIds => _npcTreeLeafIds ??= HostIds("tree_leaves", "pine_needles", "palm_frond",
        "tree_leaves_b", "tree_leaves_c", "blossom_leaves");

    /// <summary>Harvestable cells around <paramref name="center"/> within <paramref name="radius"/> (at most
    /// <paramref name="max"/>, nearest first): crops and edible wild flora on loaded chunks, never a player-planted plant,
    /// never one in a protected settlement/station/base, never a species this world rolled toxic. Each column of the
    /// disc is walked down from a few blocks above the centre's feet: air, leaves, logs and plants are looked through
    /// (a fruit hangs under a crown), the first other block is the ground and ends the column — so a plant in a cave
    /// below is never offered, and one on a column whose ground is not loaded neither. Canonical cells.</summary>
    /// <param name="respectAllProtection">A wanderer's search (#2646): additionally skips every cell of
    /// <see cref="IsSettlementBlock"/> — the lodges' boxes included — and of a ruined settlement's box
    /// (<see cref="NpcCellStrictlyProtected"/>). A lodge resident keeps the default: it works its own plot.</param>
    private List<Vector3i> FindNpcGatherSites(Vector3f center, int radius, int max, bool respectAllProtection = false)
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
            if (_floraRegrow.ContainsKey(cell) || GatherCellProtected(cell)
                || (respectAllProtection && NpcCellStrictlyProtected(cell)) || (anyEdits && NpcCellPlayerEdited(cell)))
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
    /// may have been picked, or the cell edited, while the NPC walked over. <paramref name="trustedSite"/> marks a cell of
    /// the NPC's own stamped plot (a lodge's crop bed): the repository keeps the last player as a cell's owner for good, so a
    /// crop a visitor once picked would otherwise read as player-planted and the gatherer would never tend its own bed again.
    /// <paramref name="respectAllProtection"/> is the wanderer's stricter rule (see <see cref="FindNpcGatherSites"/>).</remarks>
    private bool NpcHarvestFlora(ServerNpc npc, Vector3i cell, System.Action<string, int> bank, bool trustedSite = false,
        bool respectAllProtection = false)
    {
        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        var id = _world.GetBlockIfLoaded(c);
        if (id.IsAir || !IsGatherableFlora(id.Value) || _floraRegrow.ContainsKey(c) || GatherCellProtected(c)
            || (respectAllProtection && NpcCellStrictlyProtected(c)) || (!trustedSite && NpcCellPlayerEdited(c)))
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

    /// <summary>The wanderers' stricter protection (#2646): every cell <see cref="IsSettlementBlock"/> protects — an intact
    /// settlement, the intercity line and (since #2640) a lodge's box — and the box of a ruined settlement, which nobody
    /// protects from players but no wanderer works in.</summary>
    private bool NpcCellStrictlyProtected(Vector3i c) => IsSettlementBlock(c) || InRuinedSettlementBox(c);

    /// <summary>True if the cell lies in a RUINED settlement's box (a ruin, a monument, a rail ruin — the instances
    /// <see cref="IsSettlementBoxBlock"/> skips). Unprotected for players; the NPC work cores still keep out (no woodcutter
    /// fells a ruined building's log frame, no hunter shoots inside its walls).</summary>
    private bool InRuinedSettlementBox(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var s in _settlements)
        {
            if (!s.Ruined)
            {
                continue;
            }

            int lx = WorldConstants.WrapDeltaX(pos.X - s.Min.X, circ);
            int lz = WorldConstants.WrapDeltaZ(pos.Z - s.Min.Z, circ);
            if (lx >= 0 && lx <= s.Max.X - s.Min.X && lz >= 0 && lz <= s.Max.Z - s.Min.Z && pos.Y >= s.Min.Y && pos.Y <= s.Max.Y)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether any player edit lies in the box (inclusive) — one aggregate query per canonical piece of the box
    /// (<see cref="AnyCanonicalBox"/>: one, or up to four where it crosses the longitude or latitude seam), so it never
    /// misses an edit and never asks cell by cell.</summary>
    private bool NpcAreaHasPlayerEdits(Vector3i min, Vector3i max)
        => AnyCanonicalBox(min, max, (lo, hi) => _repo.HasPlayerBlockEdits(_world.LocationId, lo, hi));

    /// <summary>Splits the inclusive box <paramref name="min"/>–<paramref name="max"/> (any frame, max ≥ min) into the
    /// canonical boxes the edit store indexes — X wraps at the circumference into [0, C), Z at the latitude period into
    /// [−P/2, P/2) — and asks <paramref name="query"/> for each until one answers true. A box as wide as the world on an
    /// axis covers that whole axis.</summary>
    private bool AnyCanonicalBox(Vector3i min, Vector3i max, System.Func<Vector3i, Vector3i, bool> query)
    {
        int circ = _world.Circumference;
        int period = WorldConstants.LatitudePeriodFor(circ);
        Span<(int Lo, int Hi)> xs = stackalloc (int, int)[2];
        Span<(int Lo, int Hi)> zs = stackalloc (int, int)[2];
        int nx = SplitWrapped(min.X, max.X, WorldConstants.WrapX(min.X, circ), 0, circ, xs);
        int nz = SplitWrapped(min.Z, max.Z, WorldConstants.WrapZ(min.Z, circ), -period / 2, period, zs);
        for (int i = 0; i < nx; i++)
            for (int k = 0; k < nz; k++)
            {
                if (query(new Vector3i(xs[i].Lo, min.Y, zs[k].Lo), new Vector3i(xs[i].Hi, max.Y, zs[k].Hi)))
                {
                    return true;
                }
            }

        return false;

        // One axis: the canonical start, the domain [origin, origin + period) — one range, two across the seam.
        static int SplitWrapped(int from, int to, int start, int origin, int period, Span<(int Lo, int Hi)> into)
        {
            int length = to - from + 1;
            if (period <= 0 || length >= period)
            {
                into[0] = (origin, origin + period - 1);
                return 1;
            }

            int end = start + length - 1;
            if (end < origin + period)
            {
                into[0] = (start, end);
                return 1;
            }

            into[0] = (start, origin + period - 1);
            into[1] = (origin, end - period);
            return 2;
        }
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam (#2642): <see cref="FindNpcGatherSites"/> on the active world.</summary>
    public IReadOnlyList<Vector3i> FindNpcGatherSitesForTest(Vector3f center, int radius, int max, bool respectAllProtection = false)
        => FindNpcGatherSites(center, radius, max, respectAllProtection);

    /// <summary>Test seam (#2642/#2644/#2645 review): registers a RUINED settlement box on the active world (unprotected
    /// for players; the NPC work cores keep out).</summary>
    public void RegisterTestRuinForTest(string name, Vector3i min, Vector3i max)
        => _settlements.Add(new SettlementInstance { Name = name, Min = min, Max = max, Tier = "village", GroundY = min.Y, Ruined = true });

    /// <summary>Test seam (#2642): <see cref="NpcHarvestFlora"/> by the NPC with this id; the banked drops are appended to
    /// <paramref name="banked"/>. False when there is no such NPC or nothing to harvest.</summary>
    public bool NpcHarvestFloraForTest(int npcId, Vector3i cell, List<(string Item, int Count)> banked)
        => _npcs.FirstOrDefault(n => n.Id == npcId) is { } npc && NpcHarvestFlora(npc, cell, (item, count) => banked.Add((item, count)));

    /// <summary>Test seam (#2642): whether a player edited this cell (a block-edit row with an owner).</summary>
    public bool IsBlockPlayerEditedForTest(Vector3i cell) => NpcCellPlayerEdited(cell);

    /// <summary>Test seam (#2644 review): <see cref="NpcAreaHasPlayerEdits"/> for a box in any frame.</summary>
    public bool NpcAreaHasPlayerEditsForTest(Vector3i min, Vector3i max) => NpcAreaHasPlayerEdits(min, max);

    /// <summary>Test seam (#2642): the colour a regrowing cell comes back in (its persisted tint), or -1 when nothing
    /// regrows there.</summary>
    public int FloraRegrowTintForTest(Vector3i cell)
        => _floraRegrow.TryGetValue(WorldConstants.CanonicalBlock(cell, _world.Circumference), out var r) ? r.Tint : -1;
}
