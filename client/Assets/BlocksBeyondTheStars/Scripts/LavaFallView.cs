// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Lavafall impact FX (L3) — the molten counterpart to <see cref="WaterfallMistView"/>. Where lava falls
    /// more than three blocks it throws up <b>embers</b> (hot orange flecks that arc and cool to dark) instead of
    /// the white water spray the mist view deliberately skips, and it feeds a localized <b>heat-haze</b> into
    /// <see cref="HeatShimmer"/> so the air boils near the fall. Purely cosmetic, client-side, inferred from block
    /// ids (works on every world / old save); wired up in WorldRig. The scan / emit / particle pool live in
    /// <see cref="FallImpactFx"/> (same "more than 3 blocks" gate as the water mist).
    /// </summary>
    public sealed class LavaFallView : FallImpactFx
    {
        private const float HeatRange = 7f;       // within this many blocks of an impact, the air shimmers

        private Material _hotMat, _coolMat;

        private readonly List<float> _pmax = new(); // per particle, index-aligned with Pt

        protected override string FluidKey => "lava";
        protected override int MaxLive => 180;
        protected override float EmitPerSecond => 12f;
        protected override float RateCeiling => 1.5f;
        protected override string ParticleName => "lavafall_ember";

        protected override void BeforeEmit() => FeedHeat();

        protected override void OnParticleCreated(GameObject go) => _pmax.Add(0f);

        /// <summary>Boil the air near the nearest impact (localized heat-haze via the shared shimmer).</summary>
        private void FeedHeat()
        {
            if (Impacts.Count == 0)
            {
                return;
            }

            var p = Game.PlayerPosition;
            float best = float.MaxValue;
            foreach (var c in Impacts.Keys)
            {
                float dx = c.x + 0.5f - p.x, dy = c.y + 0.5f - p.y, dz = c.z + 0.5f - p.z;
                float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d < best) best = d;
            }

            float heat = Mathf.Clamp01(1f - best / HeatRange);
            if (heat > 0f)
            {
                HeatShimmer.AddProximityHeat(heat * 0.85f);
            }
        }

        protected override void Spawn(Vector3 at)
        {
            if (Pt.Count - DeadCount() >= MaxLive)
            {
                return;
            }

            Transform t = AcquireParticle();
            t.gameObject.SetActive(true);
            t.position = at + new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0f, 0.2f), Random.Range(-0.2f, 0.2f));
            t.localScale = Vector3.one * Random.Range(0.06f, 0.16f);
            t.GetComponent<Renderer>().sharedMaterial = _hotMat; // born bright; cools as it ages (Step swaps to cool)

            int idx = Pt.IndexOf(t);
            // A sharp upward pop off the impact + a quick gravity arc — reads as a spat ember, not a fountain.
            Pv[idx] = new Vector3(Random.Range(-1.6f, 1.6f), Random.Range(2.2f, 4.4f), Random.Range(-1.6f, 1.6f));
            float life = Random.Range(0.5f, 1.1f);
            Plife[idx] = life;
            _pmax[idx] = life;
        }

        protected override void Step(float dt)
        {
            for (int i = 0; i < Pt.Count; i++)
            {
                if (Plife[i] <= 0f)
                {
                    continue;
                }

                Plife[i] -= dt;
                if (Plife[i] <= 0f)
                {
                    Pt[i].gameObject.SetActive(false);
                    continue;
                }

                Vector3 v = Pv[i];
                v.y -= 9f * dt; // embers are heavier than mist → a snappier fall
                Pv[i] = v;
                Pt[i].position += v * dt;
                Pt[i].localScale *= Mathf.Max(0f, 1f - 1.0f * dt);

                // Cool past half-life: swap the bright material for the dark ember so the spark fades to ash.
                if (_pmax[i] > 0f && Plife[i] < _pmax[i] * 0.45f)
                {
                    var r = Pt[i].GetComponent<Renderer>();
                    if (r.sharedMaterial != _coolMat) r.sharedMaterial = _coolMat;
                }
            }
        }

        protected override void EnsureMaterials()
        {
            if (_hotMat != null)
            {
                return;
            }

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            _hotMat = new Material(shader) { color = ShaderColor.Srgb(new Color(1f, 0.62f, 0.18f)) };  // bright molten ember
            _coolMat = new Material(shader) { color = ShaderColor.Srgb(new Color(0.42f, 0.10f, 0.05f)) }; // cooled dark cinder
        }
    }
}
