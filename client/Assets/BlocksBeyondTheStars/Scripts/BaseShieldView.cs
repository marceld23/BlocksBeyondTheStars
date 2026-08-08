// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The base life-support shield (#795): a soft translucent dome drawn over every founded base's core so
    /// the air field (#782) reads at a glance. Render-only sugar in the <see cref="DoorView"/> pattern — the
    /// server stays authoritative over who actually breathes (including the sealed rooms of #794, which the
    /// dome deliberately does NOT try to outline; it marks the unconditional core zone). Drawn only on
    /// worlds whose own air is NOT breathable — under a breathable sky the base adds nothing, so no bubble.
    /// Reuses the energy-door field material (always-included Cloud shader) so nothing can strip to pink.
    /// </summary>
    public sealed class BaseShieldView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Drawn dome radius. Cosmetic: the RULE stays the Chebyshev cube of
        /// <see cref="BlocksBeyondTheStars.Shared.World.WorldConstants.BaseZoneRadius"/> (8) — the sphere
        /// just reads better than a floating box; r 9.5 circumscribes most of the cube's volume.</summary>
        private const float DomeRadius = 9.5f;

        private const float RefreshInterval = 0.5f;
        private const float BaseAlpha = 0.10f;    // faint — a shimmer, not a wall (the player builds in here)

        private readonly Dictionary<int, Transform> _domes = new Dictionary<int, Transform>();
        private Material _mat;
        private float _timer;

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
            {
                return;
            }

            _timer = RefreshInterval;
            bool visible = Game != null && Game.Environment != null && !Game.Environment.Breathable
                && Game.Bases != null;
            var wanted = new HashSet<int>();
            if (visible)
            {
                foreach (var b in Game.Bases)
                {
                    wanted.Add(b.Id);
                    if (!_domes.TryGetValue(b.Id, out var dome) || dome == null)
                    {
                        _domes[b.Id] = dome = BuildDome();
                    }

                    // NetBase carries the core cell's centre; lift the dome onto it so it sits on the core.
                    dome.position = new Vector3(b.X, b.Y + 0.5f, b.Z);
                }
            }

            // Drop domes whose base is gone (core mined / world switched) or that a breathable sky hides.
            List<int> stale = null;
            foreach (var kv in _domes)
            {
                if (!wanted.Contains(kv.Key))
                {
                    (stale ??= new List<int>()).Add(kv.Key);
                }
            }

            if (stale != null)
            {
                foreach (int id in stale)
                {
                    if (_domes[id] != null)
                    {
                        Destroy(_domes[id].gameObject);
                    }

                    _domes.Remove(id);
                }
            }
        }

        /// <summary>One dome: an inside-visible-too sphere in the energy-field look (no collider — it is
        /// pure light; the player walks and builds straight through it).</summary>
        private Transform BuildDome()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "BaseShield";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            if (_mat == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
                _mat = new Material(shader);
                _mat.SetColor(Shader.PropertyToID("_Color"),
                    ShaderColor.Srgb(new Color(0.35f, 0.80f, 1f, BaseAlpha))); // the energy-door blue
                _mat.renderQueue = 2995; // transparent, just under the door fields
            }

            go.GetComponent<Renderer>().sharedMaterial = _mat;
            go.transform.localScale = Vector3.one * (DomeRadius * 2f);
            go.transform.SetParent(transform, false);
            return go.transform;
        }
    }
}
