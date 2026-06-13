using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders the glowing "data cubes" scattered on a world (<see cref="DataCubeList"/>) — old download
    /// terminals that grant a bundled minigame. Each cube hovers and slowly spins with a holographic glow; the
    /// player walks up and presses E to download it (see <see cref="NearestDataCube"/> + PlayerController).
    /// Which game a cube holds is resolved from its seed via <see cref="MinigameCatalog"/>, so the floating
    /// label shows the real title and whether it is already in the player's collection. Mirrors
    /// <see cref="DoorView"/> (server-authoritative entities rendered + lightly collided client-side).
    /// </summary>
    public sealed class DataCubeView : MonoBehaviour
    {
        public GameBootstrap Game;

        public static DataCubeView Instance { get; private set; }

        private sealed class Cube
        {
            public GameObject Go;
            public Transform Spin;     // the rotating/bobbing cube mesh
            public Material GlowMat;    // pulsing emissive material
            public Vector3 World;       // raw world pos (cube centre)
            public long Seed;
            public string GameKey;      // resolved from Seed via the catalogue (may be empty if catalogue empty)
        }

        private readonly Dictionary<int, Cube> _cubes = new Dictionary<int, Cube>();
        private MinigameCatalog _catalog;
        private bool _subscribed;
        private int _hummingId; // the cube we last played the proximity hum for (so it triggers once per approach)

        private const float Reach = 3.2f;   // matches the server's DataCubeReach
        private const float Size = 0.95f;

        private void Awake()
        {
            Instance = this;
            _catalog = MinigameCatalog.Load();
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.DataCubesReceived += OnCubes;
                _subscribed = true;
            }

            float t = Time.time;
            foreach (var c in _cubes.Values)
            {
                var basePos = Game != null ? Game.ScenePos(c.World.x, c.World.y, c.World.z) : c.World;
                c.Go.transform.position = basePos + Vector3.up * (0.4f + Mathf.Sin(t * 1.6f + c.Seed) * 0.12f);
                c.Spin.localRotation = Quaternion.Euler(18f, t * 40f, 0f);

                if (c.GlowMat != null)
                {
                    bool owned = Owned(c.GameKey);
                    var col = owned ? new Color(0.45f, 1f, 0.62f) : new Color(0.30f, 0.80f, 1f);
                    float pulse = 0.55f + 0.45f * Mathf.Sin(t * 3f + c.Seed);
                    c.GlowMat.SetColor(ColorId, ShaderColor.Srgb(new Color(col.r, col.g, col.b, 0.35f + 0.25f * pulse)));
                }
            }
        }

        private bool Owned(string gameKey) => !string.IsNullOrEmpty(gameKey) && Game != null && Game.UnlockedGames.Contains(gameKey);

        private void OnCubes(DataCubeList m)
        {
            var seen = new HashSet<int>();
            foreach (var nc in m.Cubes)
            {
                seen.Add(nc.Id);
                if (!_cubes.TryGetValue(nc.Id, out var c))
                {
                    _cubes[nc.Id] = Build(nc);
                }
            }

            if (_cubes.Count > seen.Count)
            {
                var stale = new List<int>();
                foreach (var id in _cubes.Keys)
                {
                    if (!seen.Contains(id)) stale.Add(id);
                }

                foreach (var id in stale)
                {
                    Destroy(_cubes[id].Go);
                    _cubes.Remove(id);
                }
            }
        }

        private Cube Build(NetDataCube nc)
        {
            var go = new GameObject($"DataCube {nc.Id}");
            go.transform.SetParent(transform, true);

            var spin = new GameObject("Spin").transform;
            spin.SetParent(go.transform, false);

            // Solid core cube — textured if a data-cube texture is bundled, else a flat tinted block.
            var core = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(core);
            core.transform.SetParent(spin, false);
            core.transform.localScale = Vector3.one * Size;
            PaintCore(core);

            // Translucent glow shell, slightly larger, pulsing each frame (the holographic aura).
            var glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(glow);
            glow.transform.SetParent(spin, false);
            glow.transform.localScale = Vector3.one * (Size * 1.25f);
            var glowMat = GlowMaterial();
            glow.GetComponent<Renderer>().sharedMaterial = glowMat;

            // A soft point light so the cube reads as a beacon in the dark.
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(go.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 6f;
            light.intensity = 1.4f;
            light.color = new Color(0.4f, 0.85f, 1f);

            // Light collision so the player bumps into it instead of walking through.
            var col = go.AddComponent<BoxCollider>();
            col.center = Vector3.up * 0.4f;
            col.size = Vector3.one * Size;

            var entry = _catalog?.GameForSeed(nc.Seed);
            return new Cube
            {
                Go = go,
                Spin = spin,
                GlowMat = glowMat,
                World = new Vector3(nc.X, nc.Y, nc.Z),
                Seed = nc.Seed,
                GameKey = entry != null ? entry.key : string.Empty,
            };
        }

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Shader _litShader, _glowShader;
        private static Texture2D _cubeTex;
        private static bool _texTried;

        private static void PaintCore(GameObject go)
        {
            if (_litShader == null) _litShader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            if (!_texTried) { _cubeTex = Resources.Load<Texture2D>("props/data_cube"); _texTried = true; }

            var mat = new Material(_litShader) { color = ShaderColor.Srgb(new Color(0.16f, 0.36f, 0.5f)) };
            if (_cubeTex != null && mat.HasProperty("_MainTex")) mat.mainTexture = _cubeTex;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static Material GlowMaterial()
        {
            if (_glowShader == null) _glowShader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(_glowShader);
            mat.SetColor(ColorId, ShaderColor.Srgb(new Color(0.30f, 0.80f, 1f, 0.4f)));
            mat.renderQueue = 3000;
            return mat;
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
        }

        /// <summary>The nearest data cube within reach of a point, for the player's E-download. Returns the cube
        /// id (0 if none), the game key it holds, and whether the player already owns that game.</summary>
        public int NearestDataCube(Vector3 worldPos, float reach, out string gameKey, out bool owned)
        {
            int best = 0; float bestSq = reach * reach; gameKey = string.Empty; owned = false;
            foreach (var kv in _cubes)
            {
                var c = kv.Value;
                Vector3 scene = Game != null ? Game.ScenePos(c.World.x, c.World.y, c.World.z) : c.World;
                float sq = (scene - worldPos).sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq; best = kv.Key; gameKey = c.GameKey; owned = Owned(c.GameKey);
                }
            }

            return best;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || Game == null || ScreenLabelLayer.Instance == null) return;

            int near = NearestDataCube(Game.PlayerPosition, Reach + 0.6f, out _, out bool owned);
            if (near == 0 || !_cubes.TryGetValue(near, out var c)) { _hummingId = 0; return; }

            // Gentle hum once per approach (when the nearest cube changes).
            if (_hummingId != near)
            {
                _hummingId = near;
                ClientAudio.Instance?.At("data_cube_hum", c.Go.transform.position, 1f, 0.6f);
            }

            // The cube reads as a data fragment to recover — the contents stay a surprise until collected.
            string label = owned
                ? (Game.Localizer?.Get("ui.datacube.owned") ?? "✓ Fragment recovered")
                : (Game.Localizer?.Get("ui.datacube.prompt") ?? "E: Extract fragment");

            ScreenLabelLayer.Instance.World(cam, c.Go.transform.position + Vector3.up * 1.3f, label,
                owned ? UiKit.Ok : UiKit.Cyan);
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null) Game.Network.DataCubesReceived -= OnCubes;
            if (Instance == this) Instance = null;
        }
    }
}
