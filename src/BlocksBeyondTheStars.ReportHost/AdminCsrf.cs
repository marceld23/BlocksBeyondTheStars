// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Security.Cryptography;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// Cross-site request forgery guard for the admin forms (#1369). The admin UI sits behind Basic Auth, and a
/// browser re-sends Basic credentials on ANY request to the origin — including a form auto-submitted by a
/// page the operator opened elsewhere. Since #1327 a form POST can send text to a player, so every admin
/// form now carries a token the foreign page cannot know: one random value per process, rendered as a
/// hidden field (<see cref="FieldName"/>) and checked with a fixed-time compare on every admin POST.
/// Deliberately the simplest robust shape — no cookie, no per-session table: the inbox is one process
/// behind one operator, and a restart merely makes an already-open page's next submit fail with 403 until
/// it is reloaded.
/// </summary>
public sealed class AdminCsrf
{
    /// <summary>Name of the hidden form field (and of the form key the check reads).</summary>
    public const string FieldName = "csrf";

    /// <summary>The token pages render — 32 random bytes as lowercase hex, fixed for the process lifetime.</summary>
    public string Token { get; }

    public AdminCsrf()
        : this(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant())
    {
    }

    /// <summary>Test seam: a fixed token.</summary>
    public AdminCsrf(string token)
    {
        Token = token;
    }

    /// <summary>The hidden input every admin form must contain.</summary>
    public string HiddenField() => $"<input type='hidden' name='{FieldName}' value='{Token}'>";

    /// <summary>True when the submitted value is this process's token (fixed-time compare; empty never matches).</summary>
    public bool IsValid(string? presented) => BasicAuth.TokenEquals(presented, Token);
}
