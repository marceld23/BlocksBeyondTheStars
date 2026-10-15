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
    /// server stays authoritative over who actually breathes. The dome marks the unconditional core zone, the
    /// start zone where a player breathes before building a room; it does not outline sealed rooms. Since every
    /// enclosed room of a base breathes (#2651), the dome fades out once the base has a sealed room with air and
    /// fades back in when it has none (#2659, <see cref="BaseDomeFade"/>) — otherwise it reads as a boundary that
    /// does not exist. Drawn only on worlds whose own air is NOT breathable — under a breathable sky the base adds
    /// nothing, so no bubble. Reuses the energy-door field material (always-included Cloud shader) so nothing can
    /// strip to pink.
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

        private static readonly Color DomeTint = new Color(0.35f, 0.80f, 1f, 1f); // the energy-door blue
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>One base's dome: its own material instance, so each fades on its own.</summary>
        private sealed class Dome
        {
            public Transform Transform;
            public Renderer Renderer;
            public Material Material;
            public float Alpha;
            public float Target;
        }

        private readonly Dictionary<int, Dome> _domes = new Dictionary<int, Dome>();
        private Material _template;
        private float _timer;

        private void Update()
        {
            FadeDomes(Time.deltaTime);

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
                    if (!_domes.TryGetValue(b.Id, out var dome) || dome.Transform == null)
                    {
                        float start = BaseDomeFade.TargetAlpha(b.SealedRooms, BaseAlpha); // a new dome starts where it belongs
                        _domes[b.Id] = dome = BuildDome(start);
                    }

                    dome.Target = BaseDomeFade.TargetAlpha(b.SealedRooms, BaseAlpha);

                    // NetBase carries the core cell's centre; lift the dome onto it so it sits on the core. #2613: at the
                    // copy nearest the player (ScenePos) — the canonical spot drew the dome a whole circumference away
                    // once the player stood across the seam from the core (the home pad lies at X = 0).
                    dome.Transform.position = Game.ScenePos(b.X, b.Y + 0.5f, b.Z);
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
                    var dome = _domes[id];
                    if (dome.Transform != null)
                    {
                        Destroy(dome.Transform.gameObject);
                    }

                    if (dome.Material != null)
                    {
                        Destroy(dome.Material);
                    }

                    _domes.Remove(id);
                }
            }
        }

        /// <summary>Moves every dome's opacity towards its target (#2659) and hides the renderer once it is gone.</summary>
        private void FadeDomes(float deltaSeconds)
        {
            foreach (var dome in _domes.Values)
            {
                if (dome.Transform == null || Mathf.Approximately(dome.Alpha, dome.Target))
                {
                    continue;
                }

                dome.Alpha = BaseDomeFade.Step(dome.Alpha, dome.Target, BaseAlpha, deltaSeconds);
                ApplyAlpha(dome);
            }
        }

        private static void ApplyAlpha(Dome dome)
        {
            var tint = DomeTint;
            tint.a = dome.Alpha;
            dome.Material.SetColor(ColorId, ShaderColor.Srgb(tint));
            dome.Renderer.enabled = dome.Alpha > 0.001f;
        }

        /// <summary>One dome: an inside-visible-too sphere in the energy-field look (no collider — it is
        /// pure light; the player walks and builds straight through it).</summary>
        private Dome BuildDome(float alpha)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "BaseShield";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            if (_template == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
                _template = new Material(shader);
                _template.renderQueue = 2995; // transparent, just under the door fields
            }

            var dome = new Dome
            {
                Transform = go.transform,
                Renderer = go.GetComponent<Renderer>(),
                Material = new Material(_template),
                Alpha = alpha,
                Target = alpha,
            };
            dome.Renderer.sharedMaterial = dome.Material;
            ApplyAlpha(dome);
            go.transform.localScale = Vector3.one * (DomeRadius * 2f);
            go.transform.SetParent(transform, false);
            return dome;
        }

        private void OnDestroy()
        {
            foreach (var dome in _domes.Values)
            {
                if (dome.Material != null)
                {
                    Destroy(dome.Material);
                }
            }

            if (_template != null)
            {
                Destroy(_template);
            }
        }
    }
}
