// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Transport;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// WebGL client transport. Browsers cannot use the native LiteNetLib UDP socket, so the
    /// WebGL player talks to the authoritative .NET server through a browser WebSocket while
    /// still exchanging the same NetCodec payloads as native clients.
    /// </summary>
    public sealed class BrowserWebSocketClientTransport : IClientTransport
    {
        private readonly ConcurrentQueue<Action> _events = new();
        private int _socketId;
        private bool _disposed;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<byte[]> PayloadReceived;

        [DllImport("__Internal")]
        private static extern void BbsWsConnect(int id, string url);

        [DllImport("__Internal")]
        private static extern void BbsWsSendBase64(int id, string payload);

        [DllImport("__Internal")]
        private static extern void BbsWsDisconnect(int id);

        public void Connect(string host, int port)
        {
            if (_disposed)
            {
                return;
            }

            NetCodec.UseJsonEncoding = true;
            Disconnect();
            _socketId = BrowserWebSocketBridge.Register(this);
            string url = BuildWebSocketUrl(host, port);
            Debug.Log($"Connecting to browser WebSocket game server: {url}");
            BbsWsConnect(_socketId, url);
        }

        public void Send(byte[] payload, DeliveryMode mode)
        {
            if (_disposed || _socketId == 0 || payload == null || payload.Length == 0)
            {
                return;
            }

            // The JavaScript bridge sends binary WebSocket frames. Base64 is only the
            // WebGL interop envelope between managed C# and the browser runtime.
            BbsWsSendBase64(_socketId, Convert.ToBase64String(payload));
        }

        public void Poll()
        {
            while (_events.TryDequeue(out var action))
            {
                action();
            }
        }

        public void Disconnect()
        {
            if (_socketId == 0)
            {
                return;
            }

            int id = _socketId;
            _socketId = 0;
            BrowserWebSocketBridge.Unregister(id);
            BbsWsDisconnect(id);
        }

        public void Dispose()
        {
            _disposed = true;
            Disconnect();
        }

        internal void QueueConnected()
            => _events.Enqueue(() => Connected?.Invoke());

        internal void QueueDisconnected()
            => _events.Enqueue(() => Disconnected?.Invoke());

        internal void QueuePayload(string base64)
        {
            if (string.IsNullOrEmpty(base64))
            {
                return;
            }

            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                Debug.LogWarning($"Dropped malformed WebSocket payload: {ex.Message}");
                return;
            }

            _events.Enqueue(() => PayloadReceived?.Invoke(payload));
        }

        internal void QueueError(string message)
            => _events.Enqueue(() => Debug.LogWarning("Browser WebSocket error: " + message));

        private static string BuildWebSocketUrl(string host, int port)
        {
            host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            if (host.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
                || host.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
            {
                return host;
            }

            if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "wss://" + host.Substring("https://".Length).TrimEnd('/');
            }

            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "ws://" + host.Substring("http://".Length).TrimEnd('/');
            }

            string scheme = Application.absoluteURL.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? "wss"
                : "ws";

            bool omitPort = port <= 0
                || (scheme == "wss" && port == 443)
                || (scheme == "ws" && port == 80);
            return omitPort ? $"{scheme}://{host}" : $"{scheme}://{host}:{port}";
        }

        private sealed class BrowserWebSocketBridge : MonoBehaviour
        {
            private const string GameObjectName = "BbsWebSocketBridge";
            private static readonly Dictionary<int, BrowserWebSocketClientTransport> Transports = new();
            private static BrowserWebSocketBridge _instance;
            private static int _nextId;

            public static int Register(BrowserWebSocketClientTransport transport)
            {
                EnsureInstance();
                int id = ++_nextId;
                Transports[id] = transport;
                return id;
            }

            public static void Unregister(int id)
            {
                if (id != 0)
                {
                    Transports.Remove(id);
                }
            }

            public void HandleOpen(string idText)
            {
                if (TryGet(idText, out var transport))
                {
                    transport.QueueConnected();
                }
            }

            public void HandleClose(string idText)
            {
                if (TryGet(idText, out var transport))
                {
                    transport.QueueDisconnected();
                }
            }

            public void HandleError(string payload)
            {
                Split(payload, out int id, out string message);
                if (Transports.TryGetValue(id, out var transport))
                {
                    transport.QueueError(message);
                }
            }

            public void HandleMessage(string payload)
            {
                Split(payload, out int id, out string message);
                if (Transports.TryGetValue(id, out var transport))
                {
                    transport.QueuePayload(message);
                }
            }

            private static void EnsureInstance()
            {
                if (_instance != null)
                {
                    return;
                }

                var go = new GameObject(GameObjectName);
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<BrowserWebSocketBridge>();
            }

            private static bool TryGet(string idText, out BrowserWebSocketClientTransport transport)
            {
                if (int.TryParse(idText, out int id) && Transports.TryGetValue(id, out transport))
                {
                    return true;
                }

                transport = null;
                return false;
            }

            private static void Split(string payload, out int id, out string message)
            {
                int separator = string.IsNullOrEmpty(payload) ? -1 : payload.IndexOf('|');
                if (separator < 0 || !int.TryParse(payload.Substring(0, separator), out id))
                {
                    id = 0;
                    message = string.Empty;
                    return;
                }

                message = payload.Substring(separator + 1);
            }
        }
    }
}
#endif
