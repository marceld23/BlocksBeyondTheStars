// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The reward for the end (#3294, Justus' idea: "Belohnung für das Ende!!!"). When the Guardian core is shut down
/// (<c>MarkGuardianDefeated</c>) every player of the world — online and offline — is put on a roster; each of them gets
/// ONE <b>owner-locked crate</b> holding a <c>robot_core</c> (milestone <see cref="GuardianRewardMilestone"/>), placed in
/// front of them the next time they stand on foot on a loaded world: at once for whoever is outside, after stepping out
/// for whoever is aboard, on the next join (at the saved position) for whoever was offline. Players who first join after
/// the win are not on the roster and get nothing (maintainer decision).
/// <para>
/// A crate and not the bare item, because a full backpack must never swallow the reward. The same delivery carries a
/// knocked-out hunter robot's parts (#3295, <c>TamedCreature.KnockedOut</c>). The spot is a free cell with ground in a ring
/// two to three blocks in front of the player — never in a ship, a settlement, a station, a foreign base, a landing pad or
/// the core's protection. No spot: one hint, a retry every second, and after <see cref="OwnerCrateBackpackAfterSeconds"/>
/// the items go straight into the backpack once they fit. The crate, its container, the milestone and the roster entry
/// are written in one transaction, so a crash can neither double nor lose the reward.
/// </para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The finale reward's item: the hunter robot is built from it (#3295).</summary>
    internal const string RobotCoreItem = "robot_core";

    /// <summary>The once-per-player milestone of the finale reward crate.</summary>
    internal const string GuardianRewardMilestone = "reward:guardian_core";

    /// <summary>Seconds a player stands on foot with no free spot before the items go into the backpack instead.</summary>
    internal const double OwnerCrateBackpackAfterSeconds = 60.0;

    /// <summary>The named blob holding the roster — player ids still owed a reward crate.</summary>
    private const string GuardianRewardRosterBlob = "story:guardian_reward_roster";

    /// <summary>The block an owner-locked crate is (the workshop crate: unbounded, mined back into a crate item).</summary>
    private const string OwnerCrateBlockKey = "crate";

    /// <summary>The delivery pass runs at 1 Hz per world.</summary>
    private const double OwnerCrateTryInterval = 1.0;

    private static readonly int[] OwnerCrateBearings = { 0, 45, -45, 90, -90, 135, -135, 180 };
    private static readonly int[] OwnerCrateRings = { 2, 3 };

    private HashSet<string>? _guardianRewardRoster;

    /// <summary>Per "player|delivery": since when the player has been waiting on foot without a free spot.</summary>
    private readonly Dictionary<string, double> _ownerCrateWaitSince = new(StringComparer.Ordinal);

    /// <summary>Per world: the uptime of its next delivery pass (one gate per world, #2226's rule).</summary>
    private readonly Dictionary<string, double> _ownerCrateNextAt = new(StringComparer.Ordinal);

    /// <summary>The player ids still owed the finale reward, loaded from the save on first use.</summary>
    private HashSet<string> GuardianRewardRoster
    {
        get
        {
            if (_guardianRewardRoster is not null)
            {
                return _guardianRewardRoster;
            }

            _guardianRewardRoster = new HashSet<string>(StringComparer.Ordinal);
            if (_repo.LoadNamedBlob(GuardianRewardRosterBlob) is { Length: > 0 } json)
            {
                try
                {
                    foreach (string id in JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>())
                    {
                        _guardianRewardRoster.Add(id);
                    }
                }
                catch (JsonException ex)
                {
                    _log.Warn($"Guardian reward roster unreadable, starting empty: {ex.Message}");
                }
            }

            return _guardianRewardRoster;
        }
    }

    private void SaveGuardianRewardRoster()
        => _repo.SaveNamedBlob(GuardianRewardRosterBlob,
            JsonSerializer.Serialize(GuardianRewardRoster.OrderBy(id => id, StringComparer.Ordinal).ToList()));

    /// <summary>The win (called from <c>MarkGuardianDefeated</c>): every player the save knows, plus everyone joined right
    /// now, is owed a reward crate. A player who already has one (an earlier win, another story pack) is skipped by the
    /// milestone at delivery time.</summary>
    private void SnapshotGuardianRewardRoster()
    {
        var roster = GuardianRewardRoster;
        foreach (string id in _repo.ListPlayerIds())
        {
            roster.Add(id);
        }

        foreach (var s in _sessions.Values)
        {
            if (s.Joined && s.State.PlayerId.Length > 0)
            {
                roster.Add(s.State.PlayerId);
            }
        }

        SaveGuardianRewardRoster();
    }

    /// <summary>The items of the finale reward crate.</summary>
    private List<ItemStack> GuardianRewardItems() => new() { new ItemStack(RobotCoreItem, 1) };

    // ---------------- The delivery pass ----------------

    /// <summary>1 Hz per world: every player here who is on foot and owed a crate — the finale reward, a knocked-out robot's
    /// parts — gets it placed in front of them (or, after a minute without a spot, into the backpack).</summary>
    private void TickOwnerCrates()
    {
        string world = _world.LocationId;
        if (_ownerCrateNextAt.TryGetValue(world, out double next) && _uptime < next)
        {
            return;
        }

        _ownerCrateNextAt[world] = _uptime + OwnerCrateTryInterval;
        foreach (var s in JoinedInActiveWorld().ToList())
        {
            if (OwnerCrateRecipientHere(s))
            {
                DeliverPendingOwnerCrates(s);
            }
        }
    }

    /// <summary>Whether a crate can be handed to this player right now: joined, on foot on this (non-void) world — not in
    /// space, aboard, on an EVA, in a ship interior or station, observing or down.</summary>
    private bool OwnerCrateRecipientHere(PlayerSession s)
    {
        var p = s.State;
        bool aboard = p.AboardShip && _worlds.Active.LandedShips.TryGetValue(p.PlayerId, out var landed) && landed.Placed;
        return s.Joined && !s.Spectating && s.CurrentLocationId == _world.LocationId && !_world.Planet.Void
               && !aboard && !p.InEva && p.Health > 0f
               && !InSpace(p.PlayerId) && !InShipInterior(p.PlayerId) && !InStation(p.PlayerId);
    }

    private void DeliverPendingOwnerCrates(PlayerSession s)
    {
        var p = s.State;
        var roster = GuardianRewardRoster;
        if (roster.Contains(p.PlayerId))
        {
            if (p.Milestones.Contains(GuardianRewardMilestone))
            {
                roster.Remove(p.PlayerId); // already rewarded (an earlier win) — nothing more to give
                SaveGuardianRewardRoster();
            }
            else
            {
                TryDeliverOwnerCrate(s, GuardianRewardMilestone, GuardianRewardItems(), "vega.reward.core_crate", string.Empty, () =>
                {
                    p.Milestones.Add(GuardianRewardMilestone);
                    roster.Remove(p.PlayerId);
                    SaveGuardianRewardRoster();
                });
            }
        }

        foreach (var tc in p.TamedCreatures.Where(t => t.KnockedOut).ToList())
        {
            TryDeliverOwnerCrate(s, "robot:" + tc.Id, RobotPartItems(tc), "vega.robot.parts_crate", CompanionDisplayName(tc),
                () => p.TamedCreatures.Remove(tc));
        }
    }

    /// <summary>Places one owed crate in front of the player, or — blocked — hints once and, after
    /// <see cref="OwnerCrateBackpackAfterSeconds"/>, puts the items into the backpack if they all fit. <paramref name="settle"/>
    /// writes what the delivery settles (the milestone, the roster, the knocked-out record) and runs inside the save
    /// transaction. Returns true when the items were handed over either way.</summary>
    private bool TryDeliverOwnerCrate(PlayerSession s, string deliveryKey, List<ItemStack> items, string lineKey, string lineArg,
        Action settle)
    {
        var p = s.State;
        string waitKey = p.PlayerId + "|" + deliveryKey;
        if (TryFindOwnerCrateSpot(p.Position, p.Yaw, p.PlayerId, out var cell) && PlaceOwnerCrate(s, cell, items, settle))
        {
            _ownerCrateWaitSince.Remove(waitKey);
            SendVegaLine(s, lineKey, 3, lineArg); // system line: never muted — it says where the crate is
            return true;
        }

        if (!_ownerCrateWaitSince.TryGetValue(waitKey, out double since))
        {
            _ownerCrateWaitSince[waitKey] = _uptime;
            SendVegaLine(s, "vega.owner_crate.no_spot", 3); // the one hint: step onto open ground
            return false;
        }

        if (_uptime - since < OwnerCrateBackpackAfterSeconds || !GiveOwnerCrateIntoBackpack(s, items, settle))
        {
            return false; // keep trying — the items are never lost, only waiting
        }

        _ownerCrateWaitSince.Remove(waitKey);
        Send(s, new ServerMessage { Text = "@srv.owner_crate.into_backpack" });
        return true;
    }

    /// <summary>A free cell with ground in the ring <see cref="OwnerCrateRings"/> around <paramref name="origin"/>, the bearings
    /// fanned out from <paramref name="yawDegrees"/> (straight ahead first), that is no protected place for
    /// <paramref name="ownerId"/>.</summary>
    private bool TryFindOwnerCrateSpot(Vector3f origin, float yawDegrees, string ownerId, out Vector3i cell)
    {
        cell = default;
        if (_world.Planet.Void || _content.GetBlock(OwnerCrateBlockKey) is null)
        {
            return false;
        }

        int circ = _world.Circumference;
        int refY = (int)Math.Floor(origin.Y);
        foreach (int ring in OwnerCrateRings)
        {
            foreach (int bearing in OwnerCrateBearings)
            {
                double a = (yawDegrees + bearing) * Math.PI / 180.0;
                int cx = (int)Math.Floor(WorldConstants.WrapX(origin.X + (Math.Sin(a) * ring), circ));
                int cz = (int)Math.Floor(WorldConstants.WrapZ(origin.Z + (Math.Cos(a) * ring), circ));
                if (TryGroundFeetYAt(cx, cz, refY, 1, 3, out int y, canopyCarries: false)
                    && OwnerCrateSpotFree(new Vector3i(cx, y, cz), ownerId))
                {
                    cell = new Vector3i(cx, y, cz);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether an owner-locked crate may stand at <paramref name="c"/>: a loaded, empty cell on solid ground, no
    /// container or player in it, and none of the places a reward must never land in.</summary>
    private bool OwnerCrateSpotFree(Vector3i c, string ownerId)
    {
        if (!WithinBuildHeight(c.Y) || !_world.IsChunkLoaded(WorldConstants.WorldToChunk(c))
            || !_world.GetBlockIfLoaded(c).IsAir || !IsSolidBlock(_world.GetBlockIfLoaded(new Vector3i(c.X, c.Y - 1, c.Z))))
        {
            return false;
        }

        var centre = new Vector3f(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f);
        if (IsShipBlock(c) || EntityBlockedByShip(centre) || IsSettlementBlock(c) || IsStationBlock(c) || IsOnLandingPad(c)
            || IsProtectedStructureCell(c) // generation 39: a structure its template protects
            || BasePadBusy(c) || IsGuardianCoreProtected(c) || IsTextMonumentProtected(c) // #3311
            || IsBaseProtected(c, ownerId, false) || IsFactoryProtected(c, ownerId, false))
        {
            return false;
        }

        if (_containers.Any(k => k.Position.Equals(c)))
        {
            return false; // a crate, capsule or ground packet already sits there
        }

        foreach (var s in JoinedInActiveWorld())
        {
            var feet = s.State.Position.ToBlock();
            if (WorldConstants.SameCell(feet.X, feet.Y, feet.Z, c.X, c.Y, c.Z, _world.Circumference)
                || WorldConstants.SameCell(feet.X, feet.Y + 1, feet.Z, c.X, c.Y, c.Z, _world.Circumference))
            {
                return false; // never inside somebody
            }
        }

        return true;
    }

    /// <summary>Sets the crate block and its owner-locked container, and settles the delivery — one transaction — then
    /// tells the world.</summary>
    private bool PlaceOwnerCrate(PlayerSession s, Vector3i cell, List<ItemStack> items, Action settle)
    {
        if (_content.GetBlock(OwnerCrateBlockKey) is not { } crateDef)
        {
            return false;
        }

        var p = s.State;
        var container = new StoredContainer
        {
            Id = "crate_" + Guid.NewGuid().ToString("N"),
            Planet = _world.LocationId,
            Kind = "crate",
            Position = cell,
            Items = items.Select(i => new ItemStack(i.Item, i.Count)).ToList(),
            Owner = p.PlayerId,
            OwnerName = p.Name,
        };
        _repo.RunInTransaction(() =>
        {
            _world.SetBlock(cell, crateDef.NumericId, owner: p.PlayerId);
            _repo.SaveContainer(container);
            settle();
            _repo.SavePlayer(p);
        });
        _containers.Add(container);
        BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = crateDef.NumericId.Value });
        BroadcastContainers();
        _log.Info($"Owner-locked crate for '{p.Name}' at {cell.X},{cell.Y},{cell.Z}: {string.Join(", ", items.Select(i => i.Count + "x " + i.Item))}.");
        return true;
    }

    /// <summary>The fallback after a minute without a spot: every stack into the backpack — all of them or none.</summary>
    private bool GiveOwnerCrateIntoBackpack(PlayerSession s, List<ItemStack> items, Action settle)
    {
        var inv = s.State.Inventory;
        var trial = inv.Clone();
        foreach (var stack in items)
        {
            if (trial.Add(stack.Item, stack.Count, MaxStackOf(stack.Item)) > 0)
            {
                return false; // not all of it fits — wait for room (or a spot) rather than lose a part
            }
        }

        _repo.RunInTransaction(() =>
        {
            foreach (var stack in items)
            {
                inv.Add(stack.Item, stack.Count, MaxStackOf(stack.Item));
            }

            settle();
            _repo.SavePlayer(s.State);
        });
        SendInventory(s);
        return true;
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the player ids still owed the finale reward.</summary>
    public IReadOnlyCollection<string> GuardianRewardRosterForTest => GuardianRewardRoster;

    /// <summary>Test seam: takes the win's roster snapshot again, as a second win (another story run) would.</summary>
    public void SnapshotGuardianRewardRosterForTest() => SnapshotGuardianRewardRoster();

    /// <summary>Test seam: runs the delivery pass on the active world right now, skipping its 1 Hz gate.</summary>
    public void TickOwnerCratesForTest()
    {
        _ownerCrateNextAt.Remove(_world.LocationId);
        TickOwnerCrates();
    }

    /// <summary>Test seam: whether an owner-locked crate could stand at a cell for a player.</summary>
    public bool OwnerCrateSpotFreeForTest(Vector3i cell, string ownerId) => OwnerCrateSpotFree(cell, ownerId);
}
