// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Feature 40 — terrain-scanner overlay. Renders the server's <see cref="OreScanResult"/> as glowing
    /// marker cubes at the found ore/crystal/data-cache cells, visible THROUGH terrain (the always-included
    /// <c>BlocksBeyondTheStars/SunGlow</c> shader draws additively with ZTest Always), gently pulsing and fading out
    /// over the scan duration. Markers are tinted by ore type so a prospector can tell iron from gold at a
    /// glance. Purely cosmetic — the server validated energy/cooldown and produced the hit list.
    /// </summary>
    public sealed class OreScanView : MonoBehaviour
    {
        public GameBootstrap Game;

        private sealed class Marker
        {
            public GameObject Go;
            public Material Mat;
            public Color Base;
            public float Phase;
        }

        private readonly List<Marker> _markers = new();
        private float _until;
        private bool _subscribed;
        private static Shader _glow;

        /// <summary>The scene's overlay (one per world rig) — lets the Tab menu drop a station marker (#1072).</summary>
        public static OreScanView Instance { get; private set; }

        private void Awake() => Instance = this;

        /// <summary>#1072: a single through-wall marker on a station block the menu just pointed at (the
        /// "go there" hint made visible in the world once the menu closes). Same glow cube as an ore hit,
        /// cyan so it reads as "station", auto-clears after <paramref name="seconds"/>.</summary>
        public void ShowStationMarker(int x, int y, int z, float seconds)
        {
            if (Game == null)
            {
                return;
            }

            Clear();
            _glow ??= Shader.Find("BlocksBeyondTheStars/SunGlow") ?? Shader.Find("Unlit/Color");
            _until = Time.time + Mathf.Max(2f, seconds);
            AddMarker(Game.ScenePos(x + 0.5f, y + 0.5f, z + 0.5f), new Color(0.45f, 0.95f, 1f), 0f, 0.8f);
        }

        private void AddMarker(Vector3 scenePos, Color tint, float phase, float scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "OreMarker";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            go.transform.SetParent(transform, true); // under the game root → not leaked into menus/editors
            go.transform.position = scenePos;
            go.transform.localScale = Vector3.one * scale;
            var mat = new Material(_glow) { color = ShaderColor.Srgb(tint * 0.8f) };
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _markers.Add(new Marker { Go = go, Mat = mat, Base = ShaderColor.Srgb(tint * 0.8f), Phase = phase });
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

            // Pulse gently; fade out over the last 2 seconds (additive shader → fading the tint fades the glow).
            float fade = Mathf.Clamp01(left / 2f);
            foreach (var m in _markers)
            {
                float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 4f + m.Phase);
                m.Mat.color = m.Base * (pulse * fade);
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
                    : string.Format(loc.Get("ui.scan.ore.found"), scan.Capped ? hits + "+" : hits.ToString()));
            }

            if (hits == 0)
            {
                return;
            }

            _glow ??= Shader.Find("BlocksBeyondTheStars/SunGlow") ?? Shader.Find("Unlit/Color");
            _until = Time.time + Mathf.Max(2f, scan.Seconds);

            for (int i = 0; i < scan.X.Length; i++)
            {
                // Smaller than the block — reads as "inside" it.
                AddMarker(Game.ScenePos(scan.X[i] + 0.5f, scan.Y[i] + 0.5f, scan.Z[i] + 0.5f),
                    TintFor(i < scan.Block.Length ? scan.Block[i] : (ushort)0), i * 0.61f, 0.65f);
            }
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
            foreach (var m in _markers)
            {
                if (m.Go != null)
                {
                    Destroy(m.Go);
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
