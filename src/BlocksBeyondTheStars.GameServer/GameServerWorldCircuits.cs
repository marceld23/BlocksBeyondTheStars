// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Pre-built world circuits (#2260): a settlement or station module (or a crystal vault) can carry a working Crystal Net
/// circuit — street lamps on a daylight sensor, a doorbell at the trading post, a station airlock, a vault puzzle. The
/// template's conduit and device cells arrive as <see cref="TemplateDevices.Marker"/> markers with the device's mode,
/// settings and name; the server registers each as a <b>world circuit</b> (owner <see cref="CrystalNetRules.WorldOwnerId"/>):
/// anyone may operate it (the puzzle must be solvable), only an admin may re-configure it, it has a small budget of its
/// own and never counts against a player's caps. A horizontal direction comes from the stamped block's front (so a turned
/// module turns its circuit along); up / down rides in the settings (<c>yaw=4</c> / <c>yaw=5</c>).
/// <para>Settlements are stamped while the world loads, BEFORE <c>LoadCrystalNet</c> rebuilds the net from its rows, so
/// their devices wait in a list and are registered right after the load (guarded by the cell, like the intercity rail's
/// stops); a station is stamped on boarding, after the load, and registers at once.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>World-circuit cells collected while this world's structures were stamped, waiting for the net to load.</summary>
    private readonly List<(Vector3i Cell, string Data)> _pendingWorldCircuits = new();

    /// <summary>Remembers a world-circuit cell for the registration after the net has loaded.</summary>
    private void QueueWorldCircuitCell(Vector3i cell, string data) => _pendingWorldCircuits.Add((cell, data));

    /// <summary>Registers the world-circuit cells queued during this world's load (rows that came back already are skipped).</summary>
    private void RegisterPendingWorldCircuits()
    {
        foreach (var (cell, data) in _pendingWorldCircuits)
        {
            RegisterWorldCircuitCell(cell, data);
        }

        _pendingWorldCircuits.Clear();
    }

    /// <summary>Registers one pre-built conduit / device at a world cell, unless the net knows the cell already (a reload:
    /// its row came back) or the block that stands there is no Crystal Net cell any more.</summary>
    private void RegisterWorldCircuitCell(Vector3i cell, string data)
    {
        if (CrystalNet.Cells.ContainsKey(cell))
        {
            return;
        }

        var def = _content.BlockById(_world.GetBlock(cell));
        var kind = CrystalNetRules.KindOf(def);
        if (def is null || kind == CrystalDeviceKind.None || CrystalNetRules.IsPassivePort(kind) || CrystalNetRules.IsShipOnly(kind)
            || (CrystalNetRules.IsPlanetOnly(kind) && _world.Planet.Void))
        {
            return;
        }

        var (mode, rawConfig, rawLabel) = TemplateDevices.Decode(data);
        string config = SanitizeCrystalConfig(rawConfig);
        int yaw = 0;
        if (CrystalNetRules.IsDirectional(kind))
        {
            yaw = CrystalConfigValue(config, "yaw") is not null
                ? CrystalNetRules.ClampYaw(CrystalConfigInt(config, "yaw", 0))
                : def.Facing is not null ? CubeFacing.LookHeadingOf(def.Facing, CubeFacing.FrontOf(_world.GetShape(cell))) : 0;
            config = CrystalConfigWith(config, "yaw", yaw.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (kind == CrystalDeviceKind.BridgeMotor && CrystalConfigValue(config, "len") is null)
        {
            config = CrystalConfigWith(config, "len", CrystalNetRules.BridgeDefaultLength.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        int modes = CrystalNetRules.ModeCount(kind);
        string label = rawLabel.Length > 0 ? SanitizeBeamName(rawLabel) : string.Empty;
        var registered = RegisterCrystalCell(cell, kind, def.Key, CrystalNetRules.WorldOwnerId, modes > 0 ? System.Math.Max(0, System.Math.Min(modes - 1, mode)) : 0,
            config, label, yaw, persist: true);
        if (registered.Inert)
        {
            // Over the world circuits' budget: the stamped block stays a plain block. An inert wire must not sit there and
            // hold the lamps beside it in a dark, sourceless network.
            UnregisterCrystalCell(registered, relight: true);
            return;
        }

        DiscoverCrystalNeighbours(cell, CrystalNetRules.WorldOwnerId); // the lamps beside the wire join as world ports
    }

    /// <summary>Test seam: registers a world-circuit cell as a stamped template would.</summary>
    public void RegisterWorldCircuitForTest(Vector3i cell, int mode = 0, string config = "", string label = "")
        => RegisterWorldCircuitCell(cell, TemplateDevices.Encode(mode, config, label));
}
