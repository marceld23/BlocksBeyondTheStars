// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Makes a flora SPAWN/REGROW source recognizable. When a harvested plant is scheduled to regrow the
    /// server sends <see cref="FloraRegrowStarted"/>; this spawns a small green sprout at that cell and grows
    /// it in over the regrow delay, so the otherwise-bare cell visibly reads as "something is growing here"
    /// rather than the plant popping back from nothing. When the real plant returns (a <see cref="BlockChanged"/>
    /// at the cell) or the timer elapses, the sprout is removed. Purely cosmetic + render-only — the server
    /// stays authoritative over the actual block; a missed message just means no sprout, never a gameplay change.
    ///
    /// Code-built on the always-included Unlit shader (no assets, no new shader), mirroring <see cref="MiningFx"/>.
    /// This also subsumes the original "tint the host block" idea, which is impossible: the host's top face is
    /// culled under the plant, so it never reaches the mesh — a cell marker is the only workable cue (and it
    /// also covers the bare regrow window, which a host tint never could).
    /// </summary>
    public sealed class FloraGrowthFx : MonoBehaviour
    {
        public GameBootstrap Game;

        // The minimum / full sprout size (local units) and the start fraction so it is visible the instant it
        // appears rather than growing up from an invisible speck.
        private const float Width = 0.42f;
        private const float Height = 0.62f;
        private const float StartFraction = 0.12f;

        private readonly Dictionary<Vector3Int, Sprout> _sprouts = new();
        private bool _subscribed;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.FloraRegrowStartedReceived += OnRegrowStarted;
                Game.Network.BlockChanged += OnBlockChanged;
                _subscribed = true;
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.FloraRegrowStartedReceived -= OnRegrowStarted;
                Game.Network.BlockChanged -= OnBlockChanged;
            }
        }

        private void OnRegrowStarted(FloraRegrowStarted m)
        {
            var cell = new Vector3Int(m.X, m.Y, m.Z);
            if (_sprouts.TryGetValue(cell, out var existing) && existing != null)
            {
                existing.Restart(Mathf.Max(0.1f, m.Seconds));
                return;
            }

            var sprout = Sprout.Spawn(cell, Mathf.Max(0.1f, m.Seconds), Width, Height, StartFraction);
            sprout.OnFinished = () => _sprouts.Remove(cell);
            _sprouts[cell] = sprout;
        }

        /// <summary>The real plant (or any block) arrived on a tracked cell — the sprout has served its purpose,
        /// drop it so it doesn't overlap the freshly meshed flora.</summary>
        private void OnBlockChanged(BlockChanged m)
        {
            var cell = new Vector3Int(m.X, m.Y, m.Z);
            if (m.Block != 0 && _sprouts.TryGetValue(cell, out var sprout))
            {
                _sprouts.Remove(cell);
                if (sprout != null)
                {
                    Destroy(sprout.gameObject);
                }
            }
        }

        private static Material _mat;

        private static Material SproutMaterial()
        {
            if (_mat != null)
            {
                return _mat;
            }

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            _mat = new Material(shader) { color = ShaderColor.Srgb(new Color(0.36f, 0.62f, 0.26f)) };
            return _mat;
        }

        /// <summary>One growing sprout: two crossed quads on a base-pivoted root that scales up from a seedling
        /// to full height over the regrow delay, then self-removes (the real plant takes over).</summary>
        private sealed class Sprout : MonoBehaviour
        {
            public System.Action OnFinished;

            private float _seconds;
            private float _t;
            private float _width;
            private float _height;
            private float _startFraction;

            public static Sprout Spawn(Vector3Int cell, float seconds, float width, float height, float startFraction)
            {
                // The cell is the air cell above the host, so the sprout's base sits on the ground at cell.Y.
                var root = new GameObject("FloraSprout");
                root.transform.position = new Vector3(cell.x + 0.5f, cell.y, cell.z + 0.5f);

                var mat = SproutMaterial();
                for (int i = 0; i < 2; i++)
                {
                    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    var col = quad.GetComponent<Collider>();
                    if (col != null)
                    {
                        Destroy(col);
                    }

                    quad.transform.SetParent(root.transform, false);
                    quad.transform.localPosition = new Vector3(0f, 0.5f, 0f); // base at the root origin (the ground)
                    quad.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                    quad.GetComponent<Renderer>().sharedMaterial = mat;
                    quad.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                var sprout = root.AddComponent<Sprout>();
                sprout._width = width;
                sprout._height = height;
                sprout._startFraction = startFraction;
                sprout.Restart(seconds);
                return sprout;
            }

            public void Restart(float seconds)
            {
                _seconds = seconds;
                _t = 0f;
                Apply();
            }

            private void Update()
            {
                _t += Time.deltaTime;
                Apply();
                if (_t >= _seconds)
                {
                    OnFinished?.Invoke();
                    Destroy(gameObject);
                }
            }

            private void Apply()
            {
                float grow = Mathf.Lerp(_startFraction, 1f, _seconds > 0f ? Mathf.Clamp01(_t / _seconds) : 1f);
                transform.localScale = new Vector3(_width, _height * grow, _width);
            }
        }
    }
}
