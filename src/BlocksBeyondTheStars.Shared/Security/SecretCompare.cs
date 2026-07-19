// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Security;

/// <summary>
/// Fixed-time secret comparison for protocol-level checks (#424 S14). Length-then-XOR-accumulate like
/// <see cref="HostedJoinToken"/>'s signature check — netstandard2.1 (Unity profile) has no
/// <c>CryptographicOperations.FixedTimeEquals</c>, so this hand-rolled variant is shared instead.
/// A length mismatch still returns early: secret length is far less sensitive than a byte-by-byte
/// match prefix, and padding to a fixed width would change no caller's behavior.
/// </summary>
public static class SecretCompare
{
    /// <summary>Whether two secrets match, without leaking a match prefix through timing. Null is
    /// treated as empty; two empties match (callers gate "no secret configured" themselves).</summary>
    public static bool FixedTimeEquals(string? a, string? b)
    {
        a ??= string.Empty;
        b ??= string.Empty;
        if (a.Length != b.Length)
        {
            return false;
        }

        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }

        return diff == 0;
    }
}
