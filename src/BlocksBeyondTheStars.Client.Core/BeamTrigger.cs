// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What one frame of a <see cref="BeamTrigger"/> asks the client to do.</summary>
    public readonly struct BeamStep
    {
        public BeamStep(bool started, bool tick, bool stopped, bool starved)
        {
            Started = started;
            Tick = tick;
            Stopped = stopped;
            Starved = starved;
        }

        /// <summary>The beam came on this frame (a tick comes with it).</summary>
        public bool Started { get; }

        /// <summary>Send one beam tick to the server now.</summary>
        public bool Tick { get; }

        /// <summary>The beam went off this frame, or a hold whose beam the server or the reserve had ended was let go —
        /// tell the server and take the beam away.</summary>
        public bool Stopped { get; }

        /// <summary>The suit is at its reserve: the beam stopped, or did not start, for lack of energy — say so once.</summary>
        public bool Starved { get; }
    }

    /// <summary>
    /// The fire control of a held-beam weapon (#3898, the Guardian laser), Unity-free: it tells a tap from a hold and
    /// paces the beam's ticks. The Unity side feeds it the control's state once a frame and does what the returned
    /// <see cref="BeamStep"/> says; it never fires the weapon's shot.
    /// <para><b>A press is always the shot.</b> The ordinary attack fires on the press itself, exactly as for every
    /// other weapon — so a short press is the shot with the timing it always had. Only when the control is still down
    /// <see cref="BeamWeaponRules.HoldSeconds"/> later does the beam come on, and it stays on while the control is held.
    /// Letting go ends it; nothing fires on the release, so a hold never ends in a stray shot.</para>
    /// <para><b>It needs its own press.</b> A control that was already down when the weapon came into the hand (the
    /// drill's hold, then a scroll to the laser) starts nothing until it is released and pressed again.</para>
    /// <para><b>The ticks</b> are at least one weapon tick apart (never closer — the server drops a tick that comes too
    /// early), the first one on the frame the beam starts.</para>
    /// <para><b>It stops</b> on the release, when the hand can no longer fire (<c>canBeam</c>: the item changed, a menu,
    /// a seat, a carried block …), when the suit reaches its reserve (<c>canPay</c>), and when the server says so
    /// (<see cref="ServerStopped"/>). After a stop for energy or by the server it waits for the release before a new
    /// press counts — a held control does not restart the beam four times a second. That release is reported as a stop
    /// as well, so the server hears the end of every hold it refused.</para>
    /// </summary>
    public sealed class BeamTrigger
    {
        /// <summary>Where the control is in a press.</summary>
        public enum Phase
        {
            /// <summary>Not pressed (or pressed without a beam weapon in the hand).</summary>
            Idle,

            /// <summary>Pressed with a beam weapon in the hand; the shot went out, the hold is not long enough yet.</summary>
            Pressed,

            /// <summary>The beam is on.</summary>
            Beam,

            /// <summary>The beam ended for energy or by the server while the control is still down.</summary>
            Spent,
        }

        private bool _wasHeld;
        private float _pressedFor;
        private float _sinceTick;

        /// <summary>The current phase.</summary>
        public Phase State { get; private set; } = Phase.Idle;

        /// <summary>The beam is on.</summary>
        public bool Beaming => State == Phase.Beam;

        /// <summary>
        /// One frame. <paramref name="held"/>: the fire control is down. <paramref name="canBeam"/>: a beam weapon is in
        /// the hand and the body may fire. <paramref name="canPay"/>: the suit can pay one more tick above its reserve.
        /// <paramref name="tick"/>: the weapon's seconds between two hits.
        /// </summary>
        public BeamStep Update(float dt, bool held, bool canBeam, bool canPay, float tick)
        {
            bool pressed = held && !_wasHeld;
            _wasHeld = held;
            switch (State)
            {
                case Phase.Pressed:
                    return WhilePressed(dt, held, canBeam, canPay);
                case Phase.Beam:
                    return WhileBeaming(dt, held, canBeam, canPay, tick);
                case Phase.Spent:
                    if (held)
                    {
                        return default;
                    }

                    // The release ends the hold for the server too. It remembers that it refused this hold and says so
                    // only once; without the release it would stay silent at the next refused hold, and the client's
                    // own beam of that hold would stay drawn.
                    State = Phase.Idle;
                    return new BeamStep(started: false, tick: false, stopped: true, starved: false);
                default:
                    if (pressed && canBeam)
                    {
                        State = Phase.Pressed;
                        _pressedFor = 0f;
                    }

                    return default;
            }
        }

        private BeamStep WhilePressed(float dt, bool held, bool canBeam, bool canPay)
        {
            if (!held || !canBeam)
            {
                State = Phase.Idle; // a tap: the shot already went out on the press
                return default;
            }

            _pressedFor += dt;
            if (_pressedFor < BeamWeaponRules.HoldSeconds)
            {
                return default;
            }

            if (!canPay)
            {
                State = Phase.Spent;
                return new BeamStep(started: false, tick: false, stopped: false, starved: true);
            }

            State = Phase.Beam;
            _sinceTick = 0f;
            return new BeamStep(started: true, tick: true, stopped: false, starved: false);
        }

        private BeamStep WhileBeaming(float dt, bool held, bool canBeam, bool canPay, float tick)
        {
            if (!held || !canBeam)
            {
                State = Phase.Idle;
                return new BeamStep(started: false, tick: false, stopped: true, starved: false);
            }

            if (!canPay)
            {
                State = Phase.Spent;
                return new BeamStep(started: false, tick: false, stopped: true, starved: true);
            }

            _sinceTick += dt;
            if (_sinceTick < tick)
            {
                return default;
            }

            _sinceTick = 0f; // from this frame on: two ticks are never closer than one weapon tick
            return new BeamStep(started: false, tick: true, stopped: false, starved: false);
        }

        /// <summary>The server ended the beam (the reserve, a seat, the other side …): it is off until the control is
        /// released and pressed again. True when a beam was on.</summary>
        public bool ServerStopped()
        {
            if (State != Phase.Beam)
            {
                return false;
            }

            State = Phase.Spent;
            return true;
        }

        /// <summary>The on-foot input did not run this frame (a menu, a vehicle, the ship's view): whatever was going on
        /// is over. True when a beam was on, or a hold the beam ended in was still down (<see cref="Phase.Spent"/> — its
        /// release is owed to the server, see <see cref="Update"/>) — the caller then tells the server and takes the beam
        /// away.</summary>
        public bool Cancel()
        {
            bool tellServer = State == Phase.Beam || State == Phase.Spent;
            State = Phase.Idle;
            _wasHeld = true; // a control still down when the input comes back is no new press
            return tellServer;
        }
    }
}
