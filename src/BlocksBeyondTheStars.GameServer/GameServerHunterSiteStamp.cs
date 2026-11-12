// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3097: the landing of a hunter shuttle's camp on the ground — the spot search near the planned spot (dry, fairly flat, out
/// of every base zone, village and landing pad), the one-time stamp of the hull and its robot docks from existing blocks with
/// a <c>BlockChanged</c> for every changed cell, and the test seams. See GameServerHunterSites.cs for the camp itself.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Stamps a pending camp on the active world (someone is on the body): the spot, the blocks, the record turns
    /// active, VEGA tells the players here and the map marker moves onto the hull. A body with no usable ground near the plan
    /// (or a gas giant) gets no camp — the record goes.</summary>
    private void MaterializeHunterSite(HunterShuttleSiteRecord rec, List<PlayerSession> present)
    {
        string body = _world.LocationId;
        var rng = new System.Random(StableHashing.Seed32("huntersite:" + body, rec.Seed));
        if (GasSeaTopY() != int.MinValue || !TryPickHunterSiteSpot(rec, rng, out int ox, out int oz))
        {
            _meta.HunterShuttleSites.Remove(body);
            _repo.SaveMetadata(_meta);
            _log.Info($"The hunter shuttle bound for '{body}' found no ground to land on near {rec.X},{rec.Z}.");
            return;
        }

        int floorY = HunterGroundFeet(ox + HunterShuttleHull.Width / 2, oz + HunterShuttleHull.Length / 2);
        _repo.RunInTransaction(() => StampHunterSite(rec, rng, ox, floorY, oz));
        rec.State = HunterShuttleSiteRecord.Active;
        rec.X = ox;
        rec.Y = floorY;
        rec.Z = oz;
        _repo.SaveMetadata(_meta);
        foreach (var s in present)
        {
            SendVegaLine(s, "vega.hunter_site.landed_here", 2);
        }

        BroadcastPlanetPois();
        _log.Info($"A hunter shuttle camp landed on '{body}' at {ox},{floorY},{oz} with {rec.Spawners.Count} robot docks.");
    }

    /// <summary>The flattest dry spot on rings of <see cref="HunterSiteSearchStep"/> around the plan, out to
    /// <see cref="HunterSiteSearchRings"/> rings (a plan that fell into a lake moves to its shore) — the generator's surface,
    /// no chunk is loaded for the search (at most 73 spots of 8 probes, once per camp). Out: the hull's origin (its −X/−Z
    /// corner).</summary>
    private bool TryPickHunterSiteSpot(HunterShuttleSiteRecord rec, System.Random rng, out int ox, out int oz)
    {
        int best = int.MaxValue;
        ox = oz = 0;
        for (int ring = 0; ring <= HunterSiteSearchRings; ring++)
        {
            int tries = ring == 0 ? 1 : 12;
            double turn = rng.NextDouble() * System.Math.PI * 2.0;
            for (int k = 0; k < tries; k++)
            {
                double a = turn + k * System.Math.PI / 6.0;
                int cx = rec.X + (int)System.Math.Round(System.Math.Cos(a) * ring * HunterSiteSearchStep);
                int cz = rec.Z + (int)System.Math.Round(System.Math.Sin(a) * ring * HunterSiteSearchStep);
                int spread = HunterSpotSpread(cx, cz);
                if (spread >= 0 && spread < best)
                {
                    best = spread;
                    ox = cx - HunterShuttleHull.Width / 2;
                    oz = cz - HunterShuttleHull.Length / 2;
                }
            }

            if (best <= 2)
            {
                break; // flat enough — no need to wander further from the plan
            }
        }

        return best <= HunterSiteMaxSpread;
    }

    /// <summary>How much the ground rises across a camp centred here, or -1 when any probe is wet, inside a base zone or a
    /// village, or on a landing pad.</summary>
    private int HunterSpotSpread(int cx, int cz)
    {
        var planet = _world.Planet;
        int circ = _world.Circumference;
        int min = int.MaxValue, max = int.MinValue;
        foreach (var (dx, dz) in HunterSpotProbes)
        {
            int x = WorldConstants.WrapX(cx + dx, circ);
            int z = WorldConstants.WrapZ(cz + dz, circ);
            int h = _generator.SurfaceHeight(planet, x, z);
            if (_generator.TryGetWaterSurface(planet, x, z, out int top, out _) && top >= h + 1)
            {
                return -1;
            }

            var cell = new Vector3i(x, h + 1, z);
            if (InAnyBaseZone(cell) || IsSettlementBlock(cell))
            {
                return -1;
            }

            min = System.Math.Min(min, h);
            max = System.Math.Max(max, h);
        }

        foreach (var pad in _landingPads)
        {
            float dx = (float)WorldConstants.WrapDeltaX(pad.CenterX - cx, circ);
            float dz = (float)WorldConstants.WrapDeltaZ(pad.CenterZ - cz, circ);
            float keep = LandingPadRadius + 14f;
            if (dx * dx + dz * dz < keep * keep)
            {
                return -1;
            }
        }

        return max - min;
    }

    /// <summary>The one-time stamp (#3522: the geometry is <see cref="HunterCampBuilder"/>'s): the hull box (hull cells, air
    /// everywhere else in it — the hill, the grass, a trunk), the ground filled under the deck where it dips (at most four
    /// deep), the shuttle's own dock in the hold and one or two more on a steel plate beside it, on the real ground of their
    /// columns and only where it is dry. Records the docks.</summary>
    private void StampHunterSite(HunterShuttleSiteRecord rec, System.Random rng, int ox, int fy, int oz)
    {
        var planet = _world.Planet;
        string surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
        rec.Spawners.Clear();
        HunterCampBuilder.Build(new HunterCampSink(this, rec), _content, surface, ox, fy, oz, rng, HunterGroundFeet,
            (x, z, feet) => MachineWaterDepth(x, z, feet) <= 0 && WithinBuildHeight(feet + 1));
    }

    /// <summary>The hunter camp's sink: every cell through <see cref="StampHunterCell"/> (inside the build height, no air
    /// written over air, a <c>BlockChanged</c> for the clients — the camp carries no glow), reads from the world, and every
    /// dock marker becomes one of the record's docks.</summary>
    private sealed class HunterCampSink : IStructureCellSink
    {
        private readonly GameServer _server;
        private readonly HunterShuttleSiteRecord _rec;

        public HunterCampSink(GameServer server, HunterShuttleSiteRecord rec)
        {
            _server = server;
            _rec = rec;
        }

        public void Set(int x, int y, int z, BlockId block, int tint, int glow, int shape)
            => _server.StampHunterCell(new Vector3i(x, y, z), block, tint, shape);

        public BlockId Get(int x, int y, int z) => _server._world.GetBlock(new Vector3i(x, y, z));

        public void Mark(string type, int x, int y, int z, string data)
        {
            if (type == HunterCampBuilder.DockMarker)
            {
                _rec.Spawners.Add(new HunterShuttleSiteCell { X = x, Y = y, Z = z });
            }
        }
    }

    /// <summary>Writes one cell (a server stamp — no owner) and tells the world's clients when it changed.</summary>
    private void StampHunterCell(Vector3i cell, BlockId id, int tint, int shape)
    {
        if (!WithinBuildHeight(cell.Y))
        {
            return;
        }

        var before = _world.GetBlock(cell);
        if (id.IsAir && before.IsAir)
        {
            return;
        }

        _world.SetBlock(cell, id, tint, 0, shape);
        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        BroadcastToWorld(new BlockChanged { X = c.X, Y = c.Y, Z = c.Z, Block = id.Value, Tint = tint, Shape = shape });
    }

    /// <summary>The feet cell on the real ground of a column (loads its chunk): the first block from above that carries a
    /// body — trees, leaves and walk-through plants passed.</summary>
    private int HunterGroundFeet(int x, int z)
    {
        int top = _generator.SurfaceHeight(_world.Planet, WorldConstants.WrapX(x, _world.Circumference), WorldConstants.WrapZ(z, _world.Circumference)) + 12;
        var vegetation = SettlementVegetationIds;
        for (int y = top; y > top - 40; y--)
        {
            var id = _world.GetBlock(new Vector3i(x, y, z));
            if (id.IsAir || vegetation.Contains(id.Value) || !IsCollidingBlock(id, fluidsPass: false, foliagePasses: true))
            {
                continue;
            }

            return y + 1;
        }

        return top - 11;
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/util: the spot search's ground spread for a camp centred here (-1 = unusable).</summary>
    public int HunterSpotSpreadForTest(int cx, int cz) => HunterSpotSpread(cx, cz);

    /// <summary>Test/util: the camp record of a body (null = none).</summary>
    public HunterShuttleSiteRecord? HunterSiteForTest(string bodyId)
        => _meta.HunterShuttleSites.TryGetValue(bodyId, out var rec) ? rec : null;

    /// <summary>Test/util: plans a camp on the active world at a spot (bypassing the shuttle and the distance roll) — false
    /// when <see cref="RegisterHunterSite"/> would refuse (Peaceful, an open camp there, too many open).</summary>
    public bool PlanHunterSiteForTest(int x, int z)
    {
        if (!RegisterHunterSite(_world.LocationId, null))
        {
            return false;
        }

        var rec = _meta.HunterShuttleSites[_world.LocationId];
        rec.X = x;
        rec.Z = z;
        return true;
    }

    /// <summary>Test/util: runs the active world's camp pass now, as if <paramref name="seconds"/> went by.</summary>
    public void TickHunterSitesForTest(double seconds)
    {
        if (_meta.HunterShuttleSites.TryGetValue(_world.LocationId, out var rec) && rec.State != HunterShuttleSiteRecord.Cleared)
        {
            RunHunterSitePass(rec, HunterSiteRuntimeFor(_world.LocationId), seconds);
        }
    }

    /// <summary>Test/util: the ids of the machines the active camp's docks released and that are still about.</summary>
    public IReadOnlyList<string> HunterDockRobotsForTest()
    {
        var ids = new List<string>();
        if (_hunterSiteRuntime.TryGetValue(_world.LocationId, out var rt))
        {
            foreach (var set in rt.DockRobots)
            {
                foreach (var id in set)
                {
                    if (_planetEnemies.Any(e => e.Id == id))
                    {
                        ids.Add(id);
                    }
                }
            }
        }

        return ids;
    }

    /// <summary>Test/util: the id of the active camp's live guard ("" = none).</summary>
    public string HunterGuardIdForTest()
        => _hunterSiteRuntime.TryGetValue(_world.LocationId, out var rt) ? rt.GuardId : string.Empty;

    /// <summary>Test/util: takes a planet machine of the active world away, as a kill would.</summary>
    public bool RemovePlanetMachineForTest(string id)
    {
        int removed = _planetEnemies.RemoveAll(e => e.Id == id);
        ForgetMachine(id);
        return removed > 0;
    }
}
