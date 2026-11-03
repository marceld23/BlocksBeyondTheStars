// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The pieces of a ship setting down or lifting off on a surface (#3140/#3141), shared by the own ship's transition
    /// cinematics and the other players' landings (<see cref="ShipTransitView"/>): a render-only stand-in of a parked ship,
    /// the engine fire out of its real engines, dust in the colour of the ground it stands on, the touchdown thump and the
    /// steam, the vapour cone of the climb. All through the pooled <see cref="FxKit"/> emitters, density-scaled.
    /// </summary>
    public static class ShipCinematicFx
    {
        /// <summary>A render-only copy of a meshed ship (the same meshes and materials, no colliders) at the same pose —
        /// it can fly while the real hull, with its colliders, stays where the server put it.</summary>
        public static GameObject CloneRenderers(GameObject source, string name)
        {
            var clone = new GameObject(name);
            clone.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            foreach (var mf in source.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mf.sharedMesh == null || mr == null)
                {
                    continue;
                }

                var go = new GameObject(mf.gameObject.name);
                go.transform.SetParent(clone.transform, false);
                go.transform.localPosition = source.transform.InverseTransformPoint(mf.transform.position);
                go.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * mf.transform.rotation;
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials;
                r.shadowCastingMode = mr.shadowCastingMode;
            }

            return clone;
        }

        /// <summary>Shows or hides every renderer under <paramref name="root"/>.</summary>
        public static void SetRenderersEnabled(GameObject root, bool on)
        {
            if (root == null)
            {
                return;
            }

            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                r.enabled = on;
            }
        }

        /// <summary>The world box around every renderer under <paramref name="root"/> (empty when it has none).</summary>
        public static Bounds RenderBounds(GameObject root)
        {
            var box = new Bounds(root.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!any)
                {
                    box = r.bounds;
                    any = true;
                }
                else
                {
                    box.Encapsulate(r.bounds);
                }
            }

            return box;
        }

        /// <summary>Removes the colliders of an adopted parked ship and frees their meshes — the render meshes stay.</summary>
        public static void StripColliders(GameObject root)
        {
            foreach (var mc in root.GetComponentsInChildren<MeshCollider>())
            {
                if (mc.sharedMesh != null)
                {
                    Object.Destroy(mc.sharedMesh);
                }

                Object.Destroy(mc);
            }
        }

        /// <summary>Destroys an adopted ship together with the render meshes it owns (a parked ship's chunk meshes are its
        /// own; Unity would not free them with the GameObject).</summary>
        public static void DestroyWithMeshes(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh != null)
                {
                    Object.Destroy(mf.sharedMesh);
                }
            }

            Object.Destroy(root);
        }

        /// <summary>The colour of the first solid block at or below <paramref name="cell"/> (a few blocks down) — the dust a
        /// ship kicks up there. Sand, snow, grass and regolith each give their own cloud.</summary>
        public static Color GroundColor(GameBootstrap game, Vector3i cell)
        {
            var fallback = new Color(0.62f, 0.57f, 0.5f);
            if (game?.World == null || game.Atlas == null)
            {
                return fallback;
            }

            for (int dy = 0; dy < 8; dy++)
            {
                var b = game.World.GetBlock(cell.X, cell.Y - dy, cell.Z);
                if (!b.IsAir)
                {
                    var c = game.Atlas.AverageColor(b.Value);
                    return Color.Lerp(c, new Color(0.8f, 0.78f, 0.74f), 0.25f); // dust reads lighter than the block
                }
            }

            return fallback;
        }

        /// <summary>One frame of engine fire out of <paramref name="exhausts"/> (ship-local, under <paramref name="ship"/>)
        /// streaming along <paramref name="push"/> (world). <paramref name="power"/> 0..1 sets the amount and the length.</summary>
        public static void EngineFire(Transform ship, List<(Vector3 Local, float Size)> exhausts, Vector3 fallbackLocal, Vector3 push, float power)
        {
            if (ship == null || power <= 0.01f)
            {
                return;
            }

            int engines = exhausts?.Count ?? 0;
            int n = Mathf.RoundToInt(4f * power * FxKit.Density * Mathf.Sqrt(Mathf.Max(1, engines))) + 1;
            for (int k = 0; k < n; k++)
            {
                var nozzle = ship.TransformPoint(fallbackLocal);
                float size = 1f;
                if (engines > 0)
                {
                    var ex = exhausts[Random.Range(0, engines)];
                    nozzle = ship.TransformPoint(ex.Local);
                    size = Mathf.Max(0.6f, ex.Size);
                }

                var hot = Color.Lerp(new Color(1f, 0.55f, 0.2f), new Color(1f, 0.92f, 0.7f), Random.value);
                FxKit.Emit(FxKit.Kind.Glow, nozzle + Random.insideUnitSphere * 0.3f * size, push * Random.Range(7f, 14f) * (0.6f + power),
                    Random.Range(0.5f, 0.9f) * size * (0.7f + power * 0.5f), Random.Range(0.14f, 0.26f), hot);
            }
        }

        /// <summary>Dust swirling out from under a hovering ship: <paramref name="strength"/> 0..1 (higher as the ship nears
        /// the ground), racing outward and a little round along the ground.</summary>
        public static void DustSwirl(Vector3 ground, float radius, float strength, Color dust)
        {
            if (strength <= 0.02f)
            {
                return;
            }

            int n = Mathf.RoundToInt(3f * strength * FxKit.Density) + (Random.value < strength ? 1 : 0);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var swirl = new Vector3(-outward.z, 0f, outward.x);
                var at = ground + outward * Random.Range(0.3f, radius) + Vector3.up * 0.3f;
                var vel = (outward * Random.Range(5f, 10f) + swirl * 3f + Vector3.up * Random.Range(0.5f, 2f)) * (0.5f + strength);
                FxKit.Emit(FxKit.Kind.Dust, at, vel, Random.Range(0.8f, 1.6f) * (0.7f + strength), Random.Range(0.9f, 1.6f),
                    new Color(dust.r, dust.g, dust.b, 0.75f));
            }
        }

        /// <summary>The touchdown (or lift-off) burst: a ring of dust rolling out, a ground ring, a glow under the hull.</summary>
        public static void DustBurst(Vector3 ground, float radius, Color dust)
        {
            int n = FxKit.Scaled(36);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f + Random.value * 0.2f;
                var dir = new Vector3(Mathf.Cos(a), 0.12f, Mathf.Sin(a));
                FxKit.Emit(FxKit.Kind.Dust, ground + dir * radius * 0.5f, dir * Random.Range(6f, 12f), Random.Range(1f, 2f),
                    Random.Range(1.2f, 2.2f), new Color(dust.r, dust.g, dust.b, 0.85f));
            }

            FxKit.Ring(ground + Vector3.up * 0.15f, Vector3.up, Color.Lerp(dust, new Color(1f, 0.8f, 0.55f), 0.5f), radius * 0.4f,
                radius * 2.4f, 0.8f, thickness: 0.14f, fill: 0.05f, intensity: 1.6f);
        }

        /// <summary>Steam venting from the hull's sides after touchdown (the engines cooling down).</summary>
        public static void Steam(Bounds hull, float strength)
        {
            if (strength <= 0.02f || Random.value > strength * FxKit.Density)
            {
                return;
            }

            float side = Random.value < 0.5f ? -1f : 1f;
            var at = new Vector3(hull.center.x + side * hull.extents.x, hull.min.y + Random.Range(0.4f, 1.2f), hull.center.z + Random.Range(-hull.extents.z, hull.extents.z) * 0.6f);
            FxKit.Emit(FxKit.Kind.Smoke, at, new Vector3(side * Random.Range(1.5f, 3f), Random.Range(1f, 2.2f), 0f), Random.Range(0.8f, 1.4f),
                Random.Range(1.1f, 1.8f), new Color(0.93f, 0.95f, 0.98f, 0.8f));
        }

        /// <summary>The vapour cone of a ship punching through the sound barrier: a white shock ring around the hull, a
        /// quick shell and a burst of condensation streaming back.</summary>
        public static void VapourCone(Vector3 at, Vector3 heading, float size)
        {
            FxKit.Ring(at, heading, new Color(0.92f, 0.96f, 1f), size * 0.6f, size * 3.2f, 0.7f, thickness: 0.2f, fill: 0.12f, intensity: 2.2f);
            FxKit.Shell(at, new Color(0.85f, 0.92f, 1f), size * 0.5f, size * 2.4f, 0.45f, 2f, 0.06f, 1.2f * FxKit.FlashScale);
            FxKit.Burst(FxKit.Kind.Smoke, at, 18, -heading, 70f, 6f, 14f, 0.8f, 1.6f, 0.5f, 1f, new Color(0.95f, 0.97f, 1f, 0.7f));
        }
    }
}
