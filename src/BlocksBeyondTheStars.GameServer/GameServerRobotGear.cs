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

/// <summary>#3398/#3399: the robot's gear on its live entity — copies of its record's (<see cref="TamedCreature.GearWeapon"/>,
/// <see cref="TamedCreature.GearJetpack"/>) plus the flight it is in (server-only, never persisted).</summary>
public sealed partial class CombatEntity
{
    /// <summary>The weapon the robot fights with (an item key; empty = none).</summary>
    public string RobotWeapon { get; set; } = string.Empty;

    /// <summary>The robot wears a jetpack.</summary>
    public bool RobotJetpack { get; set; }

    /// <summary>The robot flies on its jetpack right now.</summary>
    public bool RobotFlying { get; set; }

    /// <summary>The robot landed beside its owner after a flight and holds still there (a companion's idle wander would walk it
    /// off the ledge it flew up to) until its owner walks away.</summary>
    public bool RobotPerched { get; set; }
}

/// <summary>
/// The hunter robot's gear (#3398/#3399, Justus' "Robot upgrade": "a jetpack or weapons that it uses — just NO armour!").
/// <list type="bullet">
/// <item><b>Give</b> — E at the own robot while a weapon or a jetpack is held (<see cref="GiveCompanionGearIntent"/>): one of
/// the item leaves the hotbar slot and the robot carries it; one it already had comes back (refused while there is no room).
/// Only the sender's own robot, switched on, on this world, within reach and not in space takes it.</item>
/// <item><b>Take back</b> — the Companions tab (<see cref="TakeCompanionGearIntent"/>), refused while the backpack is full.
/// The power-down returns the gear with the robot's item, and a knock-out crate holds it: nothing is ever lost.</item>
/// <item><b>Weapon</b> — the robot's shot does <see cref="RobotGear.ShotDamage"/>, reaches <see cref="RobotGear.Reach"/> and
/// its tracer takes the weapon's FX colour; the target rules stay the sentry's (never players, NPCs, traders, companions).</item>
/// <item><b>Jetpack</b> — when its owner is more than <see cref="RobotFlyRise"/> blocks above it, the robot flies straight to a
/// free spot beside them (never into blocks or a parked hull) and lands once they stand again. No fuel.</item>
/// </list>
/// There is deliberately no armour.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The robot takes off when its owner is more than this many blocks above it.</summary>
    internal const float RobotFlyRise = 2.5f;

    /// <summary>Its flying speed (blocks per second).</summary>
    internal const float RobotFlySpeed = 6f;

    /// <summary>It lands when it is this close to its spot beside an owner who stands on the ground — and has footing itself.</summary>
    private const float RobotLandDistance = 1f;

    /// <summary>On its spot (which has footing): it lands whatever is under its own feet's cell.</summary>
    private const float RobotLandSnap = 0.25f;

    /// <summary>After a landing it holds still beside its owner while they stay this close (the follow gap and a block).</summary>
    private const float RobotPerchHold = CompanionFollowDistance + 1f;

    /// <summary>How far beside its owner it hovers and lands (the first ring; the second if that is blocked).</summary>
    private static readonly float[] RobotFlyRings = { 1.5f, 2.5f };

    // ---------------- the record and the entity ----------------

    /// <summary>The robot's piece of gear (<see cref="RobotGear.WeaponPiece"/> / <see cref="RobotGear.JetpackPiece"/>), or empty.</summary>
    private static string GearOf(TamedCreature tc, string piece)
        => (piece == RobotGear.WeaponPiece ? tc.GearWeapon : piece == RobotGear.JetpackPiece ? tc.GearJetpack : null) ?? string.Empty;

    private static void SetGear(TamedCreature tc, string piece, string item)
    {
        if (piece == RobotGear.WeaponPiece)
        {
            tc.GearWeapon = item;
        }
        else if (piece == RobotGear.JetpackPiece)
        {
            tc.GearJetpack = item;
        }
    }

    /// <summary>The gear a robot carries, as the stacks that go back with it (power-down, knock-out crate).</summary>
    private static List<ItemStack> RobotGearItems(TamedCreature tc)
    {
        var items = new List<ItemStack>();
        foreach (string item in new[] { tc.GearWeapon, tc.GearJetpack })
        {
            if (!string.IsNullOrEmpty(item))
            {
                items.Add(new ItemStack(item, 1));
            }
        }

        return items;
    }

    /// <summary>Copies a companion's gear onto its live entity (spawn, give, take back).</summary>
    private static void StampRobotGear(CombatEntity entity, TamedCreature tc)
    {
        entity.RobotWeapon = tc.GearWeapon ?? string.Empty;
        entity.RobotJetpack = !string.IsNullOrEmpty(tc.GearJetpack);
        if (!entity.RobotJetpack && entity.RobotFlying)
        {
            entity.Vert.Airborne = true; // the jetpack was taken back mid-flight: gravity takes it down
        }

        entity.RobotFlying &= entity.RobotJetpack;
        entity.RobotPerched &= entity.RobotJetpack;
    }

    /// <summary>The weapon a robot entity fights with, or null.</summary>
    private ItemDefinition? RobotWeaponOf(CombatEntity robot)
        => robot.RobotWeapon.Length > 0 ? _content.GetItem(robot.RobotWeapon) : null;

    /// <summary>The robot's gear on the creature wire entry (the client draws the weapon, the jetpack and its flame).</summary>
    private static NetCreature WithRobotGear(NetCreature net, CombatEntity e)
    {
        if (e.IsCompanion && (e.RobotWeapon.Length > 0 || e.RobotJetpack))
        {
            net.GearWeapon = e.RobotWeapon;
            net.GearFlags = (e.RobotJetpack ? RobotGear.FlagJetpack : 0) | (e.RobotFlying ? RobotGear.FlagFlying : 0);
        }

        return net;
    }

    // ---------------- give / take back ----------------

    /// <summary>E at the own robot with a weapon or a jetpack in hand: one of it goes to the robot, a piece it had comes back.</summary>
    private void HandleGiveCompanionGear(PlayerSession session, GiveCompanionGearIntent intent)
    {
        var p = session.State;
        int slot = intent.Slot;
        if (slot < 0 || slot >= HotbarSlots || slot >= p.Inventory.SlotCount || p.Inventory.Slots[slot] is not { Count: > 0 } stack
            || TalkableRobot(session, intent.CompanionId ?? string.Empty) is not { } tc)
        {
            return; // nothing in that hand, or not the sender's own robot beside them — ignored like a talk
        }

        string item = stack.Item, piece = RobotGear.PieceOf(_content.GetItem(item));
        if (piece.Length == 0)
        {
            return; // the robot takes a weapon or a jetpack — never armour
        }

        string old = GearOf(tc, piece);
        var trial = p.Inventory.Clone();
        TakeOneFromSlot(trial, slot);
        if (old.Length > 0 && trial.Add(old, 1, MaxStackOf(old)) > 0)
        {
            Reject(session, "companion", "@inventory_full"); // the old piece would have nowhere to go
            return;
        }

        _repo.RunInTransaction(() =>
        {
            TakeOneFromSlot(p.Inventory, slot);
            if (old.Length > 0)
            {
                p.Inventory.Add(old, 1, MaxStackOf(old));
            }

            SetGear(tc, piece, item);
            _repo.SavePlayer(p);
        });
        AfterRobotGearChange(session, tc);
        string line = piece == RobotGear.WeaponPiece ? "@srv.robot.weapon_given:" : "@srv.robot.jetpack_given:";
        Send(session, new ServerMessage { Text = line + CompanionDisplayName(tc) });
    }

    /// <summary>"Take back" in the Companions tab: the robot's piece comes back into the backpack (refused while it is full).</summary>
    private void HandleTakeCompanionGear(PlayerSession session, TakeCompanionGearIntent intent)
    {
        var p = session.State;
        string piece = intent.Piece ?? string.Empty;
        var tc = p.TamedCreatures.FirstOrDefault(t => t.Id == intent.CompanionId && t.Kind == TamedCreature.RobotKind);
        if (tc is null || !RobotGear.IsPiece(piece) || GearOf(tc, piece) is not { Length: > 0 } item)
        {
            return;
        }

        if (p.Inventory.Clone().Add(item, 1, MaxStackOf(item)) > 0)
        {
            Reject(session, "companion", "@inventory_full");
            return;
        }

        _repo.RunInTransaction(() =>
        {
            p.Inventory.Add(item, 1, MaxStackOf(item));
            SetGear(tc, piece, string.Empty);
            _repo.SavePlayer(p);
        });
        AfterRobotGearChange(session, tc);
        Send(session, new ServerMessage { Text = "@srv.robot.gear_back:" + CompanionDisplayName(tc) });
    }

    /// <summary>Takes one item out of an inventory slot (a fresh stack, so a cloned inventory never touches the original's).</summary>
    private static void TakeOneFromSlot(Inventory inventory, int slot)
    {
        var stack = inventory.Slots[slot];
        inventory.SetSlot(slot, stack is null || stack.Count <= 1 ? null : new ItemStack(stack.Item, stack.Count - 1));
    }

    /// <summary>The gear changed: the live robot shows it, and the backpack and the Companions tab follow.</summary>
    private void AfterRobotGearChange(PlayerSession session, TamedCreature tc)
    {
        foreach (var c in _creatures.Where(c => c.CompanionId == tc.Id))
        {
            StampRobotGear(c, tc);
        }

        SendInventory(session);
        SendCompanions(session);
        BroadcastCreatures();
    }

    // ---------------- the jetpack (#3399) ----------------

    /// <summary>One movement step of a robot with a jetpack: it takes off when its owner is more than
    /// <see cref="RobotFlyRise"/> above it, flies to a free spot beside them and lands once they stand on the ground again
    /// and it is there. True when it flew this step (the walk is skipped); false for every other companion.</summary>
    private bool RobotJetpackStep(CombatEntity c, CreatureSpecies sp, Vector3f ownerPos, double moveDt)
    {
        if (!c.RobotJetpack || !IsRobotEntity(c))
        {
            return false;
        }

        if (!c.RobotFlying && ownerPos.Y - c.Position.Y <= RobotFlyRise)
        {
            return RobotHoldsPerch(c, ownerPos); // the owner is not above it: it walks — or stays on the ledge it flew up to
        }

        c.RobotPerched = false;
        if (RobotFlyTarget(c, sp, ownerPos) is not { } target)
        {
            return LandRobot(c); // nowhere free beside the owner: it walks (the leash catches up)
        }

        double toSpot = WrapDistSq(c.Position, target);
        if (c.RobotFlying && HasFooting(ownerPos)
            && (toSpot <= RobotLandSnap * RobotLandSnap || (toSpot <= RobotLandDistance * RobotLandDistance && HasFooting(c.Position))))
        {
            LandRobot(c);
            c.RobotPerched = true; // on its spot — or close to it with ground under its own feet (never over a ledge's edge)
            return RobotHoldsPerch(c, ownerPos);
        }

        if (RobotFlyStep(c, sp, target, (float)(RobotFlySpeed * moveDt)) is not { } next)
        {
            return LandRobot(c); // boxed in: it walks again
        }

        if (!c.RobotFlying)
        {
            c.RobotFlying = true;
            BroadcastCreatures(); // the client lights the flame
        }

        c.Position = next;
        c.Vert.Airborne = false; // no gravity while it flies
        c.Vert.VertVel = 0f;
        return true;
    }

    /// <summary>Ends a flight: it stands where it is when there is ground right under its feet, else gravity takes it down the
    /// last bit; it walks again. Returns false (the walk runs).</summary>
    private bool LandRobot(CombatEntity c)
    {
        if (c.RobotFlying)
        {
            var p = c.Position;
            c.RobotFlying = false;
            c.Vert.Airborne = !IsCollidingCellIfLoaded((int)System.Math.Floor(p.X), (int)System.Math.Floor(p.Y - 0.05f),
                (int)System.Math.Floor(p.Z), foliagePasses: false) && !EntityBlockedByShip(new Vector3f(p.X, p.Y - 0.6f, p.Z));
            c.Vert.VertVel = 0f;
            c.Vert.AirTime = 0f;
            BroadcastCreatures();
        }

        return false;
    }

    /// <summary>A robot that landed beside its owner holds still on its spot while the owner stays close and it stands (true:
    /// the walk is skipped) — a companion's idle wander would walk it off the ledge it flew up to, and it would fall and fly
    /// again for ever. Once the owner walks away it follows on foot again (false).</summary>
    private bool RobotHoldsPerch(CombatEntity c, Vector3f ownerPos)
    {
        if (c.RobotPerched && !c.Vert.Airborne && WrapDistSq(c.Position, ownerPos) <= RobotPerchHold * RobotPerchHold)
        {
            return true;
        }

        c.RobotPerched = false;
        return false;
    }

    /// <summary>A free spot beside the owner at their height (two rings of eight bearings from the robot's own angle, the spot
    /// a block higher when the first height is taken — a roof's edge), or null. While the owner stands, a spot with footing
    /// comes first, so the robot never lands over a cliff edge and drops (and takes off again) for ever.</summary>
    private Vector3f? RobotFlyTarget(CombatEntity c, CreatureSpecies sp, Vector3f ownerPos)
    {
        float baseAngle = StableHashing.Index(Hash(c.CompanionId, "fly"), 360) * (float)(System.Math.PI / 180.0);
        for (int pass = HasFooting(ownerPos) ? 0 : 1; pass < 2; pass++)
        {
            for (int lift = 0; lift <= 1; lift++)
            {
                foreach (float radius in RobotFlyRings)
                {
                    if (RobotFlySpot(sp, ownerPos, baseAngle, radius, lift, needsFooting: pass == 0) is { } spot)
                    {
                        return spot;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>The first free spot of one ring of eight bearings around the owner, <paramref name="lift"/> blocks above their
    /// feet (with footing under it when <paramref name="needsFooting"/>), or null.</summary>
    private Vector3f? RobotFlySpot(CreatureSpecies sp, Vector3f ownerPos, float baseAngle, float radius, int lift, bool needsFooting)
    {
        for (int i = 0; i < 8; i++)
        {
            float ang = baseAngle + i * 0.7853982f;
            var spot = new Vector3f(ownerPos.X + (float)System.Math.Cos(ang) * radius, ownerPos.Y + lift,
                ownerPos.Z + (float)System.Math.Sin(ang) * radius);
            if (!CreatureBodyBlocked(sp, spot) && !EntityBlockedByShip(spot) && (!needsFooting || HasFooting(spot)))
            {
                return spot;
            }
        }

        return null;
    }

    /// <summary>The robot's next position on the way to <paramref name="target"/>: below it, first straight up to a block over
    /// the spot's height (up along the wall, never diagonally into a ledge), then straight there, else level — whichever of
    /// those is free of blocks and hulls along the whole step; null when none is.</summary>
    private Vector3f? RobotFlyStep(CombatEntity c, CreatureSpecies sp, Vector3f target, float step)
    {
        var from = c.Position;
        float dx = (float)WorldConstants.WrapDeltaX(target.X - from.X, _world.Circumference);
        float dz = (float)WorldConstants.WrapDeltaZ(target.Z - from.Z, _world.Circumference);
        float dy = target.Y - from.Y;
        float flat = (float)System.Math.Sqrt(dx * dx + dz * dz);
        float dist = (float)System.Math.Sqrt(flat * flat + dy * dy);
        float k = dist <= step || dist < 1e-4f ? 1f : step / dist;
        var straight = new Vector3f(from.X + dx * k, from.Y + dy * k, from.Z + dz * k);
        var level = flat < 1e-4f ? from
            : new Vector3f(from.X + dx / flat * System.Math.Min(step, flat), from.Y, from.Z + dz / flat * System.Math.Min(step, flat));
        var candidates = from.Y < target.Y - 0.25f && flat > RobotLandDistance // below the spot: climb first (the descent ends at its height)
            ? new[] { new Vector3f(from.X, from.Y + System.Math.Min(step, target.Y + 1f - from.Y), from.Z), straight, level }
            : new[] { straight, level };
        foreach (var next in candidates)
        {
            if (WrapDistSq(next, from) > 1e-6 && !CreaturePathBlocked(sp, from, next, foliagePasses: true) && !EntityBlockedByShip(next))
            {
                return next;
            }
        }

        return null;
    }

    /// <summary>Whether something carries a body standing at <paramref name="pos"/> — a colliding cell within two blocks under
    /// its feet, or a parked hull — so an owner there is not mid-jetpack or mid-glide.</summary>
    private bool HasFooting(Vector3f pos)
    {
        int x = (int)System.Math.Floor(pos.X), z = (int)System.Math.Floor(pos.Z);
        int below = (int)System.Math.Floor(pos.Y - 0.05f);
        return IsCollidingCellIfLoaded(x, below, z, foliagePasses: false) || IsCollidingCellIfLoaded(x, below - 1, z, foliagePasses: false)
               || EntityBlockedByShip(new Vector3f(pos.X, pos.Y - 0.6f, pos.Z));
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the robot's damage per shot, reach and tracer colour with the weapon it carries now.</summary>
    public (float Damage, float Reach, int TracerRgb) RobotWeaponStatsForTest(string playerId)
    {
        var weapon = RobotEntityForTest(playerId) is { } robot ? RobotWeaponOf(robot) : null;
        return (RobotGear.ShotDamage(weapon, RobotShotDamage), RobotGear.Reach(weapon, SentryRange), RobotGear.TracerRgb(weapon));
    }
}
