using System.Collections.Generic;
using Spacecraft.Networking.Messages;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Real space view (M25b). When the server reports the player is in space, this builds a
    /// lightweight space scene far above the world (starfield + planet sphere + a blocky ship +
    /// the server's entities) and takes over the camera with two modes — third-person and
    /// cockpit (V cycles). A launch sequence plays on entry and a landing sequence on exit
    /// (with a fade). The on-foot controller is frozen meanwhile (GameBootstrap.SpaceViewActive).
    /// Presentation only; combat/movement stay server-authoritative.
    /// </summary>
    public sealed class SpaceView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private static readonly Vector3 SceneOrigin = new Vector3(0f, 6000f, 0f);

        private GameObject _root;
        private GameObject _ship;
        private readonly Dictionary<string, GameObject> _entities = new Dictionary<string, GameObject>();

        private Transform _camPrevParent;
        private Vector3 _camPrevLocalPos;
        private Quaternion _camPrevLocalRot;
        private float _camPrevFar;

        private bool _active;
        private int _viewMode; // 0 = third-person, 1 = cockpit
        private float _seq;    // sequence timer
        private bool _landing; // true while playing the landing (exit) sequence
        private const float SeqDuration = 1.6f;

        private void Update()
        {
            if (Game == null || Camera == null)
            {
                return;
            }

            // Enter space.
            if (Game.InSpace && !_active && !_landing)
            {
                Enter();
            }

            // Begin landing when the server says we left (but keep the view until the sequence ends).
            if (!Game.InSpace && _active && !_landing)
            {
                _landing = true;
                _seq = 0f;
            }

            if (!_active)
            {
                return;
            }

            _seq += Time.deltaTime;

            if (Input.GetKeyDown(KeyCode.V))
            {
                _viewMode = 1 - _viewMode;
            }

            SyncEntities();
            AnimateAndPlaceCamera();

            if (_landing && _seq >= SeqDuration)
            {
                Exit();
            }
        }

        private void Enter()
        {
            _active = true;
            _landing = false;
            _seq = 0f;
            Game.SpaceViewActive = true;

            BuildScene();

            // Take over the camera.
            _camPrevParent = Camera.transform.parent;
            _camPrevLocalPos = Camera.transform.localPosition;
            _camPrevLocalRot = Camera.transform.localRotation;
            _camPrevFar = Camera.farClipPlane;
            Camera.farClipPlane = 3000f;
            Camera.transform.SetParent(_root.transform, false);
        }

        private void Exit()
        {
            // Restore the camera to the on-foot rig.
            Camera.transform.SetParent(_camPrevParent, false);
            Camera.transform.localPosition = _camPrevLocalPos;
            Camera.transform.localRotation = _camPrevLocalRot;
            Camera.farClipPlane = _camPrevFar;

            if (_root != null)
            {
                Destroy(_root);
                _root = null;
            }

            _entities.Clear();
            _ship = null;
            _active = false;
            _landing = false;
            Game.SpaceViewActive = false;
        }

        private void BuildScene()
        {
            _root = new GameObject("SpaceScene");
            _root.transform.position = SceneOrigin;

            // Starfield: many tiny unlit cubes on a far shell.
            var star = Unlit(new Color(0.9f, 0.95f, 1f));
            var rng = new System.Random(1234);
            for (int i = 0; i < 220; i++)
            {
                var dir = new Vector3(
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1)).normalized;
                var s = Cube("Star", _root.transform, dir * 250f, Vector3.one * (1.2f + (float)rng.NextDouble() * 1.6f), star);
                s.transform.SetParent(_root.transform, false);
                s.transform.localPosition = dir * 250f;
            }

            // Planet below.
            var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planet.name = "Planet";
            StripCollider(planet);
            planet.transform.SetParent(_root.transform, false);
            planet.transform.localPosition = new Vector3(0f, -110f, 60f);
            planet.transform.localScale = Vector3.one * 150f;
            planet.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.25f, 0.45f, 0.65f));

            // The player's ship (blocky, code-built).
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

        private void AnimateAndPlaceCamera()
        {
            // Launch: ship rises from the planet; landing: descends back. Eased.
            float t = Mathf.Clamp01(_seq / SeqDuration);
            float ease = _landing ? t * t : 1f - (1f - t) * (1f - t);
            float shipY = _landing ? Mathf.Lerp(0f, -40f, ease) : Mathf.Lerp(-40f, 0f, ease);
            if (_ship != null)
            {
                _ship.transform.localPosition = new Vector3(0f, shipY, 0f);
            }

            Vector3 shipPos = _ship != null ? _ship.transform.localPosition : Vector3.zero;
            if (_viewMode == 1)
            {
                // Cockpit: just ahead of the ship, looking forward.
                Camera.transform.localPosition = shipPos + new Vector3(0f, 0.9f, 1.8f);
                Camera.transform.localRotation = Quaternion.identity;
            }
            else
            {
                // Third-person: behind and above, looking at the ship.
                Camera.transform.localPosition = shipPos + new Vector3(0f, 5f, -13f);
                Camera.transform.localRotation = Quaternion.LookRotation((shipPos + Vector3.up * 0.5f) - Camera.transform.localPosition, Vector3.up);
            }
        }

        private void OnGUI()
        {
            if (!_active)
            {
                return;
            }

            // Launch/landing fade.
            float t = Mathf.Clamp01(_seq / SeqDuration);
            float alpha = _landing ? t : 1f - t;
            if (alpha > 0.01f)
            {
                var prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, alpha);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = prev;
            }
        }

        private void OnDestroy()
        {
            if (_active)
            {
                Exit();
            }
        }

        // --- helpers ---

        private static Vector3 EntityScale(string kind) => kind switch
        {
            "Asteroid" => Vector3.one * 2.4f,
            "Ufo" => new Vector3(2.4f, 0.7f, 2.4f),
            "Cruiser" => new Vector3(3f, 1.5f, 5f),
            _ => Vector3.one * 1.1f, // Drone
        };

        private static Color EntityColor(string kind) => kind switch
        {
            "Asteroid" => new Color(0.45f, 0.42f, 0.38f),
            "Ufo" => new Color(0.6f, 0.35f, 0.8f),
            "Cruiser" => new Color(0.7f, 0.3f, 0.3f),
            _ => new Color(0.85f, 0.2f, 0.2f), // Drone
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
