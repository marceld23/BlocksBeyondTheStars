// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>#2018: the thrown piece of food a world's begging herds rush (one per world at a time — a second throw re-targets
/// the herd). Transient runtime state on <see cref="WorldRuntime"/>; the packet itself is an ordinary creature-loot drop.</summary>
public sealed class ThrownFood
{
    public string PacketId { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public Vector3i Cell { get; set; }
    public string ThrowerId { get; set; } = string.Empty;
    public double GraceUntil { get; set; }
    public int Squabblers { get; set; }
}

/// <summary>
/// The begging herds (#2018, 2026-09). A passive land species with <see cref="CreatureSpecies.BegsForFood"/> reacts to food in a
/// nearby player's hand: its animals come hopping over at burst speed, orbit the player on one of two rings and (client-side)
/// call at a fast cadence; they trot off when the food is stowed, when the player walks away or after
/// <see cref="HerdRules.InterestSeconds"/>. A piece thrown with the Feed action (<see cref="ThrowFoodIntent"/>) is rushed,
/// squabbled over and eaten.
/// <para><b>Cost discipline</b> (the analysis this was built from): the per-player held-food check runs once per tick
/// (<see cref="RefreshLureTargets"/>), the per-creature branch tests the species flag first and reads the tiny lure list only
/// then; no line-of-sight ray, no container scan, no LINQ and no extra broadcast anywhere on the tick path. A begging animal
/// costs what a hunting one costs: the same <c>LocomotionController.Step</c> + <c>ApplyCreatureStep</c> it runs anyway.</para>
/// </summary>
public sealed partial class GameServer
{
    private readonly List<PlayerSession> _lureTargets = new(); // players holding food this tick (reused, no per-tick alloc)

    /// <summary>Who holds food right now: one item lookup per player per tick, read by every beggar afterwards.</summary>
    private void RefreshLureTargets(List<PlayerSession> targets)
    {
        _lureTargets.Clear();
        foreach (var s in targets)
        {
            if (HerdRules.IsFoodForCreatures(_content.GetItem(HeldItemKey(s.State))))
            {
                _lureTargets.Add(s);
            }
        }
    }

    /// <summary>The nearest player holding food within <paramref name="range"/> of <paramref name="from"/>, or null.</summary>
    private PlayerSession? NearestLure(Vector3f from, float range)
    {
        PlayerSession? best = null;
        double bestSq = range * range;
        foreach (var s in _lureTargets)
        {
            if (s.State.IgnoredByHostiles) // cloaked / god-mode / creative-override players are invisible to fauna
            {
                continue;
            }

            double d = WrapDistSq(s.State.Position, from);
            if (d <= bestSq)
            {
                bestSq = d;
                best = s;
            }
        }

        return best;
    }

    /// <summary>The begging routine for one wild animal this tick. Returns true (with the intent + target set) while the animal is
    /// in a begging phase; false leaves the caller's temperament intents in charge. The phase is dropped outright when the animal
    /// is frozen, asleep or panicked (those gates run before this in <c>MoveCreatures</c>, but a phase can be live when one
    /// starts), so a startled herd flees exactly as it always did.</summary>
    private bool TryBegIntent(CombatEntity c, CreatureSpecies sp, Vector3f? nearestPlayer,
        ref LocomotionProfile profile, ref MoveMode intent, ref Vector3f? target)
    {
        if (!HerdRules.BegsForFood(sp) || c.IsCompanion || c.IsGiant)
        {
            return false;
        }

        bool asleep = !SpeciesActive(sp, c.Position) && c.AwakeOverrideTimer <= 0;
        if (c.FrozenTimer > 0 || asleep || c.PanicTimer > 0 || c.ProvokeTimer > 0)
        {
            c.BegPhase = BegPhase.None; // a startled or frozen animal forgets the food
            return false;
        }

        var food = _worlds.Active.ThrownFood;
        switch (c.BegPhase)
        {
            case BegPhase.None:
                {
                    if (_uptime < c.BegCooldownUntil || _lureTargets.Count == 0)
                    {
                        return false;
                    }

                    var lure = NearestLure(c.Position, HerdRules.LureRange);
                    if (lure is null)
                    {
                        return false;
                    }

                    c.BegPhase = BegPhase.Beg;
                    c.BegUntil = _uptime + HerdRules.InterestSeconds;
                    c.NextBegHopAt = _uptime + BegHash(c) % 50 / 100.0; // the herd does not hop in lockstep
                    ShipAiHintOnce(lure, "feed_herd"); // VEGA explains the Feed action once per player
                    return BegOrbit(c, sp, lure.State.Position, ref intent, ref target);
                }

            case BegPhase.Beg:
                {
                    var lure = NearestLure(c.Position, HerdRules.LeaveRange);
                    if (lure is null || _uptime >= c.BegUntil)
                    {
                        BeginLeave(c);
                        return LeaveIntent(nearestPlayer, ref profile, ref intent, ref target);
                    }

                    return BegOrbit(c, sp, lure.State.Position, ref intent, ref target);
                }

            case BegPhase.Rush:
                {
                    if (food is null || !FoodPieceExists(food))
                    {
                        BeginLeave(c);
                        return LeaveIntent(nearestPlayer, ref profile, ref intent, ref target);
                    }

                    var piece = Center(food.Cell);
                    if (WrapDistSq(c.Position, piece) <= HerdRules.ArriveRange * HerdRules.ArriveRange)
                    {
                        c.BegPhase = BegPhase.Squabble;
                        c.BegUntil = _uptime + HerdRules.SquabbleMinSeconds
                            + (HerdRules.SquabbleMaxSeconds - HerdRules.SquabbleMinSeconds) * (BegHash(c) % 1000 / 999.0);
                        return SquabbleOrbit(c, sp, food, ref intent, ref target);
                    }

                    BegHop(c, sp);
                    intent = MoveMode.Seek;
                    target = piece;
                    return true;
                }

            case BegPhase.Squabble:
                {
                    if (food is null || !FoodPieceExists(food))
                    {
                        BeginLeave(c);
                        return LeaveIntent(nearestPlayer, ref profile, ref intent, ref target);
                    }

                    if (_uptime >= c.BegUntil)
                    {
                        // The first squabbler whose timer runs out wins the piece; the rest find it gone next tick and leave too.
                        EatThrownFood(food);
                        BeginLeave(c);
                        return LeaveIntent(nearestPlayer, ref profile, ref intent, ref target);
                    }

                    return SquabbleOrbit(c, sp, food, ref intent, ref target);
                }

            case BegPhase.Leave:
                {
                    if (_uptime >= c.BegUntil)
                    {
                        c.BegPhase = BegPhase.None;
                        return false;
                    }

                    return LeaveIntent(nearestPlayer, ref profile, ref intent, ref target);
                }

            default:
                c.BegPhase = BegPhase.None;
                return false;
        }
    }

    /// <summary>Seek a point that travels around the player: the herd surrounds and circles instead of stacking on one spot. Two
    /// rings (the half of a big herd with the odd hash takes the outer one) so twelve bodies fit; the hop beat on top.</summary>
    private bool BegOrbit(CombatEntity c, CreatureSpecies sp, Vector3f player, ref MoveMode intent, ref Vector3f? target)
    {
        uint h = BegHash(c);
        bool outer = HerdRules.IsBigHerd(sp) && (h & 1) == 1;
        float angle0 = (h % 6283) / 1000f; // 0..2π, stable per individual
        float angle = angle0 + (float)(_uptime * HerdRules.OrbitRate);
        BegHop(c, sp);
        intent = MoveMode.Seek;
        target = HerdRules.OrbitPoint(player, HerdRules.OrbitRadiusFor(sp, outer), angle);
        return true;
    }

    /// <summary>A tight, fast orbit around the thrown piece — the scrum.</summary>
    private bool SquabbleOrbit(CombatEntity c, CreatureSpecies sp, ThrownFood food, ref MoveMode intent, ref Vector3f? target)
    {
        // sp: the hop needs the species (a crawler squabbles on foot, a jumper bounces)
        float angle0 = (BegHash(c) % 6283) / 1000f;
        float angle = angle0 + (float)(_uptime * HerdRules.SquabbleRate);
        BegHop(c, sp);
        intent = MoveMode.Seek;
        target = HerdRules.OrbitPoint(Center(food.Cell), HerdRules.SquabbleRadiusFor(food.Squabblers), angle);
        return true;
    }

    /// <summary>The trot away: a flee from the nearest player at CRUISE speed (a copy of the profile — never a panic sprint),
    /// or a plain roam when nobody is around to trot away from.</summary>
    private static bool LeaveIntent(Vector3f? nearestPlayer, ref LocomotionProfile profile, ref MoveMode intent, ref Vector3f? target)
    {
        if (nearestPlayer is { } np)
        {
            profile.BurstSpeed = profile.CruiseSpeed;
            intent = MoveMode.Flee;
            target = np;
        }
        else
        {
            intent = MoveMode.Roam;
            target = null;
        }

        return true;
    }

    private void BeginLeave(CombatEntity c)
    {
        c.BegPhase = BegPhase.Leave;
        c.BegUntil = _uptime + HerdRules.LeaveSeconds;
        c.BegCooldownUntil = _uptime + HerdRules.CooldownSeconds;
    }

    /// <summary>A grounded jumper hops on the beat while it begs — the Hopper's own launch, on a timer instead of the gait's wave.
    /// Crawlers, giants and the floating grazers (which cannot jump) simply run their orbit.</summary>
    private void BegHop(CombatEntity c, CreatureSpecies sp)
    {
        if (_uptime < c.NextBegHopAt || c.Vert.Airborne || c.Vert.ClimbTargetY > 0f || !CreatureMotion.CanJump(sp))
        {
            return;
        }

        c.NextBegHopAt = _uptime + HerdRules.HopInterval;
        float g = VerticalMotion.Gravity(_gravityFactor);
        VerticalMotion.Launch(ref c.Vert, VerticalMotion.ImpulseFor(g, VerticalMotion.JumpHeightFor(_gravityFactor, VerticalMotion.HopHeight)));
    }

    /// <summary>Personal space on the ring (#2018): the separation half of <see cref="GroupSteer"/> without its cohesion (the
    /// player is the centre already). O(kin) against the ≤ 64-entity list, only while the animal begs.</summary>
    private Vector3f BegSeparation(CombatEntity self, CreatureSpecies sp, Vector3f stepped, double dt, in LocomotionProfile prof)
    {
        float nearestSq = float.MaxValue, nearX = 0f, nearZ = 0f;
        foreach (var other in _creatures)
        {
            if (ReferenceEquals(other, self) || other.IsCompanion || other.SpeciesId != self.SpeciesId)
            {
                continue;
            }

            float dx = other.Position.X - stepped.X, dz = other.Position.Z - stepped.Z;
            float distSq = dx * dx + dz * dz;
            if (distSq < nearestSq)
            {
                nearestSq = distSq;
                nearX = other.Position.X;
                nearZ = other.Position.Z;
            }
        }

        float sepDist = System.Math.Max(1.5f, sp.Size * 0.6f);
        float nd = (float)System.Math.Sqrt(nearestSq);
        if (nd > 1e-4f && nd < sepDist)
        {
            float push = System.Math.Min(sepDist - nd, (float)(prof.CruiseSpeed * dt));
            return new Vector3f(stepped.X + (stepped.X - nearX) / nd * push, stepped.Y, stepped.Z + (stepped.Z - nearZ) / nd * push);
        }

        return stepped;
    }

    private static uint BegHash(CombatEntity c) => (uint)StableStringHash(c.Id);

    // --- The Feed action -------------------------------------------------------------------------------------------------

    /// <summary>The Feed action (#2018): one piece of the food in the slot lands <see cref="HerdRules.ThrowDistance"/> blocks ahead
    /// of the player as a creature-loot drop packet (it ages out if nobody eats it), the thrower may not pick it up for
    /// <see cref="HerdRules.PickupGraceSeconds"/>, and every begging animal within <see cref="HerdRules.PacketRushRange"/> rushes
    /// it. Refused without food in the slot or without a begging animal near — the piece would just lie there.</summary>
    private void HandleThrowFood(PlayerSession session, ThrowFoodIntent intent)
    {
        var p = session.State;
        if (p.AboardShip || InSpace(p.PlayerId))
        {
            return;
        }

        int slot = intent.Slot;
        if (slot < 0 || slot >= p.Inventory.SlotCount || p.Inventory.Slots[slot] is not { IsEmpty: false } stack)
        {
            return;
        }

        string item = stack.Item;
        if (!HerdRules.IsFoodForCreatures(_content.GetItem(item)))
        {
            Reject(session, "feed", "@srv.feed.not_food");
            return;
        }

        if (!AnyBeggarNear(p.Position, HerdRules.PacketRushRange))
        {
            Reject(session, "feed", "@srv.feed.nobody_hungry");
            return;
        }

        if (!p.Inventory.Remove(item, 1))
        {
            return;
        }

        SendInventory(session);

        // Where it lands: ahead along the facing (yaw 0 = +Z, 90 = +X — the streaming code's convention), then the ordinary
        // drop-cell settle so it lies on the floor and not in a wall.
        double yawRad = p.Yaw * System.Math.PI / 180.0;
        var origin = new Vector3i(
            (int)System.Math.Floor(p.Position.X + System.Math.Sin(yawRad) * HerdRules.ThrowDistance),
            (int)System.Math.Floor(p.Position.Y + 0.5f),
            (int)System.Math.Floor(p.Position.Z + System.Math.Cos(yawRad) * HerdRules.ThrowDistance));
        var cell = SettleDropCell(origin);
        var packet = FindOrCreatePacket(cell, item, creatureLoot: true);
        packet.LifetimeLeft = System.Math.Max(packet.LifetimeLeft, LootPacketLifetime);
        CapLootOverFire(packet);
        var existing = packet.Items.Find(s => s.Item == item);
        if (existing is null)
        {
            packet.Items.Add(new ItemStack(item, 1));
        }
        else
        {
            existing.Count += 1;
        }

        _repo.SaveContainer(packet);
        BroadcastDropPackets();

        var food = new ThrownFood
        {
            PacketId = packet.Id,
            Item = item,
            Cell = packet.Position,
            ThrowerId = p.PlayerId,
            GraceUntil = _uptime + HerdRules.PickupGraceSeconds,
        };

        // One pass, once, at the throw — never a per-tick search: every begging animal (or one of a begging species that is awake
        // and calm, cooldown or not — a piece on the ground is worth a look) within range rushes the piece.
        var centre = Center(packet.Position);
        double rangeSq = HerdRules.PacketRushRange * HerdRules.PacketRushRange;
        foreach (var c in _creatures)
        {
            if (c.IsCompanion || c.IsGiant || c.FrozenTimer > 0 || c.PanicTimer > 0
                || !_speciesById.TryGetValue(c.SpeciesId, out var sp) || !HerdRules.BegsForFood(sp)
                || (!SpeciesActive(sp, c.Position) && c.AwakeOverrideTimer <= 0)
                || WrapDistSq(c.Position, centre) > rangeSq)
            {
                continue;
            }

            c.BegPhase = BegPhase.Rush;
            c.BegUntil = _uptime + HerdRules.InterestSeconds; // a rush that never arrives still ends
            food.Squabblers++;
        }

        _worlds.Active.ThrownFood = food;
        _log.Info($"'{p.Name}' threw 1x {item} to {food.Squabblers} begging animal(s) at {packet.Position}.");
    }

    /// <summary>Whether a begging animal (in the beg phase) is within <paramref name="range"/> — the server-side twin of the
    /// client's Feed probe.</summary>
    private bool AnyBeggarNear(Vector3f at, float range)
    {
        double rangeSq = range * range;
        foreach (var c in _creatures)
        {
            if (c.BegPhase is BegPhase.Beg or BegPhase.Rush or BegPhase.Squabble && WrapDistSq(c.Position, at) <= rangeSq)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The thrower (and their fetching companion) keep off the piece they just threw while the grace runs.</summary>
    private bool ThrownFoodBlocksPickup(PlayerSession session, StoredContainer packet)
        => _worlds.Active.ThrownFood is { } food
           && food.PacketId == packet.Id
           && _uptime < food.GraceUntil
           && food.ThrowerId == session.State.PlayerId;

    /// <summary>The thrown piece still lies in its packet (a player may have collected it after the grace, or it aged out).</summary>
    private bool FoodPieceExists(ThrownFood food)
    {
        foreach (var c in _containers)
        {
            if (c.Id == food.PacketId)
            {
                var stack = c.Items.Find(s => s.Item == food.Item);
                return stack is { Count: > 0 };
            }
        }

        _worlds.Active.ThrownFood = null; // gone — forget it, so the next throw starts clean
        return false;
    }

    /// <summary>The winner eats: one unit of the food leaves the packet (the packet itself goes when that was its last item).</summary>
    private void EatThrownFood(ThrownFood food)
    {
        _worlds.Active.ThrownFood = null;
        foreach (var packet in _containers)
        {
            if (packet.Id != food.PacketId)
            {
                continue;
            }

            var stack = packet.Items.Find(s => s.Item == food.Item);
            if (stack is not null)
            {
                stack.Count -= 1;
                if (stack.Count <= 0)
                {
                    packet.Items.Remove(stack);
                }
            }

            if (packet.Items.Count == 0)
            {
                _containers.Remove(packet);
                _repo.DeleteContainer(packet.Id);
            }
            else
            {
                _repo.SaveContainer(packet);
            }

            BroadcastDropPackets();
            return;
        }
    }

    // --- Test seams (#2018) ----------------------------------------------------------------------------------------------

    /// <summary>The begging phase of a wild animal ("None" when it does not beg).</summary>
    public string BegPhaseForTest(string creatureId)
    {
        foreach (var c in _creatures)
        {
            if (c.Id == creatureId)
            {
                return c.BegPhase.ToString();
            }
        }

        return BegPhase.None.ToString();
    }

    /// <summary>Throws one piece of the player's slot through the real handler.</summary>
    public void ThrowFoodForTest(string playerId, int slot)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleThrowFood(session, new ThrowFoodIntent { Slot = slot });
        }
    }

    /// <summary>The thrown piece's packet cell, or null when none lies in the world.</summary>
    public Vector3i? ThrownFoodCellForTest() => _worlds.Active.ThrownFood is { } food && FoodPieceExists(food) ? food.Cell : null;

    /// <summary>The weighted wild count the spawner and the prune budget against (#2018).</summary>
    public int WeightedWildCountForTest() => WildCreatureCount;

    /// <summary>Whether the spawner would place another animal of the species right now: under its share, and — for a big
    /// herd — no herd of it alive (#2018).</summary>
    public bool SpeciesMaySpawnForTest(string speciesId)
    {
        var sp = _speciesById[speciesId];
        int cap = System.Math.Min(WorldCreatureCap(System.Math.Max(1, _creatureTargets.Count)), CreatureHardCap);
        return WeightedWildCountOf(sp) < SpeciesShare(cap) && !(HerdRules.IsBigHerd(sp) && WildCountOf(sp.Id) > 0);
    }
}
