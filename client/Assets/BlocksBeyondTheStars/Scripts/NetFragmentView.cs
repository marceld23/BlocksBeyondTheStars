using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders the **net fragments** scattered on a world (<see cref="NetFragmentList"/>) — text-only story
    /// finds (distinct from the data-cube mini-games). Each hovers and spins with a category-tinted glow; the
    /// player walks up and presses E to recover it (see <see cref="NearestNetFragment"/> + PlayerController),
    /// which reveals its archive text and advances the shared story. Mirrors <see cref="DataCubeView"/>
    /// (server-authoritative entities rendered + lightly collided client-side).
    /// </summary>
    public sealed class NetFragmentView : MonoBehaviour
    {
        public GameBootstrap Game;

        public static NetFragmentView Instance { get; private set; }

        private sealed class Frag
        {
            public GameObject Go;
            public Transform Spin;
            public Material GlowMat;
            public Vector3 World;
            public string Category;
        }

        private readonly Dictionary<int, Frag> _frags = new Dictionary<int, Frag>();
        private bool _subscribed;

        private const float Reach = 3.2f; // matches the server's NetFragmentReach
        private const float Size = 0.8f;

        private void Awake() => Instance = this;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.NetFragmentsReceived += OnFragments;
                _subscribed = true;
            }

            float t = Time.time;
            foreach (var f in _frags.Values)
            {
                var basePos = Game != null ? Game.ScenePos(f.World.x, f.World.y, f.World.z) : f.World;
                f.Go.transform.position = basePos + Vector3.up * (0.4f + Mathf.Sin(t * 1.4f + f.Go.GetInstanceID()) * 0.12f);
                f.Spin.localRotation = Quaternion.Euler(20f, t * 35f, 12f);

                if (f.GlowMat != null)
                {
                    var col = CategoryColor(f.Category);
                    float pulse = 0.55f + 0.45f * Mathf.Sin(t * 2.6f + f.Go.GetInstanceID());
                    f.GlowMat.SetColor(ColorId, ShaderColor.Srgb(new Color(col.r, col.g, col.b, 0.35f + 0.25f * pulse)));
                }
            }
        }

        private void OnFragments(NetFragmentList m)
        {
            var seen = new HashSet<int>();
            foreach (var nf in m.Fragments)
            {
                seen.Add(nf.Id);
                if (!_frags.TryGetValue(nf.Id, out _))
                {
                    _frags[nf.Id] = Build(nf);
                }
            }

            if (_frags.Count > seen.Count)
            {
                var stale = new List<int>();
                foreach (var id in _frags.Keys)
                {
                    if (!seen.Contains(id)) stale.Add(id);
                }

                foreach (var id in stale)
                {
                    Destroy(_frags[id].Go);
                    _frags.Remove(id);
                }
            }
        }

        private Frag Build(NetStoryFragment nf)
        {
            var go = new GameObject($"NetFragment {nf.Id}");
            go.transform.SetParent(transform, true);

            var spin = new GameObject("Spin").transform;
            spin.SetParent(go.transform, false);

            var col = CategoryColor(nf.Category);

            // A thin, shard-like core (a flattened, tilted cube) so it reads as a recovered data shard.
            var core = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(core);
            core.transform.SetParent(spin, false);
            core.transform.localScale = new Vector3(Size * 0.35f, Size * 1.1f, Size * 0.35f);
            PaintCore(core, col);

            // Translucent glow shell, pulsing each frame.
            var glow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(glow);
            glow.transform.SetParent(spin, false);
            glow.transform.localScale = new Vector3(Size * 0.7f, Size * 1.4f, Size * 0.7f);
            var glowMat = GlowMaterial(col);
            glow.GetComponent<Renderer>().sharedMaterial = glowMat;

            // A soft point light so the shard reads as a beacon in the dark.
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(go.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 5.5f;
            light.intensity = 1.2f;
            light.color = col;

            // Light collision so the player bumps into it instead of walking through.
            var box = go.AddComponent<BoxCollider>();
            box.center = Vector3.up * 0.4f;
            box.size = Vector3.one * (Size * 0.6f);

            return new Frag { Go = go, Spin = spin, GlowMat = glowMat, World = new Vector3(nf.X, nf.Y, nf.Z), Category = nf.Category };
        }

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Shader _litShader, _glowShader;

        private static void PaintCore(GameObject go, Color tint)
        {
            if (_litShader == null) _litShader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var mat = new Material(_litShader) { color = ShaderColor.Srgb(new Color(tint.r * 0.5f, tint.g * 0.5f, tint.b * 0.6f)) };
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static Material GlowMaterial(Color tint)
        {
            if (_glowShader == null) _glowShader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(_glowShader);
            mat.SetColor(ColorId, ShaderColor.Srgb(new Color(tint.r, tint.g, tint.b, 0.4f)));
            mat.renderQueue = 3000;
            return mat;
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
        }

        /// <summary>Lore category → glow tint.</summary>
        private static Color CategoryColor(string cat)
        {
            switch (cat)
            {
                case "vega": return new Color(0.35f, 0.85f, 1f);     // cyan
                case "sps": return new Color(0.45f, 1f, 0.62f);      // green
                case "guardian": return new Color(1f, 0.40f, 0.35f); // red
                case "network": return new Color(0.45f, 0.60f, 1f);  // blue
                case "settler": return new Color(1f, 0.82f, 0.40f);  // amber
                case "netnode": return new Color(0.80f, 0.55f, 1f);  // violet
                default: return new Color(0.70f, 0.85f, 1f);
            }
        }

        /// <summary>The nearest net fragment within reach of a point, for the player's E-pickup. Returns the
        /// fragment id (0 if none) and its lore category.</summary>
        public int NearestNetFragment(Vector3 worldPos, float reach, out string category)
        {
            int best = 0; float bestSq = reach * reach; category = string.Empty;
            foreach (var kv in _frags)
            {
                var f = kv.Value;
                Vector3 scene = Game != null ? Game.ScenePos(f.World.x, f.World.y, f.World.z) : f.World;
                float sq = (scene - worldPos).sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq; best = kv.Key; category = f.Category;
                }
            }

            return best;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || Game == null || ScreenLabelLayer.Instance == null) return;

            int near = NearestNetFragment(Game.PlayerPosition, Reach + 0.6f, out _);
            if (near == 0 || !_frags.TryGetValue(near, out var f)) return;

            string label = Game.Localizer?.Get("ui.netfragment.prompt") ?? "E: Recover net fragment";
            ScreenLabelLayer.Instance.World(cam, f.Go.transform.position + Vector3.up * 1.3f, label, UiKit.Cyan);
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null) Game.Network.NetFragmentsReceived -= OnFragments;
            if (Instance == this) Instance = null;
        }
    }
}
