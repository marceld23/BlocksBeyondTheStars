// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Client tells the server its avatar colours (packed 0xRRGGBB) so other players can see them.</summary>
public sealed class SetAppearanceIntent
{
    public int Skin { get; set; }
    public int Torso { get; set; }
    public int Arms { get; set; }
    public int Legs { get; set; }

    /// <summary>Ship hull colour (packed 0xRRGGBB) — tints the player's ship hull (item 32). 0 = unset.</summary>
    public int Hull { get; set; }
}

/// <summary>Client tells the server its custom pixel face (drawn in the in-game face editor) so other
/// players can see it on this player's avatar. The payload is opaque to the server (a 16×16 palette-index
/// string the client owns) — sent once on join and again on each edit, NOT in the 10 Hz presence stream.</summary>
public sealed class SetFaceIntent
{
    /// <summary>The face as a 16×16 palette-index hex string; empty clears the custom face.</summary>
    public string Pixels { get; set; } = string.Empty;
}

/// <summary>Client tells the server one of its avatar body-paint paintings (torso/arms/legs/helmet,
/// drawn in the per-part unfolded pixel editors, #874) so other players can see it. Mirrors
/// <see cref="SetFaceIntent"/>: the payload is opaque to the server (a concatenation of 32×32
/// palette-index face chunks, one hex char per pixel — see the shared <c>BodyPaint</c> helper for the
/// per-part chunk counts) — sent once per painted part on join and again on each edit, NOT in the
/// 10 Hz presence stream.</summary>
public sealed class SetBodyPaintIntent
{
    /// <summary>Which part: 0 torso, 1 arms, 2 legs, 3 helmet (<c>BodyPaint</c> constants).</summary>
    public int Part { get; set; }

    /// <summary>The painting as concatenated 32×32 palette-index hex chunks; empty clears the part.</summary>
    public string Pixels { get; set; } = string.Empty;
}

/// <summary>Another player's custom pixel face, relayed so the client can paint it on their avatar's head.
/// Sent on join (for everyone already online) and whenever a player edits their face — out of band from the
/// frequent <see cref="PlayerPresence"/> stream, since the bitmap is heavier and changes rarely.</summary>
public sealed class PlayerFace
{
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>The face as a 16×16 palette-index hex string; empty = no custom face (default features).</summary>
    public string Pixels { get; set; } = string.Empty;
}

/// <summary>Another player's avatar body-paint painting for one part, relayed so the client can show it on
/// their avatar (#874). Mirrors <see cref="PlayerFace"/>: sent on join (for everyone already online, one
/// message per painted part) and whenever a player edits a painting — out of band from the frequent
/// <see cref="PlayerPresence"/> stream, since the bitmaps are heavy and change rarely.</summary>
public sealed class PlayerBodyPaint
{
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>Which part: 0 torso, 1 arms, 2 legs, 3 helmet (<c>BodyPaint</c> constants).</summary>
    public int Part { get; set; }

    /// <summary>The painting as concatenated 32×32 palette-index hex chunks; empty = part not painted.</summary>
    public string Pixels { get; set; } = string.Empty;
}
