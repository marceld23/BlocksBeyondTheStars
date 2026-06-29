// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BlocksBeyondTheStars.Build;
using UnityEngine;
using UnityEngine.Networking;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Optional Glitch Aegis/deployment API bridge. It is dormant in normal open-source builds and only starts
    /// when a generated secret partial or local environment variables explicitly enable it.
    /// </summary>
    public sealed class GlitchIntegration : MonoBehaviour
    {
        private const float HeartbeatSeconds = 60f;
        private static GlitchIntegration _instance;

        private string _apiBaseUrl;
        private string _titleId;
        private string _titleToken;
        private string _installId;
        private string _sessionId;
        private bool _paused;
        private bool _online;

        public static bool IsConfigured => TryReadConfig(out _, out _, out _, out _, out _);
        public static bool IsOnline => _instance != null && _instance._online;
        public static string InstallId => _instance == null ? string.Empty : _instance._installId;

        public static bool AutoJoinRequested
            => IsTruthy(FirstNonEmpty(
                ReadQueryValue(Application.absoluteURL, "bbs_auto_join"),
                ReadQueryValue(Application.absoluteURL, "auto_join"),
                GetEnv("BBS_AUTO_JOIN")));

        public static string AutoJoinPlayerName
            => FirstNonEmpty(
                ReadQueryValue(Application.absoluteURL, "player_name"),
                ReadQueryValue(Application.absoluteURL, "bbs_player_name"),
                GetEnv("BBS_PLAYER_NAME"));

        public static bool TryGetConfiguredServer(out string host, out string port, out string password)
        {
            host = FirstNonEmpty(
                ReadQueryValue(Application.absoluteURL, "server_host"),
                ReadQueryValue(Application.absoluteURL, "game_server_host"),
                GlitchIntegrationSecrets.ServerHost,
                GetEnv("GLITCH_SERVER_HOST"));
            port = FirstNonEmpty(
                ReadQueryValue(Application.absoluteURL, "server_port"),
                ReadQueryValue(Application.absoluteURL, "game_server_port"),
                GlitchIntegrationSecrets.ServerPort,
                GetEnv("GLITCH_SERVER_PORT"));
            password = FirstNonEmpty(
                ReadQueryValue(Application.absoluteURL, "server_password"),
                ReadQueryValue(Application.absoluteURL, "game_server_password"),
                GlitchIntegrationSecrets.ServerPassword,
                GetEnv("GLITCH_SERVER_PASSWORD"));

            return IsConfigured && !string.IsNullOrWhiteSpace(host);
        }

        public static void InstallIfConfigured()
        {
            if (_instance != null || !IsConfigured)
            {
                return;
            }

            var go = new GameObject("GlitchIntegration");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GlitchIntegration>();
        }

        public static bool TrySubmitScore(string boardKey, double score, IDictionary<string, string> metadata = null, Action<bool, string> onComplete = null)
        {
            if (!CanUseApi(onComplete))
            {
                return false;
            }

            var scores = new Dictionary<string, double> { [boardKey] = score };
            _instance.StartCoroutine(_instance.SubmitProgress(scores, null, metadata, onComplete));
            return true;
        }

        public static bool TrySubmitStats(IDictionary<string, double> stats, IDictionary<string, string> metadata = null, Action<bool, string> onComplete = null)
        {
            if (!CanUseApi(onComplete) || stats == null || stats.Count == 0)
            {
                onComplete?.Invoke(false, "No stats were supplied.");
                return false;
            }

            _instance.StartCoroutine(_instance.SubmitProgress(null, stats, metadata, onComplete));
            return true;
        }

        public static bool TryGetLeaderboard(string boardKey, bool aroundMe, Action<bool, string> onComplete, string seasonId = null)
        {
            if (!CanUseApi(onComplete))
            {
                return false;
            }

            _instance.StartCoroutine(_instance.GetLeaderboard(boardKey, aroundMe, seasonId, onComplete));
            return true;
        }

        public static bool TryGetAchievements(Action<bool, string> onComplete)
        {
            if (!CanUseApi(onComplete))
            {
                return false;
            }

            string url = _instance.InstallUrl("achievements");
            _instance.StartCoroutine(_instance.SendGet(url, onComplete));
            return true;
        }

        public static bool TryListCloudSaves(Action<bool, string> onComplete)
        {
            if (!CanUseApi(onComplete))
            {
                return false;
            }

            string url = _instance.InstallUrl("saves");
            _instance.StartCoroutine(_instance.SendGet(url, onComplete));
            return true;
        }

        public static bool TryUploadCloudSave(
            int slotIndex,
            byte[] rawPayload,
            int baseVersion,
            string saveType,
            string slotName,
            string metadataJson,
            long playDurationSeconds,
            Action<bool, string> onComplete = null)
        {
            if (!CanUseApi(onComplete))
            {
                return false;
            }

            if (slotIndex < 0 || slotIndex > 99 || rawPayload == null || rawPayload.Length == 0)
            {
                onComplete?.Invoke(false, "Invalid cloud-save payload.");
                return false;
            }

            _instance.StartCoroutine(_instance.UploadCloudSave(
                slotIndex,
                rawPayload,
                baseVersion,
                saveType,
                slotName,
                metadataJson,
                playDurationSeconds,
                onComplete));
            return true;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (!TryReadConfig(out _apiBaseUrl, out _titleId, out _titleToken, out string testInstallId, out _sessionId))
            {
                enabled = false;
                return;
            }

            _installId = ResolveInstallId(testInstallId);
            if (string.IsNullOrWhiteSpace(_installId))
            {
                Debug.Log("[Glitch] Optional integration is configured, but no install_id was provided.");
                enabled = false;
            }
        }

        private void Start()
        {
            if (enabled)
            {
                StartCoroutine(HeartbeatLoop());
            }
        }

        private void OnApplicationPause(bool pauseStatus) => _paused = pauseStatus;
        private void OnApplicationFocus(bool hasFocus) => _paused = !hasFocus;

        private static bool CanUseApi(Action<bool, string> onComplete)
        {
            if (_instance != null && _instance.enabled && !string.IsNullOrWhiteSpace(_instance._installId))
            {
                return true;
            }

            onComplete?.Invoke(false, "Glitch integration is not configured for this build/session.");
            return false;
        }

        private IEnumerator HeartbeatLoop()
        {
            yield return SendHeartbeat();
            yield return ValidateInstall();

            while (enabled)
            {
                float elapsed = 0f;
                while (elapsed < HeartbeatSeconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!_paused)
                {
                    yield return SendHeartbeat();
                }
            }
        }

        private IEnumerator SendHeartbeat()
        {
            string body = BuildHeartbeatJson();
            using (var request = CreateJsonRequest(TitleUrl("installs"), "POST", body))
            {
                yield return request.SendWebRequest();
                _online = IsSuccess(request);
                if (!_online)
                {
                    Debug.LogWarning("[Glitch] Install heartbeat failed: " + DescribeFailure(request));
                }
            }
        }

        private IEnumerator ValidateInstall()
        {
            using (var request = CreateJsonRequest(InstallUrl("validate"), "POST", "{}"))
            {
                yield return request.SendWebRequest();
                _online = IsSuccess(request);
                if (!_online)
                {
                    Debug.LogWarning("[Glitch] Install validation failed: " + DescribeFailure(request));
                }
            }
        }

        private IEnumerator SubmitProgress(IDictionary<string, double> scores, IDictionary<string, double> stats, IDictionary<string, string> metadata, Action<bool, string> onComplete)
        {
            string json = BuildSubmitJson(scores, stats, metadata);
            using (var request = CreateJsonRequest(InstallUrl("submit"), "POST", json))
            {
                yield return request.SendWebRequest();
                Complete(request, onComplete);
            }
        }

        private IEnumerator GetLeaderboard(string boardKey, bool aroundMe, string seasonId, Action<bool, string> onComplete)
        {
            var url = new StringBuilder(TitleUrl("leaderboards/" + UrlEncode(boardKey)));
            bool hasQuery = false;
            if (aroundMe)
            {
                url.Append("?around_me=true&install_id=").Append(UrlEncode(_installId));
                hasQuery = true;
            }

            if (!string.IsNullOrWhiteSpace(seasonId))
            {
                url.Append(hasQuery ? '&' : '?').Append("season_id=").Append(UrlEncode(seasonId));
            }

            yield return SendGet(url.ToString(), onComplete);
        }

        private IEnumerator SendGet(string url, Action<bool, string> onComplete)
        {
            using (var request = CreateRequest(url, "GET"))
            {
                yield return request.SendWebRequest();
                Complete(request, onComplete);
            }
        }

        private IEnumerator UploadCloudSave(
            int slotIndex,
            byte[] rawPayload,
            int baseVersion,
            string saveType,
            string slotName,
            string metadataJson,
            long playDurationSeconds,
            Action<bool, string> onComplete)
        {
            string json = BuildCloudSaveJson(slotIndex, rawPayload, baseVersion, saveType, slotName, metadataJson, playDurationSeconds);
            using (var request = CreateJsonRequest(InstallUrl("saves"), "POST", json))
            {
                yield return request.SendWebRequest();
                Complete(request, onComplete);
            }
        }

        private UnityWebRequest CreateJsonRequest(string url, string method, string body)
        {
            var request = CreateRequest(url, method);
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }

        private UnityWebRequest CreateRequest(string url, string method)
        {
            var request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 15,
            };
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + _titleToken);
            return request;
        }

        private void Complete(UnityWebRequest request, Action<bool, string> onComplete)
        {
            bool ok = IsSuccess(request);
            string response = request.downloadHandler == null ? string.Empty : request.downloadHandler.text;
            if (!ok && string.IsNullOrWhiteSpace(response))
            {
                response = DescribeFailure(request);
            }

            onComplete?.Invoke(ok, response);
        }

        private static bool TryReadConfig(out string apiBaseUrl, out string titleId, out string titleToken, out string testInstallId, out string sessionId)
        {
            apiBaseUrl = FirstNonEmpty(GlitchIntegrationSecrets.ApiBaseUrl, GetEnv("GLITCH_API_URL"));
            titleId = FirstNonEmpty(GlitchIntegrationSecrets.TitleId, GetEnv("GLITCH_TITLE_ID"));
            titleToken = FirstNonEmpty(GlitchIntegrationSecrets.TitleToken, GetEnv("GLITCH_TITLE_TOKEN"));
            testInstallId = FirstNonEmpty(GlitchIntegrationSecrets.DeveloperTestInstallId, GetEnv("GLITCH_TEST_INSTALL_ID"));
            sessionId = FirstNonEmpty(ReadQueryValue(Application.absoluteURL, "session_id"), GetEnv("GLITCH_SESSION_ID"));

            bool enabled = GlitchIntegrationSecrets.Enabled || IsTruthy(GetEnv("GLITCH_ENABLE_AEGIS")) || IsTruthy(GetEnv("GLITCH_ENABLE_GLITCH"));
            return enabled
                && !string.IsNullOrWhiteSpace(apiBaseUrl)
                && !string.IsNullOrWhiteSpace(titleId)
                && !string.IsNullOrWhiteSpace(titleToken);
        }

        private static string ResolveInstallId(string testInstallId)
        {
            string fromUrl = FirstNonEmpty(
                ReadQueryValue(Application.absoluteURL, "install_id"),
                ReadQueryValue(Application.absoluteURL, "glitch_install_id"));
            if (!string.IsNullOrWhiteSpace(fromUrl))
            {
                return fromUrl;
            }

            string fromArgs = FirstNonEmpty(
                ReadCommandLineValue("--install_id"),
                ReadCommandLineValue("-install_id"),
                ReadCommandLineAssignment("install_id"),
                GetEnv("GLITCH_INSTALL_ID"));
            if (!string.IsNullOrWhiteSpace(fromArgs))
            {
                return fromArgs;
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return testInstallId;
#else
            return string.Empty;
#endif
        }

        private string BuildHeartbeatJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendStringProperty(sb, "user_install_id", _installId);
            sb.Append(',');
            AppendStringProperty(sb, "platform", PlatformName());
            sb.Append(',');
            AppendStringProperty(sb, "game_version", AppShell.Version);
            if (!string.IsNullOrWhiteSpace(_sessionId))
            {
                sb.Append(',');
                AppendStringProperty(sb, "session_id", _sessionId);
            }

            sb.Append('}');
            return sb.ToString();
        }

        private static string BuildSubmitJson(IDictionary<string, double> scores, IDictionary<string, double> stats, IDictionary<string, string> metadata)
        {
            var sb = new StringBuilder();
            sb.Append("{\"idempotency_key\":\"").Append(Guid.NewGuid().ToString("N")).Append("\",\"payload\":{");
            sb.Append("\"scores\":");
            AppendNumberMap(sb, scores);
            sb.Append(",\"stats\":");
            AppendNumberMap(sb, stats);
            sb.Append(",\"metadata\":");
            AppendStringMap(sb, metadata);
            sb.Append("}}");
            return sb.ToString();
        }

        private static string BuildCloudSaveJson(
            int slotIndex,
            byte[] rawPayload,
            int baseVersion,
            string saveType,
            string slotName,
            string metadataJson,
            long playDurationSeconds)
        {
            string encoded = Convert.ToBase64String(rawPayload);
            string checksum = Sha256Hex(rawPayload);
            var sb = new StringBuilder();
            sb.Append('{');
            AppendNumberProperty(sb, "slot_index", slotIndex);
            sb.Append(',');
            AppendStringProperty(sb, "payload", encoded);
            sb.Append(',');
            AppendStringProperty(sb, "checksum", checksum);
            sb.Append(',');
            AppendStringProperty(sb, "save_type", string.IsNullOrWhiteSpace(saveType) ? "manual" : saveType);
            sb.Append(',');
            AppendStringProperty(sb, "client_timestamp", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendNumberProperty(sb, "base_version", baseVersion);
            sb.Append(',');
            AppendStringProperty(sb, "slot_name", slotName ?? string.Empty);
            sb.Append(",\"metadata\":").Append(IsJsonObject(metadataJson) ? metadataJson.Trim() : "{}");
            sb.Append(',');
            AppendStringProperty(sb, "device_id", SystemInfo.deviceUniqueIdentifier ?? string.Empty);
            sb.Append(',');
            AppendStringProperty(sb, "platform", PlatformName());
            sb.Append(',');
            AppendStringProperty(sb, "game_version", AppShell.Version);
            sb.Append(',');
            AppendStringProperty(sb, "last_played_at", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            sb.Append(',');
            AppendNumberProperty(sb, "play_duration_seconds", Math.Max(0, playDurationSeconds));
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendStringProperty(StringBuilder sb, string name, string value)
        {
            sb.Append('"').Append(JsonEscape(name)).Append("\":\"").Append(JsonEscape(value ?? string.Empty)).Append('"');
        }

        private static void AppendNumberProperty(StringBuilder sb, string name, long value)
        {
            sb.Append('"').Append(JsonEscape(name)).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendNumberMap(StringBuilder sb, IDictionary<string, double> values)
        {
            sb.Append('{');
            bool first = true;
            if (values != null)
            {
                foreach (var kv in values)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key))
                    {
                        continue;
                    }

                    if (!first)
                    {
                        sb.Append(',');
                    }

                    sb.Append('"').Append(JsonEscape(kv.Key)).Append("\":").Append(kv.Value.ToString("R", CultureInfo.InvariantCulture));
                    first = false;
                }
            }

            sb.Append('}');
        }

        private static void AppendStringMap(StringBuilder sb, IDictionary<string, string> values)
        {
            sb.Append('{');
            bool first = true;
            if (values != null)
            {
                foreach (var kv in values)
                {
                    if (string.IsNullOrWhiteSpace(kv.Key))
                    {
                        continue;
                    }

                    if (!first)
                    {
                        sb.Append(',');
                    }

                    sb.Append('"').Append(JsonEscape(kv.Key)).Append("\":\"").Append(JsonEscape(kv.Value ?? string.Empty)).Append('"');
                    first = false;
                }
            }

            sb.Append('}');
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 32)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            return sb.ToString();
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        private static bool IsJsonObject(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            return trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal);
        }

        private string TitleUrl(string suffix) => _apiBaseUrl.TrimEnd('/') + "/api/titles/" + UrlEncode(_titleId) + "/" + suffix;
        private string InstallUrl(string suffix) => _apiBaseUrl.TrimEnd('/') + "/api/titles/" + UrlEncode(_titleId) + "/installs/" + UrlEncode(_installId) + "/" + suffix;

        private static bool IsSuccess(UnityWebRequest request)
        {
            return request.result == UnityWebRequest.Result.Success
                && request.responseCode >= 200
                && request.responseCode < 300;
        }

        private static string DescribeFailure(UnityWebRequest request)
        {
            string status = request.responseCode > 0 ? request.responseCode.ToString(CultureInfo.InvariantCulture) : "network";
            return string.IsNullOrWhiteSpace(request.error) ? status : status + " " + request.error;
        }

        private static string ReadQueryValue(string url, string key)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            int question = url.IndexOf('?');
            if (question < 0 || question == url.Length - 1)
            {
                return string.Empty;
            }

            string query = url.Substring(question + 1);
            int hash = query.IndexOf('#');
            if (hash >= 0)
            {
                query = query.Substring(0, hash);
            }

            foreach (string part in query.Split('&'))
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                int equals = part.IndexOf('=');
                string candidate = equals < 0 ? part : part.Substring(0, equals);
                if (!string.Equals(UrlDecode(candidate), key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return equals < 0 ? string.Empty : UrlDecode(part.Substring(equals + 1));
            }

            return string.Empty;
        }

        private static string ReadCommandLineValue(string key)
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                    {
                        return args[i + 1];
                    }
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static string ReadCommandLineAssignment(string key)
        {
            try
            {
                string prefix = key + "=";
                foreach (string arg in Environment.GetCommandLineArgs())
                {
                    if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return arg.Substring(prefix.Length);
                    }
                }
            }
            catch (Exception)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static string GetEnv(string name)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name) ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static bool IsTruthy(string value)
        {
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static string UrlEncode(string value) => UnityWebRequest.EscapeURL(value ?? string.Empty).Replace("+", "%20");
        private static string UrlDecode(string value) => UnityWebRequest.UnEscapeURL((value ?? string.Empty).Replace("+", " "));

        private static string PlatformName()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WebGLPlayer:
                    return "webgl";
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsEditor:
                    return "windows";
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxEditor:
                    return "linux";
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXEditor:
                    return "macos";
                default:
                    return Application.platform.ToString().ToLowerInvariant();
            }
        }
    }
}
