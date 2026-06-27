// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>One animated machine inside a factory: an archetype the client knows how to animate (piston
/// "press", rotary "rotor", "conveyor") anchored at a world position. Render-only — the client runs the
/// motion procedurally; the server just says where the machines are and whether the factory is running.</summary>
public sealed class NetMachine
{
    public string Archetype { get; set; } = "press";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}

/// <summary>Snapshot of one factory for the client: where its production terminal is (to open the
/// roster-filtered crafting UI), its protection bounds, the recipes it can make (its roster), and its
/// animated machines. Server-authoritative.</summary>
public sealed class NetFactory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Production-terminal centre in world space.</summary>
    public float TerminalX { get; set; }
    public float TerminalY { get; set; }
    public float TerminalZ { get; set; }

    /// <summary>The recipe keys this factory offers (its seeded roster — never every factory recipe).</summary>
    public string[] Roster { get; set; } = System.Array.Empty<string>();

    /// <summary>Whether the factory is currently running (drives the machine animation speed). Ambient-on today.</summary>
    public bool Active { get; set; } = true;

    /// <summary>Whether this factory can be claimed with an access code (not every structure is claimable).</summary>
    public bool Claimable { get; set; }

    /// <summary>Owner player id once claimed (empty = unclaimed). A claimed factory is the owner's editable base.</summary>
    public string OwnerId { get; set; } = string.Empty;

    public NetMachine[] Machines { get; set; } = System.Array.Empty<NetMachine>();
}

/// <summary>Full set of factories the client should render for its current world (server → client).</summary>
public sealed class FactoryList
{
    public NetFactory[] Factories { get; set; } = System.Array.Empty<NetFactory>();
}

/// <summary>Player asks to claim the factory they're standing at by spending an access code (client → server).</summary>
public sealed class ClaimStructureIntent
{
    public int FactoryId { get; set; }
}
