// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Spray / mist where a waterfall lands. Scans the blocks around the player for falling-water columns
    /// (<see cref="WaterfallDetect"/>) and, at every drop taller than three blocks, keeps a continuous puff of
    /// fine particles rising and bouncing off the impact point. Purely cosmetic and client-side (the server
    /// owns the water) — inferred from block ids, so it works on old saves too. Mirrors GeyserView / WeatherFx;
    /// wired up in WorldRig. Lava is left alone — the white spray would read wrong on it.
    /// </summary>
    public sealed class WaterfallMistView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int ScanR = 8;             // horizontal block radius around the player
        private const int ScanRY = 8;            // vertical block radius (waterfalls are tall)
        private const int DropCap = 24;          // tallest drop we measure
        private const int MinDrop = 4;           // strictly "more than 3 blocks"
        private const float ScanInterval = 0.6f;
        private const int MaxLive = 220;         // hard cap on simultaneous spray particles
        private const float EmitPerSecond = 16f; // base spray rate per impact

        private readonly Dictionary<Vector3Int, float> _impacts = new(); // landing cell → emit accumulator
        private readonly List<Vector3Int> _scratch = new();
        private float _scanTimer;
        private BlockId _waterId;
        private bool _haveWater;
        private Material _mistMat;

        private readonly List<Transform> _pt = new();
        private readonly List<Vector3> _pv = new();
        private readonly List<float> _plife = new();

        private void Update()
        {
            if (Game?.World == null || Game.Content == null)
            {
                return;
            }

            if (!_haveWater)
            {
                var def = Game.Content.GetBlock("water");
                if (def == null)
                {
                    return;
                }

                _waterId = def.NumericId;
                _haveWater = true;
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                Rescan();
            }

            Emit(Time.deltaTime);
            Step(Time.deltaTime);
        }

        private void Rescan()
        {
            var p = Game.PlayerPosition;
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y), pz = Mathf.FloorToInt(p.z);
            System.Func<int, int, int, BlockId> wb = Game.World.GetBlock;

            // Forget impacts that drifted out of range or dried up; re-test the rest below.
            _scratch.Clear();
            foreach (var c in _impacts.Keys) _scratch.Add(c);
            foreach (var c in _scratch)
            {
                if (Mathf.Abs(c.x - px) > ScanR + 2 || Mathf.Abs(c.y - py) > ScanRY + 2 || Mathf.Abs(c.z - pz) > ScanR + 2
                    || WaterfallDetect.ImpactDrop(wb, _waterId, c.x, c.y, c.z, DropCap) < MinDrop)
                {
                    _impacts.Remove(c);
                }
            }

            for (int dx = -ScanR; dx <= ScanR; dx++)
            for (int dy = -ScanRY; dy <= ScanRY; dy++)
            for (int dz = -ScanR; dz <= ScanR; dz++)
            {
                int wx = px + dx, wy = py + dy, wz = pz + dz;
                if (WaterfallDetect.ImpactDrop(wb, _waterId, wx, wy, wz, DropCap) >= MinDrop)
                {
                    var cell = new Vector3Int(wx, wy, wz);
                    if (!_impacts.ContainsKey(cell))
                    {
                        _impacts[cell] = 0f;
                    }
                }
            }
        }

        private void Emit(float dt)
        {
            if (_impacts.Count == 0)
            {
                return;
            }

            EnsureMaterial();
            System.Func<int, int, int, BlockId> wb = Game.World.GetBlock;

            _scratch.Clear();
            foreach (var c in _impacts.Keys) _scratch.Add(c);
            foreach (var cell in _scratch)
            {
                int drop = WaterfallDetect.ImpactDrop(wb, _waterId, cell.x, cell.y, cell.z, DropCap);
                // Taller falls throw more spray, but flatten off so a huge drop doesn't drown the pool in particles.
                float rate = EmitPerSecond * Mathf.Lerp(0.7f, 1.6f, Mathf.Clamp01((drop - MinDrop) / 8f));
                float acc = _impacts[cell] + rate * dt;
                Vector3 at = Game.ScenePos(cell.x + 0.5f, cell.y + 1f, cell.z + 0.5f); // just above the landing, seam-aware
                while (acc >= 1f)
                {
                    acc -= 1f;
                    Spawn(at);
                }

                _impacts[cell] = acc;
            }
        }

        private void Spawn(Vector3 at)
        {
            if (_pt.Count - DeadCount() >= MaxLive)
            {
                return; // at the cap — let live particles age out first
            }

            Transform t = AcquireParticle();
            t.gameObject.SetActive(true);
            t.position = at + new Vector3(Random.Range(-0.25f, 0.25f), Random.Range(0f, 0.2f), Random.Range(-0.25f, 0.25f));
            t.localScale = Vector3.one * Random.Range(0.10f, 0.22f);

            int idx = _pt.IndexOf(t);
            // Burst up and outward off the impact, with a quick gravity arc — reads as splash droplets / mist.
            _pv[idx] = new Vector3(Random.Range(-1.3f, 1.3f), Random.Range(1.6f, 3.4f), Random.Range(-1.3f, 1.3f));
            _plife[idx] = Random.Range(0.5f, 1.0f);
        }

        private void Step(float dt)
        {
            for (int i = 0; i < _pt.Count; i++)
            {
                if (_plife[i] <= 0f)
                {
                    continue;
                }

                _plife[i] -= dt;
                if (_plife[i] <= 0f)
                {
                    _pt[i].gameObject.SetActive(false);
                    continue;
                }

                Vector3 v = _pv[i];
                v.y -= 7f * dt; // gravity arc
                _pv[i] = v;
                _pt[i].position += v * dt;
                _pt[i].localScale *= Mathf.Max(0f, 1f - 1.1f * dt); // shrink → "fade" (no transparent shader needed)
            }
        }

        private int DeadCount()
        {
            int n = 0;
            for (int i = 0; i < _plife.Count; i++)
            {
                if (_plife[i] <= 0f) n++;
            }

            return n;
        }

        private Transform AcquireParticle()
        {
            for (int i = 0; i < _pt.Count; i++)
            {
                if (_plife[i] <= 0f)
                {
                    return _pt[i];
                }
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "waterfall_mist_p";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false); // under the game root → no leak into menus
            go.GetComponent<Renderer>().sharedMaterial = _mistMat;
            _pt.Add(go.transform);
            _pv.Add(Vector3.zero);
            _plife.Add(0f);
            return go.transform;
        }

        private void EnsureMaterial()
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
