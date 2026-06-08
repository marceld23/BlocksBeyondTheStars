using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// A self-contained, live ship preview rendered into a <see cref="RenderTexture"/> for the in-game menu's
    /// Ship paint tab (item 32). It builds the same little textured hull as the flight view (iron_wall body +
    /// wings, glass cockpit, carbon engine) at an isolated far-away spot, lit by its own short-range point light,
    /// and renders it with a dedicated camera — so nothing in the game world is touched. Recolour the hull live
    /// with <see cref="SetHullColor"/>; the model slowly rotates while active.
    /// </summary>
    public sealed class ShipPreviewRig : MonoBehaviour
    {
        public RenderTexture Texture { get; private set; }

        private Camera _cam;
        private Transform _model;
        private Material _hullMat;
        private bool _active;

        // Far from any terrain/player so the main camera never sees it and the point light touches nothing else.
        private static readonly Vector3 Origin = new Vector3(0f, 100000f, 0f);

        public void EnsureBuilt(Color hull)
        {
            if (_model != null)
            {
                return;
            }

            Texture = new RenderTexture(480, 420, 16) { name = "ShipPreviewRT" };

            var camGo = new GameObject("ShipPreviewCam");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.targetTexture = Texture;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.04f, 0.07f, 0.12f, 1f);
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 30f;
            _cam.transform.position = Origin + new Vector3(2.6f, 2.0f, 4.4f); // a three-quarter view of the hull
            _cam.transform.rotation = Quaternion.Euler(16f, 210f, 0f);

            var lightGo = new GameObject("ShipPreviewLight");
            lightGo.transform.SetParent(transform, false);
            var lamp = lightGo.AddComponent<Light>();
            lamp.type = LightType.Point;       // localized — won't light the rest of the scene
            lamp.range = 16f;
            lamp.intensity = 1.5f;
            lamp.transform.position = Origin + new Vector3(2f, 3f, 3.5f);

            _model = new GameObject("ShipPreviewModel").transform;
            _model.SetParent(transform, false);
            _model.position = Origin;

            _hullMat = LitTex("iron_wall", hull);
            var glass = LitTex("glass", new Color(0.7f, 0.9f, 1f));
            var engine = LitTex("carbon", new Color(0.78f, 0.78f, 0.82f));

            // Same silhouette as SpaceView.BuildShip so the preview reads as the real flight ship.
            Cube("Body", _model, new Vector3(0f, 0f, 0f), new Vector3(1.6f, 0.9f, 3.4f), _hullMat);
            Cube("WingL", _model, new Vector3(-1.3f, 0f, -0.3f), new Vector3(1.2f, 0.2f, 1.4f), _hullMat);
            Cube("WingR", _model, new Vector3(1.3f, 0f, -0.3f), new Vector3(1.2f, 0.2f, 1.4f), _hullMat);
            Cube("Cockpit", _model, new Vector3(0f, 0.5f, 1.2f), new Vector3(0.9f, 0.6f, 1.0f), glass);
            Cube("Engine", _model, new Vector3(0f, 0f, -1.9f), new Vector3(1.0f, 0.7f, 0.5f), engine);

            SetActive(false);
        }

        /// <summary>Retints the hull live as the player cycles the colour.</summary>
        public void SetHullColor(Color hull)
        {
            if (_hullMat != null)
            {
                _hullMat.color = hull;
            }
        }

        /// <summary>Enables/disables rendering (the camera only draws while the paint tab is showing).</summary>
        public void SetActive(bool on)
        {
            _active = on;
            if (_cam != null)
            {
                _cam.enabled = on;
            }

            if (_model != null)
            {
                _model.gameObject.SetActive(on); // hide the model when inactive so the avatar camera can't pick up the ship (B53)
            }
        }

        private void Update()
        {
            if (_active && _model != null)
            {
                _model.Rotate(0f, Time.deltaTime * 26f, 0f, Space.World);
            }
        }

        private void OnDestroy()
        {
            if (Texture != null)
            {
                Texture.Release();
                Destroy(Texture);
            }
        }

        // --- Local copies of the flight-view material/cube helpers (kept self-contained like AvatarPreviewRig) ---

        private static Material LitTex(string texKey, Color tint)
        {
            var shader = Shader.Find("Spacecraft/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = tint };
            var tex = LoadTex(texKey);
            if (tex != null)
            {
                m.mainTexture = tex;
                m.mainTextureScale = new Vector2(2f, 2f);
            }

            return m;
        }

        private static Texture2D LoadTex(string key)
        {
            var asset = Resources.Load<TextAsset>("textures/" + key);
            if (asset == null || asset.bytes.Length != 64 * 64 * 4)
            {
                return null;
            }

            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            tex.LoadRawTextureData(asset.bytes);
            tex.Apply();
            return tex;
        }

        private static void Cube(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
