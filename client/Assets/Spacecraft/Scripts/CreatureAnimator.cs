using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Procedural creature animation: swings the legs while the body moves, flaps the wings (a gentle
    /// idle flap that strengthens with speed), and sways the tail. Self-driven from the root's world
    /// movement — the same approach as <see cref="PlayerAvatar"/> — so it needs no per-frame data from
    /// the server. Pivots are supplied by <see cref="CreatureBuilder"/>.
    /// </summary>
    public sealed class CreatureAnimator : MonoBehaviour
    {
        private Transform[] _legs;
        private Transform[] _wings;
        private Transform _tail;

        private float _phase;     // per-creature offset so they don't move in lockstep
        private float _walk;      // leg-swing phase
        private Vector3 _lastPos;
        private bool _hasPrev;

        public void Init(Transform[] legs, Transform[] wings, Transform tail)
        {
            _legs = legs;
            _wings = wings;
            _tail = tail;
            _phase = (GetInstanceID() & 0x3ff) * 0.1f; // stable pseudo-random offset
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            var pos = transform.position;
            float speed = 0f;
            if (_hasPrev)
            {
                var d = pos - _lastPos;
                d.y = 0f;
                speed = d.magnitude / dt;
            }

            _lastPos = pos;
            _hasPrev = true;

            float moving = Mathf.Clamp01(speed / 3f);
            _walk += dt * (3f + speed * 2.2f);
            float t = Time.time + _phase;

            // Legs: alternate front/back, amplitude scales with speed (plus a tiny idle shuffle).
            if (_legs != null)
            {
                float amp = Mathf.Lerp(2f, 32f, moving);
                for (int i = 0; i < _legs.Length; i++)
                {
                    if (_legs[i] == null)
                    {
                        continue;
                    }

                    float dir = ((i & 1) == 0) ? 1f : -1f;     // left/right out of phase
                    float row = ((i >> 1) & 1) == 0 ? 0f : Mathf.PI; // alternate leg pairs
                    _legs[i].localRotation = Quaternion.Euler(Mathf.Sin(_walk + row) * amp * dir, 0f, 0f);
                }
            }

            // Wings: flap around Z — a calm idle beat that picks up when flying.
            if (_wings != null)
            {
                float flap = Mathf.Sin(t * (6f + moving * 6f)) * Mathf.Lerp(14f, 42f, moving);
                for (int i = 0; i < _wings.Length; i++)
                {
                    if (_wings[i] != null)
                    {
                        float side = ((i & 1) == 0) ? 1f : -1f; // mirror left/right
                        _wings[i].localRotation = Quaternion.Euler(0f, 0f, flap * side);
                    }
                }
            }

            // Tail: a slow side-to-side sway, a touch livelier on the move.
            if (_tail != null)
            {
                float sway = Mathf.Sin(t * 1.8f) * Mathf.Lerp(8f, 18f, moving);
                _tail.localRotation = Quaternion.Euler(0f, sway, 0f);
            }
        }
    }
}
