using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Item 21 — geyser / vent eruptions. Scans the blocks around the player for <c>geyser_vent</c> cells and, on
    /// a per-vent timer, erupts a rising plume of particles + a hiss at each. Purely cosmetic (the server owns the
    /// world). The plume colour follows the world type: pale water/steam on wet worlds, dark ash/ember on
    /// volcanic ones. Mirrors the other world VFX views (CreatureView / WeatherFx); wired up in WorldRig.
    /// </summary>
    public sealed class GeyserView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int ScanR = 7;            // block radius around the player to look for vents
        private const float ScanInterval = 0.75f;
        private readonly Dictionary<Vector3Int, float> _vents = new(); // world cell → next eruption time
        private float _scanTimer;

        private void Update()
        {
            if (Game?.World == null || Game.Content == null)
            {
                return;
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanInterval;
                Rescan();
            }

            float now = Time.time;
            // Erupt any vent whose timer is up (snapshot the keys — Erupt doesn't mutate the dict).
            foreach (var cell in _ventKeys())
            {
                if (now >= _vents[cell])
                {
                    Erupt(cell);
                    _vents[cell] = now + Random.Range(4f, 9f); // stagger the next eruption
                }
            }
        }

        private List<Vector3Int> _keyScratch = new();
        private List<Vector3Int> _ventKeys()
        {
            _keyScratch.Clear();
            foreach (var k in _vents.Keys) _keyScratch.Add(k);
            return _keyScratch;
        }

        private void Rescan()
        {
            var p = Game.PlayerPosition;
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y), pz = Mathf.FloorToInt(p.z);

            for (int dx = -ScanR; dx <= ScanR; dx++)
            for (int dy = -4; dy <= 4; dy++)
            for (int dz = -ScanR; dz <= ScanR; dz++)
            {
                int wx = px + dx, wy = py + dy, wz = pz + dz;
                if (Game.Content.BlockById(Game.World.GetBlock(wx, wy, wz))?.Key == "geyser_vent")
                {
                    var cell = new Vector3Int(wx, wy, wz);
                    if (!_vents.ContainsKey(cell))
                    {
                        _vents[cell] = Time.time + Random.Range(0.5f, 4f); // first eruption soon after discovery
                    }
                }
            }

            // Forget vents that left range or were mined away.
            var stale = new List<Vector3Int>();
            foreach (var cell in _vents.Keys)
            {
                if (Mathf.Abs(cell.x - px) > ScanR + 2 || Mathf.Abs(cell.y - py) > 6 || Mathf.Abs(cell.z - pz) > ScanR + 2
                    || Game.Content.BlockById(Game.World.GetBlock(cell.x, cell.y, cell.z))?.Key != "geyser_vent")
                {
                    stale.Add(cell);
                }
            }

            foreach (var cell in stale) _vents.Remove(cell);
        }

        private void Erupt(Vector3Int cell)
        {
            Vector3 at = Game.ScenePos(cell.x + 0.5f, cell.y + 1f, cell.z + 0.5f); // just above the vent, seam-aware
            string biome = Game.Environment?.Biome ?? string.Empty;
            bool volcanic = biome == "ashen" || biome == "lava";
            Color lo = volcanic ? new Color(0.55f, 0.20f, 0.07f) : new Color(0.80f, 0.90f, 1f);  // ember vs water
            Color hi = volcanic ? new Color(0.62f, 0.62f, 0.64f) : new Color(0.97f, 0.99f, 1f);  // grey vs white steam

            ClientAudio.Instance?.At("geyser_erupt", at, 1f, 0.9f);

            var root = new GameObject("geyser_plume");
            root.transform.SetParent(transform, true); // under the game root → no leak into menus
            root.transform.position = at;
            root.AddComponent<Plume>().Init(lo, hi);
        }

        /// <summary>A short-lived rising column of fading (shrinking) particle cubes. Uses the same build-safe
        /// unlit shader as the other VFX; "fade" is a scale-down so no transparent shader is needed.</summary>
        private sealed class Plume : MonoBehaviour
        {
            private const float MaxLife = 1.4f;
            private readonly List<Transform> _t = new();
            private readonly List<Vector3> _v = new();
            private float _life;

            public void Init(Color lo, Color hi)
            {
                var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
                var matLo = new Material(shader) { color = ShaderColor.Srgb(lo) };
                var matHi = new Material(shader) { color = ShaderColor.Srgb(hi) };

                for (int i = 0; i < 16; i++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "geyser_p";
                    var col = go.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(Random.Range(-0.18f, 0.18f), Random.Range(0f, 0.4f), Random.Range(-0.18f, 0.18f));
                    go.transform.localScale = Vector3.one * Random.Range(0.12f, 0.3f);
                    go.GetComponent<Renderer>().sharedMaterial = Random.value < 0.5f ? matLo : matHi;
                    _t.Add(go.transform);
                    _v.Add(new Vector3(Random.Range(-0.7f, 0.7f), Random.Range(4f, 7f), Random.Range(-0.7f, 0.7f)));
                }

                Destroy(gameObject, MaxLife + 0.2f);
            }

            private void Update()
            {
                _life += Time.deltaTime;
                for (int i = 0; i < _t.Count; i++)
                {
                    var v = _v[i];
                    v.y -= 8f * Time.deltaTime; // gravity arcs the spout
                    _v[i] = v;
                    _t[i].position += v * Time.deltaTime;
                    _t[i].localScale *= Mathf.Max(0f, 1f - 0.85f * Time.deltaTime); // shrink → "fade"
                }
            }
        }
    }
}
