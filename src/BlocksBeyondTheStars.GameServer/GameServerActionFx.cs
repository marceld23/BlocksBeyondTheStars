// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The VFX overhaul's server half (#2154/#2158): other players' tool actions become visible (a cosmetic
/// <see cref="FxIntent"/> relayed as <see cref="ActionFx"/>), gadget outcomes are confirmed by the server, and a defeated
/// creature is announced (<see cref="CreatureDefeated"/>) so the client can break it apart instead of letting it vanish
/// like a despawn. None of it changes the world — a dropped or refused message only costs an effect.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Burst of the per-player <see cref="FxIntent"/> token bucket (also its starting fill).</summary>
    internal const double FxRelayBurst = 12.0;

    /// <summary>Sustained relay rate per player — well above a real fire/swing/drill cadence, far below a flood.</summary>
    private const double FxRelayRatePerSecond = 12.0;

    /// <summary>Longest item / module key a relayed effect may carry.</summary>
    private const int FxMaxItemKeyLength = 48;

    /// <summary>Surface: how far an effect may start from the sender's authoritative position (hand / muzzle / drill
    /// tip plus the 10 Hz move-stream lag).</summary>
    private const float FxMaxStartOffset = 6f;

    /// <summary>Surface: how long an effect may be (muzzle → impact). The longest on-foot weapon reaches 32.</summary>
    private const float FxMaxLength = 64f;

    /// <summary>Who sees an effect: players within this many blocks of its start.</summary>
    private const float FxAudienceRadius = 96f;

    /// <summary>Space: a ship's weapons sit on the hull, metres away from the pilot pose the server tracks.</summary>
    private const float FxSpaceMaxStartOffset = 24f;

    /// <summary>Space: ship weapons reach up to 70; a travelling bolt is drawn to its impact.</summary>
    private const float FxSpaceMaxLength = 128f;

    /// <summary>Space: ships are seen from further away than people on foot (the radar reaches 130).</summary>
    private const float FxSpaceAudienceRadius = 160f;

    /// <summary>
    /// A player's cosmetic action (#2158). Validated cheaply and dropped silently unless: a known kind, a short key the
    /// content knows (an item, a ship module, or "" for bare hands), finite numbers, a token left in the player's bucket
    /// (~12/s), the start near the sender and the end near the start — all seam-aware on a surface. Relayed to every
    /// OTHER player of the sender's world within <see cref="FxAudienceRadius"/>, never back to the sender. In space
    /// (a ship or an EVA suit) positions are the flight instance's own frame, not the body's surface: the effect goes
    /// to the other pilots of the same instance, measured against their poses, and never to players on the ground.
    /// </summary>
    private void HandleFxIntent(PlayerSession session, FxIntent intent)
    {
        string key = intent.ItemKey ?? string.Empty;
        if (!FxActionKinds.IsValid(intent.Kind)
            || key.Length > FxMaxItemKeyLength
            || !IsFxKey(key)
            || !float.IsFinite(intent.FromX) || !float.IsFinite(intent.FromY) || !float.IsFinite(intent.FromZ)
            || !float.IsFinite(intent.ToX) || !float.IsFinite(intent.ToY) || !float.IsFinite(intent.ToZ))
        {
            return;
        }

        if (session.RespawnChoiceDeadline > 0 || session.State.Health <= 0f)
        {
            return; // lying dead awaiting the respawn choice — the corpse does not shoot
        }

        if (!TakeFxToken(session))
        {
            return;
        }

        var from = new Vector3f(intent.FromX, intent.FromY, intent.FromZ);
        var to = new Vector3f(intent.ToX, intent.ToY, intent.ToZ);
        string playerId = session.State.PlayerId;
        if (InSpace(playerId))
        {
            RelayFxInSpace(session, intent.Kind, key, from, to, intent.Hit);
            return;
        }

        if (WrapDistSq(from, session.State.Position) > FxMaxStartOffset * FxMaxStartOffset
            || WrapDistSq(from, to) > FxMaxLength * FxMaxLength)
        {
            return;
        }

        var fx = NewActionFx(session, intent.Kind, key, from, to, intent.Hit, outcome: false);
        SendFxNearby(fx, from, session, includeActor: false, DeliveryMode.Unreliable);
    }

    /// <summary>A successful gadget use (#2158): the server's confirmation that the heal / freeze / blast / scan really
    /// happened, to everyone in the user's world within <see cref="FxAudienceRadius"/> — the user included, whose client
    /// plays the outcome on this message instead of optimistically. A use in space reaches only the user (the surface
    /// and the flight instance do not share a frame). Reliable: the user's own feedback rides on it.</summary>
    private void BroadcastGadgetOutcome(PlayerSession user, string gadgetKey, Vector3f target)
    {
        var from = user.State.Position;
        var to = float.IsFinite(target.X) && float.IsFinite(target.Y) && float.IsFinite(target.Z) ? target : from;
        var fx = NewActionFx(user, FxActionKinds.Gadget, gadgetKey, from, to, hit: true, outcome: true);
        if (InSpace(user.State.PlayerId))
        {
            Send(user, fx);
            return;
        }

        SendFxNearby(fx, from, user, includeActor: true, DeliveryMode.ReliableOrdered);
    }

    /// <summary>A creature was killed (#2154) — by a player, a sentry or fire. Goes to the creature's world before the
    /// end-of-tick creature list drops it, on the same reliable channel, so the client can break the animal apart first.
    /// Despawns (far away, crowded out, boxed in, tamed, released, swallowed by a sandworm — that one has its own
    /// <c>WorldFx</c> "swallow") never send it.</summary>
    private void BroadcastCreatureDefeated(CombatEntity dead)
        => BroadcastToWorld(new CreatureDefeated { Id = dead.Id, X = dead.Position.X, Y = dead.Position.Y, Z = dead.Position.Z });

    /// <summary>The per-player <see cref="FxIntent"/> bucket on the server's uptime clock (the same clock the gadget and
    /// voice gates use): refilled at <see cref="FxRelayRatePerSecond"/>, capped at <see cref="FxRelayBurst"/>.</summary>
    private bool TakeFxToken(PlayerSession session)
    {
        double elapsed = _uptime - session.FxRefilledAt;
        session.FxRefilledAt = _uptime;
        if (elapsed > 0)
        {
            session.FxBudget = System.Math.Min(FxRelayBurst, session.FxBudget + (elapsed * FxRelayRatePerSecond));
        }

        if (session.FxBudget < 1.0)
        {
            return false;
        }

        session.FxBudget -= 1.0;
        return true;
    }

    /// <summary>A key a relayed effect may name: bare hands (""), an item, or a ship module — nothing made up.</summary>
    private bool IsFxKey(string key)
        => key.Length == 0 || _content.GetItem(key) is not null || _content.GetShipModule(key) is not null;

    /// <summary>Builds the wire message. On a planet surface the start is canonical (like a presence position) and the
    /// end is the start plus the short way round the longitude / latitude seam, so the receiver gets one straight
    /// segment whichever side of the seam it stands on. Stations and space keep their own frames untouched.</summary>
    private ActionFx NewActionFx(PlayerSession actor, byte kind, string key, Vector3f from, Vector3f to, bool hit, bool outcome)
    {
        double fromX = from.X, fromZ = from.Z, toX = to.X, toZ = to.Z;
        string playerId = actor.State.PlayerId;
        if (!InSpace(playerId) && !InStation(playerId))
        {
            int circ = _world.Circumference;
            fromX = WorldConstants.WrapX((double)from.X, circ);
            fromZ = WorldConstants.WrapZ((double)from.Z, circ);
            toX = fromX + WorldConstants.WrapDeltaX((double)to.X - from.X, circ);
            toZ = fromZ + WorldConstants.WrapDeltaZ((double)to.Z - from.Z, circ);
        }

        return new ActionFx
        {
            PlayerId = playerId,
            Kind = kind,
            ItemKey = key,
            FromX = (float)fromX,
            FromY = from.Y,
            FromZ = (float)fromZ,
            ToX = (float)toX,
            ToY = to.Y,
            ToZ = (float)toZ,
            Hit = hit,
            Outcome = outcome,
        };
    }

    /// <summary>Sends an effect to the players of the active world near <paramref name="from"/> (seam-aware). Players
    /// flying above the same body are skipped — their positions are a flight instance's, not the surface's.</summary>
    private void SendFxNearby(ActionFx fx, Vector3f from, PlayerSession actor, bool includeActor, DeliveryMode mode)
    {
        byte[]? payload = null;
        const double radiusSq = FxAudienceRadius * FxAudienceRadius;
        foreach (var s in JoinedInActiveWorld())
        {
            if (s.ConnectionId == actor.ConnectionId)
            {
                if (!includeActor)
                {
                    continue; // never echo a relayed action to its sender
                }
            }
            else if (InSpace(s.State.PlayerId) || WrapDistSq(from, s.State.Position) > radiusSq)
            {
                continue;
            }

            SendFxTo(s, fx, mode, ref payload);
        }
    }

    /// <summary>Relays a pilot's or spacewalker's effect to the other players of the same flight instance near it.</summary>
    private void RelayFxInSpace(PlayerSession sender, byte kind, string key, Vector3f from, Vector3f to, bool hit)
    {
        string playerId = sender.State.PlayerId;
        if (!_playerInstance.TryGetValue(playerId, out var instanceId) || !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            return;
        }

        if (from.DistanceSquared(PilotPositionIn(instance, playerId)) > FxSpaceMaxStartOffset * FxSpaceMaxStartOffset
            || from.DistanceSquared(to) > FxSpaceMaxLength * FxSpaceMaxLength)
        {
            return;
        }

        var fx = NewActionFx(sender, kind, key, from, to, hit, outcome: false);
        byte[]? payload = null;
        const float radiusSq = FxSpaceAudienceRadius * FxSpaceAudienceRadius;
        foreach (var otherId in instance.Players)
        {
            if (otherId == playerId || FindSessionByPlayerId(otherId) is not { Joined: true } other)
            {
                continue;
            }

            if (from.DistanceSquared(PilotPositionIn(instance, otherId)) <= radiusSq)
            {
                SendFxTo(other, fx, DeliveryMode.Unreliable, ref payload);
            }
        }
    }

    /// <summary>One send, encoding the bytes once per fan-out (the loopback takes the object itself).</summary>
    private void SendFxTo(PlayerSession recipient, ActionFx fx, DeliveryMode mode, ref byte[]? payload)
    {
        if (_objectTransport != null)
        {
            _objectTransport.SendMessage(recipient.ConnectionId, fx, mode);
            return;
        }

        payload ??= NetCodec.Encode(fx);
        _transport.Send(recipient.ConnectionId, payload, mode);
    }
}
