// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

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
    internal const string BedBlock = "bed";

    /// <summary>Radius (blocks, per axis — a box, matching the crafting-station scans) around a placed
    /// heal tank within which players regenerate. Vertical reach is smaller: one room, not a tower.</summary>
    private const int HealTankRadius = 6;
    private const int HealTankRadiusY = 3;

    private const float HealTankHealPerSecond = 4f;   // 2x the breathable-air regen
    private const float HealTankFeedPerSecond = 6f;   // slower than ship life support (10/s), still generous
    private const float HealTankEnergyPerSecond = 10f; // half the aboard-ship suit recharge (20/s)

    /// <summary>The bed's weak heal (#804): HEAL ONLY — no feeding, no suit recharge. Deliberately well
    /// under the tank's 4/s so the researched heal tank stays the clear upgrade; the hand-tier bed is
    /// the survival crutch that keeps an early base from being a death trap.</summary>
    private const float BedHealPerSecond = 1.5f;

    /// <summary>Seconds between proximity rescans per player (the regen itself applies every tick).</summary>
    private const double HealTankScanInterval = 1.0;

    private ushort _healTankBlockId;
    private ushort _bedBlockId;

    /// <summary>Resolves the heal-tank + bed block ids once per content load (0 = block missing).</summary>
    private void InitHealTanks()
    {
        _healTankBlockId = _content.GetBlock(HealTankBlock)?.NumericId.Value ?? 0;
        _bedBlockId = _content.GetBlock(BedBlock)?.NumericId.Value ?? 0;
    }

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
                session.NearBed = !session.NearHealTank && _bedBlockId != 0
                    && AnchorNear(p.Position, loadedOnly: true, _bedBlockId);
            }

            if (p.GodMode || p.Health <= 0f)
            {
                // God mode is already pinned to full vitals; a downed player (0 HP, e.g. awaiting the
                // respawn choice) gets nothing — the tank never outruns the death flow.
                continue;
            }

            if (session.NearHealTank)
            {
                p.Health = System.Math.Min(100f, p.Health + (float)(dt * HealTankHealPerSecond));
                p.Hunger = System.Math.Min(100f, p.Hunger + (float)(dt * HealTankFeedPerSecond));

                if (!p.Stealthed && !p.Jetpacking)
                {
                    // The one place the suit recharges off-ship. Don't recharge while actively spending it.
                    p.SuitEnergy = System.Math.Min(100f, p.SuitEnergy + (float)(dt * HealTankEnergyPerSecond));
                }
            }
            else if (session.NearBed)
            {
                // The bed (#804): a slow heal and nothing else — no feeding, no suit energy. Resting at
                // a lived-in camp mends wounds; the machines still do everything beyond that.
                p.Health = System.Math.Min(100f, p.Health + (float)(dt * BedHealPerSecond));
            }
        }
    }

    /// <summary>Box scan of the world grid for a heal tank around the player (wider sibling of
    /// <c>NearStationBlock</c> — a regen field should cover a small room, not just arm's reach).</summary>
    private bool NearHealTankBlock(PlayerState player) => HealTankNear(player.Position, loadedOnly: true);

    /// <summary>Test/util: expose the proximity scan (mirrors <see cref="BlockedByEnergyFenceForTest"/>).</summary>
    public bool NearHealTankForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s && NearHealTankBlock(s.State);

    // --- Custom spawn point: E on a placed tank makes it home (issue #461) ---

    /// <summary>Reach for the E interaction on a placed tank (arm's length plus a step, like ship stations).</summary>
    private const float HealTankInteractReach = 5f;

    /// <summary>E on a placed heal tank OR bed (#804): store a body-qualified home spawn. Only STORES the
    /// point — the death flow consuming it (respawn choice, ship fallback) is issue #462. The spawn position
    /// is the player's own standing spot, not the tank cell, so respawning never puts anyone inside the block.</summary>
    private void HandleSetSpawnPoint(PlayerSession session, SetSpawnPointIntent intent)
    {
        var p = session.State;
        var cell = new Vector3i(intent.X, intent.Y, intent.Z);
        ushort cellBlock = _world.GetBlock(cell).Value;
        bool isAnchor = (cellBlock != 0)
            && ((cellBlock == _healTankBlockId && _healTankBlockId != 0)
                || (cellBlock == _bedBlockId && _bedBlockId != 0));
        if (!isAnchor
            || InSpace(p.PlayerId)
            || WrapDistSq(p.Position, cell) > HealTankInteractReach * HealTankInteractReach)
        {
            Reject(session, "spawn_point", "@srv.misc.no_heal_tank"); // locale text covers tank AND bed (#804)
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

    /// <summary>Answers a pending respawn choice for a player (used by local play / tests).</summary>
    public void ChooseRespawn(string playerId, bool useCustomSpawn)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleRespawnChoice(session, new RespawnChoiceIntent { UseCustomSpawn = useCustomSpawn });
        }
    }

    // --- Respawning at the home spawn (issue #462) ---

    /// <summary>Attempts the home-spawn relocation after a death. Returns false when the home is gone or
    /// unreachable (station decommissioned, heal tank mined, body unknown) — the caller then runs the ship
    /// respawn, which always works. Vitals are already reset by <c>CompleteRespawn</c>.</summary>
    private bool TryCustomRespawn(PlayerSession session, string reason, bool salvaged, bool sameWorld)
    {
        var p = session.State;
        if (string.IsNullOrEmpty(p.CustomSpawnBodyId))
        {
            return false;
        }

        if (p.CustomSpawnBodyId.StartsWith("station:", System.StringComparison.Ordinal))
        {
            return TryRespawnAtHomeStation(session, p.CustomSpawnBodyId.Substring("station:".Length), reason, salvaged);
        }

        var body = _galaxy?.FindBody(p.CustomSpawnBodyId);
        if (body is null || string.IsNullOrEmpty(body.PlanetType))
        {
            return false;
        }

        // Died on foot on the home body itself → a plain snap, no reload. The tank (or bed, #804) must
        // still stand — a razed home falls back to the ship rather than dropping the player at a ruin.
        if (sameWorld && session.CurrentLocationId == p.CustomSpawnBodyId)
        {
            if (!HomeAnchorNear(p.CustomSpawnPoint, loadedOnly: false))
            {
                return false;
            }

            p.Position = p.CustomSpawnPoint;
            p.AboardShip = false;
            Send(session, new RespawnNotice
            {
                X = p.Position.X,
                Y = p.Position.Y,
                Z = p.Position.Z,
                Reason = reason,
                SalvageCapsuleDropped = salvaged,
                Died = true,
            });
            SendInventory(session);
            SendPlayerState(session);
            return true;
        }

        // Full transition to the home body — mirrors RecoverToShip, but lands at the base. The ship
        // re-homes to this body too (TODO R4 "with your ship"): it keeps the standing invariant that the
        // parked ship is on the player's world, so teleporter/cargo/launch all keep working.
        LeaveSpace(p.PlayerId); // exit any flight view (sends SpaceClosed if in one)
        LoadWorld(body.PlanetType, p.CustomSpawnBodyId);
        SetCurrent(session);
        if (!HomeAnchorNear(p.CustomSpawnPoint, loadedOnly: false))
        {
            return false; // home tank/bed gone → the caller's ship path reloads the ship's world
        }

        if (_ship is not null)
        {
            _ship.CurrentLocationId = p.CustomSpawnBodyId;
        }

        if (_config.PlaceStarterShip)
        {
            PlaceLandedShip();
        }

        session.CurrentLocationId = p.CustomSpawnBodyId;
        MarkArrivedOnBody(session, p.CustomSpawnBodyId);
        p.Position = p.CustomSpawnPoint;
        p.RespawnPoint = _shipPlaced ? _healTank : p.RespawnPoint;
        p.AboardShip = false;
        session.SentChunks.Clear();

        var (systemName, planetName) = ActiveLocationNames();
        Send(session, new WorldReset { PlanetType = body.PlanetType, PlanetName = planetName, SystemName = systemName, Hyperjump = false });
        Send(session, new RespawnNotice
        {
            X = p.Position.X,
            Y = p.Position.Y,
            Z = p.Position.Z,
            Reason = reason,
            SalvageCapsuleDropped = salvaged,
            Died = true,
        });
        SendPlayerState(session);
        SendEnvironment(session);
        SendInventory(session);
        SendLandedShips(session);
        SendPlanetPois(session);
        SendCreatures(session);
        SendContainers(session);
        SendNpcs(session);
        return true;
    }

    /// <summary>Home spawn inside a station: re-board the station world (same stamping path as docking /
    /// travel-screen boarding) and wake at the stored spot. False when the station no longer exists or is
    /// no longer boardable for this player — the caller falls back to the ship.</summary>
    private bool TryRespawnAtHomeStation(PlayerSession session, string stationId, string reason, bool salvaged)
    {
        var p = session.State;

        // Died inside the home station itself → just snap back to the stored spot (world already loaded,
        // station membership intact).
        if (_boardedStation.TryGetValue(p.PlayerId, out var boardedId) && boardedId == stationId)
        {
            p.Position = p.CustomSpawnPoint;
            Send(session, new RespawnNotice
            {
                X = p.Position.X,
                Y = p.Position.Y,
                Z = p.Position.Z,
                Reason = reason,
                SalvageCapsuleDropped = salvaged,
                Died = true,
            });
            SendInventory(session);
            SendPlayerState(session);
            return true;
        }

        var body = _galaxy?.FindBody(stationId);
        if (body is null || body.Kind != CelestialKind.SpaceStation || !CanBoardStation(session, stationId))
        {
            return false; // decommissioned / unknown / no longer allied → ship fallback
        }

        if (!_stationsById.TryGetValue(stationId, out var station))
        {
            station = GetOrCreateStation(body.Id, body.Name, 0);
        }

        // Tear down any current presence, then run the shared boarding transition (stamps the interior,
        // registers doors/NPCs, sends WorldReset + StationBoarded) — the same path as TravelToStation.
        LeaveSpace(p.PlayerId);
        if (_playerInstance.TryGetValue(p.PlayerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst))
        {
            inst.Players.Remove(p.PlayerId);
            _playerInstance.Remove(p.PlayerId);
        }

        _boardedStation.Remove(p.PlayerId);
        _dockedFromEva.Remove(p.PlayerId);
        _boardedReturn[p.PlayerId] = StationReturnLocation(stationId, body);
        EnterBoardedStation(session, station);

        // Wake at the stored home spot instead of the arrivals pad.
        p.Position = p.CustomSpawnPoint;
        Send(session, new RespawnNotice
        {
            X = p.Position.X,
            Y = p.Position.Y,
            Z = p.Position.Z,
            Reason = reason,
            SalvageCapsuleDropped = salvaged,
            Died = true,
        });
        SendPlayerState(session);
        return true;
    }

    /// <summary>True if a placed heal tank stands within the regen-field box around <paramref name="pos"/>.
    /// The per-tick regen scan passes <paramref name="loadedOnly"/> so it never forces chunk loads (a
    /// placed tank always sits in a chunk its nearby player keeps resident anyway); the home-spawn
    /// validation at respawn time reads THROUGH the cache — the home chunks are usually not resident when
    /// the player died elsewhere, and loading the few cells there is exactly what the check is for.</summary>
    private bool HealTankNear(Vector3f pos, bool loadedOnly)
        => AnchorNear(pos, loadedOnly, _healTankBlockId);

    /// <summary>True if a home-spawn ANCHOR — a heal tank or a bed (#804) — stands near <paramref name="pos"/>.
    /// The respawn flow validates against this: a razed home (tank mined AND bed gone) falls back to the ship.</summary>
    private bool HomeAnchorNear(Vector3f pos, bool loadedOnly)
        => AnchorNear(pos, loadedOnly, _healTankBlockId, _bedBlockId);

    /// <summary>Shared box scan for the regen/home checks: true when a block matching <paramref name="a"/>
    /// (or optional <paramref name="b"/>) stands within the field box around <paramref name="pos"/>.</summary>
    private bool AnchorNear(Vector3f pos, bool loadedOnly, ushort a, ushort b = 0)
    {
        if (a == 0 && b == 0)
        {
            return false;
        }

        int px = (int)System.Math.Floor(pos.X);
        int py = (int)System.Math.Floor(pos.Y);
        int pz = (int)System.Math.Floor(pos.Z);
        for (int dx = -HealTankRadius; dx <= HealTankRadius; dx++)
        {
            for (int dy = -HealTankRadiusY; dy <= HealTankRadiusY; dy++)
            {
                for (int dz = -HealTankRadius; dz <= HealTankRadius; dz++)
                {
                    var cell = new Vector3i(px + dx, py + dy, pz + dz);
                    ushort v = (loadedOnly ? _world.GetBlockIfLoaded(cell) : _world.GetBlock(cell)).Value;
                    if (v != 0 && ((a != 0 && v == a) || (b != 0 && v == b)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
