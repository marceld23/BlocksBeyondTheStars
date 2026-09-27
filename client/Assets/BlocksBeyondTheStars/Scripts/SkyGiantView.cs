// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Poses the sky giant (#2112) every frame: the server moves only the HEAD (the creature's position, interpolated by
    /// <see cref="CreatureView"/> like any other creature's), and this view lays the body out behind it along the head's
    /// own recent track — follow-the-leader, so the sailer always bends through the curve it just flew. The sails along
    /// the back sway on a slow beat, the fluke swings with the turns. Nothing here talks to the server: the track is the
    /// head's local history (the server measures its hits along the same idea, a trail of positions).
    /// </summary>
    public sealed class SkyGiantView : MonoBehaviour
    {
        private Transform[] _segments = System.Array.Empty<Transform>();
        private Transform[] _sails = System.Array.Empty<Transform>();
        private Quaternion[] _sailRest = System.Array.Empty<Quaternion>();
        private Transform _head;
        private float _length = 60f, _segLen = 3f;

        // The head's track: world-space points from the newest (index 0) backwards, and the running distance between them.
        private readonly List<Vector3> _track = new List<Vector3>(64);
        private readonly List<float> _gap = new List<float>(64);
        private const float SampleSpacing = 1.5f;

        public void Init(Transform[] segments, Transform[] sails, Transform head, float length)
        {
            _segments = segments ?? System.Array.Empty<Transform>();
            _sails = sails ?? System.Array.Empty<Transform>();
            _sailRest = new Quaternion[_sails.Length];
            for (int i = 0; i < _sails.Length; i++)
            {
                _sailRest[i] = _sails[i] != null ? _sails[i].localRotation : Quaternion.identity;
            }

            _head = head;
            _length = Mathf.Max(10f, length);
            _segLen = _segments.Length > 1 ? _length / (_segments.Length - 1) : _length;
        }

        /// <summary>Lays the body along the head's track for this frame.</summary>
        public void Apply(float now)
        {
            var headPos = transform.position;
            if (_track.Count == 0)
            {
                _track.Add(headPos);
                _gap.Add(0f);
            }
            else if ((headPos - _track[0]).sqrMagnitude >= SampleSpacing * SampleSpacing)
            {
                float d = (headPos - _track[0]).magnitude;
                if (d > 40f)
                {
                    _track.Clear(); // a teleport (or the longitude wrap): start the track over
                    _gap.Clear();
                    _track.Add(headPos);
                    _gap.Add(0f);
                }
                else
                {
                    _track.Insert(0, headPos);
                    _gap.Insert(0, 0f);
                    _gap[1] = d;
                    int keep = Mathf.CeilToInt(_length / SampleSpacing) + 4;
                    if (_track.Count > keep)
                    {
                        _track.RemoveRange(keep, _track.Count - keep);
                        _gap.RemoveRange(keep, _gap.Count - keep);
                    }
                }
            }

            var fwd = transform.forward;
            if (_head != null)
            {
                _head.position = headPos + fwd * (_segLen * 0.4f);
                _head.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            }

            for (int i = 0; i < _segments.Length; i++)
            {
                var seg = _segments[i];
                if (seg == null)
                {
                    continue;
                }

                float back = i * _segLen;
                var p = PointBack(back, headPos, fwd);
                var ahead = PointBack(Mathf.Max(0f, back - _segLen), headPos, fwd);
                var dir = ahead - p;
                if (dir.sqrMagnitude < 1e-4f)
                {
                    dir = fwd;
                }

                seg.position = p;
                seg.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                float swell = 1f + 0.04f * Mathf.Sin(now * 1.2f - i * 0.35f);
                seg.localScale = new Vector3(swell, swell, 1f);
            }

            for (int i = 0; i < _sails.Length; i++)
            {
                if (_sails[i] != null)
                {
                    _sails[i].localRotation = _sailRest[i] * Quaternion.Euler(0f, 0f, 9f * Mathf.Sin(now * 0.9f + i * 0.8f));
                }
            }
        }

        /// <summary>The point <paramref name="back"/> blocks behind the head along its track; past the track's end the body
        /// trails straight out behind (a fresh giant has no history yet).</summary>
        private Vector3 PointBack(float back, Vector3 headPos, Vector3 fwd)
        {
            float run = 0f;
            for (int i = 1; i < _track.Count; i++)
            {
                float g = _gap[i];
                if (run + g >= back)
                {
                    float t = g < 1e-4f ? 0f : (back - run) / g;
                    return Vector3.Lerp(_track[i - 1], _track[i], t);
                }

                run += g;
            }

            var last = _track.Count > 0 ? _track[_track.Count - 1] : headPos;
            var tailDir = _track.Count > 1 ? (_track[_track.Count - 2] - last).normalized : fwd;
            return last - tailDir * (back - run);
        }
    }
}
