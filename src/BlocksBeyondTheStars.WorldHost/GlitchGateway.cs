// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.WorldHost;

/// <summary>Outcome of a glitch.fun session grant: either the join material for one arcade world or a
/// player-safe error (mapped to a machine code + HTTP status in Program.cs).</summary>
public sealed record GlitchSessionResult(
    bool Ok,
    string Error,
    string WorldId = "",
    string WorldName = "",
    string PlayerName = "",
    string WssUrl = "",
    string JoinToken = "",
    long TokenExpiresUnix = 0);

/// <summary>
/// The glitch.fun arcade gateway: instant, account-less entry into a small pool of persistent
/// multiplayer worlds that exist ONLY for the glitch.fun platform (channel 'glitch' — hidden from every
/// portal listing). The flow mirrors Glitch's Aegis contract:
///
///  1. Glitch serves our WebGL build with an <c>install_id</c> URL param.
///  2. The client posts it to <c>/api/glitch/session</c>; we validate the install server-to-server
///     against the Glitch API (the title token lives only here, never in the public build), assign a
///     stable per-install player name, pick an arcade world with headroom (waking one on demand) and
///     mint the normal HMAC join token for the synthetic guest identity <c>glitch:&lt;install_id&gt;</c>.
///  3. The client heartbeats through <c>/api/glitch/heartbeat</c>; we relay to Glitch's install
///     endpoint (their playtime/payout signal) and answer 403 for banned installs — which the client
///     treats as "stop the game", giving the operator a live kick lever without accounts.
///
/// Ban/guest bookkeeping keys on Glitch's pseudonymous install id (arcade guests have no account, so
/// the account ban lever cannot reach them).
/// </summary>
public sealed class GlitchGateway
{
    private const string GuestAccountPrefix = "glitch:";
    private const string FallbackName = "Explorer";
    private const int ValidateCacheSeconds = 300;

    /// <summary>Heartbeats arrive once a minute per install; this only blunts scripted hammering.</summary>
    private const int HeartbeatsPerMinutePerInstall = 6;

    // Glitch install ids are UUIDs in practice; accept a conservative superset so a format tweak on
    // their side doesn't lock everyone out.
    private static readonly Regex InstallIdRx = new("^[A-Za-z0-9_-]{8,64}$", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    /// <summary>Glitch's Cloud Save cap: 10 MB DECODED per slot; the base64 wire form is ~4/3 of that.</summary>
    private const long MaxSaveBytesDecoded = 10 * 1024 * 1024;

    /// <summary>The one slot the browser singleplayer uses (one world per player).</summary>
    private const int SaveSlotIndex = 0;

    private readonly WorldHostConfig _config;
    private readonly HostRegistry _registry;
    private readonly WorldOrchestrator _orchestrator;
    private readonly HttpClient _glitchApi;
    private readonly RateLimiter _heartbeatLimit;
    private readonly RateLimiter _saveLimit;
    private readonly Func<WorldRecord, Task<string?>> _statusReader;
    private readonly Lock _poolGate = new();
    private readonly ConcurrentDictionary<string, (long ExpiresUnix, string UserName)> _validateCache = new();

    public GlitchGateway(
        WorldHostConfig config,
        HostRegistry registry,
        WorldOrchestrator orchestrator,
        HttpMessageHandler? glitchHttpHandler = null,
        RateLimiter? heartbeatLimit = null,
        Func<WorldRecord, Task<string?>>? statusReader = null,
        RateLimiter? saveLimit = null)
    {
        _config = config;
        _registry = registry;
        _orchestrator = orchestrator;
        _glitchApi = glitchHttpHandler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(15) } // save payloads are MBs, not the 5s probe class
            : new HttpClient(glitchHttpHandler) { Timeout = TimeSpan.FromSeconds(15) };
        _heartbeatLimit = heartbeatLimit ?? new RateLimiter(HeartbeatsPerMinutePerInstall, TimeSpan.FromMinutes(1));
        _saveLimit = saveLimit ?? new RateLimiter(config.GlitchSavesPerHourPerInstall, TimeSpan.FromHours(1));
        _statusReader = statusReader ?? orchestrator.ReadInstanceStatusAsync; // injectable for capacity tests
    }

    /// <summary>True when the gateway is switched on and has its Glitch credentials.</summary>
    public bool Enabled => _config.GlitchConfigured;

    /// <summary>Echoes the request origin when it is one of the configured Glitch origins (the value for
    /// Access-Control-Allow-Origin), else null. Exact match, trailing slash ignored.</summary>
    public string? ResolveCorsOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return null;
        }

        string normalized = origin.TrimEnd('/');
        return _config.GlitchAllowedOrigins
            .Any(allowed => string.Equals(allowed, normalized, StringComparison.OrdinalIgnoreCase))
            ? origin
            : null;
    }

    /// <summary>Extracts joinedPlayers from an instance's /status JSON; null when unreadable — callers
    /// show "?" (admin page) or treat the instance as unavailable (world pick).</summary>
    public static int? ParseJoinedPlayers(string statusJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(statusJson);
            return doc.RootElement.TryGetProperty("joinedPlayers", out var jp) ? jp.GetInt32() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Grants one glitch.fun visitor entry into an arcade world: validate the install with
    /// Glitch, resolve the (stable, suffixed) player name, pick a world with headroom — waking a
    /// sleeping one on demand — and mint the join token for the guest identity.</summary>
    public async Task<GlitchSessionResult> SessionAsync(string? installId, string? requestedName = null)
    {
        if (!Enabled)
        {
            return new GlitchSessionResult(false, "The glitch.fun gateway is disabled.");
        }

        installId = (installId ?? string.Empty).Trim();
        if (!InstallIdRx.IsMatch(installId))
        {
            return new GlitchSessionResult(false, "This install could not be verified with glitch.fun.");
        }

        if (_registry.GetGlitchBan(installId) is { } ban)
        {
            return new GlitchSessionResult(false, string.IsNullOrEmpty(ban.Reason)
                ? "This player is banned."
                : $"This player is banned: {ban.Reason}");
        }

        var (valid, glitchUserName) = await ValidateInstallAsync(installId).ConfigureAwait(false);
        if (!valid)
        {
            return new GlitchSessionResult(false, "This install could not be verified with glitch.fun.");
        }

        string playerName = ResolvePlayerName(requestedName, glitchUserName, installId);
        EnsurePool();

        var (world, error) = await PickWorldAsync().ConfigureAwait(false);
        if (world is null)
        {
            return new GlitchSessionResult(false, error);
        }

        // The synthetic guest account rides the normal join path unchanged: the token's accountId is an
        // opaque string to the instance (it only ever compares it against BBS_WORLD_OWNER), and arcade
        // worlds have no password, so none of the account-only gates apply.
        var guest = new AccountRecord(GuestAccountPrefix + installId, playerName, AcceptedTermsVersion: _config.TermsVersion);
        var (grant, joinError) = await _orchestrator.JoinAsync(world.Id, guest, playerName).ConfigureAwait(false);
        if (grant is null)
        {
            return new GlitchSessionResult(false, joinError);
        }

        _registry.TouchGlitchGuest(installId, playerName);
        return new GlitchSessionResult(true, string.Empty,
            WorldId: grant.WorldId,
            WorldName: grant.DisplayName,
            PlayerName: playerName,
            WssUrl: grant.WssUrl,
            JoinToken: grant.JoinToken,
            TokenExpiresUnix: grant.TokenExpiresUnix);
    }

    /// <summary>Relays a client heartbeat to Glitch's install endpoint (their playtime/payout signal),
    /// keeping the title token server-side. Banned installs get 403 without any relay — the client
    /// treats that as "stop the game". Returns the status code + body to answer the client with.</summary>
    public async Task<(int Status, string Body)> RelayHeartbeatAsync(
        string? installId, string? sessionId, string? platform, string? gameVersion)
    {
        if (!Enabled)
        {
            return (StatusCodes.Status404NotFound, """{"error":"The glitch.fun gateway is disabled.","code":"glitch_disabled"}""");
        }

        installId = (installId ?? string.Empty).Trim();
        if (!InstallIdRx.IsMatch(installId))
        {
            return (StatusCodes.Status422UnprocessableEntity, """{"error":"This install could not be verified with glitch.fun.","code":"glitch_invalid_install"}""");
        }

        if (_registry.GetGlitchBan(installId) is not null)
        {
            return (StatusCodes.Status403Forbidden, """{"error":"This player is banned.","code":"banned","valid":false}""");
        }

        if (!_heartbeatLimit.TryPass(installId))
        {
            return (StatusCodes.Status429TooManyRequests, """{"error":"Too many requests — please wait a bit and try again.","code":"rate_limited"}""");
        }

        var payload = new Dictionary<string, string> { ["user_install_id"] = installId, ["platform"] = platform is { Length: > 0 } ? platform : "web" };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            payload["session_id"] = sessionId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            payload["game_version"] = gameVersion.Trim();
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, InstallsUrl());
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.GlitchTitleToken);
            request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await _glitchApi.SendAsync(request).ConfigureAwait(false);

            // Pass Glitch's DRM verdict (403 = license gone) through so the client's existing block
            // path works, but never forward their raw body — it is not ours to expose.
            return response.IsSuccessStatusCode
                ? (StatusCodes.Status200OK, """{"ok":true}""")
                : ((int)response.StatusCode, """{"error":"glitch.fun refused the heartbeat.","code":"glitch_invalid_install"}""");
        }
        catch (Exception)
        {
            // Glitch being unreachable must not read as a revoked license — the client keeps playing
            // and simply retries in a minute.
            return (StatusCodes.Status503ServiceUnavailable, """{"error":"glitch.fun is unreachable right now.","code":"glitch_unreachable"}""");
        }
    }

    // ---------------- Cloud-save relay (browser singleplayer) ----------------
    // The browser singleplayer's world snapshot lives in Glitch's per-player Cloud Save (slot 0);
    // relaying keeps the title token server-side and lets us enforce size + rate limits. Glitch's
    // rules honored here: base64 payload, SHA-256 of the DECODED bytes, 10 MB decoded cap, and the
    // 409 optimistic-concurrency conflict flow (never silently overwritten — resolve is explicit).
    // Guests get GUEST_NOT_ALLOWED from Glitch; we pass that through as 403/glitch_guest_saves and
    // the client stays local-only (IndexedDB).

    /// <summary>Common gate for every save-relay call: enabled, well-formed install id, not banned.
    /// Null = pass; otherwise the (status, body) to answer with.</summary>
    private (int Status, string Body)? SaveGate(ref string? installId)
    {
        if (!Enabled)
        {
            return (StatusCodes.Status404NotFound, """{"error":"The glitch.fun gateway is disabled.","code":"glitch_disabled"}""");
        }

        installId = (installId ?? string.Empty).Trim();
        if (!InstallIdRx.IsMatch(installId))
        {
            return (StatusCodes.Status422UnprocessableEntity, """{"error":"This install could not be verified with glitch.fun.","code":"glitch_invalid_install"}""");
        }

        if (_registry.GetGlitchBan(installId) is not null)
        {
            return (StatusCodes.Status403Forbidden, """{"error":"This player is banned.","code":"banned"}""");
        }

        return null;
    }

    /// <summary>Fetches the latest browser-singleplayer save (slot 0) for an install from Glitch's
    /// Cloud Save. 200 {version, payload} | 404 none yet | 403 guest (Cloud Save needs a logged-in
    /// Glitch account) | 503 Glitch unreachable.</summary>
    public async Task<(int Status, string Body)> LoadSaveAsync(string? installId)
    {
        if (SaveGate(ref installId) is { } refused)
        {
            return refused;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{InstallsUrl()}/{Uri.EscapeDataString(installId!)}/saves?include_payload=1");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.GlitchTitleToken);
            using var response = await _glitchApi.SendAsync(request).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return (StatusCodes.Status403Forbidden, """{"error":"Cloud saves need a logged-in Glitch account.","code":"glitch_guest_saves"}""");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (StatusCodes.Status503ServiceUnavailable, """{"error":"glitch.fun is unreachable right now.","code":"glitch_unreachable"}""");
            }

            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (FindSlotSave(doc.RootElement) is not { } save)
            {
                return (StatusCodes.Status404NotFound, """{"error":"No cloud save yet.","code":"glitch_no_save"}""");
            }

            int version = save.TryGetProperty("version", out var v) ? v.GetInt32() : 0;
            string payload = save.TryGetProperty("payload", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String
                ? p.GetString() ?? string.Empty
                : string.Empty;
            if (payload.Length == 0)
            {
                return (StatusCodes.Status404NotFound, """{"error":"No cloud save yet.","code":"glitch_no_save"}""");
            }

            return (StatusCodes.Status200OK,
                System.Text.Json.JsonSerializer.Serialize(new { version, payload }));
        }
        catch (Exception)
        {
            return (StatusCodes.Status503ServiceUnavailable, """{"error":"glitch.fun is unreachable right now.","code":"glitch_unreachable"}""");
        }
    }

    /// <summary>Stores a browser-singleplayer save into Glitch's Cloud Save (slot 0), computing the
    /// required SHA-256 over the DECODED bytes server-side. 200 {version} | 409 conflict
    /// {saveId, conflictId, serverVersion} (resolve explicitly) | 403 guest | 413 too large.</summary>
    public async Task<(int Status, string Body)> StoreSaveAsync(string? installId, string? payloadBase64, int baseVersion)
    {
        if (SaveGate(ref installId) is { } refused)
        {
            return refused;
        }

        if (!_saveLimit.TryPass(installId!))
        {
            return (StatusCodes.Status429TooManyRequests, """{"error":"Too many requests — please wait a bit and try again.","code":"rate_limited"}""");
        }

        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(payloadBase64 ?? string.Empty);
        }
        catch (FormatException)
        {
            return (StatusCodes.Status400BadRequest, """{"error":"The save payload is not valid base64.","code":"save_invalid"}""");
        }

        if (decoded.Length == 0)
        {
            return (StatusCodes.Status400BadRequest, """{"error":"Empty upload.","code":"upload_empty"}""");
        }

        if (decoded.Length > MaxSaveBytesDecoded)
        {
            return (StatusCodes.Status413PayloadTooLarge, """{"error":"The save exceeds the 10 MB cloud-save limit.","code":"upload_too_large"}""");
        }

        string checksum = Convert.ToHexString(SHA256.HashData(decoded)).ToLowerInvariant();
        var body = new Dictionary<string, object?>
        {
            ["slot_index"] = SaveSlotIndex,
            ["payload"] = payloadBase64,
            ["checksum"] = checksum,
            ["save_type"] = "auto",
            ["client_timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
            ["base_version"] = Math.Max(0, baseVersion),
            ["slot_name"] = "Browser World",
            ["platform"] = "web",
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{InstallsUrl()}/{Uri.EscapeDataString(installId!)}/saves");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.GlitchTitleToken);
            request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await _glitchApi.SendAsync(request).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.StatusCode == (System.Net.HttpStatusCode)409)
            {
                using var conflict = System.Text.Json.JsonDocument.Parse(responseBody);
                return (StatusCodes.Status409Conflict, System.Text.Json.JsonSerializer.Serialize(new
                {
                    code = "glitch_save_conflict",
                    saveId = ReadString(conflict.RootElement, "save_id"),
                    conflictId = ReadString(conflict.RootElement, "conflict_id"),
                    serverVersion = ReadInt(conflict.RootElement, "server_version"),
                }));
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return (StatusCodes.Status403Forbidden, """{"error":"Cloud saves need a logged-in Glitch account.","code":"glitch_guest_saves"}""");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (StatusCodes.Status503ServiceUnavailable, """{"error":"glitch.fun refused the save.","code":"glitch_unreachable"}""");
            }

            return (StatusCodes.Status200OK,
                System.Text.Json.JsonSerializer.Serialize(new { version = ExtractVersion(responseBody) }));
        }
        catch (Exception)
        {
            return (StatusCodes.Status503ServiceUnavailable, """{"error":"glitch.fun is unreachable right now.","code":"glitch_unreachable"}""");
        }
    }

    /// <summary>Resolves a cloud-save conflict (Glitch's explicit optimistic-concurrency flow).
    /// Choice: keep_server discards the local state, use_client overwrites the cloud.</summary>
    public async Task<(int Status, string Body)> ResolveSaveAsync(string? installId, string? saveId, string? conflictId, string? choice)
    {
        if (SaveGate(ref installId) is { } refused)
        {
            return refused;
        }

        if (choice is not ("keep_server" or "use_client") || string.IsNullOrWhiteSpace(saveId) || string.IsNullOrWhiteSpace(conflictId))
        {
            return (StatusCodes.Status400BadRequest, """{"error":"Malformed conflict resolution.","code":"save_invalid"}""");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{InstallsUrl()}/{Uri.EscapeDataString(installId!)}/saves/{Uri.EscapeDataString(saveId)}/resolve");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.GlitchTitleToken);
            request.Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["conflict_id"] = conflictId, ["choice"] = choice }),
                Encoding.UTF8, "application/json");
            using var response = await _glitchApi.SendAsync(request).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (StatusCodes.Status503ServiceUnavailable, """{"error":"glitch.fun refused the resolution.","code":"glitch_unreachable"}""");
            }

            return (StatusCodes.Status200OK,
                System.Text.Json.JsonSerializer.Serialize(new { version = ExtractVersion(responseBody) }));
        }
        catch (Exception)
        {
            return (StatusCodes.Status503ServiceUnavailable, """{"error":"glitch.fun is unreachable right now.","code":"glitch_unreachable"}""");
        }
    }

    /// <summary>Finds slot 0 in Glitch's list-saves response — tolerant of {data:[...]} or a bare array.</summary>
    private static System.Text.Json.JsonElement? FindSlotSave(System.Text.Json.JsonElement root)
    {
        var list = root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("data", out var data)
            ? data
            : root;
        if (list.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return null;
        }

        foreach (var save in list.EnumerateArray())
        {
            if (save.TryGetProperty("slot_index", out var slot) && slot.GetInt32() == SaveSlotIndex)
            {
                return save;
            }
        }

        return null;
    }

    /// <summary>Pulls the stored save's new version out of Glitch's response — {data:{version}} or {version}.</summary>
    private static int ExtractVersion(string responseBody)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
            var root = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("data", out var data)
                ? data
                : doc.RootElement;
            return root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("version", out var v)
                ? v.GetInt32()
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string ReadString(System.Text.Json.JsonElement root, string name)
        => root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty(name, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt(System.Text.Json.JsonElement root, string name)
        => root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty(name, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Number
            ? p.GetInt32()
            : 0;

    /// <summary>Lazily creates missing arcade pool worlds up to the configured count. Idempotent;
    /// serialized so two first sessions can't double-create.</summary>
    public void EnsurePool()
    {
        if (!Enabled)
        {
            return;
        }

        lock (_poolGate)
        {
            int existing = _registry.ListWorldsByChannel(WorldChannel.Glitch).Count;
            for (int i = existing + 1; i <= _config.GlitchWorldCount; i++)
            {
                _registry.CreateGlitchWorld($"Glitch Arcade {i}");
            }
        }
    }

    /// <summary>Picks the arcade world for the next guest: a running world with player headroom first
    /// (probed live), then a sleeping one to wake on demand. Racy by design — the instance's own
    /// BBS_MAX_PLAYERS cap is the hard fence; a lost race answers "Server is full" client-side.</summary>
    private async Task<(WorldRecord? World, string Error)> PickWorldAsync()
    {
        var pool = _registry.ListWorldsByChannel(WorldChannel.Glitch);
        if (pool.Count == 0)
        {
            return (null, "All arcade worlds are full right now — please try again in a few minutes.");
        }

        foreach (var world in pool.Where(w => w.Status == WorldStatus.Running))
        {
            // Unreachable /status = treat as unavailable rather than dog-piling a sick instance.
            if (await _statusReader(world).ConfigureAwait(false) is { } statusJson
                && ParseJoinedPlayers(statusJson) is { } joined
                && joined < _config.GlitchMaxPlayers)
            {
                return (world, string.Empty);
            }
        }

        if (pool.FirstOrDefault(w => w.Status != WorldStatus.Running) is { } sleeping)
        {
            return (sleeping, string.Empty);
        }

        return (null, "All arcade worlds are full right now — please try again in a few minutes.");
    }

    /// <summary>Resolves the guest's in-game name: the requested name, else the Glitch account name,
    /// else "Explorer" — reserved/blocked base names fall back too — plus a stable 3-hex-char suffix
    /// derived from the install id. The suffix gives every install the SAME identity on every visit
    /// (player state on the instance keys on the name) while two guests both named "Max" stay distinct.</summary>
    public string ResolvePlayerName(string? requestedName, string? glitchUserName, string installId)
    {
        string baseName = SanitizeName(requestedName);
        if (baseName.Length == 0)
        {
            baseName = SanitizeName(glitchUserName);
        }

        if (baseName.Length == 0 || _registry.IsReservedName(baseName) || _registry.IsBlockedName(baseName))
        {
            baseName = FallbackName;
        }

        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(installId)))
            .ToLowerInvariant().Substring(0, 3);
        return baseName + "-" + suffix;
    }

    /// <summary>Printable chars only, single spaces, capped so the "-abc" suffix still fits the
    /// instance's 24-char player-name limit.</summary>
    private static string SanitizeName(string? name)
    {
        var sb = new StringBuilder();
        foreach (char c in (name ?? string.Empty).Trim())
        {
            if (char.IsControl(c))
            {
                continue;
            }

            if (c == ' ' && (sb.Length == 0 || sb[^1] == ' '))
            {
                continue; // no leading/double spaces
            }

            sb.Append(c);
            if (sb.Length == 20)
            {
                break;
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Validates an install id with Glitch's validate endpoint (server-to-server, title token
    /// only here). Positive results are cached briefly so page reloads don't hammer Glitch; failures are
    /// never cached. Returns the Glitch account's user_name when the platform provides one.</summary>
    private async Task<(bool Valid, string UserName)> ValidateInstallAsync(string installId)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (_validateCache.TryGetValue(installId, out var cached) && cached.ExpiresUnix > now)
        {
            return (true, cached.UserName);
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{InstallsUrl()}/{Uri.EscapeDataString(installId)}/validate");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.GlitchTitleToken);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await _glitchApi.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return (false, string.Empty);
            }

            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("valid", out var validProp) && !validProp.GetBoolean())
            {
                return (false, string.Empty);
            }

            string userName = doc.RootElement.TryGetProperty("user_name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String
                ? nameProp.GetString() ?? string.Empty
                : string.Empty;
            _validateCache[installId] = (now + ValidateCacheSeconds, userName);
            return (true, userName);
        }
        catch (Exception)
        {
            return (false, string.Empty);
        }
    }

    private string InstallsUrl()
        => $"{_config.GlitchApiBaseUrl.TrimEnd('/')}/api/titles/{Uri.EscapeDataString(_config.GlitchTitleId)}/installs";
}
