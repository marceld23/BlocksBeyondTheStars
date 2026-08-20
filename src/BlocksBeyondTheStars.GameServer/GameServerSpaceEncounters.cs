// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Peaceful space encounters (#1129): once in a while a space instance holds something that is neither a
/// trader nor a threat — a drifting <b>life pod</b> (fly close to rescue the survivor: a small thank-you,
/// a person who KNOWS you from then on, and a radio call later) or a shimmering <b>anomaly</b> (scan it for
/// knowledge + a lore text). Deliberately friendly: both run under every preset including family/peaceful —
/// nothing here is hostile, nothing shoots, so none of the combat gates apply (the trader precedent).
/// Rolled once per instance from the world seed + instance id, so the same save behaves the same way.
/// </summary>
public sealed partial class GameServer
{
    private const double EncounterChance = 0.30;
    private const float PodRescueRange = 14f;
    private const int KnowledgeAnomaly = 6;

    /// <summary>Per-instance encounter roll + lifecycle — slotted right after the bandit tick.</summary>
    private void TickSpaceEncounters(SpaceInstance instance)
    {
        if (!instance.EncounterRolled)
        {
            instance.EncounterRolled = true;
            var rng = EncounterRng(instance);
            if (rng.NextDouble() < EncounterChance)
            {
                instance.EncounterKind = rng.Next(2) == 0 ? 1 : 2; // 1 = life pod, 2 = anomaly
                instance.EncounterAt = _uptime + 40 + rng.Next(100);
            }
        }

        if (instance.EncounterKind == 0)
        {
            return;
        }

        if (string.IsNullOrEmpty(instance.EncounterId) && _uptime >= instance.EncounterAt)
        {
            SpawnEncounter(instance);
            return;
        }

        if (instance.EncounterKind == 1 && !string.IsNullOrEmpty(instance.EncounterId))
        {
            TickPodRescue(instance);
        }
    }

    private Random EncounterRng(SpaceInstance instance)
    {
        long s = _meta.Seed ^ WorldGenerator.StableHash("encounter:" + instance.Id);
        return new Random(unchecked((int)(s ^ (s >> 32))));
    }

    private void SpawnEncounter(SpaceInstance instance)
    {
        var rng = EncounterRng(instance);
        rng.Next(); // skip the roll draws consumed above
        rng.Next();
        double ang = rng.NextDouble() * Math.PI * 2.0;
        var pos = new Vector3f((float)Math.Cos(ang) * 110f, rng.Next(-20, 25), (float)Math.Sin(ang) * 110f);

        bool pod = instance.EncounterKind == 1;
        string id = (pod ? "pod:" : "anomaly:") + instance.Id;
        instance.Entities.Add(new CombatEntity
        {
            Id = id,
            Kind = pod ? CombatEntityKind.EscapePod : CombatEntityKind.Anomaly,
            Name = pod ? NameGenerator.Person(rng) : "???",
            Hostile = false,
            Hull = 1f,
            HullMax = 1f,
            Position = pos,
        });
        instance.EncounterId = id;
        BroadcastSpaceState(instance);

        foreach (var pid in instance.Players)
        {
            if (FindSessionByPlayerId(pid) is { Joined: true } s)
            {
                Send(s, new ServerMessage { Text = pod ? "@srv.encounter.pod" : "@srv.encounter.anomaly" });
            }
        }

        _log.Info($"Space encounter '{id}' appeared in {instance.Id}.");
    }

    /// <summary>Flying close to the pod IS the rescue — kid-friendly, no extra button. The survivor
    /// becomes a person the rescuer KNOWS, and calls to say thanks a little later.</summary>
    private void TickPodRescue(SpaceInstance instance)
    {
        var pod = instance.Entities.FirstOrDefault(e => e.Id == instance.EncounterId);
        if (pod is null)
        {
            instance.EncounterKind = 0; // gone (shouldn't happen — pods aren't targetable)
            return;
        }

        foreach (var pid in instance.Players)
        {
            var pilotPos = PilotPositionIn(instance, pid);
            if (pod.Position.DistanceSquared(pilotPos) > PodRescueRange * PodRescueRange
                || FindSessionByPlayerId(pid) is not { Joined: true } session)
            {
                continue;
            }

            instance.Entities.Remove(pod);
            instance.EncounterKind = 0;
            BroadcastSpaceState(instance);

            // A small thank-you (never power creep) …
            Serve(session);
            var pool = new MaterialPool(_content, session.State, _ship);
            pool.Add("energy_cell_1", 2);
            pool.Add("gold_ingot", 2);
            SendInventory(session);

            // … a person who knows you now (they join "People you know" via the memory roster) …
            var p = session.State;
            string npcKey = "rescue:" + WorldGenerator.StableHash(pod.Name + instance.Id) % 100000u;
            string place = _galaxy?.FindBody(session.CurrentLocationId)?.Name ?? string.Empty;
            RecordNpcInteraction(p, npcKey, pod.Name, "settler", NpcInteractionKind.Dialog, place);
            if (p.NpcMemory.TryGetValue(npcKey, out var rel) && rel.Value < 25)
            {
                rel.Value = 25; // a rescue makes you KNOWN in one act, not over ten trades
            }

            _repo.SavePlayer(p);

            // … and a promised call once they catch their breath (PR 13's pending-call plumbing).
            _dialogRadioPending.Add((p.PlayerId, npcKey, pod.Name, place, session.CurrentLocationId,
                "npc.call.rescue", _uptime + 150.0));

            Send(session, new ServerMessage
            {
                Text = Localize(session.Locale, "srv.encounter.rescued").Replace("{name}", pod.Name),
            });
            _log.Info($"'{p.Name}' rescued '{pod.Name}' from a drifting pod in {instance.Id}.");
            return;
        }
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test seam: forces this player's instance to hold the given encounter, spawned immediately.</summary>
    public string SpawnEncounterForTest(string playerId, int kind)
    {
        if (!_playerInstance.TryGetValue(playerId, out var iid) || !_spaceInstances.TryGetValue(iid, out var instance))
        {
            return string.Empty;
        }

        instance.EncounterRolled = true;
        instance.EncounterKind = kind;
        instance.EncounterAt = _uptime;
        SpawnEncounter(instance);
        return instance.EncounterId;
    }

    /// <summary>Test seam: runs one encounter tick for this player's instance.</summary>
    public void TickSpaceEncountersForTest(string playerId)
    {
        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance))
        {
            TickSpaceEncounters(instance);
        }
    }
}
