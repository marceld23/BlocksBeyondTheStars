using System.Collections.Generic;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// A blocky humanoid avatar (head + visor, torso, two arms with hands, two legs with feet) built
    /// from cubes in code (M23b; overhauled M27). Arms and legs hang from shoulder/hip <b>pivots</b> so
    /// the avatar animates procedurally: a walk cycle whose amplitude scales with movement speed, a
    /// gentle idle sway, and a tool/weapon <see cref="Swing"/> chop. Per-part colours come from
    /// <see cref="ClientSettings"/> (or explicit colours for remote players / NPCs) and are shown in
    /// third-person; equipped gear is layered on via <see cref="SetGear"/>.
    /// </summary>
    public sealed class PlayerAvatar : MonoBehaviour
    {
        private const float SwingDuration = 0.42f;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private Material _skin, _torso, _arms, _legs;

        private Transform _head, _armL, _armR, _legL, _legR;
        private readonly List<GameObject> _gear = new List<GameObject>();
        private GameObject _held;
        private bool _visible = true;

        private Vector3 _lastPos;
        private bool _hasPrev;
        private float _walkPhase;
        private float _swingTimer;

        public void Build(ClientSettings s) => Build(s.SkinColor, s.TorsoColor, s.ArmColor, s.LegColor);

        public void Build(Color skin, Color torso, Color arms, Color legs)
        {
            EnsureTextures();
            _skin = Lit(skin, _skinTex);
            _torso = Lit(torso, _suitTex);
            _arms = Lit(arms, _suitTex);
            _legs = Lit(legs, _suitTex);

            // Head + a dark visor strip on the front.
            _head = AddCube("Head", transform, new Vector3(0f, 1.65f, 0f), new Vector3(0.5f, 0.5f, 0.5f), _skin).transform;
            AddCube("Visor", _head, new Vector3(0f, 0.05f, 0.26f), new Vector3(0.42f, 0.16f, 0.06f), Lit(new Color(0.12f, 0.5f, 0.62f), _visorTex));

            AddCube("Torso", transform, new Vector3(0f, 1.15f, 0f), new Vector3(0.55f, 0.7f, 0.3f), _torso);

            // Limbs hang from shoulder/hip pivots so they can swing.
            _armL = AddLimb("ArmLeft", new Vector3(-0.4f, 1.5f, 0f), new Vector3(0.18f, 0.7f, 0.18f), _arms, hand: true);
            _armR = AddLimb("ArmRight", new Vector3(0.4f, 1.5f, 0f), new Vector3(0.18f, 0.7f, 0.18f), _arms, hand: true);
            _legL = AddLimb("LegLeft", new Vector3(-0.15f, 0.875f, 0f), new Vector3(0.22f, 0.85f, 0.22f), _legs, foot: true);
            _legR = AddLimb("LegRight", new Vector3(0.15f, 0.875f, 0f), new Vector3(0.22f, 0.85f, 0.22f), _legs, foot: true);
        }

        private Transform AddLimb(string name, Vector3 pivotPos, Vector3 cubeScale, Material mat, bool hand = false, bool foot = false)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(transform, false);
            pivot.localPosition = pivotPos;

            AddCube(name + "Seg", pivot, new Vector3(0f, -cubeScale.y / 2f, 0f), cubeScale, mat);
            if (hand)
            {
                AddCube(name + "Hand", pivot, new Vector3(0f, -cubeScale.y - 0.06f, 0f), new Vector3(0.2f, 0.16f, 0.2f), _skin);
            }

            if (foot)
            {
                AddCube(name + "Foot", pivot, new Vector3(0f, -cubeScale.y - 0.02f, 0.06f), new Vector3(0.24f, 0.12f, 0.32f), _legs);
            }

            return pivot;
        }

        private GameObject AddCube(string partName, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col); // visual only — must not interfere with the CharacterController
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _renderers.Add(go.GetComponent<Renderer>());
            return go;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _armR == null)
            {
                return;
            }

            // Self-drive the walk cycle from world movement so it works for the local player, remote
            // players and NPCs alike (all just move their transform).
            var pos = transform.position;
            float speed = 0f;
            if (_hasPrev)
            {
                var d = pos - _lastPos;
                d.y = 0f;
                speed = d.magnitude / dt;
            }

            _lastPos = pos;
            _hasPrev = true;

            float moving = Mathf.Clamp01(speed / 4.5f);
            _walkPhase += dt * (4f + speed * 1.4f);
            float swing = Mathf.Sin(_walkPhase) * Mathf.Lerp(2f, 36f, moving);

            float armL = -swing, armR = swing, legL = swing, legR = -swing;

            if (moving < 0.04f)
            {
                float breath = Mathf.Sin(Time.time * 1.5f) * 2.5f; // idle sway
                armL = -breath; armR = breath; legL = 0f; legR = 0f;
            }

            // Tool/weapon chop overrides the right arm: raise, then drive down.
            if (_swingTimer > 0f)
            {
                _swingTimer -= dt;
                float t = 1f - Mathf.Clamp01(_swingTimer / SwingDuration);
                armR = Mathf.Lerp(-115f, 30f, Mathf.SmoothStep(0f, 1f, t));
            }

            _armL.localRotation = Quaternion.Euler(armL, 0f, 0f);
            _armR.localRotation = Quaternion.Euler(armR, 0f, 0f);
            _legL.localRotation = Quaternion.Euler(legL, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(legR, 0f, 0f);
        }

        /// <summary>Plays a tool/weapon swing of the right arm (mining, attacking, placing). Re-calling
        /// while a swing is in progress is ignored, so holding to drill produces a continuous chop.</summary>
        public void Swing()
        {
            if (_swingTimer <= 0f)
            {
                _swingTimer = SwingDuration;
            }
        }

        /// <summary>
        /// Layers equipped gear over the body: a helmet shell, a chest plate, leg plates and a back
        /// pack/tank. Rebuilds the gear set from the flags (cheap; only call it when the set changes).
        /// </summary>
        public void SetGear(bool helmet, bool chest, bool legs, bool pack)
        {
            if (_head == null)
            {
                return;
            }

            foreach (var g in _gear)
            {
                if (g != null)
                {
                    Destroy(g);
                }
            }

            _gear.Clear();

            var plate = Lit(new Color(0.62f, 0.66f, 0.72f), _armorTex);
            var packMat = Lit(new Color(0.30f, 0.34f, 0.40f), _armorTex);

            if (helmet)
            {
                _gear.Add(AddCube("GearHelmet", _head, new Vector3(0f, 0.05f, -0.02f), new Vector3(0.6f, 0.52f, 0.6f), plate));
            }

            if (chest)
            {
                _gear.Add(AddCube("GearChest", transform, new Vector3(0f, 1.18f, 0.02f), new Vector3(0.62f, 0.64f, 0.36f), plate));
            }

            if (legs)
            {
                _gear.Add(AddCube("GearLegL", _legL, new Vector3(0f, -0.38f, 0f), new Vector3(0.28f, 0.5f, 0.28f), plate));
                _gear.Add(AddCube("GearLegR", _legR, new Vector3(0f, -0.38f, 0f), new Vector3(0.28f, 0.5f, 0.28f), plate));
            }

            if (pack)
            {
                _gear.Add(AddCube("GearPack", transform, new Vector3(0f, 1.2f, -0.22f), new Vector3(0.4f, 0.5f, 0.2f), packMat));
            }
        }

        /// <summary>Shows the currently-held tool/weapon/block in the right hand (call only when it
        /// changes). Pass <see cref="HeldItem.Kind.None"/> for empty hands. The held item swings with
        /// the arm (walk + <see cref="Swing"/>).</summary>
        public void SetHeldItem(HeldItem.Kind kind, Color tint)
        {
            if (_armR == null)
            {
                return;
            }

            if (_held != null)
            {
                Destroy(_held);
                _held = null;
            }

            if (kind == HeldItem.Kind.None)
            {
                return;
            }

            _held = HeldItem.Build(_armR, kind, tint);
            if (_held != null)
            {
                _held.transform.localPosition = new Vector3(0f, -0.78f, 0.05f); // at the hand, pointing forward
                ApplyHeldVisible();
            }
        }

        private void ApplyHeldVisible()
        {
            if (_held == null)
            {
                return;
            }

            foreach (var r in _held.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = _visible;
            }
        }

        /// <summary>Re-applies the per-part colours (e.g. after the player changed them in settings).</summary>
        public void ApplyColors(ClientSettings s)
        {
            if (_skin == null)
            {
                return;
            }

            _skin.color = s.SkinColor;
            _torso.color = s.TorsoColor;
            _arms.color = s.ArmColor;
            _legs.color = s.LegColor;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            foreach (var r in _renderers)
            {
                if (r != null)
                {
                    r.enabled = visible;
                }
            }

            ApplyHeldVisible();
        }

        // Shared (loaded once) tintable grayscale textures for the suit/armor/visor/skin.
        private static Texture2D _suitTex, _armorTex, _visorTex, _skinTex;
        private static bool _texLoaded;

        private static void EnsureTextures()
        {
            if (_texLoaded)
            {
                return;
            }

            _texLoaded = true;
            _suitTex = LoadTex("avatar_suit");
            _armorTex = LoadTex("avatar_armor");
            _visorTex = LoadTex("avatar_visor");
            _skinTex = LoadTex("avatar_skin");
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

        /// <summary>A lit, tinted, optionally-textured material (the grayscale texture tints by the colour).</summary>
        private static Material Lit(Color color, Texture2D tex)
        {
            var shader = Shader.Find("Spacecraft/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { color = color };
            if (tex != null)
            {
                m.mainTexture = tex;
            }

            return m;
        }
    }
}
