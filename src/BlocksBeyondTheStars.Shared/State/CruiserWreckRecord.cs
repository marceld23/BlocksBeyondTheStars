// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// #3902: the wreck a defeated escort cruiser left behind — an entry of <see cref="WorldMetadata.CruiserWrecks"/>, written
/// when the boss falls. The hull itself is never stored: it is a pure function of the template and <see cref="Seed"/> (the
/// "fresh" ruin and the ship-mode decay), and the players' changes are the derived station's delta blob. The record keeps what
/// a restart needs to put the same wreck back at the same place, and what happened on its bridge.
/// </summary>
public sealed class CruiserWreckRecord
{
    /// <summary>The wreck's station id: <c>derelict:cruiser-&lt;system&gt;-&lt;n&gt;</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The star system it drifts in (a contact of every orbit of that system).</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>How many wrecks the system has held, this one included (the <c>n</c> of the id).</summary>
    public int Serial { get; set; }

    /// <summary>The flight position of the hull's centre — where the boss fell, clamped clear of the launch point and of the
    /// station hulls.</summary>
    public float X { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public float Y { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public float Z { get; set; }

    /// <summary>The heading the cruiser fell with (degrees). The boarded hull is never turned; kept for a later use.</summary>
    public float Yaw { get; set; }

    /// <summary>The seed of the wreck's ruin and decay.</summary>
    public long Seed { get; set; }

    /// <summary>The giant ship template it is built from.</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>The hash of the base build the wreck was first built as (a later run whose build comes out differently while
    /// the save holds the players' changes keeps the wreck away instead of laying old changes over a new hull).</summary>
    public string BaseHash { get; set; } = string.Empty;

    /// <summary>The story's cruiser: VEGA's sister core ECHO waits on its bridge (canon C22).</summary>
    public bool CarriesEcho { get; set; }

    /// <summary>A hacking chip was used at the bridge console: its lamps are blue.</summary>
    public bool ConsoleOpened { get; set; }

    /// <summary>ECHO spoke and said goodbye (only ever true with <see cref="CarriesEcho"/>).</summary>
    public bool EchoFreed { get; set; }

    /// <summary>A newer wreck took this one's place in its system: it is off the star map and is removed for good — its
    /// record, its stored changes and its interior world — once nobody is aboard.</summary>
    public bool Replaced { get; set; }

    /// <summary>The player ids of every pilot who hurt the cruiser.</summary>
    public List<string> Fighters { get; set; } = new();

    /// <summary>The fighters whose crate was delivered aboard.</summary>
    public List<string> CratesGiven { get; set; } = new();

    /// <summary>The players who got ECHO's memory core here (one per player and wreck).</summary>
    public List<string> CoresGiven { get; set; } = new();
}
