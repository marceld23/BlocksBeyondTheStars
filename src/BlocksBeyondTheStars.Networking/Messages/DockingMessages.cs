// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client asks to dock with another player's ship.</summary>
public sealed class DockRequestIntent
{
    public string TargetPlayer { get; set; } = string.Empty;
}

/// <summary>The docking-request target accepts or declines a pending request.</summary>
public sealed class DockResponseIntent
{
    public string Requester { get; set; } = string.Empty;
    public bool Accept { get; set; }
}

/// <summary>Client asks to undock from its current docking partner.</summary>
public sealed class UndockIntent
{
}

/// <summary>#2723: a docked player asks to walk over into the partner's ship while both float in space. The server checks
/// the docking, the flight instance, the range and that the host's ship floats parked (the host walks inside it or is on
/// a spacewalk), then loads the guest into the host's ship interior. An <see cref="EnterShipIntent"/> sent while visiting
/// takes the guest back to their own ship.</summary>
public sealed class VisitShipIntent
{
    /// <summary>The player id of the docking partner whose ship the sender wants to visit.</summary>
    public string HostId { get; set; } = string.Empty;
}

/// <summary>Notifies a player that another player has requested to dock with them.</summary>
public sealed class DockRequestNotice
{
    public string Requester { get; set; } = string.Empty;
}

/// <summary>Authoritative docking state change for the receiving player.</summary>
public sealed class DockStatus
{
    /// <summary>The other player in the docking (the partner).</summary>
    public string Partner { get; set; } = string.Empty;

    /// <summary>True when now docked, false when undocked / declined.</summary>
    public bool Docked { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>#2723: the server supports docking visits (<see cref="VisitShipIntent"/>). Additive — an older server
    /// leaves it false, and the client then offers no visit.</summary>
    public bool CanVisit { get; set; }
}
