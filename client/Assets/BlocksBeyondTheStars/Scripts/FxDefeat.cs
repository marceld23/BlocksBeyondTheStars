// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Hit reactions and kid-friendly defeat (#2154). The age rating promises that "defeated creatures and robots simply
    /// break apart" and that "bandits are chased away, never killed" (docs/user/PARENTS.md) — before this a defeated
    /// creature vanished on the despawn path and a bandit just disappeared. Now:
    /// <list type="bullet">
    /// <item><see cref="HitFlash"/> — a short white flash on every renderer of the hit model (a <c>_HitFlash</c> property
    /// block on LitColor / VertexColorOpaque, a <c>_Color</c> override on plain Unlit parts) plus a little recoil squash.</item>
    /// <item><see cref="BreakApart"/> — bodies are jointed cubes (ART_BIBLE §2.8), so the parts come loose, tumble,
    /// shrink and pop into sparkles with a soft puff. Robots throw sparks and little bolts too.</item>
    /// <item><see cref="BeamOut"/> — the bandit shrinks into a rising teleport column and is gone.</item>
    /// </list>
    /// </summary>
    public static class FxDefeat
    {
        private static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private const int MaxParts = 36;

        /// <summary>Flashes a model white for a moment and squashes its root (the "it was hit" read).</summary>
        public static void HitFlash(GameObject root, float strength = 1f)
        {
            if (root == null)
            {
                return;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0)
            {
                return;
            }

            var baseScale = root.transform.localScale;
            FxKit.Animate(0.16f, t =>
            {
                float f = (1f - t) * strength;
                foreach (var r in renderers)
                {
                    if (r == null || r is ParticleSystemRenderer || r is LineRenderer)
                    {
                        continue;
                    }

                    var m = r.sharedMaterial;
                    if (m == null)
                    {
                        continue;
                    }

                    var b = FxKit.Block;
                    b.Clear();
                    if (m.shader != null && m.shader.name.StartsWith("BlocksBeyondTheStars/", System.StringComparison.Ordinal))
                    {
                        b.SetFloat(HitFlashId, f * 0.85f);
                    }
                    else if (m.HasProperty(ColorId))
                    {
                        b.SetColor(ColorId, Color.Lerp(m.color, Color.white, f * 0.85f));
                    }

                    r.SetPropertyBlock(b);
                }

                // A quick squash: the body flinches a few percent and springs back.
                float s = 1f - 0.07f * Mathf.Sin(t * Mathf.PI) * strength;
                root.transform.localScale = new Vector3(baseScale.x / Mathf.Sqrt(s), baseScale.y * s, baseScale.z / Mathf.Sqrt(s));
            }, () =>
            {
                foreach (var r in renderers)
                {
                    if (r != null)
                    {
                        r.SetPropertyBlock(null);
                    }
                }

                if (root != null)
                {
                    root.transform.localScale = baseScale;
                }
            }, root);
        }

        /// <summary>Breaks a defeated model apart: up to <see cref="MaxParts"/> of its cube parts are detached and tumble
        /// away, shrinking into sparkles; the caller destroys the remaining root right after this returns.
        /// <paramref name="sparkle"/> tints the sparkles; <paramref name="robot"/> adds sparks and bolts.</summary>
        public static void BreakApart(GameObject root, Color sparkle, bool robot)
        {
            if (root == null)
            {
                return;
            }

            var center = Center(root);
            var parts = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (r is MeshRenderer && r.enabled && r.GetComponent<MeshFilter>() != null && r.bounds.size.sqrMagnitude > 1e-4f)
                {
                    parts.Add(r);
                }
            }

            parts.Sort((a, b) => b.bounds.size.sqrMagnitude.CompareTo(a.bounds.size.sqrMagnitude));
            int n = Mathf.Min(parts.Count, Mathf.Max(6, Mathf.RoundToInt(MaxParts * Mathf.Max(0.4f, FxKit.Density))));
            var host = FxKit.Ensure().transform;

            // Detach every chosen part FIRST (a limb segment may be the child of another chosen part), then drop what
            // is still hanging off them — eyes, glows, small bits: the big parts carry the read.
            for (int i = 0; i < n; i++)
            {
                parts[i].transform.SetParent(host, true);
            }

            for (int i = 0; i < n; i++)
            {
                var part = parts[i].gameObject;
                foreach (var c in part.GetComponents<Collider>())
                {
                    Object.Destroy(c);
                }

                // Strip anything still driving the part (animators live on the root; behaviours on a part would keep
                // running on a detached cube).
                foreach (var mb in part.GetComponents<MonoBehaviour>())
                {
                    Object.Destroy(mb);
                }

                for (int k = part.transform.childCount - 1; k >= 0; k--)
                {
                    Object.Destroy(part.transform.GetChild(k).gameObject);
                }

                var p = part.transform.position;
                var away = (p - center).sqrMagnitude > 1e-4f ? (p - center).normalized : Random.onUnitSphere;
                var vel = away * Random.Range(1.5f, 3.2f) + Vector3.up * Random.Range(2f, 4f);
                var spin = Random.insideUnitSphere * 540f;
                var scale0 = part.transform.localScale;
                float life = Random.Range(0.55f, 0.85f);
                var tr = part.transform;
                FxKit.Animate(life, t =>
                {
                    float dt = Time.deltaTime;
                    vel += Vector3.down * 9f * dt;
                    tr.position += vel * dt;
                    tr.Rotate(spin * dt, Space.World);
                    float s = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                    tr.localScale = scale0 * Mathf.Max(0f, s);
                }, () =>
                {
                    if (tr == null)
                    {
                        return;
                    }

                    var at = tr.position;
                    FxKit.Burst(FxKit.Kind.Motes, at, 4, Vector3.up, 90f, 0.6f, 1.8f, 0.04f, 0.08f, 0.35f, 0.7f, sparkle, Color.white);
                    Object.Destroy(tr.gameObject);
                }, part);
            }

            // The soft puff and the sparkle cloud.
            float size = Mathf.Clamp(Bounds(root).size.magnitude, 0.6f, 6f);
            FxKit.Burst(FxKit.Kind.Smoke, center, robot ? 8 : 6, Vector3.up, 90f, 0.4f, 1.4f, 0.25f * size, 0.45f * size, 0.6f, 1.1f,
                robot ? new Color(0.5f, 0.5f, 0.52f) : new Color(0.95f, 0.93f, 0.98f));
            FxKit.Burst(FxKit.Kind.Motes, center, 16, Vector3.up, 80f, 1f, 3f, 0.04f, 0.09f, 0.6f, 1.1f, sparkle, Color.white);
            FxKit.Ring(center, Vector3.up, sparkle, 0.2f, size * 1.2f, 0.45f, thickness: 0.15f, intensity: 1.6f);
            if (robot)
            {
                FxKit.Burst(FxKit.Kind.Sparks, center, 16, Vector3.up, 70f, 3f, 7f, 0.04f, 0.07f, 0.3f, 0.6f, new Color(1f, 0.75f, 0.35f), Color.white);
                FxKit.Burst(FxKit.Kind.Debris, center, 8, Vector3.up, 70f, 2f, 4.5f, 0.05f, 0.09f, 0.7f, 1.1f, new Color(0.3f, 0.32f, 0.36f));
                FxLights.Flash(center, new Color(1f, 0.7f, 0.35f), 1.6f, 6f, 0.3f);
            }
        }

        /// <summary>Beams a bandit away: the model squeezes into a rising teleport column (blue-white) and vanishes.
        /// The caller destroys the root after <paramref name="done"/>.</summary>
        public static void BeamOut(GameObject root, Color color, System.Action done)
        {
            if (root == null)
            {
                done?.Invoke();
                return;
            }

            var b = Bounds(root);
            var basePos = new Vector3(b.center.x, b.min.y, b.center.z);
            FxGadgets.Teleport(basePos, Mathf.Max(1.6f, b.size.y), color, Color.white);
            ClientAudio.Instance?.At("beam_teleport", b.center);
            var tr = root.transform;
            var scale0 = tr.localScale;
            FxKit.Animate(0.55f, t =>
            {
                float squeeze = 1f - t;
                tr.localScale = new Vector3(scale0.x * squeeze * squeeze, scale0.y * (1f + t * 0.6f), scale0.z * squeeze * squeeze);
            }, done, root);
        }

        private static Vector3 Center(GameObject root) => Bounds(root).center;

        private static Bounds Bounds(GameObject root)
        {
            var rs = root.GetComponentsInChildren<Renderer>(false);
            if (rs.Length == 0)
            {
                return new Bounds(root.transform.position + Vector3.up * 0.5f, Vector3.one);
            }

            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++)
            {
                b.Encapsulate(rs[i].bounds);
            }

            return b;
        }
    }
}
