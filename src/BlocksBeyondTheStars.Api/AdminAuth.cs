// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace BlocksBeyondTheStars.Api;

/// <summary>
/// Authorization policy for the /api admin endpoints (issue #411).
///
/// The admin password is optional for a loopback-only bind (the local dashboard case), but with a
/// non-loopback bind — which the Docker image forces via BBS_ADMIN_BIND=0.0.0.0 — an unset password
/// must FAIL CLOSED: every /api request is rejected until a password is configured. This mirrors
/// WorldHost/ReportHost, whose admin surfaces are likewise disabled when no credential is set.
/// </summary>
public static class AdminAuth
{
    /// <summary>True when the admin UI bind address can only be reached from the local machine.</summary>
    public static bool IsLoopbackBind(string? bindAddress)
    {
        if (string.IsNullOrWhiteSpace(bindAddress))
        {
            return false;
        }

        if (string.Equals(bindAddress.Trim(), "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Anything unparseable (Kestrel wildcards "*"/"+", host names) counts as non-loopback:
        // when in doubt, fail closed.
        return IPAddress.TryParse(bindAddress.Trim(), out var ip) && IPAddress.IsLoopback(ip);
    }

    /// <summary>
    /// Decides whether an /api request is authorized. With a configured password the provided header
    /// must match (fixed-time comparison); without one, only a loopback bind is allowed through.
    /// </summary>
    public static bool IsAuthorized(string? configuredPassword, string? providedPassword, bool loopbackBind)
    {
        if (string.IsNullOrEmpty(configuredPassword))
        {
            return loopbackBind;
        }

        return FixedTimeEquals(configuredPassword, providedPassword ?? string.Empty);
    }

    private static bool FixedTimeEquals(string expected, string provided)
    {
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expected);
        byte[] providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }
}
