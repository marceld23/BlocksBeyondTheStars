// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3766: a thrown block in flight (the server's <c>WorldFx</c> kind <c>"block_throw"</c>). The server flew it and
    /// already knows where it ends and whether it hit; this draws the same arc (<see cref="BlockThrow.ArcY"/>) with the
    /// block as the hand holds it — tumbling, half a block big — and a puff where it comes down. The item itself appears
    /// there as an ordinary drop packet; this picture carries no state.
    /// </summary>
    public sealed partial class WorldEntities
    {
        /// <summary>A throw further away than this is not drawn.</summary>
        private const float ThrownBlockViewRange = 70f;

        private static readonly Color ThrownDust = new Color(0.78f, 0.74f, 0.66f);

        private void OnThrownBlockFx(WorldFx fx)
        {
            if (Game == null || fx.Kind != "block_throw" || string.IsNullOrEmpty(fx.Item) || Game.Content == null)
            {
                return;
            }

            var from = Game.ScenePos(fx.X, fx.Y, fx.Z);
            var to = Game.ScenePos(fx.ToX, fx.ToY, fx.ToZ);
            var cam = Camera.main;
            if (cam != null && (cam.transform.position - from).sqrMagnitude > ThrownBlockViewRange * ThrownBlockViewRange)
            {
                return;
            }

            float seconds = Mathf.Clamp(fx.Strength, 0.05f, BlockThrow.MaxFlightSeconds);
            bool hit = fx.Radius > 0f;
            var (kind, tint, blockKey) = HeldItem.For(Game.Content, fx.Item);
            var root = new GameObject("ThrownBlock");
            root.transform.SetParent(FxKit.Ensure().transform, false);
            root.transform.position = from;
            var model = HeldItem.Build(root.transform, kind, tint, blockKey, fx.Item);
            if (model != null)
            {
                FitThrownBlock(root.transform, model);
            }

            var spin = new Vector3(260f, 90f, 40f); // degrees a second — it tumbles forward
            FxKit.Animate(seconds, t =>
            {
                float time = t * seconds;
                var p = Vector3.Lerp(from, to, t);
                p.y = BlockThrow.ArcY(from.y, to.y, seconds, time);
                root.transform.SetPositionAndRotation(p, Quaternion.Euler(spin * time));
            }, () =>
            {
                Object.Destroy(root);
                FxKit.Burst(FxKit.Kind.Smoke, to, hit ? 4 : 7, Vector3.up, 70f, 0.4f, 1.3f, 0.18f, 0.36f, 0.35f, 0.7f, ThrownDust);
                ClientAudio.Instance?.At(hit ? "melee_hit" : "land_heavy", to, hit ? 0.85f : 1.15f, hit ? 0.8f : 0.5f);
            }, root);
        }

        /// <summary>Centres the held model on its parent and scales it to half a world block (the carried size).</summary>
        private static void FitThrownBlock(Transform root, GameObject model)
        {
            var parts = model.GetComponentsInChildren<Renderer>();
            if (parts.Length == 0)
            {
                return;
            }

            var b = BoundsOf(parts);
            model.transform.localScale *= BlockCarryPose.ScaleFor(Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            model.transform.position += root.position - BoundsOf(parts).center; // measured again: the scale moved its centre
        }

        private static Bounds BoundsOf(Renderer[] parts)
        {
            var b = parts[0].bounds;
            for (int i = 1; i < parts.Length; i++)
            {
                b.Encapsulate(parts[i].bounds);
            }

            return b;
        }
    }
}
