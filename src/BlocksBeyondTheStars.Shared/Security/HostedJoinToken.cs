// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Security.Cryptography;
using System.Text;

namespace BlocksBeyondTheStars.Shared.Security;

/// <summary>
/// Stateless HMAC join tokens for hosted worlds. The control plane (which knows the shared secret) issues a
/// token binding an account to a player name on ONE world for a limited time; the game server verifies it
/// offline — no callback to the control plane on join, so a token outage can never lock players out of a
/// running world. Wire format: <c>v1.&lt;b64url account&gt;.&lt;b64url name&gt;.&lt;expiry unix&gt;.&lt;hex sig&gt;</c>;
/// the signature covers the world name, so a token for world A never opens world B.
/// </summary>
public static class HostedJoinToken
{
    private const string VersionPrefix = "v1";

    /// <summary>Issues a token admitting <paramref name="playerName"/> (account <paramref name="accountId"/>)
    /// to <paramref name="worldName"/> until <paramref name="expiresUnixSeconds"/>.</summary>
    public static string Create(string secret, string worldName, string accountId, string playerName, long expiresUnixSeconds)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("Join token secret must not be empty.", nameof(secret));
        }

        string account = Base64Url(accountId);
        string name = Base64Url(playerName);
        string sig = Sign(secret, worldName, accountId, playerName, expiresUnixSeconds);
        return $"{VersionPrefix}.{account}.{name}.{expiresUnixSeconds}.{sig}";
    }

    /// <summary>
    /// Verifies a token against this world's secret and clock. Returns false (with <paramref name="error"/>
    /// set) on any mismatch — malformed, tampered, expired, or issued for a different world. On success,
    /// <paramref name="accountId"/>/<paramref name="playerName"/> carry the identity the control plane vouched for.
    /// </summary>
    public static bool TryValidate(
        string secret,
        string worldName,
        string? token,
        long nowUnixSeconds,
        out string accountId,
        out string playerName,
        out string error)
    {
        accountId = string.Empty;
        playerName = string.Empty;

        if (string.IsNullOrEmpty(token))
        {
            error = "missing token";
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length != 5 || parts[0] != VersionPrefix)
        {
            error = "malformed token";
            return false;
        }

        string account, name;
        try
        {
            account = FromBase64Url(parts[1]);
            name = FromBase64Url(parts[2]);
        }
        catch (FormatException)
        {
            error = "malformed token";
            return false;
        }

        if (!long.TryParse(parts[3], out long expires))
        {
            error = "malformed token";
            return false;
        }

        string expected = Sign(secret, worldName, account, name, expires);
        if (!FixedTimeEquals(parts[4], expected))
        {
            error = "invalid signature";
            return false;
        }

        // Expiry only counts once the signature is proven, so an attacker learns nothing from timing here.
        if (expires < nowUnixSeconds)
        {
            error = "token expired";
            return false;
        }

        accountId = account;
        playerName = name;
        error = string.Empty;
        return true;
    }

    private static string Sign(string secret, string worldName, string accountId, string playerName, long expires)
    {
        var payload = $"{VersionPrefix}\n{worldName}\n{accountId}\n{playerName}\n{expires}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));

        // Manual hex: netstandard2.1 (Unity profile) has no Convert.ToHexString.
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
        {
            sb.Append(b.ToString("X2"));
        }

        return sb.ToString();
    }

    /// <summary>Length-then-XOR-accumulate comparison so signature checks don't leak a match prefix through
    /// timing (same loop as <see cref="SecretCompare"/>; <c>CryptographicOperations.FixedTimeEquals</c> is available on
    /// netstandard2.1 — see <see cref="BasicAuth"/> — folding the copies together is a separate cleanup).</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
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

    private static string Base64Url(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FromBase64Url(string value)
    {
        string s = value.Replace('-', '+').Replace('_', '/');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=')));
    }
}
