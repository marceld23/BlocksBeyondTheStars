// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Security.Cryptography;
using System.Text;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// Checks an HTTP <c>Authorization: Basic …</c> header against the configured admin credentials. App-level
/// (rather than delegated to a reverse proxy) so the admin UI is protected even when someone runs the
/// container bare on a LAN. Comparison is fixed-time, and empty configured credentials never match — the
/// admin surface is OFF until the operator sets both user and password.
/// </summary>
public static class BasicAuth
{
    public static bool IsAuthorized(string? authorizationHeader, string user, string password)
    {
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
        {
            return false; // admin disabled by config
        }

        const string prefix = "Basic ";
        if (authorizationHeader == null || !authorizationHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authorizationHeader.Substring(prefix.Length).Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] presented = Encoding.UTF8.GetBytes(decoded);
        byte[] expected = Encoding.UTF8.GetBytes(user + ":" + password);

        // Fixed-time even on length mismatch: compare against self when lengths differ, then fail.
        bool sameLength = presented.Length == expected.Length;
        bool equal = CryptographicOperations.FixedTimeEquals(presented, sameLength ? expected : presented);
        return sameLength && equal;
    }

    /// <summary>Fixed-time compare of a request-supplied secret (header value) against a configured one
    /// (#424 S14 — an ordinal <c>string.Equals</c> leaks secret length/prefix via response timing). An
    /// empty configured secret never matches, so an unconfigured gate stays closed.</summary>
    public static bool TokenEquals(string? presented, string configured)
    {
        if (string.IsNullOrEmpty(configured))
        {
            return false;
        }

        byte[] presentedBytes = Encoding.UTF8.GetBytes(presented ?? string.Empty);
        byte[] configuredBytes = Encoding.UTF8.GetBytes(configured);
        bool sameLength = presentedBytes.Length == configuredBytes.Length;
        bool equal = CryptographicOperations.FixedTimeEquals(presentedBytes, sameLength ? configuredBytes : presentedBytes);
        return sameLength && equal;
    }
}
