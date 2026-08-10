// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// The charset half of every player-drawn pixel payload — faces, body paintings and block designs all
/// store one symbol per pixel and are validated the same way before they are persisted or rebroadcast.
/// The client owns the palette itself (<c>FacePalette</c>); the server only needs to know which symbols
/// are legal, and it needs to know it in ONE place: the check used to be copy-pasted into three
/// validators, which is how a palette widening ends up enforced in two of them.
///
/// <para>
/// The alphabet is base32 (<c>0-9a-v</c>, 32 palette slots) as of #899, hex (<c>0-9a-f</c>) before that.
/// Because the hex digits are its first sixteen symbols, every payload written by an older client stays
/// valid — no migration, no dual-format handling. Upper case is accepted on read (clients write lower
/// case; <c>GameServerPaint</c> lower-cases design payloads before validating them, which is also why
/// the alphabet must never use upper-case letters as distinct symbols).
/// </para>
/// </summary>
public static class PaintPayload
{
    /// <summary>Number of palette slots the alphabet can address (index 0 = transparent).</summary>
    public const int PaletteSize = 32;

    /// <summary>True when every character is a legal palette symbol. An empty string is valid — it is how
    /// "not painted" is stored.</summary>
    public static bool IsValidSymbols(string? pixels)
    {
        if (string.IsNullOrEmpty(pixels))
        {
            return true;
        }

        foreach (char c in pixels)
        {
            bool ok = c is (>= '0' and <= '9') or (>= 'a' and <= 'v') or (>= 'A' and <= 'V');
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }
}
