// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BlocksBeyondTheStars.Client.Portal
{
    public sealed class PortalLoginResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;

        /// <summary>Stable machine code of the error (empty on success/unknown) — the UI localizes it.</summary>
        public string Code { get; set; } = string.Empty;
        public string AccountId { get; set; } = string.Empty;
        public string SessionToken { get; set; } = string.Empty;

        /// <summary>The community rules changed since this account accepted them — the player must
        /// re-accept on the portal website before world actions succeed.</summary>
        public bool TermsOutdated { get; set; }
    }

    public sealed class PortalWorldInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        /// <summary>The creator protected this world with a join password (#250) — the UI shows a lock.</summary>
        public bool HasPassword { get; set; }

        /// <summary>The owner listed this world in the public browser (opt-in; requires a password). Only
        /// meaningful for the caller's own worlds — the public listing returns only listed worlds.</summary>
        public bool IsPublic { get; set; }
    }

    public sealed class PortalWorldsResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;

        /// <summary>Stable machine code of the error (empty on success/unknown) — the UI localizes it.</summary>
        public string Code { get; set; } = string.Empty;
        public List<PortalWorldInfo> Worlds { get; set; } = new List<PortalWorldInfo>();
    }

    public sealed class PortalJoinResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;

        /// <summary>Stable machine code of the error (empty on success/unknown) — the UI localizes it.</summary>
        public string Code { get; set; } = string.Empty;
        public string NativeHost { get; set; } = string.Empty;
        public int NativePort { get; set; }
        public string WssUrl { get; set; } = string.Empty;
        public string JoinToken { get; set; } = string.Empty;
    }

    public sealed class PortalSimpleResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;

        /// <summary>Stable machine code of the error (empty on success/unknown) — the UI localizes it.</summary>
        public string Code { get; set; } = string.Empty;
    }

    /// <summary>Current community rules (GET /api/terms): the version number signup must echo, plus the
    /// full rules text in both languages so the client renders them in-game (#268).</summary>
    public sealed class PortalTermsResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;

        /// <summary>Stable machine code of the error (empty on success/unknown) — the UI localizes it.</summary>
        public string Code { get; set; } = string.Empty;
        public int Version { get; set; }
        public string TextDe { get; set; } = string.Empty;
        public string TextEn { get; set; } = string.Empty;
    }

    /// <summary>A single world, as answered by world creation (POST /api/worlds).</summary>
    public sealed class PortalWorldResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;

        /// <summary>Stable machine code of the error (empty on success/unknown) — the UI localizes it.</summary>
        public string Code { get; set; } = string.Empty;
        public PortalWorldInfo? World { get; set; }
    }

    /// <summary>A downloaded world save (GET /api/worlds/{id}/save): raw bytes on success, the usual
    /// error envelope otherwise (e.g. stop_first while the world still runs).</summary>
    public sealed class PortalSaveResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;

        /// <summary>Stable machine code of the error (empty on success/unknown) — the UI localizes it.</summary>
        public string Code { get; set; } = string.Empty;
        public byte[] Bytes { get; set; } = System.Array.Empty<byte>();
    }

    /// <summary>
    /// Client for the hosted-worlds control plane ("WorldHost") — full portal parity (#268-#270): sign
    /// up (incl. rules acceptance), sign in, create/list/join/manage your worlds, save backups, feedback
    /// and reports, account deletion. Mirrors <see cref="Feedback.FeedbackUploader"/>: plain
    /// <see cref="HttpClient"/> + System.Text.Json so the exact same code runs in the Unity player AND
    /// the headless test suite; calls are synchronous and never throw (the Unity layer runs them on a
    /// background task). Desktop only — the browser client never selects servers (HOSTED_WORLDS.md).
    /// </summary>
    public sealed class PortalClient
    {
        public const string DefaultPortalUrl = "https://play.blocksbeyondthestars.de";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly string _baseUrl;
        private readonly HttpClient _http;

        public PortalClient(string? baseUrl = null, HttpClient? http = null)
        {
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultPortalUrl : baseUrl!.Trim().TrimEnd('/');
            // Generous timeout: joining may WAKE a sleeping world (container start + world load, up to
            // ~90 s server-side). Other calls return in milliseconds and are unaffected by the ceiling.
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        }

        public PortalLoginResult Login(string name, string password)
        {
            var (status, body) = Post("/api/login", new { name, password, acceptedTermsVersion = 0 }, session: null);
            return ParseLogin(status, body);
        }

        public PortalWorldsResult ListWorlds(string session)
        {
            var (status, body) = Get("/api/worlds", session);
            return ParseWorlds(status, body);
        }

        /// <summary>Lists worlds other players opted into the public browser (not owner-scoped). Every
        /// listed world is password-gated — joining still needs the owner-shared password.</summary>
        public PortalWorldsResult ListPublicWorlds(string session)
        {
            var (status, body) = Get("/api/worlds/public", session);
            return ParseWorlds(status, body);
        }

        /// <summary>Requests a join grant; <paramref name="password"/> is the world's join password when
        /// the world is protected (#250) — null first, retried after a password_required/wrong_password code.</summary>
        public PortalJoinResult JoinWorld(string session, string worldId, string playerName, string? password = null)
        {
            var (status, body) = Post($"/api/worlds/{worldId}/join", new { playerName, password }, session);
            return ParseJoin(status, body);
        }

        public PortalSimpleResult Report(string session, string reportedName, string category, string message, string? worldId = null)
        {
            var (status, body) = Post("/api/reports", new { reportedName, category, message, worldId = worldId ?? string.Empty }, session);
            return ParseSimple(status, body);
        }

        /// <summary>Current rules version + text — anonymous; fetched before signup (the version must be
        /// echoed) and for the in-game rules screen (#268).</summary>
        public PortalTermsResult GetTerms()
        {
            var (status, body) = Get("/api/terms", session: null);
            return ParseTerms(status, body);
        }

        /// <summary>Creates an account; <paramref name="acceptedTermsVersion"/> must be the CURRENT
        /// version from <see cref="GetTerms"/> — the player accepted the rules in the signup UI.
        /// The answer carries the fresh session, so a successful signup is also a sign-in.</summary>
        public PortalLoginResult Signup(string name, string password, int acceptedTermsVersion)
        {
            var (status, body) = Post("/api/signup", new { name, password, acceptedTermsVersion }, session: null);
            return ParseLogin(status, body);
        }

        /// <summary>Re-accepts the current community rules after a version bump (terms_outdated).</summary>
        public PortalSimpleResult AcceptTerms(string session)
        {
            var (status, body) = Post("/api/accept-terms", new { }, session);
            return ParseSimple(status, body);
        }

        /// <summary>Creates a hosted world; <paramref name="password"/> (empty/null = open world)
        /// protects it with a join password (4-24 chars).</summary>
        public PortalWorldResult CreateWorld(string session, string name, string? password = null)
        {
            var (status, body) = Post("/api/worlds", new { name, password }, session);
            return ParseWorld(status, body);
        }

        /// <summary>Owner-only: set/change (4-24 chars) or remove (empty) the world's join password.</summary>
        public PortalSimpleResult SetWorldPassword(string session, string worldId, string password)
        {
            var (status, body) = Post($"/api/worlds/{worldId}/password", new { password }, session);
            return ParseSimple(status, body);
        }

        /// <summary>Owner-only: list (<paramref name="isPublic"/> true) or un-list a world in the public
        /// browser. Listing requires a join password first (server-enforced).</summary>
        public PortalSimpleResult SetWorldVisibility(string session, string worldId, bool isPublic)
        {
            var (status, body) = Post($"/api/worlds/{worldId}/visibility", new { @public = isPublic }, session);
            return ParseSimple(status, body);
        }

        public PortalSimpleResult StopWorld(string session, string worldId)
        {
            var (status, body) = Post($"/api/worlds/{worldId}/stop", new { }, session);
            return ParseSimple(status, body);
        }

        public PortalSimpleResult DeleteWorld(string session, string worldId)
        {
            var (status, body) = Delete($"/api/worlds/{worldId}", session);
            return ParseSimple(status, body);
        }

        /// <summary>Deletes the account, ALL its worlds and their saves — irreversible (DSGVO Art. 17).</summary>
        public PortalSimpleResult DeleteAccount(string session)
        {
            var (status, body) = Delete("/api/account", session);
            return ParseSimple(status, body);
        }

        /// <summary>Downloads the world's save (world.db) — the world must be stopped.</summary>
        public PortalSaveResult DownloadSave(string session, string worldId)
        {
            var (status, bytes) = GetBytes($"/api/worlds/{worldId}/save", session);
            return ParseSave(status, bytes);
        }

        /// <summary>Uploads a save (raw world.db bytes) — the world must be stopped; 50 MB server cap.</summary>
        public PortalSimpleResult UploadSave(string session, string worldId, byte[] save)
        {
            var (status, body) = PostBytes($"/api/worlds/{worldId}/save", save, session);
            return ParseSimple(status, body);
        }

        // ---------------- Response parsing (static + public: exercised directly by the test suite) ----------------

        public static PortalLoginResult ParseLogin(int status, string body)
        {
            var result = new PortalLoginResult();
            if (!Succeeded(status, body, out string error, out string code, out JsonDocument? doc))
            {
                result.Error = error;
                result.Code = code;
                return result;
            }

            using (doc)
            {
                result.Ok = true;
                result.AccountId = GetString(doc!, "accountId");
                result.SessionToken = GetString(doc!, "sessionToken");
                result.TermsOutdated = doc!.RootElement.TryGetProperty("termsOutdated", out var to) && to.ValueKind == JsonValueKind.True;
            }

            return result;
        }

        public static PortalWorldsResult ParseWorlds(int status, string body)
        {
            var result = new PortalWorldsResult();
            if (!Succeeded(status, body, out string error, out string code, out JsonDocument? doc))
            {
                result.Error = error;
                result.Code = code;
                return result;
            }

            using (doc)
            {
                result.Ok = true;
                if (doc!.RootElement.TryGetProperty("worlds", out var worlds) && worlds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var w in worlds.EnumerateArray())
                    {
                        result.Worlds.Add(new PortalWorldInfo
                        {
                            Id = GetString(w, "id"),
                            Name = GetString(w, "name"),
                            Status = GetString(w, "status"),
                            HasPassword = w.TryGetProperty("hasPassword", out var hp) && hp.ValueKind == JsonValueKind.True,
                            IsPublic = w.TryGetProperty("isPublic", out var ip) && ip.ValueKind == JsonValueKind.True,
                        });
                    }
                }
            }

            return result;
        }

        public static PortalJoinResult ParseJoin(int status, string body)
        {
            var result = new PortalJoinResult();
            if (!Succeeded(status, body, out string error, out string code, out JsonDocument? doc))
            {
                result.Error = error;
                result.Code = code;
                return result;
            }

            using (doc)
            {
                result.Ok = true;
                result.NativeHost = GetString(doc!, "nativeHost");
                result.WssUrl = GetString(doc!, "wssUrl");
                result.JoinToken = GetString(doc!, "joinToken");
                if (doc!.RootElement.TryGetProperty("nativePort", out var port) && port.TryGetInt32(out int p))
                {
                    result.NativePort = p;
                }
            }

            return result;
        }

        public static PortalSimpleResult ParseSimple(int status, string body)
        {
            var result = new PortalSimpleResult();
            if (!Succeeded(status, body, out string error, out string code, out JsonDocument? doc))
            {
                result.Error = error;
                result.Code = code;
                return result;
            }

            doc?.Dispose();
            result.Ok = true;
            return result;
        }

        public static PortalTermsResult ParseTerms(int status, string body)
        {
            var result = new PortalTermsResult();
            if (!Succeeded(status, body, out string error, out string code, out JsonDocument? doc))
            {
                result.Error = error;
                result.Code = code;
                return result;
            }

            using (doc)
            {
                result.Ok = true;
                result.TextDe = GetString(doc!, "textDe");
                result.TextEn = GetString(doc!, "textEn");
                if (doc!.RootElement.TryGetProperty("version", out var v) && v.TryGetInt32(out int version))
                {
                    result.Version = version;
                }
            }

            return result;
        }

        public static PortalWorldResult ParseWorld(int status, string body)
        {
            var result = new PortalWorldResult();
            if (!Succeeded(status, body, out string error, out string code, out JsonDocument? doc))
            {
                result.Error = error;
                result.Code = code;
                return result;
            }

            using (doc)
            {
                result.Ok = true;
                result.World = new PortalWorldInfo
                {
                    Id = GetString(doc!, "id"),
                    Name = GetString(doc!, "name"),
                    Status = GetString(doc!, "status"),
                    HasPassword = doc!.RootElement.TryGetProperty("hasPassword", out var hp) && hp.ValueKind == JsonValueKind.True,
                };
            }

            return result;
        }

        /// <summary>A save download is raw bytes on success; on failure the body is the usual JSON
        /// error envelope (decoded as UTF-8 before the shared parsing).</summary>
        public static PortalSaveResult ParseSave(int status, byte[] body)
        {
            var result = new PortalSaveResult();
            if (status is >= 200 and < 300)
            {
                result.Ok = true;
                result.Bytes = body;
                return result;
            }

            string text;
            try
            {
                text = Encoding.UTF8.GetString(body);
            }
            catch (Exception)
            {
                text = string.Empty;
            }

            Succeeded(status, text, out string error, out string code, out JsonDocument? doc);
            doc?.Dispose();
            result.Error = error;
            result.Code = code;
            return result;
        }

        /// <summary>Shared success/error shape: 2xx = ok (body parsed into <paramref name="doc"/>); anything
        /// else surfaces the server's player-safe <c>{"error": …}</c> text, or a status code fallback.</summary>
        private static bool Succeeded(int status, string body, out string error, out string code, out JsonDocument? doc)
        {
            doc = null;
            code = string.Empty;
            try
            {
                doc = string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                // non-JSON body (proxy error page); fall through to the status handling
            }

            if (status is >= 200 and < 300)
            {
                error = string.Empty;
                return true;
            }

            error = doc != null ? GetString(doc, "error") : string.Empty;
            code = doc != null ? GetString(doc, "code") : string.Empty;
            if (error.Length == 0)
            {
                error = status == 401 ? "unauthorized" : status == 0 ? "offline" : $"http_{status}";
                code = status == 401 ? "unauthorized" : status == 0 ? "offline" : code;
            }

            doc?.Dispose();
            doc = null;
            return false;
        }

        private static string GetString(JsonDocument doc, string property) => GetString(doc.RootElement, property);

        private static string GetString(JsonElement element, string property)
            => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

        // ---------------- Transport ----------------

        private (int Status, string Body) Post(string path, object payload, string? session)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
                };
                Authorize(request, session);
#pragma warning disable VSTHRD002 // Runs on a background Task (the menu awaits Task.Run) — no SynchronizationContext, cannot deadlock.
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                return ((int)response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
#pragma warning restore VSTHRD002
            }
            catch (Exception)
            {
                return (0, string.Empty); // offline/timeout/DNS — parsed as "offline"
            }
        }

        private (int Status, string Body) Get(string path, string? session)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
                Authorize(request, session);
#pragma warning disable VSTHRD002 // Runs on a background Task (the menu awaits Task.Run) — no SynchronizationContext, cannot deadlock.
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                return ((int)response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
#pragma warning restore VSTHRD002
            }
            catch (Exception)
            {
                return (0, string.Empty);
            }
        }

        private (int Status, string Body) Delete(string path, string? session)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, _baseUrl + path);
                Authorize(request, session);
#pragma warning disable VSTHRD002 // Runs on a background Task (the menu awaits Task.Run) — no SynchronizationContext, cannot deadlock.
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                return ((int)response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
#pragma warning restore VSTHRD002
            }
            catch (Exception)
            {
                return (0, string.Empty);
            }
        }

        private (int Status, byte[] Body) GetBytes(string path, string? session)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
                Authorize(request, session);
#pragma warning disable VSTHRD002 // Runs on a background Task (the menu awaits Task.Run) — no SynchronizationContext, cannot deadlock.
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                return ((int)response.StatusCode, response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());
#pragma warning restore VSTHRD002
            }
            catch (Exception)
            {
                return (0, System.Array.Empty<byte>());
            }
        }

        private (int Status, string Body) PostBytes(string path, byte[] payload, string? session)
        {
            try
            {
                using var content = new ByteArrayContent(payload);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path) { Content = content };
                Authorize(request, session);
#pragma warning disable VSTHRD002 // Runs on a background Task (the menu awaits Task.Run) — no SynchronizationContext, cannot deadlock.
                using var response = _http.SendAsync(request).GetAwaiter().GetResult();
                return ((int)response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
#pragma warning restore VSTHRD002
            }
            catch (Exception)
            {
                return (0, string.Empty);
            }
        }

        private static void Authorize(HttpRequestMessage request, string? session)
        {
            if (!string.IsNullOrEmpty(session))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + session);
            }
        }
    }
}
