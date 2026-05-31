using System.Collections.Generic;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Real space view (M25b). When the server reports the player is in space, this builds a
    /// lightweight space scene far above the world (black sky + starfield + a planet + a blocky
    /// flyable ship + the server's entities) and takes over the camera. The ship is flyable
    /// (WASD + mouse) in third-person or cockpit view (V cycles). A launch sequence plays on
    /// entry; pressing L (or "Return to surface") flies you home with a landing sequence.
    /// On-foot control is frozen meanwhile. Presentation only; combat stays server-authoritative.
    /// </summary>
    public sealed class SpaceView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private static readonly Vector3 SceneOrigin = new Vector3(0f, 6000f, 0f);
        private const float SeqDuration = 1.6f;
        private const float MoveSpeed = 14f;
        private const float LookSpeed = 2.2f;
        private const float Bounds = 130f;

        private enum Phase { Launch, Cruise, Landing }

        private GameObject _root;
        private GameObject _ship;
        private readonly Dictionary<string, GameObject> _entities = new Dictionary<string, GameObject>();

        private Transform _camPrevParent;
        private Vector3 _camPrevLocalPos;
        private Quaternion _camPrevLocalRot;
        private float _camPrevFar;
        private CameraClearFlags _camPrevClear;
        private Color _camPrevBg;

        private bool _active;
        private Phase _phase;
        private int _viewMode; // 0 = third-person, 1 = cockpit
        private float _seq;
        private float _yaw, _pitch;

        private void Update()
        {
            if (Game == null || Camera == null)
            {
                return;
            }

            if (Game.InSpace && !_active)
            {
                Enter();
            }

            if (!_active)
            {
                return;
            }

            // Server says we left → fly the landing sequence, then tear down.
            if (!Game.InSpace && _phase != Phase.Landing)
            {
                _phase = Phase.Landing;
                _seq = 0f;
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                _viewMode = 1 - _viewMode;
            }

            SyncEntities();

            switch (_phase)
            {
                case Phase.Launch: UpdateSequence(rising: true); break;
                case Phase.Landing: UpdateSequence(rising: false); break;
                default: UpdateCruise(); break;
            }

            PlaceCamera();
        }

        private void UpdateSequence(bool rising)
        {
            _seq += Time.deltaTime;
            float t = Mathf.Clamp01(_seq / SeqDuration);
            float ease = rising ? 1f - (1f - t) * (1f - t) : t * t;
            float y = rising ? Mathf.Lerp(-40f, 0f, ease) : Mathf.Lerp(0f, -40f, ease);
            if (_ship != null)
            {
                _ship.transform.localPosition = new Vector3(0f, y, 0f);
                _ship.transform.localRotation = Quaternion.identity;
            }

            if (_seq >= SeqDuration)
            {
                if (rising)
                {
                    _phase = Phase.Cruise;
                }
                else
                {
                    Exit();
                }
            }
        }

        private void UpdateCruise()
        {
            if (_ship == null)
            {
                return;
            }

            // L (or the Space-tab "Return to surface") asks the server to leave → triggers landing.
            if (Input.GetKeyDown(KeyCode.L))
            {
                Game.Network?.SendLeaveSpace();
            }

            _yaw += Input.GetAxis("Mouse X") * LookSpeed;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * LookSpeed, -80f, 80f);
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            _ship.transform.localRotation = rot;

            float fwd = Input.GetAxis("Vertical");
            float strafe = Input.GetAxis("Horizontal");
            var move = rot * (Vector3.forward * fwd + Vector3.right * strafe) * (MoveSpeed * Time.deltaTime);
            var pos = _ship.transform.localPosition + move;
            if (pos.magnitude > Bounds)
            {
                pos = pos.normalized * Bounds;
            }

            _ship.transform.localPosition = pos;
        }

        private void PlaceCamera()
        {
            if (_ship == null)
            {
                return;
            }

            var sp = _ship.transform.localPosition;
            var rot = _ship.transform.localRotation;
            if (_viewMode == 1)
            {
                // Cockpit: just ahead of the ship, facing its heading.
                Camera.transform.localPosition = sp + rot * new Vector3(0f, 0.7f, 1.9f);
                Camera.transform.localRotation = rot;
            }
            else
            {
                // Third-person: behind and above, looking at the ship.
                Camera.transform.localPosition = sp + rot * new Vector3(0f, 4.5f, -13f);
                Camera.transform.localRotation = Quaternion.LookRotation((sp + Vector3.up * 0.5f) - Camera.transform.localPosition, Vector3.up);
            }
        }

        private void Enter()
        {
            _active = true;
            _phase = Phase.Launch;
            _seq = 0f;
            _yaw = 0f;
            _pitch = 0f;
            Game.SpaceViewActive = true;

            BuildScene();

            _camPrevParent = Camera.transform.parent;
            _camPrevLocalPos = Camera.transform.localPosition;
            _camPrevLocalRot = Camera.transform.localRotation;
            _camPrevFar = Camera.farClipPlane;
            _camPrevClear = Camera.clearFlags;
            _camPrevBg = Camera.backgroundColor;

            Camera.transform.SetParent(_root.transform, false);
            Camera.farClipPlane = 3000f;
            Camera.clearFlags = CameraClearFlags.SolidColor;     // black space, not the blue sky
            Camera.backgroundColor = new Color(0.02f, 0.03f, 0.06f);
        }

        private void Exit()
        {
            Camera.transform.SetParent(_camPrevParent, false);
            Camera.transform.localPosition = _camPrevLocalPos;
            Camera.transform.localRotation = _camPrevLocalRot;
            Camera.farClipPlane = _camPrevFar;
            Camera.clearFlags = _camPrevClear;
            Camera.backgroundColor = _camPrevBg;

            if (_root != null)
            {
                Destroy(_root);
                _root = null;
            }

            _entities.Clear();
            _ship = null;
            _active = false;
            Game.SpaceViewActive = false;
        }

        private void BuildScene()
        {
            _root = new GameObject("SpaceScene");
            _root.transform.position = SceneOrigin;

            var star = Unlit(new Color(0.9f, 0.95f, 1f));
            var rng = new System.Random(1234);
            for (int i = 0; i < 260; i++)
            {
                var dir = new Vector3(
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1)).normalized;
                Cube("Star", _root.transform, dir * 280f, Vector3.one * (1.4f + (float)rng.NextDouble() * 2f), star);
            }

            var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planet.name = "Planet";
            StripCollider(planet);
            planet.transform.SetParent(_root.transform, false);
            planet.transform.localPosition = new Vector3(20f, -120f, 70f);
            planet.transform.localScale = Vector3.one * 160f;
            planet.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.25f, 0.45f, 0.65f));

            // A second, distant planet so space doesn't look empty.
            var planet2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planet2.name = "Planet2";
            StripCollider(planet2);
            planet2.transform.SetParent(_root.transform, false);
            planet2.transform.localPosition = new Vector3(-160f, 60f, 220f);
            planet2.transform.localScale = Vector3.one * 60f;
            planet2.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.7f, 0.5f, 0.35f));

            _ship = BuildShip(_root.transform);
        }

        private GameObject BuildShip(Transform parent)
        {
            var ship = new GameObject("Ship");
            ship.transform.SetParent(parent, false);

            var hull = Unlit(new Color(0.6f, 0.62f, 0.68f));
            var glass = Unlit(new Color(0.5f, 0.85f, 0.95f));
            var engine = Unlit(new Color(0.9f, 0.55f, 0.2f));

            Cube("Body", ship.transform, new Vector3(0f, 0f, 0f), new Vector3(1.6f, 0.9f, 3.4f), hull);
            Cube("WingL", ship.transform, new Vector3(-1.3f, 0f, -0.3f), new Vector3(1.2f, 0.2f, 1.4f), hull);
            Cube("WingR", ship.transform, new Vector3(1.3f, 0f, -0.3f), new Vector3(1.2f, 0.2f, 1.4f), hull);
            Cube("Cockpit", ship.transform, new Vector3(0f, 0.5f, 1.2f), new Vector3(0.9f, 0.6f, 1.0f), glass);
            Cube("Engine", ship.transform, new Vector3(0f, 0f, -1.9f), new Vector3(1.0f, 0.7f, 0.5f), engine);
            return ship;
        }

        private void SyncEntities()
        {
            var space = Game.Space;
            var seen = new HashSet<string>();
            if (space != null)
            {
                foreach (var e in space.Entities)
                {
                    seen.Add(e.Id);
                    if (!_entities.TryGetValue(e.Id, out var go))
                    {
                        go = Cube("Entity", _root.transform, Vector3.zero, EntityScale(e.Kind), Unlit(EntityColor(e.Kind)));
                        _entities[e.Id] = go;
                    }

                    go.transform.localPosition = new Vector3(e.X, e.Y, e.Z);
                }
            }

            if (_entities.Count > seen.Count)
            {
                var stale = new List<string>();
                foreach (var id in _entities.Keys)
                {
                    if (!seen.Contains(id))
                    {
                        stale.Add(id);
                    }
                }

                foreach (var id in stale)
                {
                    Destroy(_entities[id]);
                    _entities.Remove(id);
                }
            }
        }

        private void OnGUI()
        {
            if (!_active)
            {
                return;
            }

            if (_phase != Phase.Cruise)
            {
                float t = Mathf.Clamp01(_seq / SeqDuration);
                float alpha = _phase == Phase.Landing ? t : 1f - t;
                if (alpha > 0.01f)
                {
                    var prev = GUI.color;
                    GUI.color = new Color(0f, 0f, 0f, alpha);
                    GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                    GUI.color = prev;
                }
            }
            else
            {
                var loc = Game.Localizer;
                string hint = loc != null ? loc.Get("ui.space.controls") : "WASD/Mouse fly · V view · L land";
                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(Screen.width / 2f - 250, Screen.height - 28, 500, 22), hint, style);
            }
        }

        private void OnDestroy()
        {
            if (_active)
            {
                Exit();
            }
        }

        private static Vector3 EntityScale(string kind) => kind switch
        {
            "Asteroid" => Vector3.one * 2.4f,
            "Ufo" => new Vector3(2.4f, 0.7f, 2.4f),
            "Cruiser" => new Vector3(3f, 1.5f, 5f),
            _ => Vector3.one * 1.1f,
        };

        private static Color EntityColor(string kind) => kind switch
        {
            "Asteroid" => new Color(0.45f, 0.42f, 0.38f),
            "Ufo" => new Color(0.6f, 0.35f, 0.8f),
            "Cruiser" => new Color(0.7f, 0.3f, 0.3f),
            _ => new Color(0.85f, 0.2f, 0.2f),
        };

        private static GameObject Cube(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        private static Material Unlit(Color c)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Spacecraft/VertexColorOpaque");
            return new Material(shader) { color = c };
        }
    }
}
