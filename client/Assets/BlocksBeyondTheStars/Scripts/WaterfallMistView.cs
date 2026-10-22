// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Spray / mist where a waterfall lands. Scans the blocks around the player for falling-water columns
    /// (<see cref="WaterfallDetect"/>) and, at every drop taller than three blocks, keeps a continuous puff of
    /// fine particles rising and bouncing off the impact point. Purely cosmetic and client-side (the server
    /// owns the water) — inferred from block ids, so it works on old saves too. Mirrors GeyserView / WeatherFx;
    /// wired up in WorldRig. Lava is left alone — the white spray would read wrong on it.
    /// The scan / emit / particle pool live in <see cref="FallImpactFx"/>.
    /// </summary>
    public sealed class WaterfallMistView : FallImpactFx
    {
        private Material _mistMat;

        protected override string FluidKey => "water";
        protected override int MaxLive => 220;         // hard cap on simultaneous spray particles
        protected override float EmitPerSecond => 16f; // base spray rate per impact
        protected override float RateCeiling => 1.6f;
        protected override string ParticleName => "waterfall_mist_p";

        protected override void OnParticleCreated(GameObject go) => go.GetComponent<Renderer>().sharedMaterial = _mistMat;

        protected override void Spawn(Vector3 at)
        {
            if (Pt.Count - DeadCount() >= MaxLive)
            {
                return; // at the cap — let live particles age out first
            }

            Transform t = AcquireParticle();
            t.gameObject.SetActive(true);
            t.position = at + new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0f, 0.2f), Random.Range(-0.25f, 0.25f));
            t.localScale = Vector3.one * Random.Range(0.10f, 0.22f);

            int idx = Pt.IndexOf(t);
            // Burst up and outward off the impact, with a quick gravity arc — reads as splash droplets / mist.
            Pv[idx] = new Vector3(Random.Range(-1.3f, 1.3f), Random.Range(1.6f, 3.4f), Random.Range(-1.3f, 1.3f));
            Plife[idx] = Random.Range(0.5f, 1.0f);
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
                v.y -= 7f * dt; // gravity arc
                Pv[i] = v;
                Pt[i].position += v * dt;
                Pt[i].localScale *= Mathf.Max(0f, 1f - 1.1f * dt); // shrink → "fade" (no transparent shader needed)
            }
        }

        protected override void EnsureMaterials()
        {
            if (_mistMat != null)
            {
                return;
            }

            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            _mistMat = new Material(shader) { color = ShaderColor.Srgb(new Color(0.88f, 0.95f, 1f)) }; // pale water spray
        }
    }
}
