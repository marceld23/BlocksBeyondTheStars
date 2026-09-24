// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Poses a sandworm (#2001) every frame. The server never streams the body: it sends the move (breach or rear), its
    /// anchor, direction, height and the seconds already run, and this view builds the same <see cref="SandwormPath"/> the
    /// server hits with and places every ring segment on the head's own track — follow-the-leader, so the worm always
    /// slides through the hole its head made. Everything under the surface is simply drawn inside the terrain, which hides
    /// it; where the spine pierces the sand, fountains of sand and a dust ring go up. While the worm is under the sand it
    /// draws nothing but the moving ripple above its head (the approach). No block ever changes.
    /// </summary>
    public sealed class SandwormView : MonoBehaviour
    {
        private Transform[] _segments = System.Array.Empty<Transform>();
        private Transform _head;
        private Transform[] _petals = System.Array.Empty<Transform>();
        private Quaternion[] _petalRest = System.Array.Empty<Quaternion>();
        private Renderer[] _renderers = System.Array.Empty<Renderer>();
        private Collider[] _colliders = System.Array.Empty<Collider>();
        private float _girth = 8f, _length = 100f;

        private SandwormPath _path;
        private string _pathKey = string.Empty;
        private Vector3f[] _centers = System.Array.Empty<Vector3f>();
        private bool _visible = true;
        private float _nextFountain;
        private float _nextRipple;

        /// <summary>True while any of the body is drawn above the sand.</summary>
        public bool Surfaced { get; private set; }

        /// <summary>The head's scene position right now (for the health bar and the voice).</summary>
        public Vector3 HeadScenePos => _head != null ? _head.position : transform.position;

        public void Init(Transform[] segments, Transform head, Transform[] petals, float girth, float length, Renderer[] renderers)
        {
            _segments = segments ?? System.Array.Empty<Transform>();
            _head = head;
            _petals = petals ?? System.Array.Empty<Transform>();
            _petalRest = new Quaternion[_petals.Length];
            for (int i = 0; i < _petals.Length; i++)
            {
                _petalRest[i] = _petals[i] != null ? _petals[i].localRotation : Quaternion.identity;
            }

            _girth = girth;
            _length = length;
            _renderers = renderers ?? System.Array.Empty<Renderer>();
            _colliders = GetComponentsInChildren<Collider>(true);
            _centers = new Vector3f[_segments.Length];
            SetVisible(false);
        }

        /// <summary>Poses the worm for this frame. <paramref name="phaseTime"/> is the seconds into the current move on the
        /// local clock; <paramref name="dust"/> throws the sand effects (null = none).</summary>
        public void Apply(NetCreature c, float phaseTime, GameBootstrap game, WeaponFx dust, float now)
        {
            bool moving = (c.Phase == "breach" || c.Phase == "rear") && c.PhaseDur > 0f;
            if (!moving)
            {
                _path = null;
                _pathKey = string.Empty;
                SetVisible(false);
                Surfaced = false;
                if (c.Phase == "approach" && dust != null && now >= _nextRipple)
                {
                    // The sand heaves over the head as it comes: a line of puffs moving across the sea.
                    _nextRipple = now + 0.22f;
                    var at = game.ScenePos(c.X, c.Y, c.Z);
                    if (SurfaceAbove(game, c.X, c.Y, c.Z, out float y))
                    {
                        dust.Dust(new Vector3(at.x, y + 0.2f, at.z), 5);
                    }
                }

                return;
            }

            string key = c.Phase + "|" + c.EvX.ToString("0.0") + "|" + c.EvZ.ToString("0.0") + "|" + c.EvDirX.ToString("0.00") + "|" + c.EvDirZ.ToString("0.00");
            if (_path == null || key != _pathKey)
            {
                _pathKey = key;
                _path = SandwormPath.Build(c.Phase == "rear" ? WormMove.Rear : WormMove.Breach,
                    new Vector3f(c.EvX, c.EvY, c.EvZ), c.EvDirX, c.EvDirZ, c.EvPeak, c.WormLength > 0f ? c.WormLength : _length,
                    c.WormGirth > 0f ? c.WormGirth : _girth, c.EvStrike);
            }

            float t = Mathf.Clamp(phaseTime, 0f, _path.Duration);
            _path.SegmentCenters(t, _centers);
            SetVisible(true);

            // Segments sit on the head's track, each facing along it (toward the head), with a slow peristaltic swell.
            var fallbackUp = new Vector3(c.EvDirX, 0f, c.EvDirZ);
            bool any = false;
            for (int i = 0; i < _segments.Length; i++)
            {
                var seg = _segments[i];
                if (seg == null)
                {
                    continue;
                }

                var p = _centers[i];
                var ahead = _centers[Mathf.Max(0, i - 1)];
                var behind = _centers[Mathf.Min(_centers.Length - 1, i + 1)];
                var fwd = new Vector3(ahead.X - behind.X, ahead.Y - behind.Y, ahead.Z - behind.Z);
                if (fwd.sqrMagnitude < 1e-6f)
                {
                    fwd = fallbackUp;
                }

                seg.position = game.ScenePos(p.X, p.Y, p.Z);
                var up = Mathf.Abs(Vector3.Dot(fwd.normalized, Vector3.up)) > 0.95f ? -fallbackUp : Vector3.up;
                seg.rotation = Quaternion.LookRotation(fwd, up);
                float swell = 1f + 0.06f * Mathf.Sin(now * 5f - i * 0.45f);
                seg.localScale = new Vector3(swell, swell, 1f);
                any |= p.Y + _girth * 0.5f > c.EvY;
            }

            Surfaced = any;

            // The head leads the first segment along the track.
            if (_head != null && _centers.Length > 1)
            {
                var h0 = _centers[0];
                var h1 = _centers[1];
                var dir = new Vector3(h0.X - h1.X, h0.Y - h1.Y, h0.Z - h1.Z);
                if (dir.sqrMagnitude < 1e-6f)
                {
                    dir = fallbackUp;
                }

                dir.Normalize();
                _head.position = game.ScenePos(h0.X, h0.Y, h0.Z) + dir * (_girth * 0.35f);
                var up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? -fallbackUp : Vector3.up;
                _head.rotation = Quaternion.LookRotation(dir, up);
            }

            PosePetals(c, t, now);
            if (dust != null && now >= _nextFountain)
            {
                _nextFountain = now + 0.12f;
                SandFountains(c, game, dust);
            }
        }

        /// <summary>The mandibles: shut while it runs under the sand, parted as it breaches, wide open for the strike.</summary>
        private void PosePetals(NetCreature c, float t, float now)
        {
            float open = 18f + 8f * Mathf.Sin(now * 3f);
            if (c.Phase == "rear" && _path != null && _path.StrikeTime > 0f)
            {
                float toStrike = _path.StrikeTime - t;
                open = toStrike > 3.2f ? 25f : toStrike > -0.4f ? Mathf.Lerp(70f, 30f, Mathf.Clamp01(toStrike / 3.2f)) : 15f;
            }

            for (int i = 0; i < _petals.Length; i++)
            {
                if (_petals[i] != null)
                {
                    _petals[i].localRotation = _petalRest[i] * Quaternion.Euler(-open, 0f, 0f);
                }
            }
        }

        /// <summary>Where the spine crosses the surface, sand sprays up: one fountain per crossing, several times a second.</summary>
        private void SandFountains(NetCreature c, GameBootstrap game, WeaponFx dust)
        {
            float surface = c.EvY;
            for (int i = 1; i < _centers.Length; i++)
            {
                var a = _centers[i - 1];
                var b = _centers[i];
                if ((a.Y - surface) * (b.Y - surface) > 0f)
                {
                    continue; // both above or both below
                }

                float f = Mathf.Abs(a.Y - b.Y) < 1e-4f ? 0.5f : (surface - a.Y) / (b.Y - a.Y);
                var at = game.ScenePos(a.X + (b.X - a.X) * f, surface, a.Z + (b.Z - a.Z) * f);
                int count = Mathf.Clamp(Mathf.RoundToInt(_girth * 1.8f), 8, 22);
                for (int k = 0; k < 3; k++)
                {
                    var ring = Random.insideUnitCircle * (_girth * 0.6f);
                    dust.Dust(at + new Vector3(ring.x, 0.2f, ring.y), count / 3);
                }
            }
        }

        private void SetVisible(bool on)
        {
            if (_visible == on)
            {
                return;
            }

            _visible = on;
            foreach (var r in _renderers)
            {
                if (r != null)
                {
                    r.enabled = on;
                }
            }

            foreach (var col in _colliders)
            {
                if (col != null)
                {
                    col.enabled = on;
                }
            }
        }

        /// <summary>The first air cell above a buried point (the top of the sand over the worm), probing up to 40 blocks.
        /// World coordinates in; the scene only shifts X/Z for the longitude wrap, so the Y is the same in both.</summary>
        private static bool SurfaceAbove(GameBootstrap game, float worldX, float worldY, float worldZ, out float y)
        {
            y = worldY;
            if (game?.World == null)
            {
                return false;
            }

            int x = Mathf.FloorToInt(worldX), z = Mathf.FloorToInt(worldZ);
            int start = Mathf.FloorToInt(worldY);
            for (int dy = 0; dy < 40; dy++)
            {
                if (game.World.GetBlock(x, start + dy, z).IsAir)
                {
                    y = start + dy;
                    return true;
                }
            }

            return false;
        }
    }
}
