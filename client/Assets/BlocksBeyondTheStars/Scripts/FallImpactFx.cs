// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Primitives;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Shared base of the fall-impact FX (<see cref="WaterfallMistView"/>, <see cref="LavaFallView"/>). Scans the
    /// blocks around the player for falling columns of one fluid (<see cref="WaterfallDetect"/>), keeps every
    /// landing cell under a drop taller than three blocks as an impact with its own emit accumulator, and owns the
    /// pooled cube particles the derived view spawns there. A derived view supplies the fluid key, its rates, the
    /// particle look (<see cref="Spawn"/>, <see cref="Step"/>, materials) and any extra per-frame work
    /// (<see cref="BeforeEmit"/>). Purely cosmetic and client-side (the server owns the fluid) — inferred from block
    /// ids, so it works on every world / old save.
    /// </summary>
    public abstract class FallImpactFx : MonoBehaviour
    {
        public GameBootstrap Game;

        protected const int ScanR = 8;             // horizontal block radius around the player
        protected const int ScanRY = 8;            // vertical block radius (falls are tall)
        protected const int DropCap = 24;          // tallest drop we measure
        protected const int MinDrop = 4;           // strictly "more than 3 blocks"
        protected const float ScanInterval = 0.6f;

        protected readonly Dictionary<Vector3Int, float> Impacts = new(); // landing cell → emit accumulator
        private readonly List<Vector3Int> _scratch = new();
        private float _scanTimer;
        private BlockId _fluidId;
        private bool _haveFluid;

        protected readonly List<Transform> Pt = new();
        protected readonly List<Vector3> Pv = new();
        protected readonly List<float> Plife = new();

        /// <summary>Block key of the falling fluid ("water", "lava").</summary>
        protected abstract string FluidKey { get; }

        /// <summary>Hard cap on simultaneous particles.</summary>
        protected abstract int MaxLive { get; }

        /// <summary>Base particle rate per impact.</summary>
        protected abstract float EmitPerSecond { get; }

        /// <summary>Rate multiplier reached at the tallest drops (the floor is 0.7).</summary>
        protected abstract float RateCeiling { get; }

        /// <summary>GameObject name of a pooled particle.</summary>
        protected abstract string ParticleName { get; }

        private void Update()
        {
            if (Game?.World == null || Game.Content == null)
            {
                return;
            }

            if (!_haveFluid)
            {
                var def = Game.Content.GetBlock(FluidKey);
                if (def == null)
                {
                    return;
                }

                _fluidId = def.NumericId;
                _haveFluid = true;
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                Rescan();
            }

            BeforeEmit();
            // World time (#908): the particles hold with the rest of the world behind the pause menu.
            Emit(Game.WorldDeltaTime);
            Step(Game.WorldDeltaTime);
        }

        /// <summary>Extra per-frame work between the impact scan and the emit (default: none).</summary>
        protected virtual void BeforeEmit()
        {
        }

        private void Rescan()
        {
            var p = Game.PlayerPosition;
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y), pz = Mathf.FloorToInt(p.z);
            System.Func<int, int, int, BlockId> wb = Game.World.GetBlock;

            // Forget impacts that drifted out of range or dried up; re-test the rest below.
            _scratch.Clear();
            foreach (var c in Impacts.Keys) _scratch.Add(c);
            foreach (var c in _scratch)
            {
                if (Mathf.Abs(c.x - px) > ScanR + 2 || Mathf.Abs(c.y - py) > ScanRY + 2 || Mathf.Abs(c.z - pz) > ScanR + 2
                    || WaterfallDetect.ImpactDrop(wb, _fluidId, c.x, c.y, c.z, DropCap) < MinDrop)
                {
                    Impacts.Remove(c);
                }
            }

            for (int dx = -ScanR; dx <= ScanR; dx++)
            for (int dy = -ScanRY; dy <= ScanRY; dy++)
            for (int dz = -ScanR; dz <= ScanR; dz++)
            {
                int wx = px + dx, wy = py + dy, wz = pz + dz;
                if (WaterfallDetect.ImpactDrop(wb, _fluidId, wx, wy, wz, DropCap) >= MinDrop)
                {
                    var cell = new Vector3Int(wx, wy, wz);
                    if (!Impacts.ContainsKey(cell))
                    {
                        Impacts[cell] = 0f;
                    }
                }
            }
        }

        private void Emit(float dt)
        {
            if (Impacts.Count == 0)
            {
                return;
            }

            EnsureMaterials();
            System.Func<int, int, int, BlockId> wb = Game.World.GetBlock;

            _scratch.Clear();
            foreach (var c in Impacts.Keys) _scratch.Add(c);
            foreach (var cell in _scratch)
            {
                int drop = WaterfallDetect.ImpactDrop(wb, _fluidId, cell.x, cell.y, cell.z, DropCap);
                // Taller falls throw more particles, but flatten off so a huge drop doesn't drown the pool in them.
                float rate = EmitPerSecond * Mathf.Lerp(0.7f, RateCeiling, Mathf.Clamp01((drop - MinDrop) / 8f));
                float acc = Impacts[cell] + rate * dt;
                Vector3 at = Game.ScenePos(cell.x + 0.5f, cell.y + 1f, cell.z + 0.5f); // just above the landing, seam-aware
                while (acc >= 1f)
                {
                    acc -= 1f;
                    Spawn(at);
                }

                Impacts[cell] = acc;
            }
        }

        /// <summary>Creates the view's materials on first use (called before every non-empty emit).</summary>
        protected abstract void EnsureMaterials();

        /// <summary>Spawns one particle just above an impact.</summary>
        protected abstract void Spawn(Vector3 at);

        /// <summary>Advances every live particle by <paramref name="dt"/> world seconds.</summary>
        protected abstract void Step(float dt);

        protected int DeadCount()
        {
            int n = 0;
            for (int i = 0; i < Plife.Count; i++)
            {
                if (Plife[i] <= 0f) n++;
            }

            return n;
        }

        protected Transform AcquireParticle()
        {
            for (int i = 0; i < Pt.Count; i++)
            {
                if (Plife[i] <= 0f)
                {
                    return Pt[i];
                }
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = ParticleName;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false); // under the game root → no leak into menus
            OnParticleCreated(go);
            Pt.Add(go.transform);
            Pv.Add(Vector3.zero);
            Plife.Add(0f);
            return go.transform;
        }

        /// <summary>Hook for a freshly created pooled particle, before it joins the pool lists (default: none).</summary>
        protected virtual void OnParticleCreated(GameObject go)
        {
        }
    }
}
