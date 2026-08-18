// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The blueprint tool's client side (#1117). Using the tool on a block marks corner A, then corner B —
    /// the export dialog then names the region and copies its <c>BBTS1-B-…</c> share code to the clipboard.
    /// Using the tool while the CLIPBOARD already holds a build code offers pasting it at the aimed spot
    /// instead (the server re-validates every cell and pays blocks from the inventory). Modeled on
    /// <see cref="BeaconLabelUi"/>: modal card, Enter/Esc + on-screen buttons, menu-owner arbiter.
    /// </summary>
    public sealed class BlueprintToolUi : MonoBehaviour
    {
        public static BlueprintToolUi Instance { get; private set; }
        public GameBootstrap Game;

        private Canvas _canvas;
        private Text _title;
        private Text _info;
        private InputField _input;
        private GameObject _inputGo;
        private Text _confirmLabel;
        private Text _altLabel;
        private System.Action _onConfirm;
        private System.Action _onAlt;
        private bool _open, _built, _wired;
        private int _openFrame = -1;

        private Vector3Int? _cornerA, _cornerB;
        private string _cornerWorld = string.Empty;

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

        /// <summary>True while the overlay is capturing input.</summary>
        public bool IsOpen => _open;

        /// <summary>Tool use on an aimed block: paste offer (clipboard holds a build code), else corner A,
        /// else corner B + export dialog. Corners are per world — a world change resets them.</summary>
        public void UseAt(Vector3Int cell)
        {
            if (Game == null || _open)
            {
                return;
            }

            Wire();
            if (_cornerWorld != Game.LocationName)
            {
                _cornerA = _cornerB = null;
                _cornerWorld = Game.LocationName;
            }

            if (_cornerA == null
                && BlueprintCode.TryDecode(GUIUtility.systemCopyBuffer, out int sx, out int sy, out int sz, out string author, out string name, out _))
            {
                // The build's minimum corner sits ON the aimed ground block.
                var origin = new Vector3Int(cell.x, cell.y + 1, cell.z);
                string title = string.IsNullOrEmpty(name) ? L("ui.blueprint.paste_title") : name;
                string by = string.IsNullOrEmpty(author) ? string.Empty : "  ·  " + L("ui.blueprint.by") + " " + author;
                Open(
                    title,
                    $"{sx} × {sy} × {sz}{by}\n{L("ui.blueprint.paste_hint")}",
                    input: false,
                    confirmLabel: L("ui.blueprint.paste"),
                    onConfirm: () => Game.Network?.SendPasteBuild(GUIUtility.systemCopyBuffer.Trim(), origin.x, origin.y, origin.z),
                    altLabel: L("ui.blueprint.copy_instead"),
                    onAlt: () =>
                    {
                        _cornerA = cell;
                        Game.ShowMessage(L("ui.blueprint.corner_a") + $"  ({cell.x}, {cell.y}, {cell.z})");
                    });
                return;
            }

            if (_cornerA == null)
            {
                _cornerA = cell;
                Game.ShowMessage(L("ui.blueprint.corner_a") + $"  ({cell.x}, {cell.y}, {cell.z})");
                return;
            }

            var a = _cornerA.Value;
            if (Mathf.Abs(cell.x - a.x) >= BlueprintCode.MaxEdge
                || Mathf.Abs(cell.y - a.y) >= BlueprintCode.MaxEdge
                || Mathf.Abs(cell.z - a.z) >= BlueprintCode.MaxEdge)
            {
                Game.ShowMessage(L("ui.blueprint.too_big"));
                return; // corner A stays — aim closer for corner B
            }

            _cornerB = cell;
            int ex = Mathf.Abs(cell.x - a.x) + 1, ey = Mathf.Abs(cell.y - a.y) + 1, ez = Mathf.Abs(cell.z - a.z) + 1;
            Open(
                L("ui.blueprint.export_title"),
                $"{ex} × {ey} × {ez}\n{L("ui.blueprint.export_hint")}",
                input: true,
                confirmLabel: L("ui.blueprint.export"),
                onConfirm: () =>
                {
                    var b = _cornerB!.Value;
                    Game.Network?.SendCopyBuild(a.x, a.y, a.z, b.x, b.y, b.z, _input.text?.Trim() ?? string.Empty);
                },
                altLabel: L("ui.blueprint.reset"),
                onAlt: () =>
                {
                    _cornerA = _cornerB = null;
                    Game.ShowMessage(L("ui.blueprint.reset_done"));
                });
        }

        /// <summary>Server replies land on the HUD message line; a successful export fills the clipboard.</summary>
        private void Wire()
        {
            if (_wired || Game?.Network == null)
            {
                return;
            }

            Game.Network.BuildCodeReceived += m =>
            {
                if (m.Success)
                {
                    GUIUtility.systemCopyBuffer = m.Code;
                    _cornerA = _cornerB = null;
                    Game.ShowMessage(L("ui.blueprint.copied"));
                }
                else
                {
                    Game.ShowMessage(Readable(m.Reason));
                }
            };
            Game.Network.BuildPasteResultReceived += m =>
            {
                if (m.Success)
                {
                    string by = string.IsNullOrEmpty(m.Author) ? string.Empty : "  ·  " + L("ui.blueprint.by") + " " + m.Author;
                    int skipped = m.SkippedMaterials + m.SkippedProtected + m.SkippedSpecial;
                    string tail = skipped > 0
                        ? "  ·  " + L("ui.blueprint.skipped").Replace("{n}", skipped.ToString())
                          + (m.SkippedMaterials > 0 ? " (" + L("ui.blueprint.skipped_materials") + ")" : string.Empty)
                        : string.Empty;
                    Game.ShowMessage(L("ui.blueprint.pasted").Replace("{n}", m.Placed.ToString()) + by + tail);
                }
                else
                {
                    Game.ShowMessage(Readable(m.Reason));
                }
            };
            _wired = true;
        }

        private string Readable(string reason)
            => string.IsNullOrEmpty(reason) ? L("ui.blueprint.failed")
                : reason.StartsWith("@", System.StringComparison.Ordinal) ? L(reason.Substring(1)) : reason;

        private void Open(string title, string info, bool input, string confirmLabel, System.Action onConfirm, string altLabel, System.Action onAlt)
        {
            EnsureBuilt();
            _title.text = title;
            _info.text = info;
            _inputGo.SetActive(input);
            _input.text = string.Empty;
            _confirmLabel.text = confirmLabel;
            _altLabel.text = altLabel;
            _onConfirm = onConfirm;
            _onAlt = onAlt;
            _open = true;
            _openFrame = Time.frameCount;
            _canvas.gameObject.SetActive(true);

            Game?.SetMenuOwner(this, true); // freezes player control + frees the cursor via the arbiter (#413)
            if (input)
            {
                _input.ActivateInputField();
                _input.Select();
            }
        }

        private void Update()
        {
            if (!_open)
            {
                return;
            }

            if (Time.frameCount != _openFrame
                && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                Confirm();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Game?.MarkMenuInputHandled(); // this Esc is consumed — don't also pop the quit prompt (#413 N1)
                Close();
            }
        }

        private void Confirm()
        {
            if (!_open)
            {
                return;
            }

            var cb = _onConfirm;
            Close();
            cb?.Invoke();
        }

        private void Close()
        {
            _open = false;
            _onConfirm = null;
            _onAlt = null;
            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(false);
            }

            Game?.SetMenuOwner(this, false); // arbiter re-locks only once NO other panel is open (#413)
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _canvas = UiKit.CreateCanvas("BlueprintToolUI");
            _canvas.sortingOrder = 58; // above the HUD/chat, below the world map (60)
            var root = _canvas.transform;

            UiKit.AddPanel(root, 0, 0, 1920, 1080, new Color(0f, 0f, 0f, 0.45f));

            const float w = 560f, h = 320f;
            float x = (1920f - w) * 0.5f, y = (1080f - h) * 0.5f;
            UiKit.AddPanel(root, x, y, w, h, UiKit.Panel);

            _title = UiKit.AddText(root, x + 24, y + 22, w - 48, 30, string.Empty, 24, UiKit.TextCol, TextAnchor.MiddleLeft);
            _title.fontStyle = FontStyle.Bold;

            _info = UiKit.AddText(root, x + 24, y + 62, w - 48, 60, string.Empty, 18, UiKit.CyanDim, TextAnchor.UpperLeft);
            _info.horizontalOverflow = HorizontalWrapMode.Wrap;

            _input = UiKit.AddInput(root, x + 24, y + 132, w - 48, 40, string.Empty, null, L("ui.blueprint.name_placeholder"));
            _input.characterLimit = 24;
            _input.lineType = InputField.LineType.SingleLine;
            _inputGo = _input.gameObject;

            // Same touch etiquette as the beacon dialog (#408): no onEndEdit auto-confirm, on-screen buttons.
            UiKit.AddText(root, x + 24, y + 180, w - 48, 24, L("ui.beacon.confirm") + " — Enter   ·   " + L("ui.beacon.cancel") + " — Esc",
                16, UiKit.CyanDim, TextAnchor.MiddleLeft);

            float bw = (w - 72f) / 3f;
            var confirm = UiKit.AddButton(root, x + 24, y + 232, bw, 52, string.Empty, Confirm);
            _confirmLabel = confirm.GetComponentInChildren<Text>();
            var alt = UiKit.AddButton(root, x + 36 + bw, y + 232, bw, 52, string.Empty, () =>
            {
                var cb = _onAlt;
                Close();
                cb?.Invoke();
            });
            _altLabel = alt.GetComponentInChildren<Text>();
            UiKit.AddButton(root, x + 48 + bw * 2f, y + 232, bw, 52, L("ui.beacon.cancel"), () =>
            {
                Game?.MarkMenuInputHandled();
                Close();
            });

            _canvas.gameObject.SetActive(false);
            _built = true;
        }

        private string L(string k) => Game?.Localizer?.Get(k) ?? k;
    }
}
