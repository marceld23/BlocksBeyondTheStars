// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2778: putting the held item away and taking out the next one — one animation for every item, in first person
    /// (<c>Viewmodel</c>) and on the avatar (<c>PlayerAvatar</c>, so other players see it too). The old item sinks out of
    /// view (<see cref="HolsterSeconds"/>), the new one is swapped in at the bottom and rises into the hand
    /// (<see cref="DrawSeconds"/>), then settles with a small bump (<see cref="SettleSeconds"/>).
    /// <list type="bullet">
    /// <item>Purely cosmetic: <see cref="Finish"/> cuts it short (an action never waits for it — the server does not know
    /// about it).</item>
    /// <item>Fast scrolling: a new item while the old one is still going down only replaces the item waiting at the bottom;
    /// one while the new item is coming up turns it round from where it is — the hand never snaps, and only the last item
    /// picked is drawn.</item>
    /// <item>A used-up stack (<see cref="Begin"/> with <c>holster: false</c>): nothing to put away, the new item starts at
    /// the bottom.</item>
    /// </list>
    /// Unity-free so the headless suite pins it; the callers turn <see cref="Drop"/> and <see cref="Settle"/> into a pose.
    /// </summary>
    public sealed class HeldSwap
    {
        /// <summary>Seconds the old item takes to sink out of view.</summary>
        public const float HolsterSeconds = 0.12f;

        /// <summary>Seconds the new item takes to rise into the hand.</summary>
        public const float DrawSeconds = 0.2f;

        /// <summary>Seconds of the settle bump after the draw.</summary>
        public const float SettleSeconds = 0.14f;

        /// <summary>Where the swap stands.</summary>
        public enum Phase
        {
            /// <summary>The item is in the hand (the settle may still play).</summary>
            Idle,

            /// <summary>The old item is going down.</summary>
            Holstering,

            /// <summary>The new item is coming up.</summary>
            Drawing,
        }

        private float _settle = SettleSeconds; // seconds since the last draw ended; ≥ SettleSeconds = no bump

        /// <summary>Where the swap stands.</summary>
        public Phase Current { get; private set; }

        /// <summary>How far down the item is: 0 in the hand, 1 all the way out of view (linear in time).</summary>
        public float Lowered { get; private set; }

        /// <summary>True while a new item waits to be swapped in at the bottom.</summary>
        public bool HasPending { get; private set; }

        /// <summary>True while anything moves (a swap or its settle).</summary>
        public bool Active => Current != Phase.Idle || _settle < SettleSeconds;

        /// <summary>The eased depth for the pose: 0 in the hand, 1 out of view — smooth at both ends, and the same curve both
        /// ways, so turning round mid-way never jumps.</summary>
        public float Drop => SmoothStep(Lowered);

        /// <summary>The settle bump after the draw: rises to under 0.6 and fades back to 0 over <see cref="SettleSeconds"/>
        /// (the caller scales it into a little lift and tilt).</summary>
        public float Settle
        {
            get
            {
                if (_settle >= SettleSeconds)
                {
                    return 0f;
                }

                float s = _settle / SettleSeconds;
                return (float)Math.Sin(Math.PI * s) * (1f - s);
            }
        }

        /// <summary>
        /// A new item is to be held. With <paramref name="holster"/> the current one goes down first and the new one waits
        /// (<see cref="Step"/> says when to swap); without, the caller has swapped already — the old item is gone (a
        /// used-up stack) — and the new one starts at the bottom.
        /// </summary>
        public void Begin(bool holster)
        {
            _settle = SettleSeconds;
            if (!holster)
            {
                HasPending = false;
                Lowered = 1f;
                Current = Phase.Drawing;
                return;
            }

            HasPending = true;
            Current = Phase.Holstering; // from wherever it is: a draw in progress turns round
        }

        /// <summary>Advances by <paramref name="dt"/> seconds. True exactly once per swap: the old item is all the way down
        /// and the waiting one must be swapped in now.</summary>
        public bool Step(float dt)
        {
            if (dt <= 0f)
            {
                return false;
            }

            switch (Current)
            {
                case Phase.Holstering:
                    Lowered = Math.Min(1f, Lowered + (dt / HolsterSeconds));
                    if (Lowered >= 1f)
                    {
                        Current = Phase.Drawing;
                        bool swap = HasPending;
                        HasPending = false;
                        return swap;
                    }

                    return false;

                case Phase.Drawing:
                    Lowered = Math.Max(0f, Lowered - (dt / DrawSeconds));
                    if (Lowered <= 0f)
                    {
                        Current = Phase.Idle;
                        _settle = 0f;
                    }

                    return false;

                default:
                    if (_settle < SettleSeconds)
                    {
                        _settle = Math.Min(SettleSeconds, _settle + dt);
                    }

                    return false;
            }
        }

        /// <summary>Cuts the swap short — the item is used, or the hand is not on screen: back in the hand at once. True
        /// when an item was still waiting, which the caller must swap in now.</summary>
        public bool Finish()
        {
            bool swap = HasPending;
            HasPending = false;
            Lowered = 0f;
            Current = Phase.Idle;
            _settle = SettleSeconds;
            return swap;
        }

        private static float SmoothStep(float x)
        {
            x = Math.Max(0f, Math.Min(1f, x));
            return x * x * (3f - (2f * x));
        }
    }
}
