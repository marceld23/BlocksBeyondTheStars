// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPC professions (Justus' ideas, 2026-09-15): doctor, grocer, arms dealer, sage, animal tamer, blockfarmer, streamer and
/// reporter (<see cref="NpcProfessions"/>). They staff a post marker — in a settlement building of their own function, in an
/// author's structure or station template, or at the post block a player builds at home or on their station. A trading
/// profession is a <c>vendor</c> with its own job and market theme, so the market, the trade-or-talk question and the
/// relationship memory work unchanged; everything the profession adds keys off <see cref="ServerNpc.Job"/>.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Dresses an NPC for its profession: job, nameplate key, market theme (trading professions) and what it
    /// carries. The role must already match (<see cref="MakeNpc"/> was called with <see cref="NpcProfession.Role"/>).</summary>
    private static void ApplyProfession(ServerNpc npc, NpcProfession profession)
    {
        npc.Role = profession.Role;
        npc.Job = profession.Job;
        npc.NameKey = profession.NameKey;
        npc.Held = profession.Held;
        if (profession.Trades)
        {
            npc.Theme = profession.Theme;
        }
    }

    /// <summary>A profession that works standing at its post (the tight work leash) — not the blockfarmer, who is out
    /// in the field.</summary>
    private static bool IsStandingProfession(string job) => NpcProfessions.ByJob(job) is { WorksOutside: false };

    /// <summary>
    /// One resident per profession post of a settlement, spawned after its regular residents with a generator seeded
    /// from the settlement and the post's index — never the shared world generator, so the names, looks and beds of
    /// every other resident stay exactly what they were. Each takes the free bed nearest its post (the building's own
    /// back room or upstairs flat), or none.
    /// </summary>
    private void SpawnProfessionResidents(SettlementInstance settlement, List<Vector3i> freeBeds, List<Vector3i> seats, ref int seatCursor)
    {
        string settlementTheme = SettlementTradeFor(settlement.Name);
        int index = 0;
        foreach (var (type, pos) in settlement.Markers)
        {
            if (NpcProfessions.ByMarker(type) is not { } profession)
            {
                continue;
            }

            var rng = new System.Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("profession:" + settlement.Name + ":" + index++))));
            var standing = new Vector3f(pos.X, (float)System.Math.Floor(System.Math.Max(settlement.Min.Y + 1f, pos.Y)), pos.Z);
            var npc = MakeNpc(profession.Role, profession.Trades ? profession.Theme : settlementTheme, robotic: false, standing, rng);
            npc.Settlement = settlement.Name;
            ApplyProfession(npc, profession);
            npc.Work = standing;
            npc.HasWork = true;
            npc.Bed = TakeNearestBed(freeBeds, standing);
            npc.FurnitureScanned = true;
            if (seats.Count > 0)
            {
                npc.Seat = seats[seatCursor % seats.Count];
                seatCursor++;
            }
            else if (npc.Bed is { } own)
            {
                npc.Seat = NearestLayoutSeat(settlement, own);
            }

            npc.RoutineEnabled = true;
            _npcs.Add(npc);
        }
    }

    /// <summary>Test seam: every NPC's job, role, theme, nameplate key and held item on the active world.</summary>
    public IReadOnlyList<(string Job, string Role, string Theme, string NameKey, string Held, string Settlement)> NpcJobsForTest
        => _npcs.Select(n => (n.Job, n.Role, n.Theme, n.NameKey, n.Held, n.Settlement)).ToList();
}
