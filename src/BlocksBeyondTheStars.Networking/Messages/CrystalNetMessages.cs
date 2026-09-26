// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>One crystal network on the wire (#2046): its cells and whether it is ON. The client draws the glow on
/// exactly these cells — the state never rides in the voxel, so a level change costs one message per world
/// instead of one <see cref="BlockChanged"/> (and a chunk re-mesh) per conduit.</summary>
public sealed class NetCrystalNet
{
    public int Id { get; set; }
    public bool On { get; set; }

    /// <summary>Cell coordinates as x, y, z triples.</summary>
    public int[] Cells { get; set; } = System.Array.Empty<int>();
}

/// <summary>Every crystal network of the player's world; sent on join and coalesced once per tick on change.</summary>
public sealed class CrystalNetList
{
    public NetCrystalNet[] Nets { get; set; } = System.Array.Empty<NetCrystalNet>();
}

/// <summary>A device of the Crystal Net as the client sees it: where it is, what it is, its mode / config and
/// whether its own output is ON (the switch's lever, the sensor's eye, the drill's "blocked" light).</summary>
public sealed class NetCrystalDevice
{
    public int Id { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>The <c>CrystalDeviceKind</c> name.</summary>
    public string Kind { get; set; } = string.Empty;
    public int Mode { get; set; }

    /// <summary>Kind-specific settings as a short "key=value;…" line (a recipe, a pair target, a period).</summary>
    public string Config { get; set; } = string.Empty;

    /// <summary>The player-typed name of a receiver / announcer line (screened like a beacon label).</summary>
    public string Label { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public bool Output { get; set; }
}

/// <summary>Every device of the player's world; sent on join and coalesced once per tick on change.</summary>
public sealed class CrystalDeviceList
{
    public NetCrystalDevice[] Devices { get; set; } = System.Array.Empty<NetCrystalDevice>();
}

/// <summary>Client → server: the player toggles a switch, presses a button or configures a device they look at.
/// The server validates reach, ownership / alliance and the value range; nothing here is trusted.</summary>
public sealed class SetCrystalDeviceIntent
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>0 = toggle (switch), 1 = press (button, manual start), 2 = configure (mode + config + label).</summary>
    public int Action { get; set; }
    public int Mode { get; set; }
    public string Config { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

/// <summary>Server → world: a device plays a sound at a cell (#2052). A loop is started once and stopped with
/// <see cref="Stop"/>; the client keeps it playing in between (nothing is re-sent per beat).</summary>
public sealed class SoundFx
{
    /// <summary>A cue id of the client's audio bank or synthesizer (<c>alarm_siren</c>, <c>chime_bell</c>, <c>note_c4</c> …).</summary>
    public string SoundId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Pitch { get; set; } = 1f;
    public bool Loop { get; set; }
    public bool Stop { get; set; }

    /// <summary>The device id, so a stop finds its loop.</summary>
    public int SourceId { get; set; }
}
