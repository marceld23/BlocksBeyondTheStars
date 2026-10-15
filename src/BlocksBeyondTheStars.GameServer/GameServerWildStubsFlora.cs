// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// CONTRACT STUB (#2639 package, wave 1) — the gatherer (#2642) and woodcutter (#2644) work cores. The real implementations
/// live in <c>GameServerNpcGathering.cs</c> and <c>GameServerNpcWoodcutting.cs</c>; whoever writes them deletes this file.
/// The cores are shared by the lodge residents (#2641) and the wanderers (#2646): the caller picks the site, walks the NPC
/// there and lets it work with its tool for a few seconds, then calls the core. Drops go to <c>bank(itemKey, count)</c>.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Harvestable cells around <paramref name="center"/> within <paramref name="radius"/> (at most
    /// <paramref name="max"/>, nearest first): crops and edible wild flora on loaded chunks, never a player-planted plant,
    /// never one in a protected settlement/station/base, never a species this world rolled toxic.</summary>
    private List<Vector3i> FindNpcGatherSites(Vector3f center, int radius, int max) => new();

    /// <summary>Harvests one flora cell the gardener's way (air + <c>BlockChanged</c> + regrow), banking the drops. False
    /// when the cell no longer holds harvestable flora.</summary>
    private bool NpcHarvestFlora(ServerNpc npc, Vector3i cell, System.Action<string, int> bank) => false;

    /// <summary>Trunk bases of natural trees around <paramref name="center"/> within <paramref name="radius"/> (at most
    /// <paramref name="max"/>, nearest first): this world's trunk ids on ground, no player-edited cell in the tree, not in a
    /// protected area, loaded chunks only.</summary>
    private List<Vector3i> FindNpcTrees(Vector3f center, int radius, int max) => new();

    /// <summary>Starts felling the tree at <paramref name="trunkBase"/>: its trunk and leaves come down over the next ticks
    /// (<see cref="TickNpcFelling"/>), the logs go to <paramref name="bank"/> and a sapling is planted on the trunk's ground
    /// cell. False when there is no natural tree there (any more) or this NPC is already felling one.</summary>
    private bool NpcFellTree(ServerNpc npc, Vector3i trunkBase, System.Action<string, int> bank) => false;

    /// <summary>True while a felling this NPC started is still coming down (the caller keeps it standing and swinging).</summary>
    private bool NpcFellingActive(ServerNpc npc) => false;

    /// <summary>Per-tick step of every running felling (registered in the tick loop after <c>TickFlora</c>).</summary>
    private void TickNpcFelling(double dt)
    {
    }
}
