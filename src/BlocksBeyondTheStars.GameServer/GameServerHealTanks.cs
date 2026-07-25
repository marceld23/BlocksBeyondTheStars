// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Heal tanks: the placeable life-support unit for player bases and stations (issue #460).
///
/// A planet base has no life support of its own — health only regenerates in breathable air, hunger
/// drains, and the suit "only refills at a heal-tank" (the long-standing intent in TickEnvironment).
/// This block closes that gap: every on-foot player within <see cref="HealTankRadius"/> of a placed
/// heal tank is slowly healed, fed and has their suit recharged. It is deliberately STATELESS — the
/// voxel itself is the whole machine (persisted by the ordinary block-edit store), mirroring the
/// algae tank; there is no registry to keep in sync with mining/explosions.
///
/// The proximity test is a box scan of the world grid (like <c>NearStationBlock</c>) throttled to one
/// rescan per second per player; the regen itself applies every tick so the HUD bars move smoothly.
/// A heal tank never revives a downed player (mirrors the field medkit) and never outruns the death
/// check — regen is skipped at 0 HP just like the passive atmosphere regen.
/// </summary>
public sealed partial class GameServer
{
    internal const string HealTankBlock = "heal_tank";

    /// <summary>Radius (blocks, per axis — a box, matching the crafting-station scans) around a placed
    /// heal tank within which players regenerate. Vertical reach is smaller: one room, not a tower.</summary>
    private const int HealTankRadius = 6;
    private const int HealTankRadiusY = 3;

    private const float HealTankHealPerSecond = 4f;   // 2x the breathable-air regen
    private const float HealTankFeedPerSecond = 6f;   // slower than ship life support (10/s), still generous
    private const float HealTankEnergyPerSecond = 10f; // half the aboard-ship suit recharge (20/s)

    /// <summary>Seconds between proximity rescans per player (the regen itself applies every tick).</summary>
    private const double HealTankScanInterval = 1.0;

    private ushort _healTankBlockId;

    /// <summary>Resolves the heal-tank block id once per content load (0 = block missing).</summary>
    private void InitHealTanks()
        => _healTankBlockId = _content.GetBlock(HealTankBlock)?.NumericId.Value ?? 0;

    /// <summary>Per-world regen field: heal + feed + suit recharge for every on-foot player near a placed
    /// heal tank. Runs under its own <c>Guard</c> in the per-world tick roster.</summary>
    private void TickHealTanks(double dt)
    {
        if (_healTankBlockId == 0)
        {
            return;
        }

        foreach (var session in JoinedInActiveWorld())
        {
            var p = session.State;
            if (InSpace(p.PlayerId))
            {
                continue; // piloting in space, not on foot
            }

            session.HealTankScanIn -= dt;
            if (session.HealTankScanIn <= 0)
            {
                session.HealTankScanIn = HealTankScanInterval;
                session.NearHealTank = NearHealTankBlock(p);
            }

            if (!session.NearHealTank || p.GodMode)
            {
                continue; // god mode is already pinned to full vitals by TickEnvironment
            }

            if (p.Health > 0f)
            {
                // Never revives a downed player (0 HP) — that would outrun the death check.
                p.Health = System.Math.Min(100f, p.Health + (float)(dt * HealTankHealPerSecond));
            }

            p.Hunger = System.Math.Min(100f, p.Hunger + (float)(dt * HealTankFeedPerSecond));

            if (!p.Stealthed && !p.Jetpacking)
            {
                // The one place the suit recharges off-ship. Don't recharge while actively spending it.
                p.SuitEnergy = System.Math.Min(100f, p.SuitEnergy + (float)(dt * HealTankEnergyPerSecond));
            }
        }
    }

    /// <summary>Box scan of the world grid for a heal tank around the player (wider sibling of
    /// <c>NearStationBlock</c> — a regen field should cover a small room, not just arm's reach).</summary>
    private bool NearHealTankBlock(PlayerState player)
    {
        int px = (int)System.Math.Floor(player.Position.X);
        int py = (int)System.Math.Floor(player.Position.Y);
        int pz = (int)System.Math.Floor(player.Position.Z);
        for (int dx = -HealTankRadius; dx <= HealTankRadius; dx++)
        {
            for (int dy = -HealTankRadiusY; dy <= HealTankRadiusY; dy++)
            {
                for (int dz = -HealTankRadius; dz <= HealTankRadius; dz++)
                {
                    if (_world.GetBlock(new Vector3i(px + dx, py + dy, pz + dz)).Value == _healTankBlockId)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>Test/util: expose the proximity scan (mirrors <see cref="BlockedByEnergyFenceForTest"/>).</summary>
    public bool NearHealTankForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && NearHealTankBlock(s.State);

    // --- Custom spawn point: E on a placed tank makes it home (issue #461) ---

    /// <summary>Reach for the E interaction on a placed tank (arm's length plus a step, like ship stations).</summary>
    private const float HealTankInteractReach = 5f;

    /// <summary>E on a placed heal tank: store a body-qualified home spawn. Only STORES the point — the
    /// death flow consuming it (respawn choice, ship fallback) is issue #462. The spawn position is the
    /// player's own standing spot, not the tank cell, so respawning never puts anyone inside the block.</summary>
    private void HandleSetSpawnPoint(PlayerSession session, SetSpawnPointIntent intent)
    {
        var p = session.State;
        var cell = new Vector3i(intent.X, intent.Y, intent.Z);
        if (_healTankBlockId == 0
            || InSpace(p.PlayerId)
            || _world.GetBlock(cell).Value != _healTankBlockId
            || WrapDistSq(p.Position, cell) > HealTankInteractReach * HealTankInteractReach)
        {
            Reject(session, "spawn_point", "No heal tank in reach.");
            return;
        }

        p.CustomSpawnBodyId = session.CurrentLocationId;
        p.CustomSpawnPoint = p.Position;
        p.CustomSpawnLabel = ResolveCustomSpawnLabel(session);
        _repo.SavePlayer(p);
        Send(session, new ServerMessage { Text = "@spawn_set" }); // token → localized toast client-side
    }

    /// <summary>Cosmetic label for the stored spawn: the boarded station's name, else the name of the
    /// base whose zone the player stands in, else empty (the client falls back to a generic word).</summary>
    private string ResolveCustomSpawnLabel(PlayerSession session)
    {
        var p = session.State;
        if (_boardedStation.TryGetValue(p.PlayerId, out var stationId)
            && _stationsById.TryGetValue(stationId, out var station))
        {
            return station.Name;
        }

        var cell = new Vector3i(
            (int)System.Math.Floor(p.Position.X),
            (int)System.Math.Floor(p.Position.Y),
            (int)System.Math.Floor(p.Position.Z));
        foreach (var b in _bases)
        {
            if (b.Planet == session.CurrentLocationId && WithinBaseZone(b.Cell, cell))
            {
                return b.Name;
            }
        }

        return string.Empty;
    }

    /// <summary>Runs the authoritative spawn-point setter for a player (used by local play / tests).</summary>
    public void SetSpawnPoint(string playerId, int x, int y, int z)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleSetSpawnPoint(session, new SetSpawnPointIntent { X = x, Y = y, Z = z });
        }
    }
}
