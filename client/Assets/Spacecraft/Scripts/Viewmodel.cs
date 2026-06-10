using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// First-person viewmodel: the held tool/weapon/block shown in the lower-right of the camera (the
    /// avatar itself is hidden in first person). Bobs with movement, sways on look, and jabs forward on
    /// a <see cref="Swing"/> (mine / place / attack). Built from the shared <see cref="HeldItem"/> mesh
    /// and parented to the camera so it tracks the view. Hidden in third-person.
    /// </summary>
    public sealed class Viewmodel : MonoBehaviour
    {
        private const float SwingDuration = 0.42f;

        private static readonly Vector3 RestPos = new Vector3(0.26f, -0.22f, 0.52f);
        private static readonly Vector3 RestEuler = new Vector3(6f, -10f, 4f);

        private Transform _holder;
        private HeldItem.Kind _kind = HeldItem.Kind.None;
        private bool _visible = true;

        private float _swingTimer;
        private float _bobPhase;
        private Vector3 _lastPos;
        private bool _hasPrev;

        private void EnsureHolder()
        {
            if (_holder != null)
            {
                return;
            }

            var go = new GameObject("Viewmodel");
            _holder = go.transform;
            _holder.SetParent(transform, false); // transform = the camera
            _holder.localPosition = RestPos;
            _holder.localEulerAngles = RestEuler;
        }

        /// <summary>Sets the held item (rebuilds only when it changes — call from the controller).</summary>
        public void SetHeldItem(HeldItem.Kind kind, Color tint, string blockKey = null)
        {
            EnsureHolder();
            _kind = kind;

            for (int i = _holder.childCount - 1; i >= 0; i--)
            {
                Destroy(_holder.GetChild(i).gameObject);
            }

            var mesh = HeldItem.Build(_holder, kind, tint, blockKey);
            if (mesh != null)
            {
                mesh.transform.localScale = Vector3.one * 0.9f;
            }

            ApplyVisible();
        }

        public GameBootstrap Game; // to hide the hand viewmodel while the space view owns the camera
        private bool _hiddenForSpace;
        private bool _eva;             // on an EVA the suit's hands DO show (the tool you build/mine with)
        private string _evaKey = "\0"; // last held item shown on EVA (self-refreshed from the hotbar)

        public void SetVisible(bool visible)
        {
            _visible = visible;
            ApplyVisible();
        }

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            _eva = Game.SpaceViewActive && Game.InEva;

            // Piloting the ship: the camera is the ship's, not the player's hands — hide the viewmodel.
            if (Game.SpaceViewActive && !_eva)
            {
                if (_holder != null)
                {
                    _hiddenForSpace = true;
                    _holder.gameObject.SetActive(false);
                }

                return;
            }

            // On an EVA the suit shows the held tool (so you can see what you build/mine with). The on-foot
            // controller is frozen out here, so self-refresh the held item from the selected hotbar slot.
            if (_eva)
            {
                _hiddenForSpace = false;
                string key = Game.ItemInSlot(Game.SelectedHotbarSlot) ?? string.Empty;
                if (key != _evaKey)
                {
                    _evaKey = key;
                    var (k, t, bk) = HeldItem.For(Game.Content, key);
                    SetHeldItem(k, t, bk); // builds the holder if needed + rebuilds the mesh
                }

                ApplyVisible();
                return;
            }

            // Back on foot: re-show what the controller last set.
            _evaKey = "\0";
            if (_hiddenForSpace)
            {
                _hiddenForSpace = false;
                ApplyVisible();
            }
        }

        private void ApplyVisible()
        {
            if (_holder != null)
            {
                _holder.gameObject.SetActive((_visible || _eva) && _kind != HeldItem.Kind.None);
            }
        }

        public void Swing()
        {
            if (_swingTimer <= 0f)
            {
                _swingTimer = SwingDuration;
            }
        }

        private void LateUpdate()
        {
            if (_holder == null || !_holder.gameObject.activeSelf)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            // Movement speed from the camera's world position (drives the walk bob).
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

            float moving = Mathf.Clamp01(speed / 5f);
            _bobPhase += dt * (5f + speed * 1.6f);

            var bob = new Vector3(
                Mathf.Cos(_bobPhase) * 0.012f * moving,
                Mathf.Sin(_bobPhase * 2f) * 0.012f * moving - 0.004f * moving,
                0f);

            var rot = RestEuler;
            var posOff = RestPos + bob;

            // Attack pose — shaped by the held item (B14): blades slash in an arc, guns kick back, the rest jab.
            if (_swingTimer > 0f)
            {
                _swingTimer -= dt;
                float t = 1f - Mathf.Clamp01(_swingTimer / SwingDuration);
                float jab = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI); // 0→1→0

                if (_kind == HeldItem.Kind.Blade)
                {
                    // A diagonal slash: the blade arcs down + sweeps across (yaw) with a wrist roll.
                    posOff += new Vector3(0.11f - 0.22f * t, -0.10f * jab, 0.08f * jab);
                    rot += new Vector3(62f * jab, Mathf.Lerp(34f, -34f, t), -42f * jab);
                }
                else if (_kind == HeldItem.Kind.Gun)
                {
                    // A sharp recoil kick: snap back + up fast, then settle.
                    float kick = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI * 0.5f) * (1f - t);
                    posOff += new Vector3(0f, 0.045f * kick, -0.11f * kick);
                    rot += new Vector3(-24f * kick, 0f, 0f);
                }
                else
                {
                    // Tools / drill / block: a forward-down jab.
                    posOff += new Vector3(-0.05f, -0.06f, 0.12f) * jab;
                    rot += new Vector3(55f * jab, -8f * jab, 0f);
                }
            }

            _holder.localPosition = posOff;
            _holder.localEulerAngles = rot;
        }
    }
}
