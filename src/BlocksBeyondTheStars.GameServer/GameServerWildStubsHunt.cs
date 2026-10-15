// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// CONTRACT STUB (#2639 package, wave 1) — the miner (#2643) and hunter (#2645) work cores. The real implementations live in
/// <c>GameServerNpcMining.cs</c> and <c>GameServerNpcHunting.cs</c>; whoever writes them deletes this file. Shared by the
/// lodge residents (#2641) and the wanderers (#2646); drops go to <c>bank(itemKey, count)</c>.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Default refill delay of a mined cell (about ten minutes).</summary>
    private const double NpcMineRefillSeconds = 600.0;

    /// <summary>Breaks one cell as an NPC (<c>BreakBlockCore(session: null, ownerId: "")</c> — never a player edit), banks
    /// the drops and schedules the cell to fill up again after <paramref name="refillSeconds"/> (persisted). False when the
    /// cell is air, protected for players, player-edited or not mineable.</summary>
    private bool NpcMineCell(ServerNpc npc, Vector3i cell, System.Action<string, int> bank, double refillSeconds) => false;

    /// <summary>Natural surface stone/ore cells with air above around <paramref name="center"/> (the wanderers' prospector):
    /// never player-edited, never protected, loaded chunks only; at most <paramref name="max"/>, nearest first.</summary>
    private List<Vector3i> FindNpcOutcrops(Vector3f center, int radius, int max) => new();

    /// <summary>Puts every given cell that is air back to its block at once (a lodge restoring its stamped quarry face on
    /// load) and drops their pending refills.</summary>
    private void RestoreNpcCellsNow(IEnumerable<(Vector3i Cell, ushort Block)> cells)
    {
    }

    /// <summary>Per-world step of the persisted refill registry (registered after <c>TickFlora</c>, runs once a second).</summary>
    private void TickNpcRestores(double dt)
    {
    }

    /// <summary>The nearest animal this hunter may hunt within <paramref name="range"/>: wild, common, passive or skittish,
    /// on land, in sight — never a companion or owned animal, a giant, a story creature or clone, a begging or called animal,
    /// one a player stands next to or with a player in the line of fire. Null when there is none.</summary>
    private CombatEntity? FindNpcHuntTarget(ServerNpc npc, float range) => null;

    /// <summary>Shoots <paramref name="target"/> (range ≤ 14, line of sight): the arrow <c>WorldFx</c> + the bow sound, the
    /// animal leaves with the sparkle like a sentry kill, its loot is banked, its kin flee from the hunter. No player gets
    /// any credit. False when the shot is not possible (out of range, no sight, no longer eligible).</summary>
    private bool NpcShootCreature(ServerNpc npc, CombatEntity target, System.Action<string, int> bank) => false;
}
