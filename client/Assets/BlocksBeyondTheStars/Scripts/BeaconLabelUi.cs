using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Modal text-entry overlay for radio beacons (item 37): pops up when the player places a beacon ("name it")
    /// or presses E on one they own ("rename it"). Type a label, Enter confirms, Esc cancels. Like the other modal
    /// panels (trade/dock), it frees the cursor and pauses on-foot control via <c>GameBootstrap.MenuOpen</c>, so
    /// typing never leaks into movement/placement. The label is free-form; the server trims + clamps it.
    /// </summary>
    public sealed class BeaconLabelUi : MonoBehaviour
    {
        public static BeaconLabelUi Instance { get; private set; }
        public GameBootstrap Game;

        private Canvas _canvas;
        private Text _title;
        private InputField _input;
        private System.Action<string> _onConfirm;
        private bool _open, _built;
        private int _openFrame = -1;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        /// <summary>True while the overlay is capturing text (so other systems can ignore gameplay hotkeys).</summary>
        public bool IsOpen => _open;

        /// <summary>Opens the overlay with a title + a starting value; <paramref name="onConfirm"/> gets the typed
        /// label when the player confirms with Enter (never called on cancel).</summary>
        public void Open(string title, string current, System.Action<string> onConfirm)
        {
            EnsureBuilt();
            _onConfirm = onConfirm;
            _title.text = title;
            _input.text = current ?? string.Empty;
            _open = true;
            _openFrame = Time.frameCount;
            _canvas.gameObject.SetActive(true);

            if (Game != null)
            {
                Game.MenuOpen = true; // freezes player control + frees the cursor (modal, like trade/dock)
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _input.ActivateInputField();
            _input.Select();
        }

        private void Update()
        {
            if (!_open)
            {
                return;
            }

            if (Time.frameCount != _openFrame &&
                (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                string label = _input.text?.Trim() ?? string.Empty;
                var cb = _onConfirm;
                Close();
                cb?.Invoke(label);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close(); // cancel — no callback, nothing placed/renamed
            }
        }

        private void Close()
        {
            _open = false;
            _onConfirm = null;
            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(false);
            }

            if (Game != null)
            {
                Game.MenuOpen = false;
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("BeaconLabelUI");
            _canvas.sortingOrder = 58; // above the HUD/chat, below the world map (60)
            var root = _canvas.transform;

            // Dim the screen behind the modal so it reads as the focus (Place anchors top-left, 1920x1080 ref).
            UiKit.AddPanel(root, 0, 0, 1920, 1080, new Color(0f, 0f, 0f, 0.45f));

            // Centred card.
            const float w = 460f, h = 190f;
            float x = (1920f - w) * 0.5f, y = (1080f - h) * 0.5f;
            UiKit.AddPanel(root, x, y, w, h, UiKit.Panel);

            _title = UiKit.AddText(root, x + 24, y + 22, w - 48, 30, string.Empty, 24, UiKit.TextCol, TextAnchor.MiddleLeft);
            _title.fontStyle = FontStyle.Bold;

            _input = UiKit.AddInput(root, x + 24, y + 74, w - 48, 40, string.Empty, null, L("ui.beacon.placeholder"));
            _input.characterLimit = 24;
            _input.lineType = InputField.LineType.SingleLine;

            UiKit.AddText(root, x + 24, y + 128, w - 48, 26, L("ui.beacon.confirm") + " — Enter   ·   " + L("ui.beacon.cancel") + " — Esc",
                18, UiKit.CyanDim, TextAnchor.MiddleLeft);

            _canvas.gameObject.SetActive(false);
            _built = true;
        }

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;
    }
}
