// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Carrying a block over the head and throwing it (#3766 — a young player's idea: "carry things with both hands and
/// throw them at enemies"). The rules live in <see cref="BlockThrow"/>; this is the server's side of them.
/// <para><b>Carrying</b> is a stance of the selected hotbar slot, not a transfer: the block stays in its stack, so
/// nothing can be lost by a disconnect, a death or a full inventory. It ends by itself when another slot is selected,
/// when the stack is gone, or when the player can no longer carry (boarded, seated, driving, in space) — the last one
/// is checked whenever the stance is read, so no move report has to clear it.</para>
/// <para><b>Throwing</b> takes one block from that stack and flies it on the server: the first machine, robber or wild
/// animal on the arc is hit by the block's material (<see cref="BlockThrow.Damage"/>), booked through
/// <c>LandHit</c> like any blow. A tame animal, a Quali, a buried giant, a player and a resident are never hit — the
/// block passes them. Where the arc ends the block lies as an ordinary drop packet: an item to pick up again, never a
/// placed block (so a throw cannot build, grief a base or break a claim).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The effect kind of a thrown block's flight (<see cref="WorldFx.Kind"/>).</summary>
    public const string BlockThrowFx = "block_throw";

    /// <summary>The presence bit of a player who really carries right now.</summary>
    private int CarryBit(PlayerSession s) => s.CarryingBlock && CanCarryBlock(s.State, out _, out _) ? GearLook.CarryingOverhead : 0;

    /// <summary>True if the player can hold a block over the head right now: alive, on foot on a world (the rope's
    /// rule — not aboard, seated, on a train, driving, on a spacewalk or in a ship's interior), not climbing, and the
    /// selected hotbar slot holds an item that places a block.</summary>
    private bool CanCarryBlock(PlayerState p, out string item, out BlockDefinition? block)
    {
        item = string.Empty;
        block = null;
        if (p.Health <= 0f || p.Climbing || !RopeAllowed(p))
        {
            return false;
        }

        int slot = p.SelectedHotbarSlot;
        if (slot < 0 || slot >= p.Inventory.SlotCount || p.Inventory.Slots[slot] is not { IsEmpty: false } stack)
        {
            return false;
        }

        block = _content.GetItem(stack.Item)?.PlacesBlock is { Length: > 0 } key ? _content.GetBlock(key) : null;
        item = stack.Item;
        return block is not null;
    }

    private void HandleCarryBlock(PlayerSession session, CarryBlockIntent intent)
    {
        if (!intent.Carry)
        {
            session.CarryingBlock = false;
            return;
        }

        if (!CanCarryBlock(session.State, out _, out _))
        {
            session.CarryingBlock = false;
            Reject(session, "carry", "@srv.carry.cannot");
            return;
        }

        session.CarryingBlock = true;
    }

    private void HandleThrowBlock(PlayerSession session, ThrowBlockIntent intent)
    {
        var p = session.State;
        if (!session.CarryingBlock || !CanCarryBlock(p, out string item, out var block) || block is null)
        {
            session.CarryingBlock = false;
            return;
        }

        if (_uptime < session.NextBlockThrowAt)
        {
            return;
        }

        session.NextBlockThrowAt = _uptime + BlockThrow.ThrowGapSeconds;
        session.CarryingBlock = false; // the hands are empty; the next block is lifted with the next press

        var stack = p.Inventory.Slots[p.SelectedHotbarSlot]!;
        stack.Count -= 1;
        if (stack.Count <= 0)
        {
            p.Inventory.SetSlot(p.SelectedHotbarSlot, null);
        }

        SendInventory(session);

        var weight = BlockThrow.WeightOf(block.Hardness);
        BlockThrow.Direction(intent.DirX, intent.DirY, intent.DirZ, p.Yaw, out float dx, out float dy, out float dz);
        var from = new Vector3f(p.Position.X, p.Position.Y + BlockThrow.ReleaseHeight, p.Position.Z);
        var dir = new Vector3f(dx, dy, dz);
        var end = FlyThrownBlock(from, dir, BlockThrow.Speed(weight), out float seconds, out var hit, out var hitList, out bool hitCreature);

        BroadcastToWorld(new WorldFx
        {
            Kind = BlockThrowFx,
            X = from.X,
            Y = from.Y,
            Z = from.Z,
            ToX = end.X,
            ToY = end.Y,
            ToZ = end.Z,
            Strength = seconds,
            Radius = hit is null ? 0f : 1f,
            Item = item,
        });

        // The block first, the hit after: a beaten enemy's loot then lies beside the block, not under a rule of its own.
        SpillToGround(end.ToBlock(), item, 1);
        if (hit is not null && hitList is not null)
        {
            LandHit(session, hit, hitList, hitCreature, BlockThrow.Damage(weight), knockback: null, dir);
        }
    }

    /// <summary>Flies a thrown block from <paramref name="from"/> along <paramref name="dir"/>: step by step on the arc of
    /// <see cref="BlockThrow.PositionAt"/> until it meets a body that may be hit, a block it cannot pass (the walking
    /// body's rule — it flies through leaves, grass and water) or the end of its time. Returns the last free point.</summary>
    private Vector3f FlyThrownBlock(Vector3f from, Vector3f dir, float speed, out float seconds, out CombatEntity? hit, out List<CombatEntity>? hitList, out bool hitCreature)
    {
        hit = null;
        hitList = null;
        hitCreature = false;
        var last = from;
        seconds = 0f;
        for (float t = BlockThrow.StepSeconds; t <= BlockThrow.MaxFlightSeconds; t += BlockThrow.StepSeconds)
        {
            BlockThrow.PositionAt(from.X, from.Y, from.Z, dir.X * speed, dir.Y * speed, dir.Z * speed, t, out float x, out float y, out float z);
            var at = new Vector3f(x, y, z);
            var cell = at.ToBlock();
            if (!WithinBuildHeight(cell.Y) || IsCollidingBlock(_world.GetBlock(cell), fluidsPass: true, foliagePasses: true))
            {
                break;
            }

            last = at;
            seconds = t;
            if (ThrownBlockTarget(at, dir, out hit, out hitList, out hitCreature))
            {
                break;
            }
        }

        return last;
    }

    /// <summary>The body a flying block touches at <paramref name="at"/>, if any: a machine or a robber, or a wild animal
    /// that players may hit at all.</summary>
    private bool ThrownBlockTarget(Vector3f at, Vector3f dir, out CombatEntity? hit, out List<CombatEntity>? list, out bool isCreature)
    {
        foreach (var e in _planetEnemies)
        {
            if (e.Kind == CombatEntityKind.GuardianGiant
                    ? WrapDistSq(at, GuardianGiantAimPoint(e, at, dir)) <= 1.5 * 1.5
                    : ThrownBlockTouches(at, e))
            {
                (hit, list, isCreature) = (e, _planetEnemies, false);
                return true;
            }
        }

        foreach (var e in _bandits)
        {
            if (ThrownBlockTouches(at, e))
            {
                (hit, list, isCreature) = (e, _bandits, false);
                return true;
            }
        }

        foreach (var c in _creatures)
        {
            if (ProtectedFromPlayers(c))
            {
                continue; // #2281 / #3359: a pet, a tamer's animal, a Quali — the block passes
            }

            if (c.IsGiant ? GiantHittable(c) && WrapDistSq(at, GiantAimPoint(c, at)) <= 1.5 * 1.5 : ThrownBlockTouches(at, c))
            {
                (hit, list, isCreature) = (c, _creatures, true);
                return true;
            }
        }

        (hit, list, isCreature) = (null, null, false);
        return false;
    }

    private bool ThrownBlockTouches(Vector3f at, CombatEntity e)
        => BlockThrow.HitsBody(
            (float)WrapDistSq(new Vector3f(at.X, 0f, at.Z), new Vector3f(e.Position.X, 0f, e.Position.Z)),
            at.Y - e.Position.Y,
            e.SizeScale);

    /// <summary>Test seam: is the player carrying (as the others see it)?</summary>
    public bool CarriesBlockForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && CarryBit(s) != 0;

    /// <summary>Test seam: the Lift control (<paramref name="carry"/>) as the client sends it.</summary>
    public void CarryBlockForTest(string playerId, bool carry)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleCarryBlock(session, new CarryBlockIntent { Carry = carry });
        }
    }

    /// <summary>Test seam: the player selects a hotbar slot, as the client sends it.</summary>
    public void SelectHotbarForTest(string playerId, int slot)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleSelectHotbar(session, new SelectHotbarIntent { Slot = slot });
        }
    }

    /// <summary>Test seam: a throw along a direction, as the client sends it.</summary>
    public void ThrowBlockForTest(string playerId, float dirX, float dirY, float dirZ)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleThrowBlock(session, new ThrowBlockIntent { DirX = dirX, DirY = dirY, DirZ = dirZ });
        }
    }
}
