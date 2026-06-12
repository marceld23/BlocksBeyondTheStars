using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Standalone avatar skin designer (menu tool, sibling of <see cref="ShipEditor"/>). A slowly
    /// rotating live <see cref="PlayerAvatar"/> preview with per-part colour controls and gear toggles;
    /// <b>Apply</b> saves the colours into <see cref="ClientSettings"/> (so the in-game avatar uses them)
    /// and <b>Export</b> writes a skin bundle (skin.json) a developer can fold into the game with
    /// tools/merge_avatar.py. Self-contained on the client (no server); modern uGUI.
    /// </summary>
    public sealed class AvatarEditor : MonoBehaviour
    {
        public AppShell Shell;

        private static readonly Color[] Palette =
        {
            new Color(0.85f, 0.68f, 0.55f), new Color(0.55f, 0.40f, 0.28f), new Color(0.90f, 0.85f, 0.80f),
            new Color(0.80f, 0.20f, 0.20f), new Color(0.20f, 0.45f, 0.80f), new Color(0.20f, 0.65f, 0.35f),
            new Color(0.90f, 0.75f, 0.20f), new Color(0.55f, 0.30f, 0.70f), new Color(0.25f, 0.25f, 0.32f),
            new Color(0.92f, 0.92f, 0.95f), new Color(0.15f, 0.62f, 0.66f), new Color(0.95f, 0.55f, 0.30f),
        };

        private Camera _cam;
        private PlayerAvatar _avatar;
        private Transform _avatarRoot;
        private Canvas _canvas;

        private readonly Color[] _col = new Color[4]; // skin, torso, arms, legs
        private readonly Image[] _swatch = new Image[4];
        private readonly bool[] _gear = new bool[5];  // helmet, chest, legs, pack, lamp
        private readonly Text[] _gearLabel = new Text[5];
        private string _name = "My Skin";
        private Text _status;

        private void Start()
        {
            var s = Shell?.Settings;
            _col[0] = s?.SkinColor ?? Palette[0];
            _col[1] = s?.TorsoColor ?? Palette[4];
            _col[2] = s?.ArmColor ?? Palette[4];
            _col[3] = s?.LegColor ?? Palette[8];

            BuildScene();
            BuildUi();
        }

        private void BuildScene()
        {
            var camGo = new GameObject("AvatarCam");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.03f, 0.05f, 0.09f);
            _cam.transform.position = new Vector3(-0.7f, 1.15f, 3.4f);
            _cam.transform.rotation = Quaternion.Euler(4f, 192f, 0f); // look back at the avatar
            camGo.AddComponent<AudioListener>();

            var lightGo = new GameObject("AvatarSun");
            lightGo.transform.SetParent(transform, false);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(35f, 200f, 0f);
            sun.intensity = 1.1f;

            _avatarRoot = new GameObject("AvatarPreview").transform;
            _avatarRoot.SetParent(transform, false);
            _avatar = _avatarRoot.gameObject.AddComponent<PlayerAvatar>();
            _avatar.Build(_col[0], _col[1], _col[2], _col[3]);
            _avatar.SetVisible(true);
        }

        private void Update()
        {
            if (_avatarRoot != null)
            {
                _avatarRoot.Rotate(0f, Time.deltaTime * 24f, 0f, Space.World);
            }
        }

        private void BuildUi()
        {
            _canvas = UiKit.CreateCanvas("Avatar Editor UI");
            _canvas.sortingOrder = 5;
            var root = _canvas.transform;

            // Right-hand control panel (anchored to the top-right).
            const float w = 420f, h = 920f;
            var panel = RightPanel(root, w, h);
            UiKit.AddText(panel, 20f, 14f, w - 40f, 28f, L("ui.avatar.title"), 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            float y = 56f;
            UiKit.AddText(panel, 20f, y, w - 40f, 22f, L("ui.avatar.colors"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
            ColorRow(panel, ref y, L("ui.settings.skin"), 0);
            ColorRow(panel, ref y, L("ui.settings.torso"), 1);
            ColorRow(panel, ref y, L("ui.settings.arms"), 2);
            ColorRow(panel, ref y, L("ui.settings.legs"), 3);

            y += 8f;
            UiKit.AddText(panel, 20f, y, w - 40f, 22f, L("ui.avatar.gear_preview"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
            GearRow(panel, ref y, L("ui.avatar.helmet"), 0);
            GearRow(panel, ref y, L("ui.avatar.chest"), 1);
            GearRow(panel, ref y, L("ui.avatar.legs"), 2);
            GearRow(panel, ref y, L("ui.avatar.pack"), 3);
            GearRow(panel, ref y, L("ui.avatar.lamp"), 4);

            y += 10f;
            UiKit.AddText(panel, 20f, y, w - 40f, 22f, L("ui.avatar.name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 26f;
            UiKit.AddInput(panel, 20f, y, w - 40f, 30f, _name, v => _name = v);
            y += 44f;

            UiKit.AddButton(panel, 20f, y, w - 40f, 38f, L("ui.avatar.apply"), Apply);
            y += 46f;
            UiKit.AddButton(panel, 20f, y, w - 40f, 38f, L("ui.avatar.export"), Export);
            y += 50f;
            _status = UiKit.AddText(panel, 20f, y, w - 40f, 60f, string.Empty, 14, UiKit.Ok, TextAnchor.UpperLeft);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;

            UiKit.AddButton(panel, 20f, h - 56f, 200f, 40f, L("ui.menu.back"), () => Shell?.CloseAvatarEditor());

            // Controls hint (bottom-left under the preview).
            UiKit.AddText(root, 40f, 1020f, 900f, 26f, L("ui.avatar.hint"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
        }

        private void ColorRow(Transform panel, ref float y, string label, int which)
        {
            UiKit.AddText(panel, 24f, y, 150f, 30f, label, 16, UiKit.TextCol, TextAnchor.MiddleLeft);

            var sw = new GameObject("Swatch", typeof(RectTransform));
            sw.transform.SetParent(panel, false);
            UiKit.Place(sw, 180f, y + 3f, 24f, 24f);
            var img = sw.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = _col[which];
            img.raycastTarget = false;
            _swatch[which] = img;

            int idx = which;
            UiKit.AddButton(panel, 214f, y, 80f, 30f, "→", () => CyclePart(idx, 1));
            UiKit.AddButton(panel, 300f, y, 80f, 30f, "←", () => CyclePart(idx, -1));
            y += 38f;
        }

        private void CyclePart(int which, int dir)
        {
            _col[which] = NextColor(_col[which], dir);
            _swatch[which].color = _col[which];
            _avatar.ApplyColors(_col[0], _col[1], _col[2], _col[3]);
        }

        private void GearRow(Transform panel, ref float y, string label, int which)
        {
            UiKit.AddText(panel, 24f, y, 200f, 30f, label, 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            int idx = which;
            var btn = UiKit.AddButton(panel, 240f, y, 140f, 30f, OffOn(false), () => ToggleGear(idx));
            _gearLabel[which] = btn.GetComponentInChildren<Text>();
            y += 38f;
        }

        private void ToggleGear(int which)
        {
            _gear[which] = !_gear[which];
            _gearLabel[which].text = OffOn(_gear[which]);
            _avatar.SetGear(_gear[0], _gear[1], _gear[2], _gear[3], _gear[4]);
        }

        private string OffOn(bool on) => on ? L("ui.avatar.on") : L("ui.avatar.off");

        private void Apply()
        {
            if (Shell?.Settings is { } s)
            {
                s.SkinColor = _col[0];
                s.TorsoColor = _col[1];
                s.ArmColor = _col[2];
                s.LegColor = _col[3];
                s.Save();
                SetStatus(L("ui.avatar.applied"));
            }
        }

        [Serializable]
        private sealed class SkinJson
        {
            public string key, name, skin, torso, arms, legs;
        }

        private void Export()
        {
            string key = Slug(_name);
            if (string.IsNullOrEmpty(key))
            {
                SetStatus(L("ui.avatar.need_name"));
                return;
            }

            var skin = new SkinJson
            {
                key = key,
                name = _name,
                skin = Hex(_col[0]),
                torso = Hex(_col[1]),
                arms = Hex(_col[2]),
                legs = Hex(_col[3]),
            };

            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "avatar_exports", key);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "skin.json"), JsonUtility.ToJson(skin, true));
                SetStatus($"{L("ui.avatar.exported")}\n{dir}");
            }
            catch (Exception e)
            {
                SetStatus("Export failed: " + e.Message);
            }
        }

        private void SetStatus(string text)
        {
            if (_status != null)
            {
                _status.text = text;
            }
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        // --- helpers ---

        private static Color NextColor(Color current, int dir)
        {
            int idx = -1;
            for (int i = 0; i < Palette.Length; i++)
            {
                if (Mathf.Approximately(Palette[i].r, current.r) && Mathf.Approximately(Palette[i].g, current.g) && Mathf.Approximately(Palette[i].b, current.b))
                {
                    idx = i;
                    break;
                }
            }

            int next = ((idx < 0 ? 0 : idx) + dir + Palette.Length) % Palette.Length;
            return Palette[next];
        }

        private static string Hex(Color c)
            => $"#{Mathf.RoundToInt(c.r * 255f):X2}{Mathf.RoundToInt(c.g * 255f):X2}{Mathf.RoundToInt(c.b * 255f):X2}";

        private static string Slug(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder();
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_') sb.Append('_');
            }

            return sb.ToString();
        }

        private string L(string key) => Shell?.L(key) ?? key;

        private static RectTransform RightPanel(Transform root, float w, float h)
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            go.transform.SetParent(root, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(-16f, -16f);
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.PanelSprite;
            img.type = Image.Type.Sliced;
            img.color = UiKit.PanelFill;
            return rt;
        }
    }
}
