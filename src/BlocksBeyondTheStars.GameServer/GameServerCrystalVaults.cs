// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Crystal vaults (#2260): every third buried vault of a fresh world hides a bonus niche behind its north wall, closed by a
/// two-high phase-block door. Two crystal switches sit in the west and east walls; their glowing wires run along the top
/// of the walls into an AND logic block above the door, which points down into it. A lamp on each wire shows which
/// switch is ON — both ON and the door opens. Everything is a world circuit (anyone may flip the switches). A kid solves
/// it by following the wires; the niche holds an extra loot cache.
/// <para>Only stamped together with a fresh vault (its blocks are written once, #467) and chosen by a hash of the vault's
/// position, never by the vault loop's random stream — the loot rolls of every other vault stay exactly as they were.
/// The niche's loot has a generator of its own and is re-derived on every entry, like the vault's.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Whether the vault at this anchor is a crystal vault (one in three, by position).</summary>
    private static bool IsCrystalVault(int ax, int az) => (uint)WorldGenerator.StableHash($"crystal_vault:{ax}:{az}") % 3 == 0;

    /// <summary>Adds the crystal puzzle to a vault: blocks and world-circuit cells on the first stamp, the niche's loot on
    /// every entry (when the puzzle was stamped).</summary>
    private void StampCrystalVaultPuzzle(int ax, int az, int floorY, bool write)
    {
        string flag = $"crystalvault:{ax}:{az}";
        if (!write && !FeatureStamped(flag))
        {
            return; // a vault stamped before crystal vaults existed stays as it is
        }

        int circ = _world.Circumference;
        if (write)
        {
            // The geometry is VaultBuilder's (#3522): every device cell it writes is followed by its device marker — a
            // world-circuit cell, registered once the net has loaded (or right away, below). Its loot spot is spawned below,
            // on every entry.
            var sink = new WorldCellSink(_world, onMark: (type, x, y, z, data) =>
            {
                if (type == TemplateDevices.Marker)
                {
                    QueueWorldCircuitCell(new Vector3i(WorldConstants.WrapX(x, circ), y, z), data);
                }
            });
            VaultBuilder.BuildCrystalPuzzle(sink, _content, ax, az, floorY);
            MarkFeatureStamped(flag);
            if (CrystalNet.Subscribed)
            {
                RegisterPendingWorldCircuits(); // stamped after the net loaded: register now, not on the next load
            }
        }

        // The niche's loot: a generator of its own, so the vault loop's rolls stay as they were.
        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash($"crystal_vault_loot:{ax}:{az}")));
        var niche = VaultBuilder.CrystalNiche(ax, az, floorY);
        SpawnStructureLoot("vault", "loot", new Vector3f(WorldConstants.WrapX(niche.X, circ), niche.Y, niche.Z), rng);
    }

    /// <summary>Test seam: stamps a crystal vault's puzzle around an anchor (as a fresh vault would) and registers it.</summary>
    public void StampCrystalVaultPuzzleForTest(int ax, int az, int floorY)
    {
        StampCrystalVaultPuzzle(ax, az, floorY, write: true);
        RegisterPendingWorldCircuits();
    }

    /// <summary>A vault's phase door opened: everyone in the chamber cracked the safe.</summary>
    private void OnCrystalVaultOpened(ServerCrystalCell door)
    {
        var at = new Vector3f(door.Cell.X + 0.5f, door.Cell.Y + 0.5f, door.Cell.Z + 0.5f);
        foreach (var s in JoinedInActiveWorld())
        {
            if (WrapDistSq(s.State.Position, at) <= 10 * 10)
            {
                OnCrystalCircuitEvent(s.State.PlayerId, Shared.Missions.CircuitEvents.Vault);
            }
        }
    }
}
