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
            _cam.fieldOfView = 26f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 20f;
            _cam.transform.position = Origin + new Vector3(-0.7f, 1.15f, 3.6f);
            _cam.transform.rotation = Quaternion.Euler(4f, 192f, 0f); // look at the avatar's front

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
            _avatar = _model.gameObject.AddComponent<PlayerAvatar>();
            _avatar.Build(skin, torso, arms, legs);
            _avatar.SetVisible(true);

            SetActive(false);
        }

        public void SetColors(Color skin, Color torso, Color arms, Color legs) => _avatar?.ApplyColors(skin, torso, arms, legs);

        /// <summary>Enables/disables rendering (the camera only draws while the colour menu is showing).</summary>
        public void SetActive(bool on)
        {
            _active = on;
            if (_cam != null)
            {
                _cam.enabled = on;
            }
        }

        private void Update()
        {
            if (_active && _model != null)
            {
                _model.Rotate(0f, Time.deltaTime * 28f, 0f, Space.World);
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
