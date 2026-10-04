// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
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

        int X(int dx) => WorldConstants.WrapX(ax + dx, _world.Circumference);
        if (write)
        {
            var shell = (_content.GetBlock("deepslate") ?? _content.GetBlock("stone"))!.NumericId;
            BlockId B(string key) => _content.GetBlock(key)?.NumericId ?? BlockId.Air;
            var conduit = B("crystal_conduit");
            var phase = B("phase_block");
            var sw = B("crystal_switch");
            var gate = B("logic_block");
            var lamp = B("light_white");
            int top = floorY + 3, north = az + 4;

            // The niche: two cells of air behind the north wall, walled in.
            for (int dy = 0; dy <= 1; dy++)
            {
                _world.SetBlock(new Vector3i(X(0), floorY + dy, az + 5), BlockId.Air);
                _world.SetBlock(new Vector3i(X(-1), floorY + dy, az + 5), shell);
                _world.SetBlock(new Vector3i(X(1), floorY + dy, az + 5), shell);
                _world.SetBlock(new Vector3i(X(0), floorY + dy, az + 6), shell);
            }

            _world.SetBlock(new Vector3i(X(0), floorY - 1, az + 5), shell);
            _world.SetBlock(new Vector3i(X(0), floorY + 2, az + 5), shell);

            // The door, the gate above it (pointing down: yaw 5 rides in its settings), the wire between them.
            var cells = new System.Collections.Generic.List<(Vector3i Cell, BlockId Block, string Data)>
            {
                (new Vector3i(X(0), floorY, north), phase, TemplateDevices.Encode(0, string.Empty, string.Empty)),
                (new Vector3i(X(0), floorY + 1, north), phase, TemplateDevices.Encode(0, string.Empty, string.Empty)),
                (new Vector3i(X(0), floorY + 2, north), conduit, TemplateDevices.Encode(0, string.Empty, string.Empty)),
                (new Vector3i(X(0), top, north), gate, TemplateDevices.Encode((int)LogicMode.And, "yaw=" + CrystalNetRules.YawDown, string.Empty)),
            };

            // A wire along the top of the north wall to each side, down the side wall's corner to a switch.
            foreach (int side in new[] { -1, 1 })
            {
                for (int dx = 1; dx <= 4; dx++)
                {
                    cells.Add((new Vector3i(X(side * dx), top, north), conduit, TemplateDevices.Encode(0, string.Empty, string.Empty)));
                }

                cells.Add((new Vector3i(X(side * 4), top, north - 1), conduit, TemplateDevices.Encode(0, string.Empty, string.Empty)));
                cells.Add((new Vector3i(X(side * 4), top - 1, north - 1), conduit, TemplateDevices.Encode(0, string.Empty, string.Empty)));
                cells.Add((new Vector3i(X(side * 4), floorY + 1, north - 1), sw, TemplateDevices.Encode(0, string.Empty, string.Empty)));
                _world.SetBlock(new Vector3i(X(side * 2), top - 1, north), lamp); // the wire's ON lamp
            }

            foreach (var (cell, block, data) in cells)
            {
                if (!block.IsAir)
                {
                    _world.SetBlock(cell, block, 0, 0, 0);
                    QueueWorldCircuitCell(cell, data); // registered once the net has loaded (or right away, below)
                }
            }

            MarkFeatureStamped(flag);
            if (CrystalNet.Subscribed)
            {
                RegisterPendingWorldCircuits(); // stamped after the net loaded: register now, not on the next load
            }
        }

        // The niche's loot: a generator of its own, so the vault loop's rolls stay as they were.
        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash($"crystal_vault_loot:{ax}:{az}")));
        SpawnStructureLoot("vault", "loot", new Vector3f(X(0), floorY, az + 5), rng);
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
