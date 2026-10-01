// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Feature 40 — terrain-scanner overlay, reworked in the VFX overhaul (#2153). Renders the server's
    /// <see cref="OreScanResult"/> as holographic GHOST CUBES at the found ore/crystal/data-cache cells, drawn by
    /// <c>FxHolo</c>: glowing edges and scan lines where the cube is in view, a softer x-ray glow where rock hides it
    /// (a ZTest Greater pass in the Transparent queue — the old markers used <c>SunGlow</c>, whose Background queue drew
    /// them BEFORE the terrain, so the terrain painted over them and "visible through walls" never held, #2151). Each
    /// cube pops in with a little spring exactly when the scan wave (<see cref="FxScanWave"/>) reaches it, is tinted by
    /// ore type, and fades over the server's duration. One cached material per ore colour, so the cubes batch.
    /// Purely cosmetic — the server validated energy/cooldown and produced the hit list.
    /// </summary>
    public sealed class OreScanView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>The local player rig (the scan wave starts at the player), set by the world rig.</summary>
        public PlayerController Player;

        /// <summary>Wave speed of the terrain scanner in blocks/s — the same as <see cref="FxGadgets.TerrainScan"/>.</summary>
        private const float WaveSpeed = 14f;

        private sealed class Marker
        {
            public MeshRenderer Renderer;
            public Color Tint;
            public float Appear;   // Time.time when the wave front reaches it
            public float Scale;
            public bool Popped;
        }

        private readonly List<Marker> _markers = new();
        private float _until;
        private bool _subscribed;

        /// <summary>The scene's overlay (one per world rig) — lets the Tab menu drop a station marker (#1072).</summary>
        public static OreScanView Instance { get; private set; }

        private void Awake() => Instance = this;

        /// <summary>#1072: a single through-wall marker on a station block the menu just pointed at (the
        /// "go there" hint made visible in the world once the menu closes). Same ghost cube as an ore hit,
        /// cyan so it reads as "station", auto-clears after <paramref name="seconds"/>.</summary>
        public void ShowStationMarker(int x, int y, int z, float seconds)
        {
            if (Game == null)
            {
                return;
            }

            Clear();
            _until = Time.time + Mathf.Max(2f, seconds);
            AddMarker(Game.ScenePos(x + 0.5f, y + 0.5f, z + 0.5f), new Color(0.45f, 0.95f, 1f), Time.time, 1.02f);
        }

        private void AddMarker(Vector3 scenePos, Color tint, float appear, float scale)
        {
            var mat = FxGadgets.HoloMaterial(tint, 0.65f);
            if (mat == null)
            {
                return;
            }

            var mr = FxKit.Ensure().RentMesh(FxKit.CubeMesh, mat);
            mr.transform.position = scenePos;
            mr.transform.localScale = Vector3.zero;
            _markers.Add(new Marker { Renderer = mr, Tint = tint, Appear = appear, Scale = scale });
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.OreScanReceived += OnScan;
                Game.Network.WorldResetReceived += _ => Clear();
                _subscribed = true;
            }

            if (_markers.Count == 0)
            {
                return;
            }

            float left = _until - Time.time;
            if (left <= 0f)
            {
                Clear();
                return;
            }

            // Pop in as the wave arrives (scale spring + a sparkle), gently pulse, fade out over the last 2 seconds.
            float fade = Mathf.Clamp01(left / 2f);
            foreach (var m in _markers)
            {
                if (m.Renderer == null)
                {
                    continue;
                }

                float age = Time.time - m.Appear;
                if (age < 0f)
                {
                    m.Renderer.transform.localScale = Vector3.zero;
                    continue;
                }

                if (!m.Popped)
                {
                    m.Popped = true;
                    var at = m.Renderer.transform.position;
                    FxKit.Burst(FxKit.Kind.Motes, at, 4, Vector3.up, 90f, 0.4f, 1.2f, 0.04f, 0.07f, 0.4f, 0.7f, m.Tint, Color.white);
                }

                float pop = age < 0.14f ? Mathf.Lerp(0.2f, 1.18f, age / 0.14f) : Mathf.Lerp(1.18f, 1f, Mathf.Clamp01((age - 0.14f) / 0.12f));
                m.Renderer.transform.localScale = Vector3.one * (m.Scale * pop);
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * 4f + m.Appear * 3.1f);
                var lin = FxKit.Lin(m.Tint);
                var b = FxKit.Block;
                b.Clear();
                b.SetColor("_Color", new Color(lin.r, lin.g, lin.b, pulse * fade));
                b.SetFloat("_Behind", 0.65f);
                b.SetFloat("_EdgeWidth", 0.08f);
                b.SetFloat("_Fill", 0.14f);
                b.SetFloat("_LineDensity", 4f);
                b.SetFloat("_Intensity", 2.2f);
                m.Renderer.SetPropertyBlock(b);
            }
        }

        private void OnScan(OreScanResult scan)
        {
            Clear();
            int hits = scan.X?.Length ?? 0;

            // Text feedback on the HUD toast. The pulse used to produce glow markers and NOTHING else, so a
            // zero-hit scan — the common case on ore-poor worlds — spent 10 suit energy and showed an empty
            // screen, indistinguishable from a broken item (#482).
            var loc = Game?.Localizer;
            if (loc != null)
            {
                Game.ShowMessage(hits == 0
                    ? loc.Get("ui.scan.ore.none")
                    : string.Format(loc.Get("ui.scan.ore.found_list"), scan.Capped ? hits + "+" : hits.ToString(), FindsByKind(scan, loc)));
            }

            // The wave normally starts on the server's confirmation (ActionFx); start it here when it did not (yet).
            var origin = Player != null ? Player.transform.position + Vector3.up : Vector3.zero;
            if (!FxScanWave.Running && Player != null)
            {
                FxGadgets.TerrainScan(FxLook.ForItem(Game.Content, "terrain_scanner"), origin);
            }

            if (hits == 0)
            {
                return;
            }

            _until = Time.time + Mathf.Max(2f, scan.Seconds);
            for (int i = 0; i < scan.X.Length; i++)
            {
                // Slightly smaller than the block — reads as "inside" it.
                var p = Game.ScenePos(scan.X[i] + 0.5f, scan.Y[i] + 0.5f, scan.Z[i] + 0.5f);
                float delay = FxScanWave.ArrivalDelay(Vector3.Distance(origin, p), WaveSpeed);
                AddMarker(p, TintFor(i < scan.Block.Length ? scan.Block[i] : (ushort)0), Time.time + delay, 0.82f);
            }
        }

        /// <summary>What the pulse found, most common first (#2139) — "Eisenerz ×8 · Kupfererz ×3 · Kristall ×1". The toast used
        /// to give a bare count, so the player had to walk to each glow to learn whether it was worth digging for.</summary>
        private string FindsByKind(OreScanResult scan, BlocksBeyondTheStars.Shared.Localization.Localizer loc)
        {
            var counts = new Dictionary<ushort, int>();
            foreach (var b in scan.Block ?? System.Array.Empty<ushort>())
            {
                counts[b] = counts.TryGetValue(b, out int n) ? n + 1 : 1;
            }

            const int shown = 4; // a toast line, not a table
            var parts = new List<string>();
            foreach (var kv in counts.OrderByDescending(kv => kv.Value).Take(shown))
            {
                string key = Game.Content?.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(kv.Key))?.Key ?? string.Empty;
                string name = loc.Has($"block.{key}.name") ? loc.Get($"block.{key}.name") : key;
                parts.Add($"{name} ×{kv.Value}");
            }

            string list = string.Join(" · ", parts);
            return counts.Count > shown ? list + " · …" : list;
        }

        /// <summary>Marker tint by block kind: gold warm yellow, copper orange, iron rust, crystal cyan,
        /// data cache green, titanium pale silver — everything else a generic amber.</summary>
        private Color TintFor(ushort blockId)
        {
            string key = Game?.Content?.BlockById(new BlocksBeyondTheStars.Shared.Primitives.BlockId(blockId))?.Key ?? string.Empty;
            if (key.Contains("gold")) return new Color(1f, 0.84f, 0.2f);
            if (key.Contains("copper")) return new Color(1f, 0.55f, 0.25f);
            if (key.Contains("iron")) return new Color(0.95f, 0.45f, 0.35f);
            if (key.Contains("titanium")) return new Color(0.8f, 0.85f, 0.95f);
            if (key == "crystal") return new Color(0.45f, 0.95f, 1f);
            if (key == "data_cache") return new Color(0.4f, 1f, 0.55f);
            return new Color(1f, 0.75f, 0.3f); // other ores (rare earths etc.) — prospecting amber
        }

        private void Clear()
        {
            var kit = FxKit.Instance;
            foreach (var m in _markers)
            {
                if (m.Renderer != null)
                {
                    if (kit != null)
                    {
                        kit.ReleaseMesh(m.Renderer);
                    }
                    else
                    {
                        Destroy(m.Renderer.gameObject);
                    }
                }
            }

            _markers.Clear();
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.OreScanReceived -= OnScan;
            }
        }
    }
}
