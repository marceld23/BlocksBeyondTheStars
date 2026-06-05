using System.Collections.Generic;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// A blocky humanoid avatar built from cubes in code (M23b; proportions + jointed limbs overhaul).
    /// The body has a head + neck, a tapered torso (chest/abdomen/pelvis), and two-segment arms and legs
    /// that bend at the elbow/knee. Limbs hang from shoulder/hip <b>pivots</b> so the avatar animates
    /// procedurally: a speed-scaled walk/run cycle with knee + elbow bends, an idle sway with a slow
    /// look-around, a jump/fall tuck, and a tool/weapon <see cref="Swing"/> chop. Per-part colours come
    /// from <see cref="ClientSettings"/> (or explicit colours for remotes / NPCs); equipped gear and a
    /// held item are layered on via <see cref="SetGear"/> / <see cref="SetHeldItem"/>.
    /// </summary>
    public sealed class PlayerAvatar : MonoBehaviour
    {
        private const float SwingDuration = 0.42f;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private Material _skin, _torso, _arms, _legs;

        private Transform _head, _armL, _armR, _legL, _legR;
        private Transform _elbowL, _elbowR, _kneeL, _kneeR, _handR;
        private readonly List<GameObject> _gear = new List<GameObject>();
        private GameObject _held;
        private bool _visible = true;

        private float _phase;     // per-instance offset so avatars don't move in lockstep
        private Vector3 _lastPos;
        private float _prevY;
        private bool _hasPrev;
        private float _walkPhase;
        private float _swingTimer;

        public void Build(ClientSettings s) => Build(s.SkinColor, s.TorsoColor, s.ArmColor, s.LegColor);

        public void Build(Color skin, Color torso, Color arms, Color legs)
        {
            EnsureTextures();
            _phase = (GetInstanceID() & 0x3ff) * 0.11f;
            _skin = Lit(skin, _skinTex);
            _torso = Lit(torso, _suitTex);
            _arms = Lit(arms, _suitTex);
            _legs = Lit(legs, _suitTex);

            // Torso, tapered: pelvis → abdomen → wider chest.
            AddCube("Pelvis", transform, new Vector3(0f, 0.97f, 0f), new Vector3(0.46f, 0.22f, 0.28f), _legs);
            AddCube("Abdomen", transform, new Vector3(0f, 1.18f, 0f), new Vector3(0.46f, 0.26f, 0.30f), _torso);
            AddCube("Chest", transform, new Vector3(0f, 1.45f, 0.0f), new Vector3(0.58f, 0.40f, 0.34f), _torso);
            AddCube("ShoulderL", transform, new Vector3(-0.30f, 1.55f, 0f), new Vector3(0.18f, 0.18f, 0.30f), _torso);
            AddCube("ShoulderR", transform, new Vector3(0.30f, 1.55f, 0f), new Vector3(0.18f, 0.18f, 0.30f), _torso);

            // Neck + head + a dark visor strip on the front.
            AddCube("Neck", transform, new Vector3(0f, 1.69f, 0f), new Vector3(0.18f, 0.14f, 0.18f), _skin);
            _head = AddCube("Head", transform, new Vector3(0f, 1.86f, 0f), new Vector3(0.46f, 0.46f, 0.46f), _skin).transform;
            AddCube("Visor", _head, new Vector3(0f, 0.03f, 0.24f), new Vector3(0.40f, 0.15f, 0.06f), Lit(new Color(0.12f, 0.5f, 0.62f), _visorTex));

            // Eyes so the face reads (settlement NPCs especially, who go without helmets).
            var eyeWhite = Lit(new Color(0.93f, 0.94f, 0.98f), null);
            var pupil = Lit(new Color(0.06f, 0.06f, 0.09f), null);
            AddCube("EyeL", _head, new Vector3(-0.09f, 0.045f, 0.25f), new Vector3(0.10f, 0.07f, 0.03f), eyeWhite);
            AddCube("EyeR", _head, new Vector3(0.09f, 0.045f, 0.25f), new Vector3(0.10f, 0.07f, 0.03f), eyeWhite);
            AddCube("PupilL", _head, new Vector3(-0.09f, 0.045f, 0.266f), new Vector3(0.045f, 0.05f, 0.02f), pupil);
            AddCube("PupilR", _head, new Vector3(0.09f, 0.045f, 0.266f), new Vector3(0.045f, 0.05f, 0.02f), pupil);

            // Jointed arms (shoulder → elbow → hand) and legs (hip → knee → foot).
            _armL = AddArm("ArmLeft", -0.32f, out _elbowL, out _);
            _armR = AddArm("ArmRight", 0.32f, out _elbowR, out _handR);
            _legL = AddLeg("LegLeft", -0.13f, out _kneeL);
            _legR = AddLeg("LegRight", 0.13f, out _kneeR);
        }

        private Transform AddArm(string name, float x, out Transform elbow, out Transform hand)
        {
            var shoulder = NewPivot(name, transform, new Vector3(x, 1.5f, 0f));
            AddCube(name + "Upper", shoulder, new Vector3(0f, -0.21f, 0f), new Vector3(0.16f, 0.42f, 0.16f), _arms);
            elbow = NewPivot(name + "Elbow", shoulder, new Vector3(0f, -0.42f, 0f));
            AddCube(name + "Lower", elbow, new Vector3(0f, -0.21f, 0f), new Vector3(0.15f, 0.42f, 0.15f), _arms);
            hand = NewPivot(name + "Hand", elbow, new Vector3(0f, -0.44f, 0f));
            AddCube(name + "HandMesh", hand, new Vector3(0f, -0.06f, 0f), new Vector3(0.2f, 0.16f, 0.2f), _skin);
            return shoulder;
        }

        private Transform AddLeg(string name, float x, out Transform knee)
        {
            var hip = NewPivot(name, transform, new Vector3(x, 0.92f, 0f));
            AddCube(name + "Upper", hip, new Vector3(0f, -0.23f, 0f), new Vector3(0.22f, 0.46f, 0.22f), _legs);
            knee = NewPivot(name + "Knee", hip, new Vector3(0f, -0.46f, 0f));
            AddCube(name + "Lower", knee, new Vector3(0f, -0.21f, 0f), new Vector3(0.2f, 0.42f, 0.2f), _legs);
            AddCube(name + "Foot", knee, new Vector3(0f, -0.44f, 0.06f), new Vector3(0.24f, 0.12f, 0.32f), _legs);
            return hip;
        }

        private static Transform NewPivot(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
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
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.enabled = _visible;
            _renderers.Add(r);
            return go;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || _armR == null)
            {
                return;
            }

            // Self-drive from world movement so it works for the local player, remotes and NPCs alike.
            var pos = transform.position;
            float speed = 0f, vy = 0f;
            if (_hasPrev)
            {
                var d = pos - _lastPos;
                vy = (pos.y - _prevY) / dt;
                d.y = 0f;
                speed = d.magnitude / dt;
            }

            _lastPos = pos;
            _prevY = pos.y;
            _hasPrev = true;

            float moving = Mathf.Clamp01(speed / 3.2f); // legs reach a full stride a bit sooner (slow NPC strolls read as walking)
            float run = Mathf.Clamp01((speed - 4.5f) / 4f); // extra emphasis once sprinting
            _walkPhase += dt * (4f + speed * 1.4f);
            float t = Time.time + _phase;

            float swing = Mathf.Sin(_walkPhase) * Mathf.Lerp(2f, 40f, moving);
            float armL = -swing, armR = swing, legL = swing, legR = -swing;
            float kneeL = 0f, kneeR = 0f, elbowL = 12f + run * 25f, elbowR = 12f + run * 25f;
            float headYaw = 0f;

            bool airborne = Mathf.Abs(vy) > 2.6f;
            bool idle = moving < 0.03f && !airborne;

            if (airborne)
            {
                // Jump/fall tuck: legs up + bent, arms raised a little.
                legL = legR = -38f;
                kneeL = kneeR = 55f;
                armL = -48f; armR = -48f;
                elbowL = elbowR = 35f;
            }
            else if (idle)
            {
                float breath = Mathf.Sin(Time.time * 1.5f) * 2.5f; // idle sway
                armL = -breath; armR = breath; legL = 0f; legR = 0f;
                headYaw = Mathf.Sin(t * 0.5f) * 9f;                 // slow look-around
            }
            else
            {
                // Walk/run: the recovering leg bends at the knee, arms counter-swing with elbow bend.
                kneeL = Mathf.Max(0f, Mathf.Sin(_walkPhase)) * Mathf.Lerp(10f, 45f, moving);
                kneeR = Mathf.Max(0f, -Mathf.Sin(_walkPhase)) * Mathf.Lerp(10f, 45f, moving);
                elbowL += Mathf.Max(0f, -Mathf.Sin(_walkPhase)) * 22f * moving;
                elbowR += Mathf.Max(0f, Mathf.Sin(_walkPhase)) * 22f * moving;
            }

            // Tool/weapon chop overrides the right arm: raise, then drive down (with an elbow snap).
            if (_swingTimer > 0f)
            {
                _swingTimer -= dt;
                float c = 1f - Mathf.Clamp01(_swingTimer / SwingDuration);
                armR = Mathf.Lerp(-115f, 30f, Mathf.SmoothStep(0f, 1f, c));
                elbowR = 20f + 45f * Mathf.Sin(Mathf.Clamp01(c) * Mathf.PI);
            }

            _armL.localRotation = Quaternion.Euler(armL, 0f, 0f);
            _armR.localRotation = Quaternion.Euler(armR, 0f, 0f);
            _legL.localRotation = Quaternion.Euler(legL, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(legR, 0f, 0f);
            if (_elbowL != null) _elbowL.localRotation = Quaternion.Euler(elbowL, 0f, 0f);
            if (_elbowR != null) _elbowR.localRotation = Quaternion.Euler(elbowR, 0f, 0f);
            if (_kneeL != null) _kneeL.localRotation = Quaternion.Euler(-kneeL, 0f, 0f); // bends the lower leg back
            if (_kneeR != null) _kneeR.localRotation = Quaternion.Euler(-kneeR, 0f, 0f);
            if (_head != null) _head.localRotation = Quaternion.Euler(0f, headYaw, 0f);
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

        /// <summary>Shows the held tool/weapon/block in the right hand (call only when it changes).</summary>
        public void SetHeldItem(HeldItem.Kind kind, Color tint)
        {
            if (_handR == null)
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

            _held = HeldItem.Build(_handR, kind, tint);
            if (_held != null)
            {
                _held.transform.localPosition = new Vector3(0f, -0.1f, 0.06f); // in the palm, pointing forward
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

        /// <summary>
        /// Layers equipped gear over the body: a helmet shell, a chest plate, leg plates, a back
        /// pack/tank and a helmet lamp. Rebuilds the gear set from the flags (cheap; only on change).
        /// </summary>
        public void SetGear(bool helmet, bool chest, bool legs, bool pack, bool lamp = false)
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
                _gear.Add(AddCube("GearHelmet", _head, new Vector3(0f, 0.04f, -0.02f), new Vector3(0.54f, 0.5f, 0.54f), plate));
            }

            if (chest)
            {
                _gear.Add(AddCube("GearChest", transform, new Vector3(0f, 1.45f, 0.02f), new Vector3(0.62f, 0.42f, 0.38f), plate));
            }

            if (legs)
            {
                _gear.Add(AddCube("GearLegL", _legL, new Vector3(0f, -0.22f, 0f), new Vector3(0.28f, 0.46f, 0.28f), plate));
                _gear.Add(AddCube("GearLegR", _legR, new Vector3(0f, -0.22f, 0f), new Vector3(0.28f, 0.46f, 0.28f), plate));
            }

            if (pack)
            {
                _gear.Add(AddCube("GearPack", transform, new Vector3(0f, 1.4f, -0.24f), new Vector3(0.4f, 0.5f, 0.2f), packMat));
            }

            if (lamp)
            {
                // A small bright lamp on the side of the helmet (the actual light cone is the suit lamp).
                _gear.Add(AddCube("GearLamp", _head, new Vector3(0.25f, 0.05f, 0.12f), new Vector3(0.1f, 0.1f, 0.12f),
                    Lit(new Color(1f, 0.96f, 0.7f), null)));
            }
        }

        /// <summary>Re-applies the per-part colours (e.g. after the player changed them in settings).</summary>
        public void ApplyColors(ClientSettings s) => ApplyColors(s.SkinColor, s.TorsoColor, s.ArmColor, s.LegColor);

        /// <summary>Re-applies explicit per-part colours (used by the avatar designer preview).</summary>
        public void ApplyColors(Color skin, Color torso, Color arms, Color legs)
        {
            if (_skin == null)
            {
                return;
            }

            _skin.color = skin;
            _torso.color = torso;
            _arms.color = arms;
            _legs.color = legs;
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
