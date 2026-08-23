// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Companion payoff (#1210): a tamed creature finally DOES something besides following and warding the
/// Guardian machines. Three small, stateless-ish roles that ride existing systems:
/// <list type="bullet">
/// <item><b>Fetch</b> — a companion standing near a ground packet pours it into its OWNER's pool
/// (<see cref="TickDropPackets"/> reach check), owner-only, owner within leash range; first time VEGA says why.</item>
/// <item><b>Alert + distract</b> — a hostile (machine, bandit, aggressive fauna) with line of sight to a companion
/// within <see cref="CompanionAlertRange"/> makes it growl: one toast per owner per <see cref="CompanionAlertCooldown"/>
/// s and a short client flag (<c>NetCreature.Alerting</c>: amber nameplate + "!"); a robber on its way to the
/// owner stalls at the companion for <see cref="CompanionBanditStallSeconds"/> s (no damage either way); at
/// Bond ≥ <see cref="CompanionBanditWardBond"/> a present companion wards its owner from hold-ups the way it
/// already wards them from machines (the companion must be within <c>CompanionWardRange</c>, so a hold-up
/// stays possible when the pet is left behind).</item>
/// <item><b>Produce</b> — every <see cref="CompanionProduceInterval"/> s a present companion drops its species'
/// <c>DropItem</c> at its feet (spill, auto-picked — fetch synergy; a penned pet stockpiles for later).</item>
/// </list>
/// Nothing new is persisted; no new NetCodec tag (additive <c>NetCreature.Alerting</c> only).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A companion fetches packets within this radius (three times the player's own pickup reach).</summary>
    private const float CompanionFetchRadius = DropPickupRadius * 3f;

    /// <summary>A hostile with line of sight inside this radius of a companion makes it growl.</summary>
    private const float CompanionAlertRange = 20f;

    /// <summary>An owner is warned at most this often (the growl, not the danger, is rate-limited).</summary>
    private const double CompanionAlertCooldown = 30.0;

    /// <summary>How long the client shows the alert pose after a growl.</summary>
    private const double CompanionAlertShowSeconds = 4.0;

    /// <summary>A robber walking up to its mark stops at a companion this close…</summary>
    private const float CompanionBanditStallRange = 6f;

    /// <summary>…for this long (once per <see cref="CompanionBanditStallRepeat"/> s per robber).</summary>
    private const double CompanionBanditStallSeconds = 8.0;
    private const double CompanionBanditStallRepeat = 30.0;

    /// <summary>Bond a present companion needs to ward its owner from bandit hold-ups.</summary>
    private const int CompanionBanditWardBond = 70;

    /// <summary>A present companion drops its species' DropItem this often.</summary>
    private const double CompanionProduceInterval = 600.0;

    /// <summary>The alert/produce scans run at this cadence (the hooks are cheap but need no 15 Hz).</summary>
    private const double CompanionPayoffScanInterval = 1.0;

    private double _nextCompanionPayoffAt;

    /// <summary>The owner session of a companion entity when the owner is joined, on this world, on foot or
    /// not — null when the owner is elsewhere (a pet alone on its home world does nothing for anyone).</summary>
    private PlayerSession? CompanionOwnerHere(CombatEntity c)
        => c.IsCompanion && FindSessionByPlayerId(c.OwnerId) is { Joined: true } owner
           && owner.CurrentLocationId == _world.LocationId && !InSpace(c.OwnerId)
            ? owner
            : null;

    // ---------------- Fetch ----------------

    /// <summary>Whether one of the session's present companions stands within fetch reach of a packet while the
    /// owner is within leash range of that companion (so a pet never teleports loot across the world — a penned
    /// pet's produce waits for the owner's visit). Owner-only by construction: only THIS session's pets count.</summary>
    private bool CompanionFetches(PlayerSession session, Vector3f packetCenter)
    {
        float leash2 = CompanionLeashRange * CompanionLeashRange;
        foreach (var c in _creatures)
        {
            if (!c.IsCompanion || c.OwnerId != session.State.PlayerId)
            {
                continue;
            }

            // The reach grows with the bond (#1225) — a well-fed pet hoovers a wider circle.
            float r = CompanionFetchRadiusFor(session.State.TamedCreatures.FirstOrDefault(t => t.Id == c.CompanionId));
            if (WrapDistSq(c.Position, packetCenter) <= r * r
                && WrapDistSq(c.Position, session.State.Position) <= leash2)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Alert + distract + bandit ward ----------------

    /// <summary>Guard-registered: the 1 Hz companion scans — growl at hostiles in sight, stall approaching
    /// robbers, and drop produce when due. Skips worlds without companions entirely.</summary>
    private void TickCompanionPayoff()
    {
        if (_uptime < _nextCompanionPayoffAt)
        {
            return;
        }

        _nextCompanionPayoffAt = _uptime + CompanionPayoffScanInterval;
        bool broadcast = false;
        foreach (var c in _creatures)
        {
            if (!c.IsCompanion || CompanionOwnerHere(c) is not { } owner)
            {
                continue;
            }

            broadcast |= CompanionAlertScan(c, owner);
            CompanionStallScan(c, owner);
            CompanionProduceScan(c, owner);
        }

        if (broadcast)
        {
            BroadcastCreatures(); // the Alerting flag flips now, not at the next position-sync beat
        }
    }

    /// <summary>A hostile in sight of the companion: warn the owner (rate-limited) and pose the pet.</summary>
    private bool CompanionAlertScan(CombatEntity c, PlayerSession owner)
    {
        if (_uptime < owner.NextCompanionAlertAt || !HostileInSightOf(c.Position))
        {
            return false;
        }

        owner.NextCompanionAlertAt = _uptime + CompanionAlertCooldown;
        c.AlertUntil = _uptime + CompanionAlertShowSeconds;
        Send(owner, new ServerMessage { Text = "@srv.companion.alert:" + (string.IsNullOrEmpty(c.CustomName) ? owner.State.Name : c.CustomName) });
        return true;
    }

    /// <summary>Anything that would hurt a player, with a clear line to the companion: Guardian machines,
    /// bandits that are not leaving (camp guards, robbers on approach, fighters), and aggressive awake fauna.</summary>
    private bool HostileInSightOf(Vector3f at)
    {
        float r2 = CompanionAlertRange * CompanionAlertRange; // HasLineOfSight itself sights from head height on both ends
        foreach (var e in _planetEnemies)
        {
            if (WrapDistSq(e.Position, at) <= r2 && HasLineOfSight(e.Position, at))
            {
                return true;
            }
        }

        foreach (var b in _bandits)
        {
            if (b.BanditPhase != BanditPhase.Leaving && WrapDistSq(b.Position, at) <= r2 && HasLineOfSight(b.Position, at))
            {
                return true;
            }
        }

        foreach (var w in _creatures)
        {
            if (w.IsCompanion || w.FrozenTimer > 0 || !_speciesById.TryGetValue(w.SpeciesId, out var sp))
            {
                continue;
            }

            if ((sp.Hostile || w.ProvokeTimer > 0) && SpeciesActive(sp)
                && WrapDistSq(w.Position, at) <= r2 && HasLineOfSight(w.Position, at))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A robber still walking up to (or facing off with) its mark stops at a companion this close — the
    /// pet is in the way, and a robber who has not turned hostile yet does not push past an animal.</summary>
    private void CompanionStallScan(CombatEntity c, PlayerSession owner)
    {
        float r2 = CompanionBanditStallRange * CompanionBanditStallRange;
        foreach (var b in _bandits)
        {
            if (b.BanditPhase is not (BanditPhase.Approach or BanditPhase.Demanding) || b.BanditTargetId != owner.State.PlayerId
                || _uptime < b.NextStallAt || WrapDistSq(b.Position, c.Position) > r2)
            {
                continue;
            }

            b.StallUntil = _uptime + CompanionBanditStallSeconds;
            b.NextStallAt = _uptime + CompanionBanditStallRepeat;
        }
    }

    /// <summary>Whether a bandit is currently held up by a companion (consulted by MoveBandit).</summary>
    private bool BanditStalledByCompanion(CombatEntity bandit) => _uptime < bandit.StallUntil;

    /// <summary>The bandit-side ward (#1210): a present companion with Bond ≥ <see cref="CompanionBanditWardBond"/>
    /// within <c>CompanionWardRange</c> of its owner keeps robbers from picking them as a mark — and a robber
    /// already on approach thinks better of it. Bond finally has a mechanical reason to exist.</summary>
    private bool BanditWardedByCompanion(PlayerState p)
    {
        float r2 = CompanionWardRange * CompanionWardRange;
        foreach (var c in _creatures)
        {
            if (!c.IsCompanion || c.OwnerId != p.PlayerId || WrapDistSq(p.Position, c.Position) > r2)
            {
                continue;
            }

            var tc = p.TamedCreatures.FirstOrDefault(t => t.Id == c.CompanionId);
            if (tc is not null && tc.Bond >= CompanionBanditWardBond)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Produce ----------------

    /// <summary>Every ten minutes a present companion leaves its species' DropItem at its feet. Nothing for
    /// species without a drop; the first interval is armed when the pet is first seen by the scan.</summary>
    private void CompanionProduceScan(CombatEntity c, PlayerSession owner)
    {
        if (c.NextProduceAt == 0)
        {
            c.NextProduceAt = _uptime + CompanionProduceInterval; // first seen by the scan → arm
            return;
        }

        if (_uptime < c.NextProduceAt)
        {
            return;
        }

        c.NextProduceAt = _uptime + CompanionProduceInterval;
        if (!_speciesById.TryGetValue(c.SpeciesId, out var sp) || string.IsNullOrEmpty(sp.DropItem) || _content.GetItem(sp.DropItem) is null)
        {
            return;
        }

        var feet = new Vector3i((int)System.Math.Floor(c.Position.X), (int)System.Math.Floor(c.Position.Y), (int)System.Math.Floor(c.Position.Z));
        SpillToGround(feet, sp.DropItem, 1);
        _ = owner; // the packet reaches the owner through the ordinary sweep (fetch synergy) — no toast spam
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test seam: runs the companion scans right now (alert / stall / produce), skipping the 1 Hz timer.</summary>
    public void TickCompanionPayoffForTest()
    {
        _nextCompanionPayoffAt = 0;
        TickCompanionPayoff();
    }

    /// <summary>Test seam: makes every present companion's next produce due on the next scan.</summary>
    public void DueCompanionProduceForTest()
    {
        foreach (var c in _creatures)
        {
            if (c.IsCompanion)
            {
                c.NextProduceAt = -1; // armed AND due (0 would read as "unset")
            }
        }
    }

    /// <summary>Test/inspection: whether the bandit-side companion ward currently covers a player.</summary>
    public bool BanditWardedByCompanionForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && BanditWardedByCompanion(s.State);

    /// <summary>Test/inspection: whether a bandit is held up by a companion right now.</summary>
    public bool BanditStalledForTest(string banditId)
        => _bandits.FirstOrDefault(b => b.Id == banditId) is { } b && BanditStalledByCompanion(b);

    /// <summary>Test/inspection: whether a companion entity is currently posing alert (client flag).</summary>
    public bool CompanionAlertingForTest(string entityId)
        => _creatures.FirstOrDefault(c => c.Id == entityId) is { } c && _uptime < c.AlertUntil;
}
