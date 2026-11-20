// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The guardian giant's end (#3676) — once only. At zero hull it collapses (<see cref="GuardianGiantMove.Collapse"/>, its
/// blows stop, nothing shoots it any more); when the collapse is over its body lies where it fell — anchor = its root's cell
/// corner, facing = its heading rounded to a quarter turn, lights out, stamped only into air and plants on the ground there —
/// the boss is gone (<see cref="PlanetEnemyDefeated"/>), the record is <see cref="GuardianGiantRecord.Defeated"/> with the
/// corpse's pose and the time, and it never wakes again; the corpse's blocks are ordinary blocks.
/// <para><b>The loot.</b> Every fighter (who hit it within its fight range, or was hit by it — see
/// <c>GuardianGiantFight.Fighters</c>) still in the game gets an owner-locked crate (#3294's) beside the corpse: the data's
/// loot (<c>data/enemies.json</c>, a little more or less guardian plate by a seeded roll), the giant's core
/// (<see cref="GuardianGiantCoreItem"/>, when the content has it) and — learned at once, not as an item — one guardian
/// blueprint the player does not know yet; one who knows them all finds a finished guardian piece in the crate instead.
/// <see cref="GuardianGiantRecord.Rewarded"/> holds each player once. Each fighter also counts the achievement
/// (<c>defeat:guardian_giant</c>), the defeat, and the story event <see cref="GameEventKeys.GuardianGiantDefeated"/>.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The story's keepsake of a fallen giant — one in each fighter's crate when the content defines the item.</summary>
    internal const string GuardianGiantCoreItem = "guardian_giant_core";

    /// <summary>The achievement counter of a defeated guardian giant.</summary>
    internal const string GuardianGiantDefeatCounter = "defeat:guardian_giant";

    /// <summary>The guardian blueprints the loot teaches, one per crate.</summary>
    private static readonly string[] GuardianGiantBlueprints = { "guardian_gear", "guardian_sword", "guardian_drill" };

    /// <summary>The finished pieces a fighter who knows every guardian blueprint finds instead (one, seeded).</summary>
    private static readonly string[] GuardianGiantPieces = { "guardian_helmet", "guardian_chest", "guardian_legs", "guardian_boots", "guardian_sword", "guardian_drill" };

    /// <summary>The rings (blocks from the corpse's centre) the crates are placed on.</summary>
    private static readonly int[] GuardianGiantCrateRings = { 9, 11, 13, 15 };

    /// <summary>The collapse starts: it stops being a target, its pose settles on the ground where it stands (the corpse's
    /// anchor and facing go out in the status), and it falls at the move's impact.</summary>
    private void StartGuardianGiantCollapse(GuardianGiantFight f, double now)
    {
        var e = f.Entity!;
        e.Hostile = false; // sentries and hunter robots stop shooting
        f.Returning = false;
        int x = (int)System.Math.Round(e.Position.X), z = (int)System.Math.Round(e.Position.Z);
        int facing = (((int)System.Math.Round(e.Yaw / 90f) % 4) + 4) % 4;
        int feet = (int)System.Math.Floor(e.Position.Y);
        int best = int.MinValue;
        for (int dx = -1; dx <= 0; dx++)
        {
            for (int dz = -1; dz <= 0; dz++)
            {
                if (GuardianGiantFloor(x + dx, z + dz, feet, GuardianGiantRules.HopHeight + 4, out int floor))
                {
                    best = System.Math.Max(best, floor);
                }
            }
        }

        feet = best == int.MinValue ? feet : best;
        (f.CorpseX, f.CorpseY, f.CorpseZ, f.CorpseFacing) = (x, feet - GuardianGiantBody.LyingBottom - 1, z, facing);
        e.Position = GuardianGiantWrapped(x, feet, z);
        e.Yaw = facing * 90f;
        f.Moved = true;
        StartGuardianGiantMove(f, GuardianGiantMove.Collapse, e.Position, now);
    }

    /// <summary>The collapse is over: the corpse lies, the boss is gone, the record is defeated for good, every fighter gets
    /// their crate, VEGA cheers.</summary>
    private void FinishGuardianGiantDefeat(GuardianGiantWorld gw, GuardianGiantFight f)
    {
        var rec = f.Rec;
        var e = f.Entity!;
        StampGuardianGiantBody(f.CorpseX, f.CorpseY, f.CorpseZ, f.CorpseFacing, rec.Seed, lit: false, overwrite: false);
        _planetEnemies.Remove(e);
        BroadcastToWorld(new PlanetEnemyDefeated { Id = e.Id });
        BroadcastPlanetEnemies();
        rec.State = GuardianGiantRecord.Defeated;
        (rec.CorpseX, rec.CorpseY, rec.CorpseZ, rec.CorpseFacing) = (f.CorpseX, f.CorpseY, f.CorpseZ, f.CorpseFacing);
        rec.DefeatedAt = NowUnixSeconds;
        rec.Found = true;
        _repo.SaveMetadata(_meta);
        SendGuardianGiantStatus(f, SnapshotTime, over: true);
        gw.Fight = null;
        var at = GuardianGiantCentre(rec);
        RewardGuardianGiantFighters(f, at);
        foreach (var s in GuardianGiantAudience(at))
        {
            SendGuardianGiantVega(s, GuardianGiantVegaMoment.Victory);
        }

        BroadcastPlanetPois();
        _enemySpawnTimer = System.Math.Min(_enemySpawnTimer, -EnemyKillSpawnGrace); // a breather after the fight
        _log.Info($"The guardian giant '{rec.SiteId}' was defeated by {f.Fighters.Count} fighter(s); its body lies at {rec.CorpseX},{rec.CorpseY},{rec.CorpseZ}.");
    }

    /// <summary>A crate for every fighter still in the game who has none from this giant yet, plus their credit.</summary>
    private void RewardGuardianGiantFighters(GuardianGiantFight f, Vector3f centre)
    {
        foreach (string id in f.Fighters.OrderBy(id => id, System.StringComparer.Ordinal).ToList())
        {
            if (!f.Rec.Rewarded.Contains(id) && FindSessionByPlayerId(id) is { Joined: true } s)
            {
                RewardGuardianGiantFighter(f.Rec, s, centre);
            }
        }
    }

    /// <summary>One fighter's crate (beside the corpse; the backpack, else the ground, when there is no spot), the blueprint
    /// learned with it, the achievement, the defeat and the story event.</summary>
    private void RewardGuardianGiantFighter(GuardianGiantRecord rec, PlayerSession s, Vector3f centre)
    {
        var p = s.State;
        var items = GuardianGiantLootFor(rec, p, out string blueprint);
        void Settle()
        {
            rec.Rewarded.Add(p.PlayerId);
            if (blueprint.Length > 0)
            {
                p.UnlockedBlueprints.Add(blueprint);
            }

            _repo.SaveMetadata(_meta);
        }

        bool placed = TryFindGuardianGiantCrateSpot(centre, p.PlayerId, out var cell) && PlaceOwnerCrate(s, cell, items, Settle);
        if (!placed && !GiveOwnerCrateIntoBackpack(s, items, Settle))
        {
            _repo.RunInTransaction(() =>
            {
                Settle();
                _repo.SavePlayer(p);
            });
            SpillToGround(centre.ToBlock(), items.Select(i => new ItemAmount(i.Item, i.Count)).ToList());
        }

        if (blueprint.Length > 0 && _content.GetBlueprint(blueprint) is { } bp)
        {
            Send(s, new ServerMessage { Text = "@srv.giant.blueprint:" + LocalizedName(s.Locale, bp.NameKey, bp.Key) });
            SendInventory(s); // the blueprint list rides the inventory
        }

        Advance(s, GuardianGiantDefeatCounter);
        OnAchievementDefeat(s);
        RaiseGameEvent(s, GameEventKeys.DefeatAny, GameEventKeys.DefeatGiant);
        RaiseGameEvent(s, GameEventKeys.GuardianGiantDefeated, rec.SiteId);
    }

    /// <summary>One fighter's loot: the data's loot (guardian plate ±2 by a roll per giant and player), the core, and the
    /// blueprint to learn (<paramref name="blueprint"/>, empty when they know them all — then a finished piece instead).</summary>
    private List<ItemStack> GuardianGiantLootFor(GuardianGiantRecord rec, PlayerState p, out string blueprint)
    {
        var rng = new System.Random(StableHashing.Seed32("guardian-giant-loot:" + rec.SiteId + ":" + p.PlayerId, rec.Seed));
        var items = new List<ItemStack>();
        foreach (var loot in _content.Enemy(GuardianGiantRules.EnemyKey).Tier(EnemyTiers.Normal).Loot)
        {
            int count = loot.Item == EnemyCatalog.GuardianPlate ? System.Math.Max(1, loot.Count + rng.Next(-2, 3)) : loot.Count;
            items.Add(new ItemStack(loot.Item, count));
        }

        var missing = GuardianGiantBlueprints.Where(k => _content.GetBlueprint(k) is not null && !p.UnlockedBlueprints.Contains(k)).ToList();
        blueprint = missing.Count > 0 ? missing[rng.Next(missing.Count)] : string.Empty;
        if (blueprint.Length == 0)
        {
            string piece = GuardianGiantPieces[rng.Next(GuardianGiantPieces.Length)];
            if (_content.GetItem(piece) is not null)
            {
                items.Add(new ItemStack(piece, 1));
            }
        }

        if (_content.GetItem(GuardianGiantCoreItem) is not null)
        {
            items.Add(new ItemStack(GuardianGiantCoreItem, 1));
        }

        return items;
    }

    /// <summary>A free crate spot with ground on rings around the corpse's centre (outside the body), sixteen bearings each.</summary>
    private bool TryFindGuardianGiantCrateSpot(Vector3f centre, string ownerId, out Vector3i cell)
    {
        cell = default;
        int refY = (int)System.Math.Floor(centre.Y) + 2;
        int circ = _world.Circumference;
        foreach (int ring in GuardianGiantCrateRings)
        {
            for (int k = 0; k < 16; k++)
            {
                double a = k * System.Math.PI / 8.0;
                int cx = (int)System.Math.Floor(WorldConstants.WrapX(centre.X + (System.Math.Sin(a) * ring), circ));
                int cz = (int)System.Math.Floor(WorldConstants.WrapZ(centre.Z + (System.Math.Cos(a) * ring), circ));
                _world.GetBlock(new Vector3i(cx, refY, cz)); // the column's chunk is in (the fight was here anyway)
                if (TryGroundFeetYAt(cx, cz, refY, 1, 8, out int y, canopyCarries: false) && OwnerCrateSpotFree(new Vector3i(cx, y, cz), ownerId))
                {
                    cell = new Vector3i(cx, y, cz);
                    return true;
                }
            }
        }

        return false;
    }
}
