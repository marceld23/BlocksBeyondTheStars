// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Crystal Net 2 (#2251): the devices and ports that came after the first net —
/// <list type="bullet">
/// <item><b>Twinned blocks</b> (#2261, #2264): a phase block, a trapdoor, a force field, a campfire and a forge swap with
/// their open / unlit twin like a lamp, keeping dye, glow and form. Signal ON = passage open (for a fire: burning). A
/// block that would close onto a player, an NPC or a creature waits and tries again; a floor that opens grants the players
/// above it a short fall grace — a moving block never hurts (kid rule).</item>
/// <item><b>Statuses of ports</b> (#2261): the heal tank ("someone heals here"), a bed ("someone lies here"), a seat
/// ("someone sits here") — read by a Device Eye.</item>
/// <item><b>New devices</b> (#2263): the display, the dice block, the signal sender / receiver and the environment sensor.</item>
/// <item><b>Moving blocks</b> (#2265): the bridge motor and the piston.</item>
/// </list>
/// Cost: every swap is one <see cref="BlockChanged"/>, rate-limited like a lamp; the bridge places or removes one deck
/// block per step; pistons share a per-tick budget of pushes.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Pushes so far in this tick across every piston of the world (reset by <c>TickCrystalNet</c>).</summary>
    private int _pistonPushesThisTick;

    /// <summary>The dice block's roll — fun, not a save's fate, so a plain generator.</summary>
    private readonly Random _crystalDice = new();

    private const string BridgeDeckBlock = "bridge_deck";
    private const string PistonHeadBlock = "piston_head";

    // ------------------------------------------------------------------------------------------------------
    // Twinned blocks
    // ------------------------------------------------------------------------------------------------------

    /// <summary>Whether a kind swaps its block with a twin on the net (a lamp, a phase block, a trapdoor, a field, a fire).</summary>
    private static bool IsCrystalTwinKind(CrystalDeviceKind kind)
        => kind == CrystalDeviceKind.Light || CrystalNetRules.TwinKeyFor(kind, true) is not null;

    /// <summary>Swaps a twinned block to the twin its network level asks for. False when it could not act this beat (the
    /// chunk is not loaded, or the block would close onto someone) — the caller tries again on the next beat.</summary>
    private bool SwapCrystalTwin(ServerCrystalCell c, bool on)
    {
        if (c.Kind == CrystalDeviceKind.Light)
        {
            return SwapCrystalLight(c, on);
        }

        var id = CrystalReadBlock(c.Cell);
        var current = _content.BlockById(id);
        if (current is null || id.IsAir)
        {
            return false; // chunk not loaded (or the block is gone): try again next beat
        }

        string? wantKey = CrystalNetRules.TwinKeyFor(c.Kind, on);
        if (wantKey is null || wantKey == current.Key || CrystalNetRules.KindOfKey(current.Key) != c.Kind)
        {
            return true;
        }

        var want = _content.GetBlock(wantKey);
        if (want is null)
        {
            return true; // a pack without the twin: the block simply stays as it is
        }

        if (want.Solid && !current.Solid && CellOccupiedByBody(CrystalToWorld(c.Cell)))
        {
            return false; // kid rule: never close onto a player, an NPC or an animal — wait until the cell is free
        }

        var (tint, glow) = CrystalReadModifier(c.Cell);
        int shape = CrystalReadShape(c.Cell);
        if (c.Kind == CrystalDeviceKind.Trapdoor)
        {
            shape = on ? PropShapes.TrapdoorOpen(PropShapes.TrapdoorClosedFrom(shape)) : PropShapes.TrapdoorClosedFrom(shape);
        }

        if (!want.Solid && current.Solid)
        {
            GrantMovingFallGrace(CrystalToWorld(c.Cell)); // a floor that opens never hurts
        }

        CrystalWriteCell(c.Cell, want.NumericId, tint, glow, shape);
        c.BlockKey = want.Key;
        CrystalTwinFx(c, on);
        if (on && c.Kind == CrystalDeviceKind.PhaseBlock)
        {
            OnCrystalCircuitEvent(c.OwnerId, Shared.Missions.CircuitEvents.PhaseOpen); // #2258: a secret door opened
            if (CrystalNetRules.IsWorldOwner(c.OwnerId))
            {
                OnCrystalVaultOpened(c); // a crystal vault's door: "Safecracker" for everyone in the chamber
            }
        }

        return true;
    }

    /// <summary>One cell written by the net: the voxel, the wire, the station's own build (so a boarded station keeps it).</summary>
    private void CrystalWriteCell(Vector3i cell, BlockId id, int tint, int glow, int shape)
    {
        if (_crystalFrame is { } frame)
        {
            // #2268: aboard, the ship's structure — live only: a parked ship's net re-applies its levels after every
            // rebuild (landing, edit), so the swap never needs to reach the ship's stored design.
            frame.Rec.Structure.Set(cell, id, tint, glow, shape);
            BroadcastToWorld(new StructureBlockChanged { StructureId = frame.StructureId, X = cell.X, Y = cell.Y, Z = cell.Z, Block = id.Value, Tint = tint, Glow = glow, Shape = shape });
            return;
        }

        _world.SetBlock(cell, id, tint, glow, shape);
        BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = id.Value, Tint = tint, Glow = glow, Shape = shape });
        WriteBackStationCell(cell, id, tint, glow, shape);
        NudgeCreatureBodyChecks(cell);
    }

    /// <summary>A body stands in the cell (feet or head): a player, an NPC or a creature.</summary>
    private bool CellOccupiedByBody(Vector3i cell)
    {
        if (CellOccupiedByPlayer(cell) || CellOccupiedByNpc(cell))
        {
            return true;
        }

        foreach (var cr in _creatures)
        {
            var feet = cr.Position.ToBlock();
            if (feet == cell || new Vector3i(feet.X, feet.Y + 1, feet.Z) == cell)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>#2264: players standing on or just above a cell that opens fall without harm for a moment.</summary>
    private void GrantMovingFallGrace(Vector3i opened)
    {
        foreach (var s in JoinedInActiveWorld())
        {
            var p = s.State.Position;
            double dx = p.X - (opened.X + 0.5), dz = p.Z - (opened.Z + 0.5), dy = p.Y - opened.Y;
            if (dx * dx + dz * dz <= 2.25 && dy >= -0.5 && dy <= 3.5)
            {
                s.MovingFallGraceUntil = _uptime + CrystalNetRules.MovingFallGraceSeconds;
            }
        }
    }

    /// <summary>The sound and the shimmer of a swap the player should notice (lamps and fires switch silently).</summary>
    private void CrystalTwinFx(ServerCrystalCell c, bool on)
    {
        string? sound = c.Kind switch
        {
            CrystalDeviceKind.PhaseBlock => "phase_shimmer",
            CrystalDeviceKind.Trapdoor => on ? "trapdoor_open" : "trapdoor_close",
            CrystalDeviceKind.ForceField => on ? "force_field_off" : "force_field_on",
            _ => null,
        };
        if (sound is null)
        {
            return;
        }

        var at = CrystalWorldCentre(c.Cell);
        float x = at.X, y = at.Y, z = at.Z;
        BroadcastToWorld(new SoundFx { SoundId = sound, X = x, Y = y, Z = z, SourceId = c.Id });
        BroadcastToWorld(new WorldFx { Kind = c.Kind == CrystalDeviceKind.ForceField ? "field_flicker" : "phase_shimmer", X = x, Y = y, Z = z, Strength = on ? 1f : 0.6f });
    }

    /// <summary>A twinned block leaving the net goes back to its plain state (a closed phase block or trapdoor, a field that
    /// is on, a burning fire) — unless it would close onto someone, then it stays as it is.</summary>
    private void RestoreCrystalTwin(ServerCrystalCell c)
    {
        string? plain = CrystalNetRules.PlainKeyFor(c.Kind);
        if (plain is null)
        {
            return;
        }

        bool plainIsOn = CrystalNetRules.TwinKeyFor(c.Kind, true) == plain;
        SwapCrystalTwin(c, plainIsOn);
    }

    // ------------------------------------------------------------------------------------------------------
    // Port statuses and the environment sensor (sensor beat)
    // ------------------------------------------------------------------------------------------------------

    /// <summary>The sensor beat's reading for the Crystal Net 2 kinds; null for a kind it does not poll.</summary>
    private bool? CrystalSensorReads2(ServerCrystalCell c) => c.Kind switch
    {
        CrystalDeviceKind.HealTank => PresenceInBox(c.Cell, HealTankRadius, HealTankRadiusY, PresenceFilter.Players, c.OwnerId),
        CrystalDeviceKind.Bed => PresenceInCell(c.Cell, PresenceFilter.Anyone, c.OwnerId) && AnyoneButCreatures(c.Cell),
        CrystalDeviceKind.Seat => SeatTaken(c.Cell),
        CrystalDeviceKind.FlowerPot => HydroTrayRipe(c.Cell), // a pot reports "ripe" like a hydro tray
        CrystalDeviceKind.EnvironmentSensor => EnvironmentSensorReads(c),
        CrystalDeviceKind.ShipSensor => ShipSensorReads(c), // #2268
        _ => null,
    };

    /// <summary>A bed counts players and NPCs lying on it, never an animal walking across.</summary>
    private bool AnyoneButCreatures(Vector3i cell)
        => PresenceInCell(cell, PresenceFilter.Players, string.Empty) || PresenceInCell(cell, PresenceFilter.Npcs, string.Empty);

    /// <summary>Someone matching the filter inside a box around a cell (the heal tank's field).</summary>
    private bool PresenceInBox(Vector3i cell, int radius, int radiusY, PresenceFilter filter, string owner)
    {
        foreach (var p in _crystalPresence)
        {
            if (!PresenceMatches(filter, p.Kind, p.PlayerId, owner))
            {
                continue;
            }

            var b = p.Pos.ToBlock();
            if (Math.Abs(b.X - cell.X) <= radius && Math.Abs(b.Y - cell.Y) <= radiusY && Math.Abs(b.Z - cell.Z) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>#2263: what the environment sensor sees where it stands.</summary>
    private bool EnvironmentSensorReads(ServerCrystalCell c)
    {
        var at = new Vector3f(c.Cell.X + 0.5f, c.Cell.Y + 0.5f, c.Cell.Z + 0.5f);
        switch ((EnvironmentSensorMode)c.Mode)
        {
            case EnvironmentSensorMode.NoAir:
                return !BreathableAirAt(c.Cell) && !(AtmospherePresent && AtmosphereBreathable);
            case EnvironmentSensorMode.TooHot:
                return CrystalTemperatureAt(at) > 35f;
            case EnvironmentSensorMode.TooCold:
                return CrystalTemperatureAt(at) < 0f;
            case EnvironmentSensorMode.Storm:
                return BiomeWeatherAt(at).Intensity >= 0.6f; // a storm, a blizzard, a sandstorm — any weather at full strength
            case EnvironmentSensorMode.ToxicAir:
                return ActiveTraits.CorrosiveAir && _world.Planet.AirDamagePerSecond > 0 && !BreathableAirAt(c.Cell);
            case EnvironmentSensorMode.HostileInBase:
            case EnvironmentSensorMode.AllianceInBase:
                {
                    var home = BaseZoneHolding(c.Cell);
                    if (home is null)
                    {
                        return false;
                    }

                    bool hostile = (EnvironmentSensorMode)c.Mode == EnvironmentSensorMode.HostileInBase;
                    foreach (var p in _crystalPresence)
                    {
                        bool match = hostile
                            ? p.Kind == PresenceFilter.Hostile
                            : PresenceMatches(PresenceFilter.Owner, p.Kind, p.PlayerId, c.OwnerId);
                        if (match && WithinBaseZone(home.Cell, p.Pos.ToBlock()))
                        {
                            return true;
                        }
                    }

                    return false;
                }

            default:
                return false;
        }
    }

    /// <summary>The air temperature at a point as a player standing there would feel it (weather, day, local fires).</summary>
    private float CrystalTemperatureAt(Vector3f at)
    {
        var (weather, _) = BiomeWeatherAt(at);
        return ApplyLocalSources(at, CurrentTemperature(weather, _dayFraction, at));
    }

    /// <summary>The founded base whose zone holds this cell on the current world, or null.</summary>
    private ServerBase? BaseZoneHolding(Vector3i cell)
    {
        string body = _world.LocationId;
        foreach (var b in _bases)
        {
            if (b.Planet == body && WithinBaseZone(b.Cell, cell))
            {
                return b;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------------------------------------------
    // Display, dice block, signal sender / receiver (logic beat)
    // ------------------------------------------------------------------------------------------------------

    /// <summary>#2263: the display follows its network — its light mirrors the level (the client shows the symbol / the
    /// text from it), and a counter display counts the network's rising edges.</summary>
    private void UpdateCrystalDisplay(ServerCrystalCell c, CrystalNetwork net)
    {
        if (c.Output != net.Level)
        {
            c.Output = net.Level;
            CrystalNet.DeviceListDirty = true;
        }

        if ((DisplayMode)c.Mode == DisplayMode.Counter && net.Level && !net.PrevLevel)
        {
            int n = Math.Min(CrystalNetRules.DisplayCounterMax, CrystalConfigInt(c.Config, "n", 0) + 1);
            c.Config = CrystalConfigWith(c.Config, "n", n.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SaveCrystalCell(c);
            CrystalNet.DeviceListDirty = true;
        }
    }

    /// <summary>#2263: a dice block answers each rising edge of its inputs with a pulse — by chance, 1 in 2 / 3 / 4 / 6.</summary>
    private void StepCrystalDice(ServerCrystalCell c, List<bool> inputs)
    {
        bool input = inputs.Any(i => i);
        if (input && !c.DiceLastInput)
        {
            int chance = CrystalNetRules.DiceChances[Math.Max(0, Math.Min(CrystalNetRules.DiceChances.Length - 1, c.Mode))];
            bool win = _crystalDice.Next(chance) == 0;
            var at = CrystalWorldCentre(c.Cell);
            BroadcastToWorld(new SoundFx { SoundId = "dice_roll", X = at.X, Y = at.Y, Z = at.Z, SourceId = c.Id });
            if (win)
            {
                PulseCrystalCell(c);
                BroadcastToWorld(new WorldFx { Kind = "dice_win", X = at.X, Y = at.Y + 0.5f, Z = at.Z, Strength = 1f });
            }
        }

        c.DiceLastInput = input;
    }

    /// <summary>#2263: a signal receiver repeats its paired sender's level (one beat later) — or what a remote control set.</summary>
    private void StepCrystalReceiver(ServerCrystalCell c)
    {
        bool sender = CrystalPairCell(c.Config) is { } at && CrystalNet.Cells.TryGetValue(at, out var s)
            && s.Kind == CrystalDeviceKind.SignalSender && !s.Inert && CanConfigureCrystal(s, c.OwnerId, false)
            && s.NetId != 0 && CrystalNet.Nets.TryGetValue(s.NetId, out var net) && net.Level;
        bool next = sender || c.RemoteOn;
        if (next != c.Output)
        {
            c.Output = next;
            CrystalNet.DeviceListDirty = true;
            if (next)
            {
                var ping = CrystalWorldCentre(c.Cell);
                BroadcastToWorld(new WorldFx { Kind = "signal_ping", X = ping.X, Y = ping.Y + 0.5f, Z = ping.Z, Strength = 0.6f });
            }
        }
    }

    /// <summary>#2263: the remote control flips the receiver it is paired with. Owner / alliance only, same world.</summary>
    private bool FlipCrystalRemote(PlayerSession session, Vector3i receiverCell)
    {
        if (!CrystalNet.Cells.TryGetValue(receiverCell, out var r) || r.Kind != CrystalDeviceKind.SignalReceiver || r.Inert)
        {
            return false;
        }

        if (!CanOperateCrystal(r, session.State.PlayerId, session.State.IsAdmin))
        {
            Reject(session, "crystal", "@srv.crystal.owner_only");
            return true;
        }

        r.RemoteOn = !r.RemoteOn;
        r.Config = CrystalConfigWith(r.Config, "remote", r.RemoteOn ? "1" : "0");
        SaveCrystalCell(r);
        CrystalNet.DeviceListDirty = true;
        var at = session.State.Position;
        Send(session, new SoundFx { SoundId = "remote_click", X = at.X, Y = at.Y + 1f, Z = at.Z, SourceId = r.Id });
        Send(session, new ServerMessage { Text = r.RemoteOn ? "@srv.crystal.remote_on" : "@srv.crystal.remote_off" });
        return true;
    }

    /// <summary>The remote control's item key (#2263).</summary>
    internal const string RemoteControlItem = "remote_control";

    /// <summary>#2263: the remote control. Used while aiming at a signal receiver within reach that the player may operate,
    /// it PAIRS with it; used anywhere else it flips the paired receiver — on the same world. False when nothing came of
    /// it (no cooldown, no effect). #2268: a receiver aboard the own ship pairs by the ship (<c>@store|cell</c>), so the
    /// remote reaches it wherever the ship is parked.</summary>
    private bool UseRemoteControl(PlayerSession session, Vector3f target)
    {
        var p = session.State;
        var aimed = NearestCrystalReceiver(target);
        CrystalShipFrame? aimedFrame = null;
        if (aimed is null)
        {
            foreach (var frame in CrystalShipFrames.Values)
            {
                InCrystalFrame(frame, () => aimed = NearestCrystalReceiver(target));
                if (aimed is not null)
                {
                    aimedFrame = frame;
                    break;
                }
            }
        }

        if (aimed is not null && WithinReach(p, aimedFrame is null ? aimed.Cell : InFrame(aimedFrame, () => CrystalToWorld(aimed.Cell))))
        {
            if (!CanOperateCrystal(aimed, p.PlayerId, p.IsAdmin))
            {
                Reject(session, "crystal", "@srv.crystal.owner_only");
                return false;
            }

            p.RemoteReceiver = (aimedFrame is null ? _world.LocationId : "@" + aimedFrame.StoreId) + "|" + CrystalPairValue(aimed.Cell);
            _repo.SavePlayer(p);
            Send(session, new ServerMessage { Text = "@srv.crystal.remote_paired:" + (aimed.Label.Length > 0 ? aimed.Label : "?") });
            ShipAiHintOnce(session, "crystal_remote");
            return true;
        }

        string pairing = p.RemoteReceiver;
        int bar = pairing.IndexOf('|');
        if (bar <= 0)
        {
            Reject(session, "crystal", "@srv.crystal.remote_unpaired");
            return false;
        }

        string where = pairing.Substring(0, bar);
        var shipFrame = where.StartsWith("@", StringComparison.Ordinal)
            ? CrystalShipFrames.Values.FirstOrDefault(f => f.Rec.Placed && "@" + f.StoreId == where)
            : null;
        if ((shipFrame is null && where != _world.LocationId) || CrystalPairCell("pair=" + pairing.Substring(bar + 1)) is not { } cell)
        {
            Reject(session, "crystal", "@srv.crystal.remote_far");
            return false;
        }

        if (!(shipFrame is null ? FlipCrystalRemote(session, cell) : InFrame(shipFrame, () => FlipCrystalRemote(session, cell))))
        {
            Reject(session, "crystal", "@srv.crystal.remote_gone");
            return false;
        }

        return true;
    }

    /// <summary>The signal receiver whose cell is nearest to an aim point (within one block of it), or null.</summary>
    private ServerCrystalCell? NearestCrystalReceiver(Vector3f at)
    {
        ServerCrystalCell? best = null;
        double bestSq = 1.0;
        foreach (var c in CrystalNet.Cells.Values)
        {
            if (c.Kind != CrystalDeviceKind.SignalReceiver || c.Inert)
            {
                continue;
            }

            double d = WrapDistSq(at, CrystalWorldCentre(c.Cell));
            if (d <= bestSq)
            {
                bestSq = d;
                best = c;
            }
        }

        return best;
    }

    // ------------------------------------------------------------------------------------------------------
    // Bridge motor (#2265)
    // ------------------------------------------------------------------------------------------------------

    private int BridgeLength(ServerCrystalCell c)
        => Math.Max(CrystalNetRules.BridgeMinLength, Math.Min(CrystalNetRules.BridgeMaxLength, CrystalConfigInt(c.Config, "len", CrystalNetRules.BridgeDefaultLength)));

    /// <summary>A bridge motor's beat: held ON it extends one deck block per step until its length (or an obstacle); OFF it
    /// pulls the deck back in, last block first.</summary>
    private void BridgeMotorBeat(ServerCrystalCell c, bool held)
    {
        if (_uptime < c.NextBeat)
        {
            return;
        }

        var face = CrystalNetRules.OutputFace(c.Yaw);
        int len = BridgeLength(c);
        if (held && c.Extended < len)
        {
            var target = c.Cell + face * (c.Extended + 1);
            var id = _world.GetBlock(target);
            if (!id.IsAir || BridgeCellProtected(target, c.OwnerId) || CellOccupiedByBody(target))
            {
                SetCrystalBlocked(c, true); // something in the way: the deck stops here (amber light), tries again
                c.NextBeat = _uptime + CrystalNetRules.ActuatorMinIntervalSeconds;
                return;
            }

            var deck = _content.GetBlock(BridgeDeckBlock);
            if (deck is null)
            {
                return;
            }

            CrystalWriteCell(target, deck.NumericId, 0, 0, 0);
            c.Extended++;
            SaveBridgeState(c);
            SetCrystalBlocked(c, c.Extended >= len); // amber = the bridge is fully out
            c.NextBeat = _uptime + CrystalNetRules.BridgeStepSeconds;
            BridgeFx(c, target);
            return;
        }

        if (!held && c.Extended > 0)
        {
            BridgeRetractOne(c, face);
            c.NextBeat = _uptime + CrystalNetRules.BridgeStepSeconds;
        }
    }

    private void BridgeRetractOne(ServerCrystalCell c, Vector3i face)
    {
        var target = c.Cell + face * c.Extended;
        var def = _content.BlockById(_world.GetBlock(target));
        if (def?.Key == BridgeDeckBlock)
        {
            GrantMovingFallGrace(target);
            CrystalWriteCell(target, BlockId.Air, 0, 0, 0);
            OnSupportRemoved(target);
            OnFluidRemoved(target);
            BridgeFx(c, target);
        }

        c.Extended--;
        SaveBridgeState(c);
        SetCrystalBlocked(c, false);
    }

    /// <summary>The whole deck back in at once (the motor was mined or turned).</summary>
    private void BridgeRetractAll(ServerCrystalCell c)
    {
        var face = CrystalNetRules.OutputFace(c.Yaw);
        while (c.Extended > 0)
        {
            BridgeRetractOne(c, face);
        }
    }

    private void SaveBridgeState(ServerCrystalCell c)
    {
        c.Config = CrystalConfigWith(c.Config, "ext", c.Extended.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SaveCrystalCell(c);
        CrystalNet.DeviceListDirty = true;
    }

    private void BridgeFx(ServerCrystalCell c, Vector3i at)
    {
        BroadcastToWorld(new SoundFx { SoundId = "bridge_motor", X = c.Cell.X + 0.5f, Y = c.Cell.Y + 0.5f, Z = c.Cell.Z + 0.5f, SourceId = c.Id });
        BroadcastToWorld(new WorldFx { Kind = "motor_sparks", X = at.X + 0.5f, Y = at.Y + 0.5f, Z = at.Z + 0.5f, Strength = 0.5f });
    }

    /// <summary>Cells a bridge deck may never fill: someone else's base, a settlement, a station hull, a ship, the Guardian.</summary>
    private bool BridgeCellProtected(Vector3i p, string ownerId)
        => IsShipBlock(p) || IsSettlementProtected(p, BlockId.Air) || IsStationBlock(p) || IsGuardianCoreProtected(p)
           || IsBaseProtected(p, ownerId, false);

    // ------------------------------------------------------------------------------------------------------
    // Piston (#2265)
    // ------------------------------------------------------------------------------------------------------

    /// <summary>The piston pushes the line in front of it one cell forward (up to <see cref="CrystalNetRules.PistonMaxPush"/>
    /// blocks) and fills the freed cell with its head. False when it cannot push now — something immovable in the line,
    /// no room at its end, a body in the way, or the world's budget for this tick is spent (it tries again).</summary>
    private bool PistonPush(ServerCrystalCell c)
    {
        if (c.Pushed)
        {
            return true;
        }

        if (_pistonPushesThisTick >= CrystalNetRules.MaxPistonPushesPerTick)
        {
            return false;
        }

        var face = CrystalNetRules.OutputFace(c.Yaw);
        var line = new List<Vector3i>();
        Vector3i? free = null;
        for (int i = 1; i <= CrystalNetRules.PistonMaxPush + 1; i++)
        {
            var p = c.Cell + face * i;
            var id = _world.GetBlock(p);
            if (id.IsAir)
            {
                free = p;
                break;
            }

            if (i > CrystalNetRules.PistonMaxPush || !PistonMayMove(p, id, c.OwnerId))
            {
                SetCrystalBlocked(c, true);
                return false;
            }

            line.Add(p);
        }

        if (free is not { } end || PistonCellProtected(end, c.OwnerId) || CellOccupiedByBody(end) || DoorFillsCell(end))
        {
            SetCrystalBlocked(c, true);
            return false;
        }

        // From the far end back: every block moves one cell forward, carrying its dye, glow and form.
        for (int i = line.Count - 1; i >= 0; i--)
        {
            var from = line[i];
            var to = from + face;
            var id = _world.GetBlock(from);
            var (tint, glow) = _world.GetModifier(from);
            int shape = _world.GetShape(from);
            _world.SetBlock(to, id, tint, glow, shape, c.OwnerId);
            BroadcastToWorld(new BlockChanged { X = to.X, Y = to.Y, Z = to.Z, Block = id.Value, Tint = tint, Glow = glow, Shape = shape });
            WriteBackStationCell(to, id, tint, glow, shape);
            NudgeCreatureBodyChecks(to);
        }

        var head = _content.GetBlock(PistonHeadBlock);
        if (head is not null)
        {
            CrystalWriteCell(c.Cell + face, head.NumericId, 0, 0, 0);
        }

        _pistonPushesThisTick++;
        c.Pushed = true;
        c.Config = CrystalConfigWith(c.Config, "out", "1");
        SaveCrystalCell(c);
        SetCrystalBlocked(c, false);
        var fx = c.Cell + face;
        BroadcastToWorld(new SoundFx { SoundId = "piston_push", X = fx.X + 0.5f, Y = fx.Y + 0.5f, Z = fx.Z + 0.5f, SourceId = c.Id });
        // Radius stays 0: on the client a WorldFx radius is the reach of a blast that throws players clear.
        BroadcastToWorld(new WorldFx { Kind = "piston_puff", X = fx.X + 0.5f, Y = fx.Y + 0.5f, Z = fx.Z + 0.5f, Strength = line.Count > 0 ? 1f : 0.5f });
        return true;
    }

    /// <summary>The head goes back in; a sticky piston pulls the block in front of its head back with it.</summary>
    private bool PistonRetract(ServerCrystalCell c, bool sticky)
    {
        if (!c.Pushed)
        {
            return true;
        }

        var face = CrystalNetRules.OutputFace(c.Yaw);
        var headCell = c.Cell + face;
        var headDef = _content.BlockById(_world.GetBlock(headCell));
        if (headDef?.Key == PistonHeadBlock)
        {
            var beyond = headCell + face;
            var beyondId = _world.GetBlock(beyond);
            if (sticky && !beyondId.IsAir && PistonMayMove(beyond, beyondId, c.OwnerId))
            {
                var (tint, glow) = _world.GetModifier(beyond);
                int shape = _world.GetShape(beyond);
                _world.SetBlock(headCell, beyondId, tint, glow, shape, c.OwnerId);
                BroadcastToWorld(new BlockChanged { X = headCell.X, Y = headCell.Y, Z = headCell.Z, Block = beyondId.Value, Tint = tint, Glow = glow, Shape = shape });
                WriteBackStationCell(headCell, beyondId, tint, glow, shape);
                CrystalWriteCell(beyond, BlockId.Air, 0, 0, 0);
                OnSupportRemoved(beyond);
                OnFluidRemoved(beyond);
            }
            else
            {
                CrystalWriteCell(headCell, BlockId.Air, 0, 0, 0);
                OnSupportRemoved(headCell);
                OnFluidRemoved(headCell);
            }
        }

        c.Pushed = false;
        c.Config = CrystalConfigWith(c.Config, "out", "0");
        SaveCrystalCell(c);
        SetCrystalBlocked(c, false);
        BroadcastToWorld(new SoundFx { SoundId = "piston_retract", X = headCell.X + 0.5f, Y = headCell.Y + 0.5f, Z = headCell.Z + 0.5f, SourceId = c.Id });
        return true;
    }

    /// <summary>Whether a piston may move this block: an ordinary block the owner (or their alliance) may edit — never a
    /// container, a Crystal Net cell, a door, a fluid, a plant, a bed, a form over several cells, bedrock or anything that
    /// cannot be mined.</summary>
    private bool PistonMayMove(Vector3i p, BlockId id, string ownerId)
    {
        var def = _content.BlockById(id);
        if (def is null || !def.Mineable || def.Liquid || IsFluid(id.Value) || IsFlora(id.Value) || IsContainerBlock(def.Key)
            || def.Key is "bed" or "crew_bunk" or "bedrock" or PistonHeadBlock or "lift_platform"
            || CrystalNet.Cells.ContainsKey(p) || DoorFillsCell(p) || MultiCellVoxelsOf(_world.GetShape(p)) is not null)
        {
            return false;
        }

        return !PistonCellProtected(p, ownerId);
    }

    /// <summary>Cells a piston may never touch: ships, settlements, stations, the Guardian core, factories, other players'
    /// bases. Unlike a drill it may move what a player built — that is what a piston is for.</summary>
    private bool PistonCellProtected(Vector3i p, string ownerId)
    {
        var id = _world.GetBlock(p);
        return IsShipBlock(p) || IsSettlementProtected(p, id) || IsStationBlock(p) || IsGuardianCoreProtected(p)
               || IsFactoryProtected(p, ownerId, false) || IsBaseProtected(p, ownerId, false);
    }
}
