// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// "Who lives here?" at the base core (#2717): pressing E at your own Grundstein asks for the base's residents — name and
/// role, what they are doing right now, where they are from the core (or that they are not here right now) and whether
/// they have a bed. A player searched the whole base for their settlers: nothing showed more than "Residents 1/5". Read-only
/// and cheap — the saved plan and the live NPCs, no index rebuild.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Closer than this to the core (metres, horizontal), a resident is "right by the core" — no direction.</summary>
    private const int ResidentListNearCore = 3;

    private void HandleRequestBaseResidents(PlayerSession session, RequestBaseResidentsIntent intent)
        => Send(session, BaseResidentListFor(session.State.PlayerId, intent.BodyId ?? string.Empty, intent.HasCell,
            new Vector3i(intent.CellX, intent.CellY, intent.CellZ)));

    /// <summary>The resident list of the player's base on a body (#2996: the one at the core cell, or without a cell the
    /// first), founding settler first; an empty list with <c>Cap</c> 0 when they have no such base.</summary>
    private BaseResidentList BaseResidentListFor(string playerId, string bodyId, bool hasCell = false, Vector3i cell = default)
    {
        var b = FindOwnBase(playerId, bodyId, hasCell, cell);
        if (b is null)
        {
            return new BaseResidentList { BodyId = bodyId, CellX = cell.X, CellY = cell.Y, CellZ = cell.Z };
        }

        var plan = _meta.BaseResidents.TryGetValue(BaseBoardKey(b), out var saved) ? saved : new List<BaseResidentRecord>();
        bool onWorld = b.Planet == _world.LocationId;
        var live = new Dictionary<int, ServerNpc>();
        if (onWorld)
        {
            foreach (var npc in ResidentNpcs(b))
            {
                live[npc.BaseSlot] = npc;
            }
        }

        var owner = FindSessionByPlayerId(b.OwnerId);
        var people = new List<NetBaseResident>();
        foreach (var r in plan.OrderBy(x => x.Slot))
        {
            live.TryGetValue(r.Slot, out var npc);
            string name = npc?.Name ?? string.Empty;
            if (name.Length == 0 && owner != null && owner.State.NpcMemory.TryGetValue(BaseResidentKey(b, r), out var rel))
            {
                name = rel.Name; // not on this world right now: the name the owner knows them by
            }

            var entry = new NetBaseResident
            {
                Name = name,
                RoleKey = npc?.NameKey is { Length: > 0 } nameKey ? nameKey : ResidentNameKey(r.Job),
                HasBed = r.Bed.Length > 0,
                Founder = r.Slot == 0,
                Here = npc != null,
            };

            if (npc != null)
            {
                entry.ActivityKey = ResidentListActivityKey(npc);
                double dx = WorldConstants.WrapDeltaX((double)(npc.Pos.X - (b.Cell.X + 0.5f)), _world.Circumference);
                double dz = WorldConstants.WrapDeltaZ((double)(npc.Pos.Z - (b.Cell.Z + 0.5f)), _world.Circumference);
                entry.Distance = (int)System.Math.Round(System.Math.Sqrt((dx * dx) + (dz * dz)));
                entry.DirectionKey = entry.Distance < ResidentListNearCore ? string.Empty : DirectionKey(dx, dz);
            }

            people.Add(entry);
        }

        int beds = onWorld && _baseIndex.TryGetValue(b.Id, out var idx) ? idx.BedHeads.Count : plan.Count(r => r.Bed.Length > 0);
        return new BaseResidentList
        {
            BodyId = bodyId,
            CellX = b.Cell.X,
            CellY = b.Cell.Y,
            CellZ = b.Cell.Z,
            BaseName = b.Name,
            Beds = beds,
            Cap = BaseResidentRoom(b), // #2996: the residents a player's bases on one body hold together
            People = people.ToArray(),
        };
    }

    /// <summary>What a resident is doing right now, for the list: asleep in bed, the activity the nameplate shows (at work,
    /// resting, celebrating …), else what the routine phase means — on the way to bed, resting, working or strolling.</summary>
    private static string ResidentListActivityKey(ServerNpc npc)
    {
        if (npc.Pose == 2)
        {
            return "npc.activity.sleeping";
        }

        if (npc.ActivityKey.Length > 0)
        {
            return npc.ActivityKey;
        }

        if (npc.Pose == 1)
        {
            return "npc.activity.resting";
        }

        return npc.Phase switch
        {
            NpcPhase.Night => "ui.base.res_to_bed",
            NpcPhase.Evening => "npc.activity.resting",
            _ => npc.Job.Length > 0 ? "npc.activity.working" : "ui.base.res_strolling",
        };
    }

    /// <summary>Test seam (#2717): the resident list the player would get for their base on the active world.</summary>
    public BaseResidentList BaseResidentListForTest(string playerId) => BaseResidentListFor(playerId, _world.LocationId);
}
