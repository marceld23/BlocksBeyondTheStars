// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Jobs for base residents (#1868, Marcel 2026-09-13: "visible and with yield"). The base's own blocks decide who
/// does what (see <see cref="AssignBaseResidents"/>); this is what they do on duty.
/// <list type="bullet">
/// <item><b>Gardener</b> (crops, hydro trays, saplings): walks from bed to bed, stays a while at each, harvests a
/// standing crop into a base crate every <see cref="GardenerHarvestSeconds"/> — the crop's normal drops, regrowth
/// scheduled exactly as when a player picks it — and tends saplings, which grow faster. No crate: tends only. The
/// owner hears what was stored (at most every <see cref="HarvestToastSeconds"/>).</item>
/// <item><b>Craftsman</b> (workbench, forge): every <see cref="CraftsmanYieldSeconds"/> at the bench puts two plant
/// fibres in a base crate; with a forge and iron ore in that crate it smelts two ore into one ingot instead — the
/// workshop recipe's ratio, so no metal appears from nothing.</item>
/// <item><b>Guard</b> (a walled yard or a sentry post; the night shift): walks the inside of the wall, and on seeing
/// bandit scouts at the base or a robber closing in, warns the owner by radio once per visit and drives them off
/// (they turn and leave). No combat — a fighting bandit is the sentry's business.</item>
/// <item>Vendor and quartermaster simply staff their post by day (barter and missions hang on the post).</item>
/// <item>Deposits use <see cref="NpcDepositToContainer"/>: consumables may go in (a gardener stores berries) — the
/// player's own stash rule is untouched — but a crate's filter and a wood box's slot limit are respected.</item>
/// </list>
/// Jobs run only while a player is near (the world stands still otherwise), and only on duty.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds a gardener spends at one garden site.</summary>
    private const double GardenerVisitSeconds = 20.0;

    /// <summary>Seconds between two harvests of one base's gardener.</summary>
    private const double GardenerHarvestSeconds = 90.0;

    /// <summary>Seconds a tended sapling's growth clock moves forward per visit.</summary>
    private const double GardenerSaplingBoostSeconds = 30.0;

    /// <summary>Most garden sites a gardener walks between.</summary>
    private const int GardenerSiteCap = 24;

    /// <summary>Seconds between two yields of a craftsman at the bench.</summary>
    private const double CraftsmanYieldSeconds = 300.0;

    /// <summary>Most seconds between two harvest toasts for one base.</summary>
    private const double HarvestToastSeconds = 600.0;

    /// <summary>How far a guard notices a bandit.</summary>
    private const float GuardSightRange = 24f;

    /// <summary>A guard this close to a bandit sends it away.</summary>
    private const float GuardDriveOffRange = 8f;

    /// <summary>A robber this close to the base core counts as closing in on the base.</summary>
    private const float GuardRobberWatchRange = 40f;

    /// <summary>Seconds between two guard radio warnings for one base.</summary>
    private const double GuardWarnCooldown = 300.0;

    /// <summary>Patrol waypoints at most.</summary>
    private const int GuardPatrolCap = 24;

    private readonly Dictionary<int, double> _guardWarnedUntil = new();
    private readonly Dictionary<int, (double NextHarvestAt, int Stored, double NextToastAt)> _baseHarvest = new();

    /// <summary>One NPC's job on duty (called from the routine pass for NPCs in reach).</summary>
    private void TickNpcJob(ServerNpc npc, double dt, List<PlayerSession> targets)
    {
        if (npc.BaseId == 0 || npc.Job.Length == 0 || npc.Phase != NpcPhase.Day || npc.Pose != 0)
        {
            return;
        }

        var b = _bases.FirstOrDefault(x => x.Id == npc.BaseId && x.Planet == _world.LocationId);
        if (b is null || !_baseIndex.TryGetValue(b.Id, out var idx))
        {
            return;
        }

        switch (npc.Job)
        {
            case "gardener":
                TickGardener(npc, b, idx);
                break;
            case "craftsman":
                TickCraftsman(npc, b, idx);
                break;
            case "guard":
                TickGuard(npc, b, targets);
                break;
        }
    }

    // ---------------- gardener ----------------

    /// <summary>The garden sites of a base: every standing crop and sapling, and the air above every hydro tray (a
    /// harvested crop regrows there), one per column, in the index's order.</summary>
    private static List<Vector3i> GardenSites(BaseIndex idx)
    {
        var sites = new List<Vector3i>();
        var columns = new HashSet<(int, int)>();
        void Add(Vector3i c)
        {
            if (sites.Count < GardenerSiteCap && columns.Add((c.X, c.Z)))
            {
                sites.Add(c);
            }
        }

        foreach (var c in idx.Crops)
        {
            Add(c);
        }

        foreach (var c in idx.Saplings)
        {
            Add(c);
        }

        foreach (var t in idx.Trays)
        {
            Add(new Vector3i(t.X, t.Y + 1, t.Z));
        }

        return sites;
    }

    private void TickGardener(ServerNpc npc, ServerBase b, BaseIndex idx)
    {
        if (npc.Goal is not null)
        {
            return; // on the way to the next bed
        }

        var sites = GardenSites(idx);
        if (sites.Count == 0)
        {
            return;
        }

        if (npc.SiteUntil <= 0)
        {
            // Just arrived (or the shift just began at the work spot): look after this site.
            npc.SiteUntil = _uptime + GardenerVisitSeconds;
            TendGardenSite(npc, b, idx, sites[npc.SiteCursor % sites.Count]);
            return;
        }

        if (_uptime < npc.SiteUntil)
        {
            return;
        }

        npc.SiteCursor = (npc.SiteCursor + 1) % sites.Count;
        npc.SiteUntil = 0;
        var site = sites[npc.SiteCursor];
        var spot = SpotBeside(site, new HashSet<Vector3i>()) ?? SpotBeside(new Vector3i(site.X, site.Y - 1, site.Z), new HashSet<Vector3i>());
        if (spot is { } s)
        {
            SetNpcGoal(npc, s, NpcArrival.None, WorkLeash);
        }
    }

    /// <summary>A gardener at a site: harvests a standing crop when the base's harvest is due and a crate has room,
    /// moves a sapling's growth clock forward.</summary>
    private void TendGardenSite(ServerNpc npc, ServerBase b, BaseIndex idx, Vector3i site)
    {
        var canonical = WorldConstants.CanonicalBlock(site, _world.Circumference);
        if (_floraRegrow.TryGetValue(canonical, out var regrow) && regrow.FloraId == _saplingId)
        {
            double t = System.Math.Max(1.0, regrow.Timer - GardenerSaplingBoostSeconds);
            _floraRegrow[canonical] = (regrow.FloraId, t, regrow.Tint);
            _repo.SaveFloraRegrow(_world.LocationId, canonical, regrow.FloraId, t, regrow.Tint);
            return;
        }

        var id = _world.GetBlockIfLoaded(canonical);
        if (id.IsAir || !_ixCrops.Contains(id.Value) || _floraRegrow.ContainsKey(canonical))
        {
            return; // nothing ripe standing here
        }

        var state = _baseHarvest.TryGetValue(b.Id, out var h) ? h : (0.0, 0, 0.0);
        if (_uptime < state.Item1)
        {
            return;
        }

        var def = _content.BlockById(id);
        if (def is null)
        {
            return;
        }

        var drops = def.Drops.Select(d => new ItemAmount(d.Item, d.Count)).ToList();
        var crate = BaseCrateFor(idx, drops);
        if (crate is null || !NpcDepositToContainer(crate, drops))
        {
            return; // no crate, or no room: the crop stays standing for the player
        }

        _world.SetBlock(canonical, BlockId.Air);
        BroadcastToWorld(new BlockChanged { X = canonical.X, Y = canonical.Y, Z = canonical.Z, Block = BlockId.AirValue });
        WriteBackStationCell(canonical, BlockId.Air);
        ScheduleFloraRegrow(canonical, id.Value); // regrows like a player's harvest
        int stored = state.Item2 + drops.Sum(d => d.Count);
        double nextToast = state.Item3;
        if (_uptime >= nextToast && FindSessionByPlayerId(b.OwnerId) is { Joined: true } owner && owner.CurrentLocationId == b.Planet)
        {
            Send(owner, new ServerMessage { Text = "@srv.base.harvest:" + stored });
            stored = 0;
            nextToast = _uptime + HarvestToastSeconds;
        }

        _baseHarvest[b.Id] = (_uptime + GardenerHarvestSeconds, stored, nextToast);
    }

    // ---------------- craftsman ----------------

    private void TickCraftsman(ServerNpc npc, ServerBase b, BaseIndex idx)
    {
        if (npc.Goal is not null || WrapDistSq(npc.Pos, npc.Work) > 2.5 * 2.5)
        {
            return; // not at the bench
        }

        if (npc.NextYieldAt <= 0)
        {
            npc.NextYieldAt = _uptime + CraftsmanYieldSeconds;
            return;
        }

        if (_uptime < npc.NextYieldAt)
        {
            return;
        }

        npc.NextYieldAt = _uptime + CraftsmanYieldSeconds;

        // A forge and ore in a crate: smelt at the workshop recipe's ratio (two ore, one ingot).
        if (idx.Forges.Count > 0)
        {
            foreach (var cell in idx.Containers)
            {
                if (ContainerAt(cell) is { } ores && ores.Items.FirstOrDefault(s => s.Item == "iron_ore" && s.Count >= 2) is { } ore)
                {
                    ore.Count -= 2;
                    ores.Items.RemoveAll(s => s.Count <= 0);
                    if (NpcDepositToContainer(ores, new[] { new ItemAmount("iron_ingot", 1) }))
                    {
                        return;
                    }

                    ores.Items.Add(new ItemStack("iron_ore", 2)); // no room for the ingot: undo
                    ores.Items = ores.Items.GroupBy(s => s.Item).Select(g => new ItemStack(g.Key, g.Sum(s => s.Count))).ToList();
                    _repo.SaveContainer(ores);
                }
            }
        }

        var fibre = new List<ItemAmount> { new("plant_fiber", 2) };
        if (BaseCrateFor(idx, fibre) is { } crate)
        {
            NpcDepositToContainer(crate, fibre);
        }
    }

    // ---------------- guard ----------------

    private void TickGuard(ServerNpc npc, ServerBase b, List<PlayerSession> targets)
    {
        if (npc.Goal is null && npc.Patrol is { Count: > 0 } patrol)
        {
            npc.PatrolIndex = (npc.PatrolIndex + 1) % patrol.Count;
            SetNpcGoal(npc, patrol[npc.PatrolIndex], NpcArrival.None, WorkLeash);
        }

        if (_uptime < npc.NextSightAt)
        {
            return;
        }

        npc.NextSightAt = _uptime + 2.0;
        var core = new Vector3f(b.Cell.X + 0.5f, b.Cell.Y, b.Cell.Z + 0.5f);
        CombatEntity? seen = null;
        double bestSq = GuardSightRange * GuardSightRange;
        foreach (var bandit in _bandits)
        {
            bool watching = bandit.BanditPhase == BanditPhase.Scouting && bandit.ScoutBaseId == b.Id;
            bool closingIn = bandit.BanditPhase is BanditPhase.Approach or BanditPhase.Demanding
                             && WrapDistSq(bandit.Position, core) <= GuardRobberWatchRange * GuardRobberWatchRange;
            if (!watching && !closingIn)
            {
                continue; // a fighting robber is the sentry's; a leaving one is already going
            }

            // No line of sight asked: the guard is on the wall and the scouts stand right outside it — a wall two
            // blocks high hides a man's eyes, not his voice.
            double d = WrapDistSq(bandit.Position, npc.Pos);
            if (d <= bestSq)
            {
                bestSq = d;
                seen = bandit;
            }
        }

        if (seen is null)
        {
            return;
        }

        if ((!_guardWarnedUntil.TryGetValue(b.Id, out double until) || _uptime >= until)
            && FindSessionByPlayerId(b.OwnerId) is { Joined: true } owner
            && owner.State.NpcCallsMode != NpcCallsMode.Off)
        {
            _guardWarnedUntil[b.Id] = _uptime + GuardWarnCooldown;
            string text = Localize(owner.Locale, "npc.call.guard_scouts").Replace("{0}", b.Name);
            Send(owner, new ChatMessage { Sender = $"📻 {npc.Name} ({b.Name})", Text = text, IsNpcCall = true });
        }

        if (bestSq <= GuardDriveOffRange * GuardDriveOffRange)
        {
            BeginBanditLeave(seen); // "move along" — the guard's word is enough
            BroadcastPlanetEnemies();
            return;
        }

        // Walk to the patrol point nearest the bandit so the next look is from close up.
        if (npc.Patrol is { Count: > 0 } ring)
        {
            int nearest = 0;
            double nearestSq = double.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                double d = WrapDistSq(ring[i], seen.Position);
                if (d < nearestSq)
                {
                    nearestSq = d;
                    nearest = i;
                }
            }

            npc.PatrolIndex = nearest;
            SetNpcGoal(npc, ring[nearest], NpcArrival.None, WorkLeash);
        }
    }

    /// <summary>
    /// The guard's walk (#1868): the inside of the base's wall ring, from the walled-yard fill at the core's feet
    /// level — the standable yard cells next to the wall (a wall up to three thick). Ordered around the core and
    /// thinned to at most <see cref="GuardPatrolCap"/> waypoints. Null when the base has no closed yard (a sentry-post
    /// base without walls gets a square round inside its core zone instead).
    /// </summary>
    private List<Vector3f>? GuardPatrolFor(ServerBase b)
    {
        // The ring is a walk over the whole fill box — reuse it until the base changed or a minute went by.
        int rebuilds = _baseIndex.TryGetValue(b.Id, out var known) ? known.Rebuilds : -1;
        if (_basePatrol.TryGetValue(b.Id, out var cached) && cached.Rebuilds == rebuilds && _uptime - cached.At < 60.0)
        {
            return cached.Patrol;
        }

        var patrol = ComputeGuardPatrol(b);
        _basePatrol[b.Id] = (rebuilds, _uptime, patrol);
        return patrol;
    }

    private readonly Dictionary<int, (int Rebuilds, double At, List<Vector3f>? Patrol)> _basePatrol = new();

    private List<Vector3f>? ComputeGuardPatrol(ServerBase b)
    {
        int circ = _world.Circumference;
        int feetY = b.Cell.Y;
        var ring = new List<Vector3i>();
        var level = RefreshBaseWalls(b, feetY);
        if (!level.FailOpen && level.Reached is not null)
        {
            int r = level.Reach;
            Vector3i Cell(int dx, int dz) => WorldConstants.CanonicalBlock(new Vector3i(b.Cell.X - r + dx, feetY, b.Cell.Z - r + dz), circ);
            bool Outside(int dx, int dz) => dx < 0 || dz < 0 || dx > 2 * r || dz > 2 * r || level.Contains(Cell(dx, dz), circ);

            // Distance (through non-standable enclosed cells — the wall) from the outside, up to three.
            int side = 2 * r + 1;
            var wallDepth = new Dictionary<(int, int), int>();
            var frontier = new Queue<(int, int)>();
            for (int dx = 0; dx < side; dx++)
                for (int dz = 0; dz < side; dz++)
                {
                    if (!Outside(dx, dz))
                    {
                        continue;
                    }

                    foreach (var (ox, oz) in WallFillDirs)
                    {
                        int nx = dx + ox, nz = dz + oz;
                        if (!Outside(nx, nz) && !wallDepth.ContainsKey((nx, nz)))
                        {
                            var c = Cell(nx, nz);
                            if (StandableSpot(c.X, c.Y, c.Z) is not null)
                            {
                                ring.Add(c); // a gateway: the yard touches the outside directly
                                wallDepth[(nx, nz)] = 0;
                            }
                            else
                            {
                                wallDepth[(nx, nz)] = 1;
                                frontier.Enqueue((nx, nz));
                            }
                        }
                    }
                }

            while (frontier.Count > 0)
            {
                var (x, z) = frontier.Dequeue();
                int depth = wallDepth[(x, z)];
                foreach (var (ox, oz) in WallFillDirs)
                {
                    int nx = x + ox, nz = z + oz;
                    if (Outside(nx, nz) || wallDepth.ContainsKey((nx, nz)))
                    {
                        continue;
                    }

                    var c = Cell(nx, nz);
                    if (StandableSpot(c.X, c.Y, c.Z) is not null)
                    {
                        ring.Add(c);
                        wallDepth[(nx, nz)] = 0;
                    }
                    else if (depth < 3)
                    {
                        wallDepth[(nx, nz)] = depth + 1;
                        frontier.Enqueue((nx, nz));
                    }
                }
            }
        }

        if (ring.Count < 8)
        {
            ring.Clear();
            if (!_baseIndex.TryGetValue(b.Id, out var idx) || idx.SentryPosts.Count == 0)
            {
                return null;
            }

            // No yard, but a sentry post: a square round on the edge of the core zone.
            const int round = 6;
            for (int dx = -round; dx <= round; dx++)
                for (int dz = -round; dz <= round; dz++)
                {
                    if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != round)
                    {
                        continue;
                    }

                    for (int y = b.Cell.Y + 2; y >= b.Cell.Y - 2; y--)
                    {
                        if (StandableSpot(b.Cell.X + dx, y, b.Cell.Z + dz) is not null)
                        {
                            ring.Add(new Vector3i(b.Cell.X + dx, y, b.Cell.Z + dz));
                            break;
                        }
                    }
                }

            if (ring.Count < 4)
            {
                return null;
            }
        }

        // Around the core, then every n-th so the round has at most the cap's waypoints.
        var ordered = ring
            .OrderBy(c => System.Math.Atan2(WorldConstants.WrapDeltaZ(c.Z - b.Cell.Z, circ), WorldConstants.WrapDeltaX(c.X - b.Cell.X, circ)))
            .ToList();
        int stride = System.Math.Max(1, (ordered.Count + GuardPatrolCap - 1) / GuardPatrolCap);
        var patrol = new List<Vector3f>();
        for (int i = 0; i < ordered.Count; i += stride)
        {
            var c = ordered[i];
            patrol.Add(new Vector3f(c.X + 0.5f, c.Y, c.Z + 0.5f));
        }

        return patrol;
    }

    // ---------------- crates ----------------

    private StoredContainer? ContainerAt(Vector3i cell)
        => _containers.FirstOrDefault(c => c.Kind == "crate"
                                           && WorldConstants.CanonicalBlock(c.Position, _world.Circumference).Equals(WorldConstants.CanonicalBlock(cell, _world.Circumference)));

    /// <summary>The first base crate whose filter lets these items in.</summary>
    private StoredContainer? BaseCrateFor(BaseIndex idx, IReadOnlyList<ItemAmount> items)
    {
        foreach (var cell in idx.Containers)
        {
            if (ContainerAt(cell) is { } c && (c.Filter.Count == 0 || items.All(i => c.Filter.Contains(ItemKey.Base(i.Item)))))
            {
                return c;
            }
        }

        return null;
    }

    /// <summary>
    /// An NPC puts items into a crate (#1868). Unlike the player's stash (<see cref="Stashable"/>) consumables may go
    /// in — the gardener's berries belong in the pantry — but the crate's filter and a wood box's slot limit are
    /// respected. All or nothing: false (and nothing stored) when anything does not fit.
    /// </summary>
    private bool NpcDepositToContainer(StoredContainer container, IEnumerable<ItemAmount> items)
    {
        var list = items.Where(i => i.Count > 0).ToList();
        if (list.Count == 0)
        {
            return true;
        }

        if (container.Filter.Count > 0 && list.Any(i => !container.Filter.Contains(ItemKey.Base(i.Item))))
        {
            return false;
        }

        bool woodBox = _world.GetBlock(container.Position).Value == (_content.GetBlock("wood_crate")?.NumericId.Value ?? 0);
        var merged = container.Items.Where(s => !s.IsEmpty).GroupBy(s => s.Item).ToDictionary(g => g.Key, g => g.Sum(s => s.Count));
        foreach (var i in list)
        {
            if (woodBox && !merged.ContainsKey(i.Item) && merged.Count >= WoodCrateStackSlots)
            {
                return false;
            }

            merged[i.Item] = (merged.TryGetValue(i.Item, out int have) ? have : 0) + i.Count;
        }

        container.Items = merged.Select(kv => new ItemStack(kv.Key, kv.Value)).ToList();
        _repo.SaveContainer(container);
        BroadcastContainers();
        return true;
    }

    /// <summary>Test seam (#1868): a base resident's patrol waypoint count (0 = none).</summary>
    public int GuardPatrolLengthForTest(int npcId) => _npcs.FirstOrDefault(n => n.Id == npcId)?.Patrol?.Count ?? 0;

    /// <summary>Test seam (#1868): the growth timer left on a regrowing cell, or -1.</summary>
    public double FloraRegrowTimerForTest(int x, int y, int z)
        => _floraRegrow.TryGetValue(WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference), out var r) ? r.Timer : -1;

    /// <summary>Test seam (#1868): runs the craftsman's next yield now.</summary>
    public void DueCraftsmanYieldForTest()
    {
        foreach (var npc in _npcs)
        {
            if (npc.Job == "craftsman")
            {
                npc.NextYieldAt = _uptime;
            }
        }
    }

    /// <summary>Test seam (#1868): makes every base's next harvest due now.</summary>
    public void DueHarvestForTest()
    {
        foreach (var key in _baseHarvest.Keys.ToList())
        {
            var v = _baseHarvest[key];
            _baseHarvest[key] = (0, v.Stored, v.NextToastAt);
        }
    }
}
