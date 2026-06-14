using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders placed beam blocks (teleporter pads): the beam_block voxel itself is drawn + collided by the chunk
    /// mesher, so this only adds the flavour the voxel can't — a glowing cyan column + soft light + idle hum above
    /// each pad, the pad's name floating over it, and the transient beam-column flash at both ends of a jump
    /// (<see cref="BeamFx"/>). Mirrors <see cref="DataCubeView"/>. The player walks onto a pad and presses E to open
    /// the transporter (<see cref="NearestUsableBeam"/> + PlayerController + <see cref="BeamPadUi"/>).
    /// </summary>
    public sealed class BeamView : MonoBehaviour
    {
        public GameBootstrap Game;

        public static BeamView Instance { get; private set; }

        private sealed class Pad
        {
            public GameObject Go;
            public Material GlowMat;   // pulsing emissive column material
            public Vector3 World;      // pad block centre (cell.X+0.5, cell.Y, cell.Z+0.5)
            public int Id;
        }

        private readonly Dictionary<int, Pad> _pads = new Dictionary<int, Pad>();
        private bool _subscribed;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Shader _glowShader;
        private static AudioClip _idleClip;
        private static bool _idleTried;

        private static readonly Color Cyan = new Color(0.28f, 0.85f, 1f);

        private void Awake() => Instance = this;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.BeamsReceived += OnBeams;
                Game.Network.BeamFxReceived += OnBeamFx;
                _subscribed = true;
                OnBeams(new BeamList { Beams = Game.Beams }); // seed from whatever arrived before we subscribed
            }

            float t = Time.time;
            foreach (var p in _pads.Values)
            {
                var basePos = Game != null ? Game.ScenePos(p.World.x, p.World.y, p.World.z) : p.World;
                // Column base sits on the pad's top face (cell.Y + 1); centred half its height above that.
                p.Go.transform.position = basePos + Vector3.up * (1f + ColumnHalfHeight);
                p.Go.transform.localRotation = Quaternion.Euler(0f, t * 50f, 0f);

                if (p.GlowMat != null)
                {
                    float pulse = 0.55f + 0.45f * Mathf.Sin(t * 3f + p.Id);
                    p.GlowMat.SetColor(ColorId, ShaderColor.Srgb(new Color(Cyan.r, Cyan.g, Cyan.b, 0.30f + 0.30f * pulse)));
                }
            }
        }

        private const float ColumnHalfHeight = 1.2f;

        private void OnBeams(BeamList m)
        {
            var beams = m?.Beams ?? System.Array.Empty<NetBeam>();
            var seen = new HashSet<int>();
            foreach (var nb in beams)
            {
                seen.Add(nb.Id);
                if (_pads.TryGetValue(nb.Id, out var existing))
                {
                    existing.World = new Vector3(nb.X, nb.Y, nb.Z);
                }
                else
                {
                    _pads[nb.Id] = Build(nb);
                }
            }

            if (_pads.Count > seen.Count)
            {
                var stale = new List<int>();
                foreach (var id in _pads.Keys)
                {
                    if (!seen.Contains(id)) stale.Add(id);
                }

                foreach (var id in stale)
                {
                    Destroy(_pads[id].Go);
                    _pads.Remove(id);
                }
            }
        }

        private Pad Build(NetBeam nb)
        {
            var go = new GameObject($"BeamPad {nb.Id}");
            go.transform.SetParent(transform, true);

            // Glowing cyan column above the pad (the mesher draws the solid block itself).
            var column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            StripCollider(column);
            column.transform.SetParent(go.transform, false);
            column.transform.localScale = new Vector3(0.38f, ColumnHalfHeight, 0.38f);
            var glowMat = GlowMaterial();
            column.GetComponent<Renderer>().sharedMaterial = glowMat;

            // A soft point light so the pad reads as active in the dark.
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(go.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 7f;
            light.intensity = 1.5f;
            light.color = Cyan;

            // A soft looping idle hum at the pad (only if the bundled clip exists).
            var clip = IdleClip();
            if (clip != null)
            {
                var src = go.AddComponent<AudioSource>();
                src.clip = clip;
                src.loop = true;
                src.spatialBlend = 1f;
                src.minDistance = 2f;
                src.maxDistance = 14f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.volume = 0.28f;
                src.Play();
            }

            return new Pad { Go = go, GlowMat = glowMat, World = new Vector3(nb.X, nb.Y, nb.Z), Id = nb.Id };
        }

        /// <summary>The nearest beam block the local player may use within range of a point (own or allied), for the
        /// E-press that opens the transporter. Returns the beam id (0 if none).</summary>
        public int NearestUsableBeam(Vector3 worldPos, float range)
        {
            if (Game?.Beams == null)
            {
                return 0;
            }

            int best = 0;
            float bestSq = range * range;
            foreach (var b in Game.Beams)
            {
                if (!Game.CanUseBeam(b))
                {
                    continue;
                }

                Vector3 scene = Game.ScenePos(b.X, b.Y, b.Z) + Vector3.up; // aim at the pad's top face
                float sq = (scene - worldPos).sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = b.Id;
                }
            }

            return best;
        }

        private void OnBeamFx(BeamFx m)
        {
            if (Game == null)
            {
                return;
            }

            SpawnFlash(Game.ScenePos(m.FromX, m.FromY, m.FromZ));
            SpawnFlash(Game.ScenePos(m.ToX, m.ToY, m.ToZ));
            ClientAudio.Instance?.At("beam_teleport", Game.ScenePos(m.ToX, m.ToY, m.ToZ), 1f, 0.85f);
        }

        /// <summary>A bright, short-lived beam column at a pad, marking a departure/arrival. Fades + self-destroys.</summary>
        private void SpawnFlash(Vector3 padTopBase)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            StripCollider(go);
            go.transform.SetParent(transform, true);
            go.transform.position = padTopBase + Vector3.up * (1f + 1.6f);
            go.transform.localScale = new Vector3(0.5f, 1.6f, 0.5f);
            var mat = GlowMaterial();
            mat.SetColor(ColorId, ShaderColor.Srgb(new Color(Cyan.r, Cyan.g, Cyan.b, 0.9f)));
            go.GetComponent<Renderer>().sharedMaterial = mat;

            var lightGo = new GameObject("Flash");
            lightGo.transform.SetParent(go.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = 3f;
            light.color = Cyan;

            go.AddComponent<BeamFlashFade>();
        }

        private static Material GlowMaterial()
        {
            if (_glowShader == null) _glowShader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(_glowShader);
            mat.SetColor(ColorId, ShaderColor.Srgb(new Color(Cyan.r, Cyan.g, Cyan.b, 0.4f)));
            mat.renderQueue = 3000;
            return mat;
        }

        private static AudioClip IdleClip()
        {
            if (!_idleTried)
            {
                _idleClip = Resources.Load<AudioClip>("audio/beam_idle");
                _idleTried = true;
            }

            return _idleClip;
        }

        private static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            var labels = ScreenLabelLayer.Instance;
            if (cam == null || Game == null || labels == null || Game.SpaceViewActive || Game.MenuOpen || Game.Beams == null)
            {
                return;
            }

            foreach (var b in Game.Beams)
            {
                string name = string.IsNullOrEmpty(b.Name) ? (Game.Localizer?.Get("ui.beam.default") ?? "Beam Block") : b.Name;
                var pos = Game.ScenePos(b.X, b.Y, b.Z) + Vector3.up * 2.6f;
                labels.World(cam, pos, name, Cyan);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.BeamsReceived -= OnBeams;
                Game.Network.BeamFxReceived -= OnBeamFx;
            }

            if (Instance == this) Instance = null;
        }
    }

    /// <summary>Fades a one-shot beam-flash column out over a short life, then destroys it.</summary>
    internal sealed class BeamFlashFade : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private float _life = 0.7f;
        private float _age;
        private Material _mat;
        private Light _light;
        private float _baseIntensity;

        private void Start()
        {
            _mat = GetComponent<Renderer>()?.sharedMaterial;
            _light = GetComponentInChildren<Light>();
            if (_light != null) _baseIntensity = _light.intensity;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float k = Mathf.Clamp01(1f - _age / _life);
            if (_mat != null)
            {
                var c = _mat.GetColor(ColorId);
                _mat.SetColor(ColorId, new Color(c.r, c.g, c.b, c.a * k));
            }

            if (_light != null) _light.intensity = _baseIntensity * k;
            transform.localScale = new Vector3(0.5f + (1f - k) * 0.6f, transform.localScale.y, 0.5f + (1f - k) * 0.6f);
            if (_age >= _life) Destroy(gameObject);
        }
    }
}
