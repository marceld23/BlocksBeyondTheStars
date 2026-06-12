using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Mining/placing feedback (M27 polish): a wireframe selection box on the block the player is
    /// looking at, and a small debris burst when a block is mined or placed. Code-built (12 thin
    /// edge cubes + cube particles) on the always-included Unlit/Color shader — no assets, no new
    /// shader. Render-only; the server stays authoritative over the actual block changes.
    /// </summary>
    public sealed class MiningFx : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public float Reach = 6f;

        private GameObject _outline;
        private Material _outlineMat;
        private Material _digMat;
        private Material _placeMat;
        private bool _subscribed;

        private void Start()
        {
            _outlineMat = Mat(new Color(0.05f, 0.05f, 0.06f));
            _digMat = Mat(new Color(0.65f, 0.58f, 0.48f));
            _placeMat = Mat(new Color(0.80f, 0.85f, 0.95f));
            _outline = BuildWireCube(_outlineMat);
            _outline.SetActive(false);
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.BlockChanged += OnBlock;
                Game.Network.MiningProgressReceived += OnMineProgress;
                _subscribed = true;
            }

            UpdateOutline();
        }

        private void UpdateOutline()
        {
            if (_outline == null || Camera == null || Game == null || Game.MenuOpen || Game.SpaceViewActive)
            {
                if (_outline != null)
                {
                    _outline.SetActive(false);
                }

                return;
            }

            var ray = new Ray(Camera.transform.position, Camera.transform.forward);
            if (Physics.Raycast(ray, out var hit, Reach))
            {
                var inside = hit.point - hit.normal * 0.5f;
                int bx = Mathf.FloorToInt(inside.x), by = Mathf.FloorToInt(inside.y), bz = Mathf.FloorToInt(inside.z);
                _outline.transform.position = new Vector3(bx + 0.5f, by + 0.5f, bz + 0.5f);
                _outline.SetActive(true);

                // Tint the box from dark toward hot orange as the block cracks under the drill.
                float frac = (bx == _crackX && by == _crackY && bz == _crackZ && Time.time - _crackAt < 0.4f) ? _crackFrac : 0f;
                _outlineMat.color = Color.Lerp(new Color(0.05f, 0.05f, 0.06f), new Color(1f, 0.45f, 0.12f), frac);
            }
            else
            {
                _outline.SetActive(false);
            }
        }

        private int _crackX, _crackY, _crackZ;
        private float _crackFrac, _crackAt;

        private void OnMineProgress(MiningProgress m)
        {
            _crackX = m.X;
            _crackY = m.Y;
            _crackZ = m.Z;
            _crackFrac = Mathf.Clamp01(m.Fraction);
            _crackAt = Time.time;
        }

        private void OnBlock(BlockChanged m)
        {
            SpawnBurst(new Vector3(m.X + 0.5f, m.Y + 0.5f, m.Z + 0.5f), m.Block == 0 ? _digMat : _placeMat);
        }

        private void SpawnBurst(Vector3 pos, Material mat)
        {
            for (int i = 0; i < 6; i++)
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                StripCollider(p);
                p.transform.position = pos;
                p.transform.localScale = Vector3.one * 0.12f;
                p.GetComponent<Renderer>().sharedMaterial = mat;
                var fx = p.AddComponent<FxParticle>();
                fx.Vel = new Vector3(Random.Range(-1.6f, 1.6f), Random.Range(1.5f, 3.5f), Random.Range(-1.6f, 1.6f));
            }
        }

        private static GameObject BuildWireCube(Material mat)
        {
            const float t = 0.05f;   // edge thickness
            const float s = 1.04f;    // edge length (slightly proud of the block)
            var root = new GameObject("BlockOutline");

            // Twelve edges of a unit cube centred on the root: 4 along each axis.
            for (int a = 0; a < 3; a++)
            {
                for (int i = 0; i < 4; i++)
                {
                    float u = (i & 1) == 0 ? -0.5f : 0.5f;
                    float v = (i & 2) == 0 ? -0.5f : 0.5f;
                    Vector3 pos, scale;
                    if (a == 0) { pos = new Vector3(0f, u, v); scale = new Vector3(s, t, t); }
                    else if (a == 1) { pos = new Vector3(u, 0f, v); scale = new Vector3(t, s, t); }
                    else { pos = new Vector3(u, v, 0f); scale = new Vector3(t, t, s); }

                    var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    StripCollider(edge);
                    edge.transform.SetParent(root.transform, false);
                    edge.transform.localPosition = pos;
                    edge.transform.localScale = scale;
                    edge.GetComponent<Renderer>().sharedMaterial = mat;
                }
            }

            return root;
        }

        private static Material Mat(Color c)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            return new Material(shader) { color = c };
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        /// <summary>A short-lived debris cube: arcs out under gravity, shrinks, then self-destroys.</summary>
        private sealed class FxParticle : MonoBehaviour
        {
            public Vector3 Vel;

            private const float Life = 0.6f;
            private float _t;

            private void Update()
            {
                _t += Time.deltaTime;
                Vel += Vector3.down * 9f * Time.deltaTime;
                transform.position += Vel * Time.deltaTime;
                transform.localScale = Vector3.one * 0.12f * Mathf.Max(0f, 1f - _t / Life);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
