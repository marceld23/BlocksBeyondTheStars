// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// First-person viewmodel: the held tool/weapon/block shown in the lower-right of the camera (the
    /// avatar itself is hidden in first person). Bobs with movement, sways on look, and jabs forward on
    /// a <see cref="Swing"/> (mine / place / attack). Built from the shared <see cref="HeldItem"/> mesh
    /// and parented to the camera so it tracks the view. Hidden in third-person.
    /// <para><b>Two hands</b> — a second, mirrored holder on the left (<c>_holderL</c>) is used for exactly two things,
    /// so every other item stays one-handed: the glove weapons (#2278; idle fists, the energy gloves' left-right jabs, the
    /// shock gloves' wind-up and two-handed palm push) and climbing (#2287). While the player climbs a wall or a ladder the
    /// held item sinks out of view and both suit hands come up and climb hand over hand, driven like the avatar's climb
    /// pose (#2193): the rhythm follows the climb speed, a tired grip reaches shorter and trembles, a spent grip drags
    /// both hands down, a pull-up presses them on the ledge; worn climbing gloves show their orange pads. Leaving the wall
    /// lowers the hands and brings the item back. No cost when unused: the left holder and the climb hands stay inactive.</para>
    /// </summary>
    public sealed class Viewmodel : MonoBehaviour
    {
        private const float SwingDuration = 0.42f;

        /// <summary>#2278: the energy gloves' jab and the shock gloves' wind-up + push + spring-back.</summary>
        private const float JabDuration = 0.30f;
        private const float PushDuration = 0.47f;
        private const float PushWindUp = 0.12f / PushDuration; // phase ends of the push, 0..1
        private const float PushPeak = 0.27f / PushDuration;

        /// <summary>#2287: seconds the held item takes to sink away (or come back), and the hands to come up.</summary>
        private const float ClimbSwapSeconds = 0.18f;

        /// <summary>How far down a sunk item / a lowered hand sits, in camera space.</summary>
        private const float SinkDepth = 0.42f;

        private static readonly Vector3 RestPos = new Vector3(0.26f, -0.22f, 0.52f);
        private static readonly Vector3 RestEuler = new Vector3(6f, -10f, 4f);

        /// <summary>Held-item mesh scale. 0.9 filled about a quarter of the screen height with a block in hand and
        /// added to the "everything is huge" feel of first person; 20 % smaller (#1591).</summary>
        private const float ItemScale = 0.72f;

        /// <summary>The vertical field of view the rest pose was tuned at. The holder is scaled by
        /// tan(fov/2) / tan(ReferenceFov/2) so the held item keeps the same screen size and corner whatever the
        /// player's FOV setting (#1590/#1591) — a wide view would otherwise leave a tiny tool, a narrow one a
        /// giant fist. Only the base FOV drives this (see <see cref="SetReferenceFov"/>); the walking kick and the
        /// binocular zoom do not resize the hands.</summary>
        private const float ReferenceFov = 60f;
        private float _fovScale = 1f;

        private Transform _holder;
        private Transform _holderL; // #2278/#2287: the left hand — gloves and climbing only
        private HeldItem.Kind _kind = HeldItem.Kind.None;
        private bool _visible = true;

        private float _swingTimer;
        private float _swingDuration = SwingDuration;
        private float _bobPhase;
        private Vector3 _lastPos;
        private bool _hasPrev;

        // #2278 gloves.
        private bool _shockGloves;  // the shock gloves push with both palms; the energy gloves jab left and right
        private bool _punchLeft;    // which hand the energy gloves' current (or last) jab is
        private GameObject _meshL;  // the left glove

        // #2287 climbing.
        private bool _climbing;
        private float _climbStrain;
        private bool _climbSliding;
        private bool _climbPullUp;
        private float _climbBlend;    // 0 = item in hand … 0.5 = item gone … 1 = both hands up on the wall
        private float _climbPhase;    // the hand-over-hand rhythm
        private bool _climbGloves, _climbClaws;
        private bool _climbHandsDirty = true;
        private GameObject _climbR, _climbL;

        private void EnsureHolder()
        {
            if (_holder != null)
            {
                return;
            }

            var go = new GameObject("Viewmodel");
            _holder = go.transform;
            _holder.SetParent(transform, false); // transform = the camera
            _holder.localPosition = Compensated(RestPos);
            _holder.localEulerAngles = RestEuler;
            _holder.localScale = Vector3.one * _fovScale;
        }

        /// <summary>The left holder (#2278/#2287), built on first use — only gloves and climbing ever need it.</summary>
        private void EnsureLeftHolder()
        {
            EnsureHolder();
            if (_holderL != null)
            {
                return;
            }

            var go = new GameObject("ViewmodelLeft");
            _holderL = go.transform;
            _holderL.SetParent(transform, false);
            _holderL.localPosition = Compensated(Mirrored(RestPos));
            _holderL.localEulerAngles = MirroredEuler(RestEuler);
            _holderL.localScale = Vector3.one * _fovScale;
            _holderL.gameObject.SetActive(false);
        }

        private static Vector3 Mirrored(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        private static Vector3 MirroredEuler(Vector3 e) => new Vector3(e.x, -e.y, -e.z);

        /// <summary>Base field of view of the owning camera (degrees); re-scales the holder so the held item
        /// stays the same size on screen. Called by the controller at start and on every settings change.</summary>
        public void SetReferenceFov(float fov)
        {
            _fovScale = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(ReferenceFov * 0.5f * Mathf.Deg2Rad);
            if (_holder != null)
            {
                _holder.localScale = Vector3.one * _fovScale;
                _holder.localPosition = Compensated(RestPos);
            }

            if (_holderL != null)
            {
                _holderL.localScale = Vector3.one * _fovScale;
                _holderL.localPosition = Compensated(Mirrored(RestPos));
            }
        }

        /// <summary>A camera-local offset with x/y scaled to the FOV factor and depth kept: a point at (x, y, z)
        /// lands on the same screen position when x/y grow by the same factor as tan(fov/2).</summary>
        private Vector3 Compensated(Vector3 local) => new Vector3(local.x * _fovScale, local.y * _fovScale, local.z);

        /// <summary>Sets the held item (rebuilds only when it changes — call from the controller).</summary>
        public void SetHeldItem(HeldItem.Kind kind, Color tint, string blockKey = null, string itemKey = null,
            System.Collections.Generic.IReadOnlyList<BlocksBeyondTheStars.Shared.Definitions.HeldModelPart> look = null)
        {
            EnsureHolder();
            _kind = kind;

            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }

            if (_meshL != null)
            {
                Destroy(_meshL);
                _meshL = null;
            }

            var mesh = HeldItem.Build(_holder, kind, tint, blockKey, itemKey, look);
            if (mesh != null)
            {
                mesh.transform.localScale = Vector3.one * ItemScale;
            }

            _mesh = mesh;

            _shockGloves = false;
            if (kind == HeldItem.Kind.Gloves)
            {
                // #2278: the left glove is the mirror image of the right one, in the second holder.
                EnsureLeftHolder();
                _meshL = HeldItem.Build(_holderL, kind, tint, blockKey, itemKey, look, left: true);
                if (_meshL != null)
                {
                    _meshL.transform.localScale = Vector3.one * ItemScale;
                }

                _shockGloves = FxLook.ForItem(Game != null ? Game.Content : null, itemKey).Is(BlocksBeyondTheStars.Shared.Definitions.FxStyles.ShockPush);
            }

            _climbHandsDirty = true; // a new arm colour or painting comes in through here too (#1464)
            _swingTimer = 0f;
            ApplyVisible();
        }

        private GameObject _mesh;

        /// <summary>The tip of the held item on screen (gun barrel / tool bit) in world space, so shots and beams leave
        /// the weapon and not the screen centre (#2151). False while the viewmodel is hidden or empty. The gloves (#2278)
        /// report the spot their blow peaks at: between both palms for the shock push, in front of the jabbing fist for the
        /// energy gloves — call it after <see cref="Swing"/>, which picks the hand.</summary>
        public bool TryMuzzle(out Vector3 world)
        {
            world = default;
            if (_holder == null || !_holder.gameObject.activeInHierarchy || _climbBlend >= 0.5f)
            {
                return false;
            }

            if (_kind == HeldItem.Kind.Gloves)
            {
                var peak = _shockGloves ? new Vector3(0f, -0.12f, 0.78f) : new Vector3(_punchLeft ? -0.07f : 0.07f, -0.11f, 0.82f);
                world = transform.TransformPoint(Compensated(peak));
                return true;
            }

            return HeldItem.MuzzleOf(_mesh, out world);
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

        /// <summary>#2287: the player climbs a wall or a ladder (the same signals the avatar's climb pose gets):
        /// <paramref name="strain"/> 0..1 shortens the reach and makes the hands tremble, <paramref name="sliding"/> drags
        /// them down, <paramref name="pullUp"/> presses them on the ledge.</summary>
        public void SetClimbing(bool climbing, float strain = 0f, bool sliding = false, bool pullUp = false)
        {
            _climbing = climbing;
            _climbStrain = climbing ? Mathf.Clamp01(strain) : 0f;
            _climbSliding = climbing && sliding;
            _climbPullUp = climbing && pullUp;
        }

        /// <summary>#2287: the worn climbing gear — its orange pads (and the claws) show on both climbing hands.</summary>
        public void SetClimbGear(bool gloves, bool claws)
        {
            if (gloves != _climbGloves || claws != _climbClaws)
            {
                _climbGloves = gloves;
                _climbClaws = claws;
                _climbHandsDirty = true;
            }
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
                    ApplyVisible();
                }

                return;
            }

            // On an EVA the suit shows the held tool (so you can see what you build/mine with). The on-foot
            // controller is frozen out here, so self-refresh the held item from the selected hotbar slot.
            if (_eva)
            {
                _hiddenForSpace = false;
                _climbing = false; // no wall climbing out in space
                string key = Game.ItemInSlot(Game.SelectedHotbarSlot) ?? string.Empty;
                if (key != _evaKey)
                {
                    _evaKey = key;
                    var (k, t, bk) = HeldItem.For(Game.Content, key);
                    SetHeldItem(k, t, bk, key); // builds the holder if needed + rebuilds the mesh
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

        /// <summary>Whether the climbing hands are the ones on screen (the item has sunk away).</summary>
        private bool HandsUp => _climbBlend >= 0.5f;

        private void ApplyVisible()
        {
            bool shown = (_visible || _eva) && !_hiddenForSpace;
            bool climbShown = _climbBlend > 0f;
            if (_holder != null)
            {
                _holder.gameObject.SetActive(shown && (_kind != HeldItem.Kind.None || climbShown));
            }

            if (_holderL != null)
            {
                _holderL.gameObject.SetActive(shown && ((_kind == HeldItem.Kind.Gloves && !HandsUp) || (climbShown && HandsUp)));
            }

            SetActive(_mesh, !HandsUp);
            SetActive(_meshL, !HandsUp);
            SetActive(_climbR, HandsUp);
            SetActive(_climbL, HandsUp);
        }

        private static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
            {
                go.SetActive(active);
            }
        }

        public void Swing()
        {
            if (_swingTimer > 0f || HandsUp)
            {
                return; // a swing in progress plays out; on the wall both hands are busy (#2287)
            }

            if (_kind == HeldItem.Kind.Gloves)
            {
                // #2278: the energy gloves alternate left and right; the shock gloves always push with both palms.
                _punchLeft = !_shockGloves && !_punchLeft;
                _swingDuration = _shockGloves ? PushDuration : JabDuration;
            }
            else
            {
                _swingDuration = SwingDuration;
            }

            _swingTimer = _swingDuration;
        }

        /// <summary>(Re)builds the two climbing hands when the suit colour, the painting or the climbing gear changed.
        /// True when it built them.</summary>
        private bool EnsureClimbHands()
        {
            if (!_climbHandsDirty && _climbR != null && _climbL != null)
            {
                return false;
            }

            EnsureLeftHolder();
            if (_climbR != null)
            {
                Destroy(_climbR);
            }

            if (_climbL != null)
            {
                Destroy(_climbL);
            }

            var tint = HeldItem.For(null, null).tint; // the suit's arm colour, like the bare hand (#1033)
            _climbR = HeldItem.BuildClimbHand(_holder, tint, left: false, _climbGloves, _climbClaws);
            _climbL = HeldItem.BuildClimbHand(_holderL, tint, left: true, _climbGloves, _climbClaws);
            _climbR.transform.localScale = Vector3.one * ItemScale;
            _climbL.transform.localScale = Vector3.one * ItemScale;
            _climbHandsDirty = false;
            return true;
        }

        private void LateUpdate()
        {
            if (_holder == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
            {
                return;
            }

            // Movement speed from the camera's world position (drives the walk bob and the climb rhythm).
            var pos = transform.position;
            float speed = 0f, vy = 0f;
            if (_hasPrev)
            {
                var d = pos - _lastPos;
                vy = d.y / dt;
                d.y = 0f;
                speed = d.magnitude / dt;
            }

            _lastPos = pos;
            _hasPrev = true;

            // #2287: the item sinks away, then the hands come up (and the other way round when the climb ends).
            bool wantClimb = _climbing && _visible && !_eva && !_hiddenForSpace;
            bool wasUp = HandsUp, wasShown = _climbBlend > 0f;
            _climbBlend = Mathf.MoveTowards(_climbBlend, wantClimb ? 1f : 0f, dt / (2f * ClimbSwapSeconds));
            bool rebuilt = _climbBlend > 0f && EnsureClimbHands();
            if (rebuilt || HandsUp != wasUp || (_climbBlend > 0f) != wasShown)
            {
                ApplyVisible(); // also hides freshly built hands until the item has sunk away
            }

            if (!_holder.gameObject.activeSelf)
            {
                return;
            }

            if (HandsUp)
            {
                // The hands rise from below during the second half of the swap.
                float rise = Mathf.SmoothStep(0f, 1f, (_climbBlend - 0.5f) * 2f);
                PoseClimbHand(_holder, +1f, dt, vy, speed, rise);
                if (_holderL != null)
                {
                    PoseClimbHand(_holderL, -1f, 0f, vy, speed, rise); // the rhythm advanced once, above
                }

                return;
            }

            float moving = Mathf.Clamp01(speed / 5f);
            _bobPhase += dt * (5f + speed * 1.6f);

            var bob = new Vector3(
                Mathf.Cos(_bobPhase) * 0.012f * moving,
                Mathf.Sin(_bobPhase * 2f) * 0.012f * moving - 0.004f * moving,
                0f);

            var rot = RestEuler;
            var posOff = RestPos + bob;
            var rotL = MirroredEuler(RestEuler);
            var posL = Mirrored(RestPos) + new Vector3(-bob.x, bob.y, 0f); // one shared bob, mirrored across

            // Attack pose — shaped by the held item (B14): blades slash in an arc, guns kick back, the rest jab.
            if (_swingTimer > 0f)
            {
                _swingTimer -= dt;
                float t = 1f - Mathf.Clamp01(_swingTimer / _swingDuration);
                float jab = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI); // 0→1→0

                if (_kind == HeldItem.Kind.Gloves)
                {
                    GlovePose(t, ref posOff, ref rot, ref posL, ref rotL);
                }
                else if (_kind == HeldItem.Kind.Blade)
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
                else if (_kind == HeldItem.Kind.Hand)
                {
                    // Bare hand: a straight punch. The forearm is an open-ended stump that sits below and
                    // right of the frustum at its shallow depths — the generic jab's 55° pitch used to swing
                    // that open rear end up into view ("the arm ends at the back", #1428). Drive the fist
                    // forward with only a light wrist tilt so the rear stays off-screen.
                    posOff += new Vector3(-0.04f, -0.02f, 0.16f) * jab;
                    rot += new Vector3(9f * jab, -7f * jab, -5f * jab);
                }
                else
                {
                    // Tools / drill / block: a forward-down jab.
                    posOff += new Vector3(-0.05f, -0.06f, 0.12f) * jab;
                    rot += new Vector3(55f * jab, -8f * jab, 0f);
                }
            }

            // #2287: the first half of the swap sinks the item (and the left glove) out of view, tipping it forward.
            float sink = Mathf.SmoothStep(0f, 1f, _climbBlend * 2f);
            posOff.y -= SinkDepth * sink;
            rot.x += 35f * sink;
            posL.y -= SinkDepth * sink;
            rotL.x += 35f * sink;

            _holder.localPosition = Compensated(posOff);
            _holder.localEulerAngles = rot;
            if (_holderL != null && _kind == HeldItem.Kind.Gloves)
            {
                _holderL.localPosition = Compensated(posL);
                _holderL.localEulerAngles = rotL;
            }
        }

        /// <summary>
        /// #2278: the two glove attacks, as offsets on both rest poses (<paramref name="t"/> runs 0..1 over the swing).
        /// <list type="bullet">
        /// <item><b>Energy gloves</b> — a straight jab of one fist forward and toward the middle, with a light wrist tilt
        /// (like the bare-hand punch, #1428, so the forearm's open rear stays off-screen); the other fist pulls back a
        /// little into its guard. The punching hand alternates with every swing.</item>
        /// <item><b>Shock gloves</b> — a short wind-up (both hands back and apart), then both palms drive forward and
        /// meet in the middle, tilted up so the emitter discs face ahead (the shockwave ring starts between them), then
        /// spring back.</item>
        /// </list>
        /// </summary>
        private void GlovePose(float t, ref Vector3 posR, ref Vector3 rotR, ref Vector3 posL, ref Vector3 rotL)
        {
            if (!_shockGloves)
            {
                float jab = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                var strike = new Vector3(-0.19f, 0.11f, 0.24f) * jab;
                var strikeRot = new Vector3(8f, -7f, -5f) * jab;
                var guard = new Vector3(0.01f, 0.02f, -0.03f) * jab;
                if (_punchLeft)
                {
                    posL += Mirrored(strike);
                    rotL += MirroredEuler(strikeRot);
                    posR += guard;
                }
                else
                {
                    posR += strike;
                    rotR += strikeRot;
                    posL += Mirrored(guard);
                }

                return;
            }

            // The shock push: wind up, push, spring back.
            Vector3 off;
            float pitch;
            var windUp = new Vector3(0.02f, -0.01f, -0.06f);
            var push = new Vector3(-0.15f, 0.10f, 0.20f);
            if (t < PushWindUp)
            {
                float w = Mathf.SmoothStep(0f, 1f, t / PushWindUp);
                off = windUp * w;
                pitch = 6f * w;
            }
            else if (t < PushPeak)
            {
                float p = Mathf.SmoothStep(0f, 1f, (t - PushWindUp) / (PushPeak - PushWindUp));
                off = Vector3.Lerp(windUp, push, p);
                pitch = Mathf.Lerp(6f, -20f, p);
            }
            else
            {
                float r = Mathf.SmoothStep(0f, 1f, (t - PushPeak) / (1f - PushPeak));
                off = Vector3.Lerp(push, Vector3.zero, r);
                pitch = Mathf.Lerp(-20f, 0f, r);
            }

            var turn = new Vector3(pitch, -8f * Mathf.Max(0f, off.z / push.z), 0f); // the palms turn in a little as they meet
            posR += off;
            rotR += turn;
            posL += Mirrored(off);
            rotL += MirroredEuler(turn);
        }

        /// <summary>
        /// #2287: one climbing hand (<paramref name="side"/> +1 right, −1 left), camera-local, in the avatar's rhythm
        /// (#2193 <c>PlayerAvatar.PoseClimb</c>): vertical plus sideways travel advance the hand-over-hand phase, the hands
        /// take turns reaching up while the other pulls down; strain shortens the reach and makes them tremble; a slide
        /// drags both down the wall in short slips; a pull-up presses both palms down on the ledge.
        /// <paramref name="rise"/> 0..1 brings the hand up from below the view during the swap.
        /// </summary>
        private void PoseClimbHand(Transform holder, float side, float dt, float vy, float speed, float rise)
        {
            Vector3 p;
            Vector3 e;
            float time = Time.time;
            if (_climbPullUp)
            {
                // Both palms flat on the ledge, pressing down as the body swings up and over.
                float press = Mathf.Sin(time * 7f) * 0.008f;
                p = new Vector3(side * 0.17f, -0.17f + press, 0.42f);
                e = new Vector3(14f, -side * 6f, 0f);
            }
            else if (_climbSliding)
            {
                // The spent grip: the palms scrape down the wall in short slips and catch again.
                float slip = Mathf.Repeat((time * 2.6f) + (side > 0f ? 0f : 0.5f), 1f);
                float jitter = Mathf.Sin((time * 41f) + side) * 0.006f;
                p = new Vector3((side * 0.22f) + jitter, 0.02f - (0.08f * slip), 0.46f);
                e = new Vector3(-72f, -side * 6f, -side * 4f);
            }
            else
            {
                float travel = Mathf.Abs(vy) + speed;
                float active = Mathf.Clamp01(travel / 1.2f);
                _climbPhase += dt * travel * 3.2f; // the avatar's rate: both views keep one rhythm
                float wave = Mathf.Sin(_climbPhase);
                float lift = side < 0f ? wave : -wave; // the left hand reaches while the right one pulls, and back
                float reach = Mathf.Lerp(0.10f, 0.055f, _climbStrain) * active; // a tired climber reaches shorter
                float breath = Mathf.Sin((time * 1.4f) + side) * 0.006f * (1f - active);
                p = new Vector3(side * 0.24f, -0.07f + (lift * reach) + breath + (side < 0f ? 0.015f : 0f),
                    0.47f + (Mathf.Max(0f, lift) * 0.04f * active));
                e = new Vector3(-58f - (lift * 14f * active), -side * 8f, -side * 6f);
                if (_climbStrain > 0.01f)
                {
                    float shake = _climbStrain * 0.005f;
                    p += new Vector3(Mathf.Sin((time * 33f) + side) * shake, Mathf.Sin((time * 37f) + (2f * side)) * shake, 0f);
                }
            }

            p.y -= SinkDepth * (1f - rise);
            holder.localPosition = Compensated(p);
            holder.localEulerAngles = e;
        }
    }
}
