// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3070: the panel of a base landing pad, opened with E at the pad block. Everyone reads its name, whose base it
    /// belongs to, its state for them (free, their ship parked here, someone's ship, locked) and who may land; the owner
    /// also names it (up to <see cref="BasePadRules.NameMaxLength"/> letters) and picks who may land — only me, me and my
    /// allies, or everyone — and saves (<c>SetBasePadIntent</c>). What it shows is the server's pad list of this body,
    /// read again whenever a new list arrives. Modal like <see cref="BeamPadUi"/>: frees the cursor, pauses on-foot control.
    /// </summary>
    public sealed class BasePadUi : UiPanel
    {
        private const float W = 640f;

        private Vector3Int _cell;          // the pad's canonical cell (the list's)
        private int _seenVersion = -1;     // the pad book version the panel shows
        private bool _builtFound;          // the pad was in the list when the panel was built
        private bool _builtOwner;          // … and it was this player's
        private string _draftName = string.Empty;
        private int _draftAccess;
        private Text _head;
        private Text _info;
        private Text _state;
        private Text _access;
        private Text _saved;
        private readonly Text[] _choices = new Text[3];

        protected override string CanvasName => "BasePadUI";

        protected override int CanvasSortingOrder => 59; // above the HUD/chat, below the world map (60)

        /// <summary>Gives the menu back if the panel was open when the rig goes (#2303); the base drops the canvas.</summary>
        protected override void OnDestroy()
        {
            if (Shown)
            {
                Shown = false;
                SetMenuOwned(false);
            }

            base.OnDestroy();
        }

        /// <summary>Opens the panel of the pad block at <paramref name="sceneCell"/> (the crosshair's cell).</summary>
        public void Open(Vector3Int sceneCell)
        {
            if (Shown)
            {
                Close(); // a second Open replaces the panel instead of stacking another overlay over it
            }

            int circ = Game != null ? Game.Circumference : WorldConstants.Circumference;
            _cell = new Vector3Int(WorldConstants.WrapX(sceneCell.x, circ), sceneCell.y, WorldConstants.WrapZ(sceneCell.z, circ));
            var pad = FindPad();
            _draftName = pad?.Name ?? string.Empty;
            _draftAccess = (int)(pad != null ? BasePadRules.AccessOf(pad.Access) : BasePadRules.DefaultAccess);
            EnsureCanvas();
            ShowCanvas();
            Build(pad);
            PlayOpenEffect(); // #2302: only here, never from a rebuild
            SetMenuOwned(true);
        }

        private void Update()
        {
            if (!Shown)
            {
                return;
            }

            if (CancelPressed())
            {
                ConsumeMenuInput();
                Close();
                return;
            }

            int version = Game != null ? Game.LandingPadBook.Version : 0;
            if (version == _seenVersion)
            {
                return;
            }

            // A new pad list: the same kind of panel only rereads its lines (a name being typed stays); a pad that was
            // registered, removed or changed hands meanwhile gets the panel it now needs.
            var pad = FindPad();
            if ((pad != null) != _builtFound || IsOwner(pad) != _builtOwner)
            {
                DestroyOverlay();
                Build(pad);
            }
            else if (pad != null)
            {
                Refresh(pad);
            }

            _seenVersion = version;
        }

        private void Close()
        {
            DestroyOverlay();
            HideCanvas();
            SetMenuOwned(false);
        }

        private NetLandingPad FindPad() => Game == null ? null : BasePadView.Find(Game.CurrentBodyPads, _cell.x, _cell.y, _cell.z);

        private static bool IsOwner(NetLandingPad pad) => pad != null && pad.Relation == NetBase.RelationOwn;

        private void Build(NetLandingPad pad)
        {
            _seenVersion = Game != null ? Game.LandingPadBook.Version : 0;
            _builtFound = pad != null;
            _builtOwner = IsOwner(pad);
            _info = _state = _access = _saved = null; // the previous build's lines went with its overlay
            System.Array.Clear(_choices, 0, _choices.Length);
            float h =pad == null ? 240f : _builtOwner ? 500f : 290f;
            var panel = AddModal((1920f - W) * 0.5f, (1080f - h) * 0.5f, W, h);
            _head = UiKit.AddText(panel, 32f, 22f, W - 64f, 36f, L("ui.basepad.title"), 26, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddOutline(_head);
            if (pad == null)
            {
                var note = UiKit.AddText(panel, 32f, 76f, W - 64f, 80f, L("ui.basepad.not_registered"), 18, UiKit.TextCol, TextAnchor.UpperLeft);
                note.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiKit.AddButton(panel, (W - 200f) * 0.5f, h - 66f, 200f, 44f, L("ui.action.close"), Close);
                return;
            }

            _info = UiKit.AddText(panel, 32f, 68f, W - 64f, 24f, string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            _state = UiKit.AddText(panel, 32f, 96f, W - 64f, 26f, string.Empty, 18, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            _access = UiKit.AddText(panel, 32f, 126f, W - 64f, 24f, string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            Refresh(pad);
            if (_builtOwner)
            {
                BuildOwnerControls(panel, 170f, h);
            }

            UiKit.AddButton(panel, W - 32f - 200f, h - 66f, 200f, 44f, L("ui.action.close"), Close);
        }

        /// <summary>The owner's part: the name field, the three "who may land" choices and Save.</summary>
        private void BuildOwnerControls(Transform panel, float y, float h)
        {
            UiKit.AddText(panel, 32f, y, W - 64f, 24f, L("ui.basepad.name_label"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            UiKit.AddInput(panel, 32f, y + 28f, W - 64f, 44f, _draftName, v => _draftName = v ?? string.Empty,
                L("ui.basepad.name_placeholder"), BasePadRules.NameMaxLength);

            UiKit.AddText(panel, 32f, y + 88f, W - 64f, 24f, L("ui.basepad.who_label"), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
            const float gap = 12f;
            float bw = (W - 64f - 2f * gap) / 3f;
            for (int a = 0; a < 3; a++)
            {
                int access = a; // captured by the click
                var btn = UiKit.AddButton(panel, 32f + a * (bw + gap), y + 116f, bw, 44f, string.Empty, () =>
                {
                    _draftAccess = access;
                    MarkChoices();
                });
                _choices[a] = btn != null ? btn.GetComponentInChildren<Text>() : null;
            }

            MarkChoices();
            UiKit.AddButton(panel, 32f, h - 66f, 200f, 44f, L("ui.basepad.save"), Save);
            _saved = UiKit.AddText(panel, 244f, h - 66f, W - 64f - 2f * 212f, 44f, string.Empty, 16, UiKit.Ok, TextAnchor.MiddleCenter);
        }

        /// <summary>Rereads the pad's lines from the list (the server's word).</summary>
        private void Refresh(NetLandingPad pad)
        {
            if (_head != null)
            {
                _head.text = L("ui.basepad.title") + " — " + BasePadText.Name(pad, Game.CurrentBodyPads, L);
            }

            if (_info != null)
            {
                _info.text = L("ui.basepad.base_line")
                    .Replace("{base}", BasePadText.BaseName(pad.BaseName, L))
                    .Replace("{owner}", string.IsNullOrEmpty(pad.OwnerName) ? "—" : pad.OwnerName);
            }

            if (_state != null)
            {
                _state.text = BasePadText.State(pad, L);
                _state.color = BasePadText.MarkerColor(pad);
            }

            if (_access != null)
            {
                _access.text = L("ui.basepad.access_line").Replace("{access}", BasePadText.Access(pad.Access, L));
            }
        }

        /// <summary>Marks the picked "who may land" choice.</summary>
        private void MarkChoices()
        {
            for (int a = 0; a < _choices.Length; a++)
            {
                if (_choices[a] != null)
                {
                    _choices[a].text = (a == _draftAccess ? "● " : string.Empty) + BasePadText.AccessChoice((BasePadAccess)a, L);
                }
            }

            if (_saved != null)
            {
                _saved.text = string.Empty; // a change since the last save is not saved yet
            }
        }

        /// <summary>Sends the name and access; the server answers with the refreshed pad list, which the panel rereads.</summary>
        private void Save()
        {
            string name = (_draftName ?? string.Empty).Trim();
            if (name.Length > BasePadRules.NameMaxLength)
            {
                name = name.Substring(0, BasePadRules.NameMaxLength);
            }

            Game?.Network?.SendSetBasePad(_cell.x, _cell.y, _cell.z, name, _draftAccess);
            if (_saved != null)
            {
                _saved.text = L("ui.basepad.saved");
            }
        }
    }
}
