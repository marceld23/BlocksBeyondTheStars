// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Text;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// Share codes for player-made content (#846): a short text a player can paste into a chat window, a forum
/// post or a message to a friend, carrying a form (or a paint design) plus its name. The in-game routes —
/// copying a form off a placed block, handing over a stencil — cover players who are in the same world; this
/// covers everyone else.
///
/// The format is deliberately dull: <c>BBTS1-&lt;kind&gt;-&lt;base64 of "name\npayload"&gt;</c>. It is not a
/// security boundary and does not pretend to be one — a decoded payload is validated by exactly the same
/// rules the server applies before anything is saved or registered, so a mistyped or hand-crafted code can
/// at worst be rejected.
/// </summary>
public static class ShareCode
{
    private const string Prefix = "BBTS1";

    /// <summary>Kind marker for a player-designed block form.</summary>
    public const string KindForm = "F";

    /// <summary>Kind marker for a painted block design.</summary>
    public const string KindDesign = "D";

    /// <summary>Builds a share code. Returns an empty string for an empty payload.</summary>
    public static string Encode(string kind, string payload, string name)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return string.Empty;
        }

        string body = (name ?? string.Empty).Replace('\n', ' ') + "\n" + payload;
        return $"{Prefix}-{kind}-{Convert.ToBase64String(Encoding.UTF8.GetBytes(body))}";
    }

    /// <summary>Parses a share code of the expected kind. False for anything malformed — a hostile or
    /// mistyped string must simply not decode, never throw.</summary>
    public static bool TryDecode(string? code, string kind, out string payload, out string name)
    {
        payload = string.Empty;
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        string trimmed = code.Trim();
        string expected = $"{Prefix}-{kind}-";
        if (!trimmed.StartsWith(expected, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            string body = Encoding.UTF8.GetString(Convert.FromBase64String(trimmed.Substring(expected.Length)));
            int split = body.IndexOf('\n');
            if (split < 0)
            {
                return false;
            }

            name = body.Substring(0, split);
            payload = body.Substring(split + 1);
            return payload.Length != 0;
        }
        catch (Exception)
        {
            return false; // not base64, not UTF-8 — same answer either way
        }
    }

    /// <summary>Encodes a player-designed form.</summary>
    public static string EncodeForm(string voxels, string name)
        => CustomShape.IsValidVoxels(voxels) ? Encode(KindForm, voxels, name) : string.Empty;

    /// <summary>Decodes a form share code, applying the SAME validation the server does before it would
    /// register the form — so an imported form can never be one the game could not render.</summary>
    public static bool TryDecodeForm(string? code, out string voxels, out string name)
    {
        if (!TryDecode(code, KindForm, out voxels, out name)
            || !CustomShape.IsValidVoxels(voxels)
            || !CustomShape.FitsBudget(voxels))
        {
            voxels = string.Empty;
            name = string.Empty;
            return false;
        }

        return true;
    }

    /// <summary>Encodes a painted block design (a palette-index hex bitmap).</summary>
    public static string EncodeDesign(string pixels, string name) => Encode(KindDesign, pixels, name);

    /// <summary>Decodes a design share code, checking the bitmap is exactly <paramref name="expectedChars"/>
    /// palette symbols — the same shape of check the server's paint validator applies.</summary>
    public static bool TryDecodeDesign(string? code, int expectedChars, out string pixels, out string name)
    {
        if (!TryDecode(code, KindDesign, out pixels, out name)
            || pixels.Length != expectedChars
            || !State.PaintPayload.IsValidSymbols(pixels))
        {
            pixels = string.Empty;
            name = string.Empty;
            return false;
        }

        return true;
    }
}
