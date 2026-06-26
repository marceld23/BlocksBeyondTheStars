// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Lavafall impact FX (L3) — the molten counterpart to <see cref="WaterfallMistView"/>. Where lava falls
    /// more than three blocks it throws up <b>embers</b> (hot orange flecks that arc and cool to dark) instead of
    /// the white water spray the mist view deliberately skips, and it feeds a localized <b>heat-haze</b> into
    /// <see cref="HeatShimmer"/> so the air boils near the fall. Purely cosmetic, client-side, inferred from block
    /// ids (works on every world / old save); wired up in WorldRig.
    /// </summary>
    public sealed class LavaFallView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int ScanR = 8;
        private const int ScanRY = 8;
        private const int DropCap = 24;
        private const int MinDrop = 4;            // strictly "more than 3 blocks", same gate as the water mist
        private const float ScanInterval = 0.6f;
        private const int MaxLive = 180;
        private const float EmitPerSecond = 12f;
        private const float HeatRange = 7f;       // within this many blocks of an impact, the air shimmers

        private readonly Dictionary<Vector3Int, float> _impacts = new();
        private readonly List<Vector3Int> _scratch = new();
        private float _scanTimer;
        private BlockId _lavaId;
        private bool _haveLava;
        private Material _hotMat, _coolMat;

        private readonly List<Transform> _pt = new();
        private readonly List<Vector3> _pv = new();
        private readonly List<float> _plife = new();
        private readonly List<float> _pmax = new();

        private void Update()
        {
            if (Game?.World == null || Game.Content == null)
            {
                return;
            }

            if (!_haveLava)
            {
                var def = Game.Content.GetBlock("lava");
                if (def == null)
                {
                    return;
                }

                _lavaId = def.NumericId;
                _haveLava = true;
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                Rescan();
            }

            FeedHeat();
            Emit(Time.deltaTime);
            Step(Time.deltaTime);
        }

        private void Rescan()
        {
            var p = Game.PlayerPosition;
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y), pz = Mathf.FloorToInt(p.z);
            System.Func<int, int, int, BlockId> wb = Game.World.GetBlock;

            _scratch.Clear();
            foreach (var c in _impacts.Keys) _scratch.Add(c);
            foreach (var c in _scratch)
            {
                if (Mathf.Abs(c.x - px) > ScanR + 2 || Mathf.Abs(c.y - py) > ScanRY + 2 || Mathf.Abs(c.z - pz) > ScanR + 2
                    || WaterfallDetect.ImpactDrop(wb, _lavaId, c.x, c.y, c.z, DropCap) < MinDrop)
                {
                    _impacts.Remove(c);
                }
            }

            for (int dx = -ScanR; dx <= ScanR; dx++)
            for (int dy = -ScanRY; dy <= ScanRY; dy++)
            for (int dz = -ScanR; dz <= ScanR; dz++)
            {
                int wx = px + dx, wy = py + dy, wz = pz + dz;
                if (WaterfallDetect.ImpactDrop(wb, _lavaId, wx, wy, wz, DropCap) >= MinDrop)
                {
                    var cell = new Vector3Int(wx, wy, wz);
                    if (!_impacts.ContainsKey(cell))
                    {
                        _impacts[cell] = 0f;
                    }
                }
            }
        }

        /// <summary>Boil the air near the nearest impact (localized heat-haze via the shared shimmer).</summary>
        private void FeedHeat()
        {
            if (_impacts.Count == 0)
            {
                return;
            }

            var p = Game.PlayerPosition;
            float best = float.MaxValue;
            foreach (var c in _impacts.Keys)
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

        private void Emit(float dt)
        {
            if (_impacts.Count == 0)
            {
                return;
            }

            EnsureMaterials();
            System.Func<int, int, int, BlockId> wb = Game.World.GetBlock;

            _scratch.Clear();
            foreach (var c in _impacts.Keys) _scratch.Add(c);
            foreach (var cell in _scratch)
            {
                int drop = WaterfallDetect.ImpactDrop(wb, _lavaId, cell.x, cell.y, cell.z, DropCap);
                float rate = EmitPerSecond * Mathf.Lerp(0.7f, 1.5f, Mathf.Clamp01((drop - MinDrop) / 8f));
                float acc = _impacts[cell] + rate * dt;
                Vector3 at = Game.ScenePos(cell.x + 0.5f, cell.y + 1f, cell.z + 0.5f);
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
                return;
            }

            Transform t = AcquireParticle();
            t.gameObject.SetActive(true);
            t.position = at + new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(0f, 0.2f), Random.Range(-0.2f, 0.2f));
            t.localScale = Vector3.one * Random.Range(0.06f, 0.16f);
            t.GetComponent<Renderer>().sharedMaterial = _hotMat; // born bright; cools as it ages (Step swaps to cool)

            int idx = _pt.IndexOf(t);
            // A sharp upward pop off the impact + a quick gravity arc — reads as a spat ember, not a fountain.
            _pv[idx] = new Vector3(Random.Range(-1.6f, 1.6f), Random.Range(2.2f, 4.4f), Random.Range(-1.6f, 1.6f));
            float life = Random.Range(0.5f, 1.1f);
            _plife[idx] = life;
            _pmax[idx] = life;
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
                v.y -= 9f * dt; // embers are heavier than mist → a snappier fall
                _pv[i] = v;
                _pt[i].position += v * dt;
                _pt[i].localScale *= Mathf.Max(0f, 1f - 1.0f * dt);

                // Cool past half-life: swap the bright material for the dark ember so the spark fades to ash.
                if (_pmax[i] > 0f && _plife[i] < _pmax[i] * 0.45f)
                {
                    var r = _pt[i].GetComponent<Renderer>();
                    if (r.sharedMaterial != _coolMat) r.sharedMaterial = _coolMat;
                }
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
            go.name = "lavafall_ember";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            _pt.Add(go.transform);
            _pv.Add(Vector3.zero);
            _plife.Add(0f);
            _pmax.Add(0f);
            return go.transform;
        }

        private void EnsureMaterials()
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
