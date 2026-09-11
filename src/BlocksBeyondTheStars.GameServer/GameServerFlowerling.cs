// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Damian's flowerling (school club wave 3, #1760) — the two authored temperament traits, kept out of the
/// creature state machine so nothing about rolled species changes:
/// <list type="bullet">
/// <item><b>Angered by mining</b> — every block a player breaks is reported here; a species with
/// <see cref="CreatureSpecies.AngeredByMining"/> within <see cref="MiningAngerRange"/> AND with line of sight
/// takes a <see cref="MiningGrudgeSeconds"/> grudge: its <c>ProvokeTimer</c> runs, so the existing "provoked"
/// path does the rest (it reads as hostile on the client — the grin becomes the maw — chases and bites with its
/// species' damage, and calms down when the timer lapses). Breaking another block in front of it refreshes the
/// grudge.</item>
/// <item><b>Gifts when calm</b> — a calm individual (no grudge, awake) with a player within
/// <see cref="GiftRange"/> whose last block break on this world lies more than <see cref="CalmSecondsForGift"/>
/// ago spills a small present at its feet, at most once per <see cref="GiftCooldownSeconds"/>: berries most of
/// the time, a material block now and then. The player is told once per gift.</item>
/// </list>
/// The trait flags come from <c>data/creatures.json</c>; nothing here is persisted.
/// </summary>
public sealed partial class GameServer
{
    private const float MiningAngerRange = 16f;
    private const double MiningGrudgeSeconds = 60.0;
    private const double CalmSecondsForGift = 120.0;
    private const float GiftRange = 3f;
    private const double GiftCooldownSeconds = 45.0;
    private const double GiftScanInterval = 1.0;

    private double _nextGiftScanAt;

    /// <summary>The uptime of each player's last block break, per world ("locationId|playerId").</summary>
    private readonly Dictionary<string, double> _lastBlockBreakAt = new();

    private static readonly string[] GiftMaterials = { "stone", "wood_log", "iron_ore", "copper_ore" };

    private string BreakKey(string playerId) => _world.LocationId + "|" + playerId;

    /// <summary>Called after every block a player breaks (#1760): remembers the break for the gifting rule and
    /// provokes every mining-angered creature that can see the spot.</summary>
    private void CreaturesOnBlockBroken(PlayerSession session, Vector3i pos)
    {
        _lastBlockBreakAt[BreakKey(session.State.PlayerId)] = _uptime;
        if (_creatures.Count == 0)
        {
            return;
        }

        var at = new Vector3f(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
        float range2 = MiningAngerRange * MiningAngerRange;
        foreach (var c in _creatures)
        {
            if (c.IsCompanion || !_speciesById.TryGetValue(c.SpeciesId, out var sp) || !sp.AngeredByMining)
            {
                continue;
            }

            if (WrapDistSq(c.Position, at) > range2 || !HasLineOfSight(c.Position, at))
            {
                continue; // it did not see that
            }

            c.ProvokeTimer = System.Math.Max(c.ProvokeTimer, MiningGrudgeSeconds);
        }
    }

    /// <summary>The gifting scan (#1760), once a second from the creature tick.</summary>
    private void TickCalmGifts(List<PlayerSession> targets)
    {
        if (_uptime < _nextGiftScanAt || _creatures.Count == 0)
        {
            return;
        }

        _nextGiftScanAt = _uptime + GiftScanInterval;
        float range2 = GiftRange * GiftRange;
        foreach (var c in _creatures)
        {
            if (c.IsCompanion || c.ProvokeTimer > 0 || c.FrozenTimer > 0 || _uptime < c.GiftReadyAt
                || !_speciesById.TryGetValue(c.SpeciesId, out var sp) || !sp.GiftsWhenCalm || !SpeciesActive(sp))
            {
                continue;
            }

            foreach (var s in targets)
            {
                if (WrapDistSq(s.State.Position, c.Position) > range2)
                {
                    continue;
                }

                if (_lastBlockBreakAt.TryGetValue(BreakKey(s.State.PlayerId), out double last) && _uptime - last < CalmSecondsForGift)
                {
                    continue; // still a miner in its eyes
                }

                var feet = new Vector3i((int)System.Math.Floor(c.Position.X), (int)System.Math.Floor(c.Position.Y), (int)System.Math.Floor(c.Position.Z));
                SpillToGround(feet, new[] { PickGift(c) });
                c.GiftReadyAt = _uptime + GiftCooldownSeconds;
                Send(s, new ServerMessage { Text = Localize(s.Locale, "srv.flowerling.gift") });
                break;
            }
        }
    }

    /// <summary>What a calm flowerling hands over: berries ×1–2 three times in five, else one material block.
    /// Runtime-random on purpose (like where an animal stands), not seed-reproducible.</summary>
    private ItemAmount PickGift(CombatEntity c)
    {
        var rng = new System.Random(unchecked((int)(_uptime * 1000.0) ^ System.StringComparer.Ordinal.GetHashCode(c.Id)));
        return rng.NextDouble() < 0.6
            ? new ItemAmount("berries", 1 + rng.Next(2))
            : new ItemAmount(GiftMaterials[rng.Next(GiftMaterials.Length)], 1);
    }

    /// <summary>Test seam (#1760): a creature's remaining grudge / provocation, 0 when calm.</summary>
    public double ProvokeTimerForTest(string creatureId)
    {
        foreach (var c in _creatures)
        {
            if (c.Id == creatureId)
            {
                return c.ProvokeTimer;
            }
        }

        return 0;
    }

    /// <summary>Test seam (#1763): the spawner's full reject list for a named roster species at a spot.</summary>
    public bool SpawnSpotClearForSpeciesTest(string speciesId, Vector3f at)
    {
        int x = (int)System.Math.Floor(at.X), z = (int)System.Math.Floor(at.Z);
        return SpawnSpotClear(_speciesById[speciesId], at, x, z, _generator.SurfaceHeight(_world.Planet, x, z));
    }

    /// <summary>Test seam (#1760): the uptime of the player's last block break on this world, or null.</summary>
    public double? LastBlockBreakForTest(string playerId)
        => _lastBlockBreakAt.TryGetValue(BreakKey(playerId), out double t) ? t : null;

    /// <summary>Test seam (#1760): forces the gift scan and cooldowns as if that much uptime had passed.</summary>
    public void AdvanceGiftClockForTest(double seconds)
    {
        _uptime += seconds;
        _nextGiftScanAt = 0;
    }
}
