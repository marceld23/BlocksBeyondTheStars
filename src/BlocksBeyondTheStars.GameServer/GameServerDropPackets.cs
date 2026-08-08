// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Ground drop packets (#853): the small block bundles a full inventory leaves lying in the world.
/// <para>
/// Mining used to be <b>refused</b> outright once the backpack (and the cargo hold, when aboard) was full —
/// <c>BreakBlockAt</c> checked <see cref="MaterialPool.CanFit"/> and left the block standing, because there
/// was nowhere in the world to put the overflow without destroying it (#600/#607, "Items futsch"). This file
/// is that missing place: the block always breaks, whatever does not fit spills onto the ground, and the
/// player walks back over it later to get it back.
/// </para>
/// <para>
/// Two rules make it bearable rather than a carpet of litter: packets <b>stack</b> (a nearby packet absorbs
/// further overflow instead of a new one spawning per mined block), and they are collected
/// <b>automatically</b> by <see cref="TickDropPackets"/> as soon as a player with room walks near.
/// </para>
/// <para>
/// A packet is a <see cref="StoredContainer"/> with <see cref="DropPacketKind"/> — the container store is
/// already generic over its kind in all three repositories, so packets persist across a reload with no
/// schema change and no save migration. They are kept OUT of <c>ContainerList</c> (see
/// <c>BroadcastContainers</c>) so they never hijack the crate/capsule loot prompt, and travel on their own
/// <see cref="DropPacketList"/> message instead.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Container kind that marks a ground drop packet (vs. "crate" / "salvage_capsule").</summary>
    internal const string DropPacketKind = "drop";

    /// <summary>How far a spill looks for an existing packet to merge into. Generous enough that a whole
    /// tunnel section's overflow lands in one bundle rather than one per cell.</summary>
    private const float DropMergeRadius = 3.5f;

    /// <summary>How close a player has to be for a packet to flow back into their inventory by itself.</summary>
    private const float DropPickupRadius = 2.5f;

    /// <summary>Distinct item keys one packet holds before a spill starts a new packet. Counts within a stack
    /// are unbounded — that IS the stacking requirement.</summary>
    private const int MaxStacksPerDropPacket = 12;

    /// <summary>Packets a single body may carry. At the cap a spill merges into the nearest packet whatever
    /// the distance, so the count stays bounded and nothing is ever destroyed.</summary>
    private const int MaxDropPacketsPerWorld = 64;

    /// <summary>Seconds between auto-pickup sweeps (4 Hz). Fast enough to feel instant when walking over a
    /// packet, cheap enough that a full inventory standing on one costs nothing.</summary>
    private const double DropPacketSweepInterval = 0.25;

    /// <summary>Ground drop packets on the active world (tests + inspection).</summary>
    public IReadOnlyList<StoredContainer> DropPackets
        => _containers.Where(c => c.Kind == DropPacketKind).ToList();

    /// <summary>
    /// Puts items that found no room anywhere onto the ground at <paramref name="origin"/>: merged into a
    /// nearby packet when there is one, otherwise as a new packet. Merging is by <b>exact item key</b>, so a
    /// dyed/glowing/shaped variant (composite <c>ItemKey</c>) keeps its own stack instead of dissolving into
    /// the plain material.
    /// </summary>
    private void SpillToGround(Vector3i origin, IEnumerable<ItemAmount> items)
    {
        var pending = items.Where(i => i.Count > 0 && !string.IsNullOrEmpty(i.Item)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var cell = SettleDropCell(origin);
        bool changed = false;
        foreach (var amount in pending)
        {
            var packet = FindOrCreatePacket(cell, amount.Item);
            var stack = packet.Items.FirstOrDefault(s => s.Item == amount.Item);
            if (stack is null)
            {
                packet.Items.Add(new ItemStack(amount.Item, amount.Count));
            }
            else
            {
                stack.Count += amount.Count;
            }

            _repo.SaveContainer(packet);
            changed = true;
        }

        if (changed)
        {
            BroadcastDropPackets();
        }
    }

    /// <summary>Convenience overload for the single-item spill sites.</summary>
    private void SpillToGround(Vector3i origin, string item, int count)
        => SpillToGround(origin, new[] { new ItemAmount(item, count) });

    /// <summary>
    /// Spills whatever a pool could not store since its last spill — the overflow-aware companion to
    /// <see cref="WarnIfPoolOverflowed"/> for events that cannot be refused after the fact (a creature is
    /// already dead, a wreck already burst). Nothing is lost; the player is still told where it went.
    /// </summary>
    private void SpillPoolOverflow(PlayerSession session, MaterialPool pool, Vector3i origin)
    {
        var leftovers = pool.TakeLeftovers();
        if (leftovers.Count == 0)
        {
            return;
        }

        SpillToGround(origin, leftovers);
        NotifyDropped(session);
    }

    /// <summary>The throttled "it is on the ground, come back for it" toast — same cadence and channel as the
    /// old "backpack full" hint it replaces (#600), because one drill swing can overflow on a dozen blocks.</summary>
    private void NotifyDropped(PlayerSession session)
    {
        if (_uptime < session.NextInventoryFullHintAt)
        {
            return;
        }

        session.NextInventoryFullHintAt = _uptime + InventoryFullHintCooldown;
        Send(session, new ServerMessage { Text = "@dropped_on_ground" });
    }

    /// <summary>The packet a spill of <paramref name="item"/> at <paramref name="cell"/> belongs in: the
    /// nearest one within <see cref="DropMergeRadius"/> that already carries the key or still has a free
    /// stack slot. Falls back to a new packet — or, at the world cap, to the nearest packet at ANY distance
    /// (a bounded pile beats destroying the items).</summary>
    private StoredContainer FindOrCreatePacket(Vector3i cell, string item)
    {
        var center = Center(cell);
        StoredContainer? best = null;
        double bestSq = DropMergeRadius * DropMergeRadius;
        int count = 0;

        foreach (var c in _containers)
        {
            if (c.Kind != DropPacketKind)
            {
                continue;
            }

            count++;
            if (!HasRoomFor(c, item))
            {
                continue;
            }

            double sq = WrapDistSq(center, Center(c.Position));
            if (sq <= bestSq)
            {
                bestSq = sq;
                best = c;
            }
        }

        if (best is not null)
        {
            return best;
        }

        if (count < MaxDropPacketsPerWorld)
        {
            var packet = new StoredContainer
            {
                Id = "drop_" + System.Guid.NewGuid().ToString("N"),
                Planet = _world.LocationId,
                Kind = DropPacketKind,
                Position = cell,
                Items = new List<ItemStack>(),
            };

            _containers.Add(packet);
            return packet;
        }

        // At the cap: pour into the nearest packet regardless of distance and stack budget. Items are never
        // destroyed — the world just stops growing new bundles.
        return _containers
            .Where(c => c.Kind == DropPacketKind)
            .OrderBy(c => WrapDistSq(center, Center(c.Position)))
            .First();
    }

    private static bool HasRoomFor(StoredContainer packet, string item)
        => packet.Items.Count < MaxStacksPerDropPacket || packet.Items.Any(s => s.Item == item);

    /// <summary>Where a packet actually comes to rest: the mined cell itself when it is free, otherwise the
    /// first free cell above it (a spill from a block broken under water/inside a wall must not end up
    /// entombed). Falls back to the origin — a packet is collected by proximity, not by line of sight.</summary>
    private Vector3i SettleDropCell(Vector3i origin)
    {
        for (int dy = 0; dy <= 2; dy++)
        {
            var cell = new Vector3i(origin.X, origin.Y + dy, origin.Z);
            if (!WithinBuildHeight(cell.Y))
            {
                break;
            }

            if (_world.GetBlock(cell).IsAir)
            {
                return cell;
            }
        }

        return origin;
    }

    private static Vector3f Center(Vector3i cell) => new(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

    /// <summary>
    /// Pours nearby packets back into the players standing over them (4 Hz). Personal inventory first, cargo
    /// hold when aboard; a packet keeps whatever still does not fit and only disappears once empty. A player
    /// whose inventory is full is a pure no-op — no inventory push, no broadcast — so mining on with a full
    /// pack costs nothing extra.
    /// </summary>
    private void TickDropPackets(double dt)
    {
        _worlds.Active.SinceDropSweep += dt;
        if (_worlds.Active.SinceDropSweep < DropPacketSweepInterval)
        {
            return;
        }

        _worlds.Active.SinceDropSweep = 0;
        if (!_containers.Any(c => c.Kind == DropPacketKind))
        {
            return;
        }

        bool anyRemoved = false;
        foreach (var session in JoinedInActiveWorld())
        {
            SetCurrent(session); // per-player ship cursor: the cargo hold we spill into must be THEIR ship's
            bool tookAnything = false;

            foreach (var packet in _containers.Where(c => c.Kind == DropPacketKind).ToList())
            {
                if (WrapDistSq(session.State.Position, Center(packet.Position)) > DropPickupRadius * DropPickupRadius)
                {
                    continue;
                }

                var pool = new MaterialPool(_content, session.State, _ship);
                var kept = new List<ItemStack>();
                bool took = false;
                foreach (var stack in packet.Items)
                {
                    if (stack.IsEmpty)
                    {
                        continue;
                    }

                    int leftover = pool.Add(stack.Item, stack.Count);
                    if (leftover < stack.Count)
                    {
                        took = true;
                    }

                    if (leftover > 0)
                    {
                        kept.Add(new ItemStack(stack.Item, leftover));
                    }
                }

                if (!took)
                {
                    continue; // full pack standing on a packet — leave it exactly as it was
                }

                tookAnything = true;
                packet.Items = kept;
                if (packet.Items.Count == 0)
                {
                    _containers.Remove(packet);
                    _repo.DeleteContainer(packet.Id);
                }
                else
                {
                    _repo.SaveContainer(packet);
                }

                anyRemoved = true;
            }

            if (tookAnything)
            {
                SendInventory(session);
            }
        }

        if (anyRemoved)
        {
            BroadcastDropPackets();
        }
    }

    private static NetDropPacket ToNetDropPacket(StoredContainer c)
    {
        var top = c.Items.OrderByDescending(s => s.Count).FirstOrDefault();
        return new NetDropPacket
        {
            Id = c.Id,
            X = c.Position.X,
            Y = c.Position.Y,
            Z = c.Position.Z,
            TopItem = top?.Item ?? string.Empty,
            StackCount = c.Items.Count,
            TotalCount = c.Items.Sum(s => s.Count),
        };
    }

    private DropPacketList DropPacketMessage()
        => new() { Packets = _containers.Where(c => c.Kind == DropPacketKind).Select(ToNetDropPacket).ToArray() };

    private void BroadcastDropPackets() => BroadcastToWorld(DropPacketMessage());

    private void SendDropPackets(PlayerSession session) => Send(session, DropPacketMessage());

    // ---------------- Test hooks ----------------

    /// <summary>Test hook: run one auto-pickup sweep on the active world without waiting out the 4 Hz
    /// throttle (the sweep is otherwise only reachable through a timed <see cref="Tick"/>).</summary>
    public void SweepDropPacketsForTest() => TickDropPackets(DropPacketSweepInterval);

    /// <summary>Test hook: drop items on the ground exactly as a full-inventory mine would.</summary>
    public void SpillToGroundForTest(Vector3i origin, string item, int count)
        => SpillToGround(origin, item, count);
}
