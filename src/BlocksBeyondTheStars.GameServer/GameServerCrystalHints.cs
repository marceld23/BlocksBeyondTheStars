// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Crystal Net discovery (#2257): one-time VEGA hints at the moments a player meets the net — the first crystal mined,
/// the blueprint becoming researchable and being researched, the first own network that turns ON, the first door on a
/// wire, the first device with an arrow, the first amber status light, the first refusal on someone else's device, the
/// first look at an old world circuit, and the first use of every new kind. Each is a per-player once-flag
/// (<c>vega:hint:&lt;id&gt;</c>) through <see cref="ShipAiHintOnce"/> (an advisor hint, muted with VEGA's hints, kept in
/// the tips log); the "this belongs to someone else" line is a system line, because it explains a refusal.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The comm radio's blueprint — the Crystal Net's tech tab opens behind it.</summary>
    private const string CrystalPrerequisiteBlueprint = "comm_radio";

    /// <summary>The first blueprint of the Crystal Net tab.</summary>
    private const string CrystalFirstBlueprint = "crystal_conduit";

    /// <summary>A block was mined: the first crystal tells the player what crystal can do.</summary>
    private void OnCrystalBlockMinedHint(PlayerSession session, string blockKey)
    {
        if (blockKey is "crystal" or "crystal_block" && !session.State.Milestones.Contains("vega:hint:crystal_mined"))
        {
            ShipAiHintOnce(session, "crystal_mined");
        }
    }

    /// <summary>A blueprint was unlocked: the comm radio opens the Crystal Net tab, the conduit opens the net itself.</summary>
    private void OnCrystalBlueprintUnlockedHint(PlayerSession session, string blueprintKey)
    {
        if (blueprintKey == CrystalPrerequisiteBlueprint)
        {
            ShipAiHintOnce(session, "crystal_researchable");
        }
        else if (blueprintKey == CrystalFirstBlueprint)
        {
            ShipAiHintOnce(session, "crystal_researched");
        }
        else
        {
            string? kindHint = blueprintKey switch
            {
                "crystal_mechanics" => "crystal_mechanics",
                "crystal_lift" => "crystal_lift",
                "crystal_signals" => "crystal_signals",
                _ => null,
            };
            if (kindHint is not null)
            {
                ShipAiHintOnce(session, kindHint);
            }
        }
    }

    /// <summary>A Crystal Net cell this player placed: the first door on a wire, the first arrow, the first moving block.</summary>
    private void OnCrystalCellPlacedHints(PlayerSession session, ServerCrystalCell cell)
    {
        if (cell.Inert)
        {
            return;
        }

        if (CrystalNetRules.IsDirectional(cell.Kind))
        {
            ShipAiHintOnce(session, "crystal_arrow");
        }

        string? kindHint = cell.Kind switch
        {
            CrystalDeviceKind.PhaseBlock => "crystal_phase",
            CrystalDeviceKind.Trapdoor => "crystal_trapdoor",
            CrystalDeviceKind.BridgeMotor => "crystal_bridge",
            CrystalDeviceKind.Piston => "crystal_piston",
            CrystalDeviceKind.LiftMotor => "crystal_lift_motor",
            CrystalDeviceKind.SignalDisplay => "crystal_display",
            CrystalDeviceKind.SignalReceiver => "crystal_receiver",
            _ => null,
        };
        if (kindHint is not null)
        {
            ShipAiHintOnce(session, kindHint);
        }

        if (CrystalCellTouchesDoor(cell.Cell))
        {
            ShipAiHintOnce(session, "crystal_door");
        }
    }

    /// <summary>Whether a door's gap touches this cell (its floor cell or the one above, six faces each).</summary>
    private bool CrystalCellTouchesDoor(Vector3i local)
    {
        var cell = CrystalToWorld(local); // #2268: aboard, the ship's doors hang in world space
        foreach (var door in _doors)
        {
            var floor = door.Pos.ToBlock();
            for (int dy = 0; dy < 2; dy++)
            {
                var gap = new Vector3i(floor.X, floor.Y + dy, floor.Z);
                foreach (var face in CrystalNetRules.Faces)
                {
                    if (gap + face == cell)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>The first refusal on someone else's device explains the rule, once (a system line — it explains a refusal).</summary>
    private void OnCrystalRefusedHint(PlayerSession session) => CrystalSystemHintOnce(session, "crystal_locked");

    /// <summary>Sensor beat: the first own network that is ON, the first amber light of an own device, the first look at a
    /// world circuit — each checked for the players on this world, cheap (flags first).</summary>
    private void CrystalDiscoveryBeat()
    {
        var state = CrystalNet;
        foreach (var s in JoinedInActiveWorld())
        {
            var p = s.State;
            bool wantNetOn = !p.Milestones.Contains("vega:hint:crystal_net_on");
            bool wantAmber = !p.Milestones.Contains("vega:hint:crystal_amber");
            bool wantRuin = !p.Milestones.Contains("vega:hint:crystal_ruin");
            if (!wantNetOn && !wantAmber && !wantRuin)
            {
                continue;
            }

            foreach (var c in state.Cells.Values)
            {
                if (c.Inert)
                {
                    continue;
                }

                if (wantNetOn && c.OwnerId == p.PlayerId && CrystalNetRules.IsSource(c.Kind) && c.Output
                    && c.NetId != 0 && state.Nets.TryGetValue(c.NetId, out var net) && net.Level)
                {
                    ShipAiHintOnce(s, "crystal_net_on");
                    OnCrystalCircuitEvent(p.PlayerId, Shared.Missions.CircuitEvents.NetOn); // #2258: "First circuit"
                    wantNetOn = false;
                }

                if (wantAmber && c.OwnerId == p.PlayerId && c.Output && !CrystalNetRules.IsSource(c.Kind) && !c.IsGate
                    && c.Kind is not CrystalDeviceKind.Light and not CrystalDeviceKind.SignalDisplay)
                {
                    ShipAiHintOnce(s, "crystal_amber");
                    wantAmber = false;
                }

                if (wantRuin && CrystalNetRules.IsWorldOwner(c.OwnerId)
                    && WrapDistSq(p.Position, CrystalWorldCentre(c.Cell)) <= 8 * 8)
                {
                    ShipAiHintOnce(s, "crystal_ruin");
                    wantRuin = false;
                }
            }
        }
    }
}
