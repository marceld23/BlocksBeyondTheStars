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
        private Transform _exhaust;
        private AudioSource _engine;
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
        private float _shipSpeedMul = 1f; // cruise-speed factor from the active ship design
        private float _shipTurnMul = 1f;  // turn-rate factor from the active ship design

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
                ClientAudio.Instance?.Cue("ship_landing");
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

            // Engine loop: swells in cruise, throttle nudges the pitch.
            if (_engine != null)
            {
                _engine.volume = Mathf.MoveTowards(_engine.volume, _phase == Phase.Cruise ? 0.25f : 0.12f, Time.deltaTime * 0.5f);
                _engine.pitch = 1f + 0.2f * Mathf.Clamp01(Mathf.Abs(Input.GetAxis("Vertical")));
            }

            // Thruster exhaust stretches + flickers with throttle.
            if (_exhaust != null)
            {
                float throttle = _phase == Phase.Cruise ? Mathf.Clamp01(Input.GetAxis("Vertical")) : 0.15f;
                float len = 0.6f + throttle * 2.6f + Mathf.Sin(Time.time * 28f) * 0.12f;
                _exhaust.localScale = new Vector3(0.6f, 0.6f, len);
                _exhaust.localPosition = new Vector3(0f, 0f, -2.0f - len * 0.5f);
            }
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

            _yaw += Input.GetAxis("Mouse X") * LookSpeed * _shipTurnMul;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * LookSpeed * _shipTurnMul, -80f, 80f);
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            _ship.transform.localRotation = rot;

            float fwd = Input.GetAxis("Vertical");
            float strafe = Input.GetAxis("Horizontal");
            var move = rot * (Vector3.forward * fwd + Vector3.right * strafe) * (MoveSpeed * _shipSpeedMul * Time.deltaTime);
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

            ResolveShipFlight();
            BuildScene();

            // Launch roar + a looping engine bed for the flight.
            ClientAudio.Instance?.Cue("ship_launch");
            var engClip = Resources.Load<AudioClip>("audio/engine_idle");
            if (engClip != null)
            {
                _engine = gameObject.AddComponent<AudioSource>();
                _engine.clip = engClip;
                _engine.loop = true;
                _engine.spatialBlend = 0f;
                _engine.volume = 0f;
                _engine.Play();
            }

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

            if (_engine != null)
            {
                Destroy(_engine);
                _engine = null;
            }

            _entities.Clear();
            _ship = null;
            _exhaust = null;
            _active = false;
            Game.SpaceViewActive = false;
        }

        /// <summary>Reads the active ship's design (data/ships.json) so heavier ships fly slower and
        /// turn more sluggishly than light scouts (presentation only; combat stays server-side).</summary>
        private void ResolveShipFlight()
        {
            _shipSpeedMul = 1f;
            _shipTurnMul = 1f;

            string type = null;
            var owned = Game?.OwnedShips;
            if (owned != null)
            {
                foreach (var s in owned)
                {
                    if (s.Active)
                    {
                        type = s.Type;
                        break;
                    }
                }
            }

            var def = type != null ? Game?.Content?.GetShip(type) : null;
            if (def != null)
            {
                _shipSpeedMul = def.FlightSpeed > 0f ? def.FlightSpeed : 1f;
                _shipTurnMul = def.Handling > 0f ? def.Handling : 1f;
            }
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

            // Planets reflect the actual world/biome: the big one is the planet you're at, the
            // distant one a neighbouring body in the same system (from the star map). Lit + textured.
            ResolvePlanetLooks(out var mainLook, out var secondLook);

            var planet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planet.name = "Planet";
            StripCollider(planet);
            planet.transform.SetParent(_root.transform, false);
            planet.transform.localPosition = new Vector3(20f, -120f, 70f);
            planet.transform.localScale = Vector3.one * 160f;
            planet.GetComponent<Renderer>().sharedMaterial = Lit(mainLook.tint, LoadTex(mainLook.tex), new Vector2(6f, 3f));

            // A second, distant planet so space doesn't look empty.
            var planet2 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planet2.name = "Planet2";
            StripCollider(planet2);
            planet2.transform.SetParent(_root.transform, false);
            planet2.transform.localPosition = new Vector3(-160f, 60f, 220f);
            planet2.transform.localScale = Vector3.one * 60f;
            planet2.GetComponent<Renderer>().sharedMaterial = Lit(secondLook.tint, LoadTex(secondLook.tex), new Vector2(3f, 2f));

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

            // Glowing thruster exhaust (stretches with throttle in Update).
            var ex = Cube("Exhaust", ship.transform, new Vector3(0f, 0f, -2.4f), new Vector3(0.6f, 0.6f, 1f), Unlit(new Color(0.6f, 0.85f, 1f)));
            _exhaust = ex.transform;
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
                UiScale.Begin();
                GUI.Label(new Rect(UiScale.Width / 2f - 250, UiScale.Height - 28, 500, 22), hint, style);
                UiScale.End();
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

        /// <summary>Lit material (fixed key light) with an optional tiled block texture.</summary>
        private static Material Lit(Color c, Texture2D tex = null, Vector2 tiling = default)
        {
            var shader = Shader.Find("Spacecraft/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = c };
            if (tex != null)
            {
                m.mainTexture = tex;
                if (tiling != default)
                {
                    m.mainTextureScale = tiling;
                }
            }

            return m;
        }

        /// <summary>Loads a bundled block texture (Resources/textures/&lt;key&gt;.bytes raw 64x64 RGBA32,
        /// via LoadRawTextureData from the core module — no ImageConversion dependency).</summary>
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

        /// <summary>
        /// Picks the look of the two space-view planets from the actual world: the main planet is the
        /// body you're at (active star-map location, falling back to the current biome), the second a
        /// neighbouring body in the same system. Both reflect their planet type's colour + texture.
        /// </summary>
        private void ResolvePlanetLooks(out (Color tint, string tex) main, out (Color tint, string tex) second)
        {
            string mainType = Game?.Environment?.Biome;
            string secondType = null;

            var map = Game?.StarMap;
            if (map?.Systems != null)
            {
                object activeSys = null;
                foreach (var sys in map.Systems)
                {
                    foreach (var b in sys.Bodies)
                    {
                        if (b.Id == map.ActiveLocationId)
                        {
                            activeSys = sys;
                            if (!string.IsNullOrEmpty(b.PlanetType))
                            {
                                mainType = b.PlanetType;
                            }

                            break;
                        }
                    }

                    if (activeSys != null)
                    {
                        // Use a different body in the same system for the distant planet.
                        foreach (var b in sys.Bodies)
                        {
                            if (b.Id != map.ActiveLocationId && b.Kind != "star" && !string.IsNullOrEmpty(b.PlanetType))
                            {
                                secondType = b.PlanetType;
                                break;
                            }
                        }

                        break;
                    }
                }
            }

            main = PlanetLook(mainType);
            second = PlanetLook(secondType ?? "rock");
        }

        /// <summary>Maps a biome / planet-type key to a planet tint + a fitting block texture. Unknown
        /// keys get a stable hash-derived colour so every world type still looks distinct.</summary>
        private static (Color tint, string tex) PlanetLook(string key)
        {
            switch ((key ?? string.Empty).ToLowerInvariant())
            {
                case "jungle":
                case "forest": return (new Color(0.32f, 0.55f, 0.30f), "grass");
                case "desert": return (new Color(0.82f, 0.68f, 0.42f), "sand");
                case "ice":
                case "frozen": return (new Color(0.72f, 0.86f, 0.96f), "ice");
                case "lava":
                case "volcanic": return (new Color(0.78f, 0.32f, 0.18f), "lava");
                case "swamp": return (new Color(0.40f, 0.45f, 0.30f), "mud");
                case "crystal": return (new Color(0.55f, 0.65f, 0.92f), "crystal");
                case "ocean":
                case "water": return (new Color(0.24f, 0.46f, 0.72f), "water");
                case "barren":
                case "asteroid":
                case "rock": return (new Color(0.52f, 0.50f, 0.47f), "stone");
                default:
                    return (HashColor(key), "stone");
            }
        }

        private static Color HashColor(string key)
        {
            int h = 0;
            foreach (char c in key ?? string.Empty)
            {
                h = h * 31 + c;
            }

            var rng = new System.Random(h);
            return new Color(
                0.40f + 0.45f * (float)rng.NextDouble(),
                0.40f + 0.45f * (float)rng.NextDouble(),
                0.40f + 0.45f * (float)rng.NextDouble());
        }
    }
}
