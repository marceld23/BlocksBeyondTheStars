// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>What the Resonance Blaster's resonate did (#3353).</summary>
internal enum ResonanceOutcome
{
    /// <summary>The aim was no rune stone of a monument — the blaster pushes instead.</summary>
    NotARuneStone,

    /// <summary>The monument's sound chamber opened.</summary>
    Opened,

    /// <summary>A monument's rune stone, but no chamber opened (open already, or no room under it): a word, nothing charged.</summary>
    Refused,
}

/// <summary>
/// The sound chambers under the monuments (#3353): the Resonance Blaster used on a rune stone of a monument makes it ring,
/// and the ground under the monument opens into a small chamber — once per monument, recorded per body with the world's
/// stamped features (<c>soundchamber:x:y:z</c>), so it never opens twice and survives every reload. Nothing about world
/// generation changes: the server carves the chamber into the live world as ordinary block edits (deltas), which is why it
/// works on every old world too.
/// <para>The chamber: a 5 × 3 × 5 room whose floor lies <see cref="ChamberDepth"/> blocks under the monument's ground, two
/// resonance crystals that light it, a stairway up to the surface along the monument's narrow side, and a relic cache
/// (<c>loot_soundchamber_relic_cache_…</c>) with the usual relic finds. The first chamber a player with the Sounders story
/// running opens holds the Great Song disc, the second the Voices disc (<see cref="SongDiscs.ChamberOrder"/>) — locked to that
/// player until they took it. The carve never touches a ship, a settlement, a station or someone's base, and never digs next
/// to water or lava (a chamber that would flood is refused). The opening raises <see cref="GameEventKeys.ChamberOpened"/> and
/// sounds <c>chamber_open</c>.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The structure kind of a chamber's relic cache (its loot id <c>loot_soundchamber_relic_cache_x_y_z</c>).</summary>
    internal const string ChamberStructureKind = "soundchamber";

    /// <summary>How far under the monument's ground the chamber floor (its lowest air) lies.</summary>
    internal const int ChamberDepth = 7;

    /// <summary>Half the chamber's width and length (5 × 5).</summary>
    private const int ChamberHalf = 2;

    /// <summary>The chamber's clear height.</summary>
    private const int ChamberHeight = 3;

    /// <summary>The most steps the stairway climbs before it must have reached the open air.</summary>
    private const int ChamberMaxSteps = 14;

    /// <summary>The stamped-feature key of a monument's chamber.</summary>
    private static string ChamberFeature(MonumentInstance m) => $"soundchamber:{m.Min.X}:{m.Min.Y}:{m.Min.Z}";

    /// <summary>Whether the chamber of <paramref name="m"/> was opened already.</summary>
    internal bool ChamberOpened(MonumentInstance m) => FeatureStamped(ChamberFeature(m));

    /// <summary>The monument whose rune stone sits at <paramref name="cell"/>, or null (also null for any other block).</summary>
    internal MonumentInstance? MonumentRuneAt(Vector3i cell)
    {
        var canonical = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        if (_world.Definition(_world.GetBlock(canonical))?.Key != "rune_stone")
        {
            return null;
        }

        foreach (var m in _monuments)
        {
            if (WrapSpan(canonical.X - m.Min.X) <= m.Max.X - m.Min.X && WrapSpan(canonical.Z - m.Min.Z) <= m.Max.Z - m.Min.Z
                && canonical.Y >= m.Min.Y - 1 && canonical.Y <= m.Max.Y)
            {
                return m;
            }
        }

        return null;
    }

    /// <summary>A horizontal offset brought into [0, circumference) — a monument may straddle the seam.</summary>
    private int WrapSpan(int delta)
    {
        int circ = _world.Circumference;
        return ((delta % circ) + circ) % circ;
    }

    /// <summary>The blaster's resonate on <paramref name="cell"/>: opens the monument's chamber once.</summary>
    internal ResonanceOutcome TryResonate(PlayerSession session, Vector3i cell)
    {
        if (MonumentRuneAt(cell) is not { } m)
        {
            return ResonanceOutcome.NotARuneStone;
        }

        if (ChamberOpened(m))
        {
            Send(session, new ServerMessage { Text = "@srv.chamber.already_open" });
            return ResonanceOutcome.Refused;
        }

        var plan = PlanChamber(session, m);
        if (plan is null)
        {
            Send(session, new ServerMessage { Text = "@srv.chamber.no_room" });
            return ResonanceOutcome.Refused;
        }

        OpenChamber(session, m, plan);
        return ResonanceOutcome.Opened;
    }

    /// <summary>The cells a chamber carves and where its lights and cache go.</summary>
    private sealed class ChamberPlan
    {
        public List<Vector3i> Carve { get; } = new();
        public List<Vector3i> Lights { get; } = new();
        public Vector3i Cache { get; set; }
    }

    /// <summary>Plans the chamber under <paramref name="m"/>, or null when it may not be dug there (protected cells, an
    /// indestructible block, water or lava beside it, no way up within <see cref="ChamberMaxSteps"/>).</summary>
    private ChamberPlan? PlanChamber(PlayerSession session, MonumentInstance m)
    {
        int cx = (int)System.Math.Floor(m.Center.X), cz = (int)System.Math.Floor(m.Center.Z);
        int floor = m.Min.Y - ChamberDepth;
        var plan = new ChamberPlan { Cache = Canon(cx, floor, cz) };
        for (int dx = -ChamberHalf; dx <= ChamberHalf; dx++)
        {
            for (int dz = -ChamberHalf; dz <= ChamberHalf; dz++)
            {
                for (int dy = 0; dy < ChamberHeight; dy++)
                {
                    plan.Carve.Add(Canon(cx + dx, floor + dy, cz + dz));
                }
            }
        }

        plan.Lights.Add(Canon(cx - ChamberHalf, floor, cz - ChamberHalf));
        plan.Lights.Add(Canon(cx - ChamberHalf, floor, cz + ChamberHalf));
        if (!PlanStairs(m, cx, cz, floor, plan))
        {
            return null;
        }

        return plan.Carve.All(c => ChamberCellDiggable(session, c)) ? plan : null;
    }

    /// <summary>The stairway: out of the room along the monument's narrow side (+X when it is no wider than long, else +Z),
    /// one step up per block, three cells of head room, until the step stands in the open air.</summary>
    private bool PlanStairs(MonumentInstance m, int cx, int cz, int floor, ChamberPlan plan)
    {
        bool alongX = m.Max.X - m.Min.X <= m.Max.Z - m.Min.Z;
        for (int i = 0; i < ChamberMaxSteps; i++)
        {
            int along = ChamberHalf + 1 + i;
            int x = alongX ? cx + along : cx, z = alongX ? cz : cz + along;
            int stepFloor = floor + 1 + i; // the lowest air cell of this step
            bool open = true;
            for (int dy = 0; dy < ChamberHeight; dy++)
            {
                var c = Canon(x, stepFloor + dy, z);
                open &= _world.GetBlock(c).IsAir;
                plan.Carve.Add(c);
            }

            if (open && stepFloor >= m.Min.Y - 1)
            {
                return true; // the step stands in the open air at the surface
            }
        }

        return false;
    }

    private Vector3i Canon(int x, int y, int z) => WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference);

    /// <summary>Whether the chamber may dig this cell: air, or a mineable block nobody protects, with no water or lava in it
    /// or beside it (a chamber must never flood).</summary>
    private bool ChamberCellDiggable(PlayerSession session, Vector3i c)
    {
        if (c.Y < 2 || HasFluidNeighbor(c) || IsFluid(_world.GetBlock(c).Value))
        {
            return false;
        }

        var b = _world.GetBlock(c);
        if (b.IsAir)
        {
            return true;
        }

        return _world.Definition(b) is { Mineable: true }
               && !IsShipBlock(c) && !IsSettlementBlock(c) && !IsStationBlock(c)
               && !IsBaseProtected(c, session.State.PlayerId, session.State.IsAdmin) && !BasePadBusy(c) && !IsLockedCrateAt(c);
    }

    /// <summary>Carves the planned chamber (one transaction of block deltas), lights it, fills its cache and tells the story.</summary>
    private void OpenChamber(PlayerSession session, MonumentInstance m, ChamberPlan plan)
    {
        _repo.RunInTransaction(() =>
        {
            foreach (var c in plan.Carve)
            {
                if (!_world.GetBlock(c).IsAir)
                {
                    BlastCell(session, c); // the terrain blaster's clear: registrations forgotten, a crate spilled, sand falls
                }
            }

            var light = (_content.GetBlock("resonance_crystal") ?? _content.GetBlock("crystal"))?.NumericId;
            foreach (var c in plan.Lights)
            {
                if (light is { } id)
                {
                    _world.SetBlock(c, id);
                    BroadcastToWorld(new BlockChanged { X = c.X, Y = c.Y, Z = c.Z, Block = id.Value });
                }
            }

            MarkFeatureStamped(ChamberFeature(m));
        });

        FillChamberCache(session, plan.Cache);
        var at = m.Center;
        BroadcastToWorld(new SoundFx { SoundId = "chamber_open", X = at.X, Y = at.Y, Z = at.Z });
        Send(session, new ServerMessage { Text = "@srv.chamber.opened" });
        RaiseGameEvent(session, GameEventKeys.ChamberOpened);
    }

    /// <summary>The chamber's relic cache: the usual relic finds, and — for a player the Sounders story runs for — the next
    /// chamber disc, the cache locked to them until they took it.</summary>
    private void FillChamberCache(PlayerSession session, Vector3i cell)
    {
        var pos = new Vector3f(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        long h = _meta.Seed ^ WorldGenerator.StableHash($"soundchamber:{_world.LocationId}:{cell.X}:{cell.Y}:{cell.Z}");
        SpawnStructureLoot(ChamberStructureKind, "relic_cache", pos, new System.Random(unchecked((int)(h ^ (h >> 32)))));
        string id = "loot_" + $"{ChamberStructureKind}:relic_cache:{cell.X}:{cell.Y}:{cell.Z}".Replace(':', '_');
        var p = session.State;
        if (!SoundersActive(session) || NextMissingDisc(p, SongDiscs.ChamberOrder) is not { } disc)
        {
            return;
        }

        var cache = _containers.FirstOrDefault(c => c.Id == id);
        if (cache is null)
        {
            cache = new StoredContainer { Id = id, Planet = _world.LocationId, Kind = "salvage", Position = cell };
            _containers.Add(cache);
        }

        cache.Items.Add(new ItemStack(disc, 1));
        cache.Owner = p.PlayerId;
        cache.OwnerName = p.Name;
        p.Milestones.Add(SongDiscs.GotMilestone(disc));
        _repo.SaveContainer(cache);
        BroadcastContainers();
    }

    /// <summary>Test seam: the monuments of the active world.</summary>
    internal IReadOnlyList<MonumentInstance> MonumentInstancesForTest => _monuments;

    /// <summary>Test seam: registers a monument occupying <paramref name="min"/>..<paramref name="max"/> (no blocks written).</summary>
    internal void AddMonumentForTest(Vector3i min, Vector3i max)
        => _monuments.Add(new MonumentInstance
        {
            Min = min,
            Max = max,
            Center = new Vector3f((min.X + max.X) / 2f, min.Y + 1, (min.Z + max.Z) / 2f),
            Archetype = "test",
        });
}
