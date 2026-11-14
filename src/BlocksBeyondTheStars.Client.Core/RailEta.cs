// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What a station's departure board shows for one stop (#3633).</summary>
    public enum RailEtaState
    {
        /// <summary>No forecast: no train on autopilot runs to the stop (a manual train, an empty line, beyond the horizon).</summary>
        None,

        /// <summary>The train stands at this stop; <see cref="RailEtaReading.Seconds"/> until it departs.</summary>
        Here,

        /// <summary>The train is on its way and halts here in <see cref="RailEtaReading.Seconds"/>.</summary>
        Coming,
    }

    /// <summary>A board's reading: the state and the seconds that go with it, as of the train list it was computed from.</summary>
    public readonly struct RailEtaReading
    {
        public RailEtaReading(RailEtaState state, double seconds)
        {
            State = state;
            Seconds = seconds;
        }

        public RailEtaState State { get; }

        public double Seconds { get; }

        public static RailEtaReading None => new RailEtaReading(RailEtaState.None, 0.0);
    }

    /// <summary>
    /// The departure board's forecast (#3633): when an autopilot train next halts at a stop of its line, from the train's
    /// replicated state alone (<see cref="NetTrain"/>: arc, direction, facing, wagons, setting, speed, halt and the halt's
    /// remaining seconds) — no wire, no save. It runs the server's own run forward in small steps with the shared rules: the
    /// speed profile (<see cref="RailMotion.Limit"/> / <see cref="RailMotion.NextSpeed"/> — the leg from the target behind to
    /// the target ahead, the braking curve into the target, the bend limit over the whole train from
    /// <see cref="RailCurveLimits"/>), the halt centred on each stop it reaches (<see cref="RailRules.HaltMiddleArc"/>) for the
    /// stop's halt time (30 s for a public train), the turn round at a stop near an open line's end while it stands, and the
    /// bounce at an open line's end. A stop the train stands at or has just left (within a wagon's length) does not catch it
    /// again, like the server's last stop. Accurate to a second or two on the generated intercity line (the client test runs
    /// it against the real server's train); it is a forecast, the server stays the truth.
    /// </summary>
    public sealed class RailEta
    {
        /// <summary>Seconds per simulated step (the server's tick is shorter; the braking curve's last steps differ by a
        /// fraction of a second).</summary>
        public const double StepSeconds = 0.1;

        /// <summary>The forecast looks this far ahead at most (seconds); a train later than that reads as no forecast.</summary>
        public const double HorizonSeconds = 1200.0;

        private const float SameArc = 0.001f;

        private readonly RailSpline _spline;
        private readonly RailCurveLimits _curves;
        private readonly float[] _stops;

        /// <summary>A forecast for one line: its spline (the one the client poses its trains on) and its stops' arcs.</summary>
        public RailEta(RailSpline spline, IReadOnlyList<float> stopArcs)
        {
            _spline = spline ?? throw new ArgumentNullException(nameof(spline));
            _curves = RailCurveLimits.Build(spline);
            _stops = new float[stopArcs?.Count ?? 0];
            for (int i = 0; i < _stops.Length; i++)
            {
                _stops[i] = stopArcs![i];
            }
        }

        /// <summary>The spline this forecast was built for (a rebuilt line has a new one: build a new forecast).</summary>
        public RailSpline Spline => _spline;

        /// <summary>The number of stops on the line.</summary>
        public int StopCount => _stops.Length;

        /// <summary>The arc of stop <paramref name="index"/>.</summary>
        public float StopArc(int index) => _stops[index];

        /// <summary>When <paramref name="train"/> next halts at stop <paramref name="stop"/>, as of the moment its list was
        /// received: <see cref="RailEtaState.Here"/> while it stands there (the seconds until it departs),
        /// <see cref="RailEtaState.Coming"/> with the seconds until it halts there, else <see cref="RailEtaState.None"/>.</summary>
        public RailEtaReading Read(NetTrain train, int stop)
        {
            float total = _spline.TotalArc;
            if (train is null || stop < 0 || stop >= _stops.Length || total <= 0f || !train.Autopilot)
            {
                return RailEtaReading.None;
            }

            var run = Run.From(train, this);
            if (run.Halted && run.Last == stop)
            {
                return new RailEtaReading(RailEtaState.Here, Math.Max(0.0, train.HaltRemaining));
            }

            if (run.Halted && !(train.HaltRemaining > 0f))
            {
                return RailEtaReading.None; // it stands without a countdown (no room to run): it is not coming
            }

            double eta = Simulate(ref run, stop);
            return eta < 0.0 ? RailEtaReading.None : new RailEtaReading(RailEtaState.Coming, eta);
        }

        /// <summary>The stop nearest to a world arc on the line (−1 without stops) — the board's own stop.</summary>
        public int StopNearest(float arc)
        {
            int best = -1;
            float bestGap = float.MaxValue;
            for (int i = 0; i < _stops.Length; i++)
            {
                float gap = Math.Abs(ArcDelta(arc, _stops[i]));
                if (gap < bestGap)
                {
                    bestGap = gap;
                    best = i;
                }
            }

            return best;
        }

        // ---------------------------------------------------------------- the run

        /// <summary>The simulated train: its middle's arc, direction, speed and halt (the server's state, read off the wire).</summary>
        private struct Run
        {
            public float Middle;
            public int Direction;
            public int Facing;
            public float Span;
            public int Wagons;
            public float Speed;
            public int Setting;
            public bool Halted;
            public double HaltLeft;
            public int Last;
            public double HaltSeconds;

            public static Run From(NetTrain t, RailEta eta)
            {
                int wagons = t.Wagons?.Length ?? 0;
                int facing = RailRules.PoseFacing(t.Facing, t.Direction);
                float span = RailRules.TrainSpan(wagons);
                var run = new Run
                {
                    Middle = RailRules.MiddleArc(t.Arc, facing, span),
                    Direction = t.Direction < 0 ? -1 : 1,
                    Facing = facing,
                    Span = span,
                    Wagons = wagons,
                    Speed = float.IsNaN(t.Velocity) ? 0f : Math.Abs(t.Velocity),
                    Setting = t.Speed,
                    Halted = t.Halted,
                    HaltLeft = Math.Max(0.0, t.HaltRemaining),
                    HaltSeconds = RailRules.IsPublic(t.OwnerId) ? RailRules.PublicStopHaltSeconds : RailRules.StopHaltSeconds,
                };
                run.Last = eta.StopWithin(run.Middle, run.Span, RailRules.WagonLength);
                return run;
            }
        }

        /// <summary>Runs the train forward until it halts at <paramref name="stop"/>: the seconds that took, or −1 past the
        /// horizon.</summary>
        private double Simulate(ref Run r, int stop)
        {
            float total = _spline.TotalArc;
            bool closed = _spline.Closed;
            var (lo, hi) = RailRules.CabRange(total, r.Span, r.Facing);
            float midLo = RailRules.MiddleArc(lo, r.Facing, r.Span), midHi = RailRules.MiddleArc(hi, r.Facing, r.Span);
            double t = 0.0;
            while (t < HorizonSeconds)
            {
                if (r.Halted)
                {
                    t += r.HaltLeft; // it departs from rest
                    r.Halted = false;
                    r.Speed = 0f;
                    continue;
                }

                var (ahead, behind, target) = Targets(r, midLo, midHi, total, closed);
                float run = Step(ref r, ahead + behind, ahead, target >= 0);
                t += StepSeconds;
                if (target >= 0 && run >= ahead)
                {
                    if (target == stop)
                    {
                        return t;
                    }

                    HaltAt(ref r, target, lo, hi, total, closed);
                    continue;
                }

                Move(ref r, run, midLo, midHi, total, closed);
                if (r.Last >= 0 && Math.Abs(ArcDelta(r.Middle, HaltArc(r.Last, r.Span))) >= RailRules.WagonLength)
                {
                    r.Last = -1; // clear of the stop it left: that stop may catch it next time
                }
            }

            return -1.0;
        }

        /// <summary>The distance to the target ahead and the one behind (the open line's ends, the stops), and the stop ahead
        /// the train halts at (−1: an end, or none on a loop) — the server's <c>TrainTargets</c>.</summary>
        private (float Ahead, float Behind, int Stop) Targets(in Run r, float midLo, float midHi, float total, bool closed)
        {
            float ahead = closed ? float.PositiveInfinity : (r.Direction > 0 ? midHi - r.Middle : r.Middle - midLo);
            float behind = closed ? float.PositiveInfinity : (r.Direction > 0 ? r.Middle - midLo : midHi - r.Middle);
            int target = -1;
            for (int i = 0; i < _stops.Length; i++)
            {
                float d = RailMotion.Gap(r.Middle, HaltArc(i, r.Span), r.Direction, total, closed);
                float back = closed ? (d <= SameArc ? 0f : total - d) : -d;
                if (back >= -SameArc)
                {
                    behind = Math.Min(behind, Math.Max(0f, back));
                }

                if (i != r.Last && d >= -SameArc && d < ahead)
                {
                    ahead = Math.Max(0f, d);
                    target = i;
                }
            }

            return (Math.Max(0f, ahead), Math.Max(0f, behind), target);
        }

        /// <summary>One step of the speed profile; returns the distance run (cut to land on a stop ahead).</summary>
        private float Step(ref Run r, float leg, float ahead, bool stopAhead)
        {
            float cab = RailRules.ArcForMiddleAt(r.Middle, r.Facing, r.Span);
            float tail = RailRules.WagonArc(cab, Math.Max(0, r.Wagons - 1), r.Facing);
            float half = RailRules.WagonLength * 0.5f;
            float curve = _curves.MinOver(Math.Min(cab, tail) - half, Math.Max(cab, tail) + half);
            float limit = RailMotion.Limit(RailMotion.TopSpeed(r.Setting), leg, ahead, curve);
            r.Speed = RailMotion.NextSpeed(r.Speed, limit, StepSeconds);
            float run = r.Speed * (float)StepSeconds;
            return stopAhead ? Math.Min(run, ahead + SameArc) : run;
        }

        /// <summary>The halt at a stop on the way: centred on it, its halt time, turned round when the line's end is near.</summary>
        private void HaltAt(ref Run r, int stop, float lo, float hi, float total, bool closed)
        {
            r.Middle = HaltArc(stop, r.Span);
            r.Halted = true;
            r.HaltLeft = r.HaltSeconds;
            r.Speed = 0f;
            r.Last = stop;
            if (!closed)
            {
                float cab = RailRules.ArcForMiddleAt(r.Middle, r.Facing, r.Span);
                if ((r.Direction > 0 ? hi - cab : cab - lo) < RailRules.WagonLength * 2f)
                {
                    r.Direction = -r.Direction;
                }
            }
        }

        /// <summary>Runs the middle on; an open line's end turns the train round from rest.</summary>
        private static void Move(ref Run r, float run, float midLo, float midHi, float total, bool closed)
        {
            if (closed)
            {
                float m = (r.Middle + (r.Direction * run)) % total;
                r.Middle = m < 0f ? m + total : m;
                return;
            }

            float room = r.Direction > 0 ? midHi - r.Middle : r.Middle - midLo;
            if (run >= room)
            {
                r.Middle = r.Direction > 0 ? midHi : midLo;
                r.Direction = -r.Direction;
                r.Speed = 0f;
                return;
            }

            r.Middle += r.Direction * run;
        }

        /// <summary>Where the train's middle halts for stop <paramref name="index"/>.</summary>
        private float HaltArc(int index, float span) => RailRules.HaltMiddleArc(_stops[index], span, _spline.TotalArc, _spline.Closed);

        /// <summary>The stop whose halt point lies within <paramref name="reach"/> of the middle (−1: none).</summary>
        private int StopWithin(float middle, float span, float reach)
        {
            for (int i = 0; i < _stops.Length; i++)
            {
                if (Math.Abs(ArcDelta(middle, HaltArc(i, span))) < reach)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The signed arc from <paramref name="from"/> to <paramref name="to"/> (the short way round on a loop).</summary>
        private float ArcDelta(float from, float to)
        {
            float d = to - from;
            float total = _spline.TotalArc;
            if (_spline.Closed && total > 0f)
            {
                d %= total;
                if (d > total * 0.5f)
                {
                    d -= total;
                }
                else if (d < -total * 0.5f)
                {
                    d += total;
                }
            }

            return d;
        }
    }
}
