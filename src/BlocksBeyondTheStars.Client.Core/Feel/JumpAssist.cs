// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Feel
{
    /// <summary>
    /// The two small forgivenesses of a jump on foot. <b>Coyote time</b>: for <see cref="CoyoteSeconds"/> after the feet
    /// walked off an edge, a press of Jump still jumps — the eye sees the edge a few frames later than the capsule leaves
    /// it. <b>Jump buffer</b>: a tap of Jump up to <see cref="BufferSeconds"/> before the landing still jumps on the
    /// landing (a HELD key always did; only the released tap was lost). Both apply only to the free body — walking and
    /// falling under gravity; water, ladders, walls, the rope, creative flight and zero-g have their own vertical rules
    /// and clear the state. Client-only like all on-foot movement.
    /// </summary>
    public sealed class JumpAssist
    {
        /// <summary>How long after walking off an edge a jump is still granted.</summary>
        public const float CoyoteSeconds = 0.12f;

        /// <summary>How long before a landing a tap of Jump is remembered.</summary>
        public const float BufferSeconds = 0.12f;

        private const float Never = 1000f;

        private float _sinceGround = Never;
        private float _sincePress = Never;
        private bool _leftByJump;

        /// <summary>
        /// One frame, before the vertical rules run. <paramref name="free"/>: the body walks or falls under gravity (none
        /// of the special states owns it); <paramref name="grounded"/>: the feet stand; <paramref name="pressed"/>: Jump
        /// went down this frame.
        /// </summary>
        public void Tick(float dt, bool free, bool grounded, bool pressed)
        {
            if (!free)
            {
                Clear();
                return;
            }

            if (grounded)
            {
                _sinceGround = 0f;
                _leftByJump = false;
            }
            else if (_sinceGround < Never)
            {
                _sinceGround += dt > 0f ? dt : 0f;
            }

            if (pressed)
            {
                _sincePress = 0f;
            }
            else if (_sincePress < Never)
            {
                _sincePress += dt > 0f ? dt : 0f;
            }
        }

        /// <summary>In the air, just off an edge the feet WALKED off (never after a jump): a press may still jump.</summary>
        public bool CoyoteReady => !_leftByJump && _sinceGround > 0f && _sinceGround <= CoyoteSeconds;

        /// <summary>On the ground: a tap of Jump from just before the landing is still waiting.</summary>
        public bool BufferedPress => _sincePress <= BufferSeconds;

        /// <summary>A jump started — the edge grace and the remembered tap are spent.</summary>
        public void Jumped()
        {
            _leftByJump = true;
            _sincePress = Never;
        }

        /// <summary>Forgets everything (a special movement state took the body, a teleport, a respawn).</summary>
        public void Clear()
        {
            _sinceGround = Never;
            _sincePress = Never;
            _leftByJump = false;
        }
    }
}
