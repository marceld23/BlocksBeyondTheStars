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
            if (scan.X == null || scan.X.Length == 0)
            {
                return;
            }

            _glow ??= Shader.Find("BlocksBeyondTheStars/SunGlow") ?? Shader.Find("Unlit/Color");
            _until = Time.time + Mathf.Max(2f, scan.Seconds);

            for (int i = 0; i < scan.X.Length; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "OreMarker";
                var col = go.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                go.transform.SetParent(transform, true); // under the game root → not leaked into menus/editors
                go.transform.position = Game.ScenePos(scan.X[i] + 0.5f, scan.Y[i] + 0.5f, scan.Z[i] + 0.5f);
                go.transform.localScale = Vector3.one * 0.65f; // smaller than the block — reads as "inside" it

                Color tint = TintFor(i < scan.Block.Length ? scan.Block[i] : (ushort)0);
                var mat = new Material(_glow) { color = ShaderColor.Srgb(tint * 0.8f) };
                go.GetComponent<Renderer>().sharedMaterial = mat;
                _markers.Add(new Marker { Go = go, Mat = mat, Base = ShaderColor.Srgb(tint * 0.8f), Phase = i * 0.61f });
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
