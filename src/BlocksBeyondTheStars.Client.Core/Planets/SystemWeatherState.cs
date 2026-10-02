// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Weather;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The client's copy of the server's <see cref="SystemWeather"/> snapshot (#2173) with the cheap extrapolation
    /// the views need between two heartbeats: fronts keep drifting at their own speed, a running body clock keeps
    /// turning, the system clock keeps counting. The weather states themselves are never guessed — they change only
    /// with the next snapshot from the server.
    /// </summary>
    public sealed class SystemWeatherState
    {
        /// <summary>Seconds per system-day — the server's fixed orbital reference day.</summary>
        public const double SystemDaySeconds = 600.0;

        private readonly Dictionary<string, NetBodyWeather> _bodies = new Dictionary<string, NetBodyWeather>(StringComparer.Ordinal);
        private double _receivedAt;
        private double _systemTimeDays;

        /// <summary>The system the last snapshot described ("" before the first).</summary>
        public string SystemId { get; private set; } = string.Empty;

        /// <summary>Bumped on every snapshot — views re-project when it changes.</summary>
        public int Version { get; private set; }

        /// <summary>Stores a snapshot received at <paramref name="now"/> (seconds, any monotonic clock).</summary>
        public void Apply(SystemWeather message, double now)
        {
            if (message is null)
            {
                return;
            }

            _bodies.Clear();
            foreach (var b in message.Bodies ?? Array.Empty<NetBodyWeather>())
            {
                if (b != null && !string.IsNullOrEmpty(b.BodyId))
                {
                    _bodies[b.BodyId] = b;
                }
            }

            SystemId = message.SystemId ?? string.Empty;
            _systemTimeDays = message.SystemTimeDays;
            _receivedAt = now;
            Version++;
        }

        /// <summary>Forgets everything (world exit).</summary>
        public void Clear()
        {
            _bodies.Clear();
            SystemId = string.Empty;
            Version++;
        }

        /// <summary>The last known weather of a body.</summary>
        public bool TryGet(string bodyId, out NetBodyWeather body)
        {
            if (!string.IsNullOrEmpty(bodyId) && _bodies.TryGetValue(bodyId, out var b))
            {
                body = b;
                return true;
            }

            body = null!;
            return false;
        }

        /// <summary>The shared system clock at <paramref name="now"/>.</summary>
        public double SystemTimeDays(double now) => _systemTimeDays + Math.Max(0.0, now - _receivedAt) / SystemDaySeconds;

        /// <summary>The body's day fraction at <paramref name="now"/>: a running clock advances by its day length, an
        /// unloaded body waits at its arrival time.</summary>
        public double TimeOfDay(NetBodyWeather body, double now, double dayLengthSeconds)
        {
            if (body is null)
            {
                return 0.35;
            }

            double t = body.TimeOfDay;
            if (body.ClockRunning && dayLengthSeconds > 1.0)
            {
                t += Math.Max(0.0, now - _receivedAt) / dayLengthSeconds;
            }

            return t - Math.Floor(t);
        }

        /// <summary>The body's fronts at <paramref name="now"/>, each moved along its drift and wrapped around the
        /// circumference.</summary>
        public List<WeatherFrontState> Fronts(NetBodyWeather body, double now, int circumference)
        {
            var list = new List<WeatherFrontState>();
            if (body?.Fronts is null)
            {
                return list;
            }

            double dt = Math.Max(0.0, now - _receivedAt);
            double circ = Math.Max(1, circumference);
            foreach (var f in body.Fronts)
            {
                double cx = f.CenterX + f.Drift * dt;
                cx -= Math.Floor(cx / circ) * circ;
                list.Add(new WeatherFrontState(cx, f.HalfWidth, f.Drift, f.Boost));
            }

            return list;
        }

        /// <summary>The body's episode in the shared projection's form.</summary>
        public static WeatherEpisode Episode(NetBodyWeather body)
            => body is null
                ? new WeatherEpisode("clear", 0, 0, 0, 0f, 0f, false)
                : new WeatherEpisode(body.State, body.LadderSeverity, body.LadderFloor, body.LadderCeiling, body.Peak, body.Intensity, body.Dynamic);
    }
}
