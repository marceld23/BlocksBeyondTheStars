// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// #3494: a world sound emitter — a place that plays a looping sound which gets louder as the listener comes closer
/// (a statue that sings across a valley). The client plays one pooled loop per emitter in range; the volume rises
/// from 0 at <see cref="Radius"/> to <see cref="Volume"/> near the source. Server-authoritative, render-only.
/// </summary>
public sealed class NetSoundEmitter
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>The sound's key (an audio clip the client knows, e.g. <c>amb_sounder</c>); an unknown key stays silent.</summary>
    public string Sound { get; set; } = string.Empty;

    /// <summary>How far the sound carries, in blocks.</summary>
    public float Radius { get; set; }

    /// <summary>The volume right at the source, 0..1.</summary>
    public float Volume { get; set; } = 1f;
}

/// <summary>
/// #3497: one landmark of the current world for the client — where it stands and its FAR SILHOUETTE, a coarse
/// occupancy model of the stamped layout (one cell per <see cref="Step"/>³ blocks, a palette block per cell). The
/// client draws it beyond the streamed chunks, where a build is otherwise only a height blob, so a tower, a gate or a
/// bridge can be told apart from far away. Render-only; the blocks themselves stream as chunks.
/// </summary>
public sealed class NetLandmark
{
    public string Key { get; set; } = string.Empty;

    /// <summary>The display name in the receiving player's language.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>World cell of the model's (0, 0, 0) corner — the landmark's ground row and up (X canonical).</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    /// <summary>Blocks per model cell along every axis.</summary>
    public int Step { get; set; } = 4;

    /// <summary>The model's size in cells.</summary>
    public int Width { get; set; }
    public int Height { get; set; }
    public int Length { get; set; }

    /// <summary>One byte per model cell, index <c>(x * Height + y) * Length + z</c>: 0 = empty, else a 1-based index
    /// into <see cref="Blocks"/> / <see cref="Tints"/>.</summary>
    public byte[] Cells { get; set; } = System.Array.Empty<byte>();

    /// <summary>The palette: the block a cell mostly consists of (the client colours it like the far terrain does, from
    /// its own block atlas) …</summary>
    public ushort[] Blocks { get; set; } = System.Array.Empty<ushort>();

    /// <summary>… and that block's dye (0xRRGGBB, 0 = none), parallel to <see cref="Blocks"/>.</summary>
    public int[] Tints { get; set; } = System.Array.Empty<int>();

    public bool Ruined { get; set; }
}

/// <summary>The landmarks and the sound emitters of the client's current world (server → client, on world entry and
/// whenever a landmark is stamped while players are there).</summary>
public sealed class LandmarkList
{
    public NetLandmark[] Landmarks { get; set; } = System.Array.Empty<NetLandmark>();

    public NetSoundEmitter[] Emitters { get; set; } = System.Array.Empty<NetSoundEmitter>();
}
