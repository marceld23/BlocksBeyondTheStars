using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// A self-contained, live faced-avatar preview rendered into a <see cref="RenderTexture"/> for showing in a
    /// uGUI panel (the in-game colour menu, B25). It builds a real <see cref="PlayerAvatar"/> (same faced body as
    /// the in-world figure + the avatar editor) at an isolated far-away spot, lit by its own short-range point
    /// light, and renders it with a dedicated camera — so nothing in the game world is touched. Recolour live
    /// with <see cref="SetColors"/>; the model slowly rotates while active.
    /// </summary>
    public sealed class AvatarPreviewRig : MonoBehaviour
    {
        public RenderTexture Texture { get; private set; }

        private Camera _cam;
        private PlayerAvatar _avatar;
        private Transform _model;
        private bool _active;

        // Far from any terrain/player so the main camera never sees it and the point light touches nothing else.
        private static readonly Vector3 Origin = new Vector3(0f, 100000f, 0f);

        public void EnsureBuilt(Color skin, Color torso, Color arms, Color legs)
        {
            if (_avatar != null)
            {
                return;
            }

            Texture = new RenderTexture(440, 660, 16) { name = "AvatarPreviewRT" };

            var camGo = new GameObject("AvatarPreviewCam");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.targetTexture = Texture;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.04f, 0.07f, 0.12f, 1f);
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 20f;
            // Frame the whole figure from the front with a slight 3/4 angle, viewing from the −Z side. The
            // LitColor shader keys off a FIXED light from (0.4, 0.7, −0.55) — i.e. the −Z side — so the avatar's
            // lit side is its −Z side. We view (and turn the avatar to face) that side so the FACE catches the
            // key light; viewing from +Z left the face on the shadow side and it washed out to a blank (B?).
            // Aim with LookAt so the avatar is always centred.
            _cam.transform.position = Origin + new Vector3(-0.6f, 1.35f, -3.9f);
            _cam.transform.LookAt(Origin + new Vector3(0f, 1.2f, 0f));

            var lightGo = new GameObject("AvatarPreviewLight");
            lightGo.transform.SetParent(transform, false);
            var lamp = lightGo.AddComponent<Light>();
            lamp.type = LightType.Point;       // localized — won't light the rest of the scene
            lamp.range = 14f;
            lamp.intensity = 1.5f;
            lamp.transform.position = Origin + new Vector3(-1.2f, 2.2f, 3f);

            _model = new GameObject("AvatarPreviewModel").transform;
            _model.SetParent(transform, false);
            _model.position = Origin;
            _model.localRotation = Quaternion.Euler(0f, 180f, 0f); // face the −Z camera (the lit side)
            _avatar = _model.gameObject.AddComponent<PlayerAvatar>();
            _avatar.Build(skin, torso, arms, legs);
            _avatar.SetVisible(true);

            SetActive(false);
        }

        public void SetColors(Color skin, Color torso, Color arms, Color legs) => _avatar?.ApplyColors(skin, torso, arms, legs);

        /// <summary>Enables/disables rendering. Toggles the MODEL too (not just the camera) so an inactive rig's
        /// avatar isn't left in the scene where the other preview's camera would pick it up (B53).</summary>
        public void SetActive(bool on)
        {
            _active = on;
            if (_cam != null)
            {
                _cam.enabled = on;
            }

            if (_model != null)
            {
                _model.gameObject.SetActive(on);
            }
        }

        private void Update()
        {
            if (_active && _model != null)
            {
                // Gently sway around the face-toward-camera pose (±28°) instead of a full turntable, so the face
                // is always shown to the player (the point of the preview) and never spins to the unlit back.
                float yaw = 180f + Mathf.Sin(Time.time * 0.7f) * 28f;
                _model.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        private void OnDestroy()
        {
            if (Texture != null)
            {
                Texture.Release();
                Destroy(Texture);
            }
        }
    }
}
