// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The guardian giants' admin tools (#3676, playtests): <c>/giant guardian</c> (the existing <c>summon_giant</c> verb, behind
/// the cheat rule) lays a giant that CAN wake about thirty blocks in front of the admin on fairly flat, dry ground — its feet
/// toward the admin — with its record (an index from <see cref="GuardianGiantAdminIndex"/> up, so it never collides with the
/// structure stamp's); <c>/tp giant [n]</c> stands the admin twelve to sixteen blocks from one of this body's giants (lying,
/// awake or defeated — whether it can wake is never said), on the side of its feet (in front of an awake one), never inside
/// the body. And the test seams of the giant runtime.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The first record index of an admin-laid giant on a body.</summary>
    internal const int GuardianGiantAdminIndex = 1000;

    /// <summary>The distances (blocks ahead of the admin) the stamp tries, nearest the asked thirty first.</summary>
    private static readonly int[] GuardianGiantAdminDistances = { 30, 26, 34, 22, 38 };

    /// <summary>The distances from the giant a teleport tries, the middle first.</summary>
    private static readonly int[] GuardianGiantTeleportDistances = { 14, 13, 15, 12, 16 };

    /// <summary>The words of <c>/giant</c> that mean a guardian giant rather than this world's colossus or sandworm.</summary>
    private static bool IsGuardianGiantWord(string? word)
        => (word ?? string.Empty).Trim().ToLowerInvariant() is "guardian" or "guardian_giant" or "guardiangiant" or "wächter"
            or "waechter" or "gigant" or "wächtergigant" or "waechtergigant";

    /// <summary><c>/giant guardian</c>: lays a wakeable giant in front of the admin (see the class summary).</summary>
    private void AdminGuardianGiant(PlayerSession session)
    {
        var p = session.State;
        if (_world.Planet.Void || InSpace(p.PlayerId) || !TryLayAdminGuardianGiant(p, out var rec))
        {
            Reject(session, "admin", "@srv.giant.no_spot");
            return;
        }

        Send(session, new ServerMessage { Text = "@srv.giant.laid" });
        CheatLog(p, $"laid the guardian giant {rec.SiteId} at {rec.X},{rec.Y},{rec.Z}");
    }

    /// <summary>Finds the spot, stamps the whole body (lights out) and writes the record — dormant, able to wake.</summary>
    private bool TryLayAdminGuardianGiant(PlayerState p, out GuardianGiantRecord rec)
    {
        rec = null!;
        double yaw = p.Yaw * System.Math.PI / 180.0;
        float fx = (float)System.Math.Sin(yaw), fz = (float)System.Math.Cos(yaw);
        // The head points away from the admin: the lying head lies toward local −Z, so the facing turns +Z toward the admin.
        int facing = (((int)System.Math.Round(System.Math.Atan2(-fx, -fz) * 2.0 / System.Math.PI) % 4) + 4) % 4;
        foreach (int dist in GuardianGiantAdminDistances)
        {
            int ax = WorldConstants.WrapX((int)System.Math.Floor(p.Position.X + (fx * dist)), _world.Circumference);
            int az = WorldConstants.WrapZ((int)System.Math.Floor(p.Position.Z + (fz * dist)), _world.Circumference);
            if (GuardianGiantSpotGround(ax, az, facing) is { } groundTop)
            {
                rec = LayGuardianGiant(ax, groundTop - GuardianGiantBody.LyingBottom, az, facing, canWake: true);
                return true;
            }
        }

        return false;
    }

    /// <summary>The ground top at the anchor when the whole footprint is fairly flat (two rows at most), dry and outside every
    /// base zone and village; null otherwise. The generator's surface — nothing is loaded for the search.</summary>
    private int? GuardianGiantSpotGround(int ax, int az, int facing)
    {
        var (minX, minZ, maxX, maxZ) = GuardianGiantBody.LyingFootprint(facing);
        int lo = int.MaxValue, hi = int.MinValue, anchor = 0;
        foreach (var (dx, dz) in new[] { (0, 0), (minX, minZ), (maxX, minZ), (minX, maxZ), (maxX, maxZ), ((minX + maxX) / 2, (minZ + maxZ) / 2) })
        {
            int x = WorldConstants.WrapX(ax + dx, _world.Circumference), z = WorldConstants.WrapZ(az + dz, _world.Circumference);
            int h = _generator.SurfaceHeight(_world.Planet, x, z);
            if ((_generator.TryGetWaterSurface(_world.Planet, x, z, out int top, out _) && top >= h + 1)
                || InAnyBaseZone(new Vector3i(x, h + 1, z)) || IsSettlementBlock(new Vector3i(x, h + 1, z)))
            {
                return null;
            }

            anchor = dx == 0 && dz == 0 ? h : anchor;
            lo = System.Math.Min(lo, h);
            hi = System.Math.Max(hi, h);
        }

        return hi - lo <= 2 ? anchor : null;
    }

    /// <summary>Stamps a whole lying giant (lights out) at an anchor and writes its dormant record under the next free admin
    /// index of this body.</summary>
    private GuardianGiantRecord LayGuardianGiant(int ax, int ay, int az, int facing, bool canWake)
    {
        string loc = _world.LocationId;
        int index = GuardianGiantAdminIndex;
        while (_meta.GuardianGiants.ContainsKey(GuardianGiantRecord.Key(loc, index)))
        {
            index++;
        }

        var rec = new GuardianGiantRecord
        {
            LocationId = loc,
            Index = index,
            X = ax,
            Y = ay,
            Z = az,
            Facing = facing,
            Seed = StableHashing.Seed32("guardian-giant-admin:" + loc + ":" + index, _meta.Seed),
            CanWake = canWake,
            State = GuardianGiantRecord.Dormant,
            CorpseX = ax,
            CorpseY = ay,
            CorpseZ = az,
            CorpseFacing = facing,
        };
        StampGuardianGiantBody(ax, ay, az, facing, rec.Seed, lit: false, overwrite: true);
        _meta.GuardianGiants[rec.SiteId] = rec;
        _repo.SaveMetadata(_meta);
        return rec;
    }

    // ---------------------------------------------------------------- /tp giant

    /// <summary><c>/tp giant [n]</c>: every giant of the current body, by index, named with its state in the player's language.</summary>
    private void AppendGuardianGiantTargets(List<TeleportTarget> list, PlayerSession session)
    {
        int n = 0;
        foreach (var rec in GuardianGiantsHere().OrderBy(r => r.Index).ToList())
        {
            n++;
            string state = Localize(session.Locale, "srv.tp.giant_" + GuardianGiantPoiState(rec));
            string label = Localize(session.Locale, "srv.tp.giant_label")
                .Replace("{n}", n.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Replace("{state}", state);
            list.Add(new TeleportTarget("giant", n, Named("giant", n, label), GuardianGiantTeleportSpot(rec)));
        }
    }

    /// <summary>A standing spot 12–16 blocks from the giant: past the lying body's feet (the corpse's for a defeated one), in
    /// front of an awake one — never on one of the body's cells.</summary>
    private Vector3f GuardianGiantTeleportSpot(GuardianGiantRecord rec)
    {
        _guardianGiantWorlds.TryGetValue(_world.LocationId, out var gw);
        var from = GuardianGiantCentre(rec, gw);
        bool dead = rec.State == GuardianGiantRecord.Defeated;
        float yaw = gw?.Fight is { Entity: { } e } f && ReferenceEquals(f.Rec, rec) ? e.Yaw : (dead ? rec.CorpseFacing : rec.Facing) * 90f;
        double a = yaw * System.Math.PI / 180.0;
        var spot = from;
        foreach (int dist in GuardianGiantTeleportDistances)
        {
            int x = WorldConstants.WrapX((int)System.Math.Floor(from.X + (System.Math.Sin(a) * dist)), _world.Circumference);
            int z = WorldConstants.WrapZ((int)System.Math.Floor(from.Z + (System.Math.Cos(a) * dist)), _world.Circumference);
            spot = StandSpot(x, z);
            var feet = spot.ToBlock();
            bool inBody = dead
                ? GuardianGiantBodyHolds(rec.CorpseX, rec.CorpseY, rec.CorpseZ, rec.CorpseFacing, feet)
                : GuardianGiantBodyHolds(rec.X, rec.Y, rec.Z, rec.Facing, feet);
            if (!inBody)
            {
                break;
            }
        }

        return spot;
    }

    // ---------------------------------------------------------------- test seams

    /// <summary>Test seam: lays a whole giant with its record at column (x, z) on the real ground there (its feet end at the
    /// anchor), dormant; <paramref name="canWake"/> as given.</summary>
    internal GuardianGiantRecord LayGuardianGiantForTest(int x, int z, int facing, bool canWake = true)
    {
        int feet = HunterGroundFeet(x, z);
        return LayGuardianGiant(x, feet - GuardianGiantBody.LyingBottom - 1, z, facing, canWake);
    }

    /// <summary>Test seam: the feet cell on the real ground of a column (the hunter camp's probe).</summary>
    internal int GroundFeetForTest(int x, int z) => HunterGroundFeet(x, z);

    /// <summary>Test seam: advances the server clock and runs the active world's giant tick, <paramref name="step"/> at a time.</summary>
    internal void TickGuardianGiantsForTest(double seconds, double step = 1.0 / 15.0)
    {
        for (double t = 0; t < seconds - 1e-9; t += step)
        {
            _uptime += step;
            TickGuardianGiants(step);
        }
    }

    /// <summary>Test seam: the awake giant entity of the active world (null when none stands).</summary>
    internal CombatEntity? GuardianGiantEntityForTest()
        => _guardianGiantWorlds.TryGetValue(_world.LocationId, out var gw) ? gw.Fight?.Entity : null;

    /// <summary>Test seam: the awake giant's current move (None when none stands).</summary>
    internal GuardianGiantMove GuardianGiantMoveForTest()
        => _guardianGiantWorlds.TryGetValue(_world.LocationId, out var gw) && gw.Fight is { } f ? f.Move : GuardianGiantMove.None;

    /// <summary>Test seam: the awake giant's fighters so far.</summary>
    internal IReadOnlyCollection<string> GuardianGiantFightersForTest()
        => _guardianGiantWorlds.TryGetValue(_world.LocationId, out var gw) && gw.Fight is { } f
            ? f.Fighters.ToList()
            : new List<string>();

    /// <summary>Test seam: starts a move of the awake giant now (an attack with its aim), as the AI would.</summary>
    internal void StartGuardianGiantMoveForTest(GuardianGiantMove move, Vector3f aim)
    {
        if (_guardianGiantWorlds.TryGetValue(_world.LocationId, out var gw) && gw.Fight is { Entity: not null } f)
        {
            StartGuardianGiantMove(f, move, aim, SnapshotTime);
        }
    }

    /// <summary>Test seam: takes hull off the awake giant through the damage hook, as a blow by <paramref name="attackerId"/>.</summary>
    internal void DamageGuardianGiantForTest(float amount, string attackerId)
    {
        if (GuardianGiantEntityForTest() is { } e)
        {
            e.Hull -= amount;
            OnGuardianGiantDamaged(e, attackerId);
        }
    }

    /// <summary>Test seam: forgets the giant runtime of the active world and its live giant, as a server restart does (the
    /// records and the blocks stay).</summary>
    internal void ForgetGuardianGiantRuntimeForTest()
    {
        if (GuardianGiantEntityForTest() is { } e)
        {
            _planetEnemies.Remove(e);
        }

        _guardianGiantWorlds.Remove(_world.LocationId);
    }
}
