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

        private Vector3 _lastPos;
        private bool _hasPrev;
        private float _walkPhase;
        private float _swingTimer;

        public void Build(ClientSettings s) => Build(s.SkinColor, s.TorsoColor, s.ArmColor, s.LegColor);

        public void Build(Color skin, Color torso, Color arms, Color legs)
        {
            _skin = Unlit(skin);
            _torso = Unlit(torso);
            _arms = Unlit(arms);
            _legs = Unlit(legs);

            // Head + a dark visor strip on the front.
            _head = AddCube("Head", transform, new Vector3(0f, 1.65f, 0f), new Vector3(0.5f, 0.5f, 0.5f), _skin).transform;
            AddCube("Visor", _head, new Vector3(0f, 0.05f, 0.26f), new Vector3(0.42f, 0.16f, 0.06f), Unlit(new Color(0.12f, 0.5f, 0.62f)));

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

            var plate = Unlit(new Color(0.62f, 0.66f, 0.72f));
            var packMat = Unlit(new Color(0.30f, 0.34f, 0.40f));

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
            foreach (var r in _renderers)
            {
                if (r != null)
                {
                    r.enabled = visible;
                }
            }
        }

        private static Material Unlit(Color color)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Spacecraft/VertexColorOpaque");
            return new Material(shader) { color = color };
        }
    }
}
