// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// One confirmation dialog for every permanent action (#3690): before it, "delete kit", "remove mine", "back to the
    /// default look", the library ✕ buttons and "generate" / "assemble" over a build had no question at all, and the two
    /// places that asked (My worlds, the editors' load picker) each built their own. The dialog is the menu template's
    /// modal (<see cref="AddModalOverlay"/>: scrim, opaque panel, pad focus trapped inside) with two buttons.
    /// </summary>
    public static partial class UiKit
    {
        /// <summary>The confirmation panel's size in the 1920 × 1080 reference (centred).</summary>
        public const float ConfirmW = 760f, ConfirmH = 300f;

        /// <summary>The name of the confirm button ("yes") of a <see cref="Confirm"/> dialog — for tests and the pad.</summary>
        public const string ConfirmYesName = "ConfirmYes";

        /// <summary>The name of the cancel button ("no") of a <see cref="Confirm"/> dialog.</summary>
        public const string ConfirmNoName = "ConfirmNo";

        /// <summary>What a <see cref="Confirm"/> dialog says and does.</summary>
        public sealed class ConfirmSpec
        {
            /// <summary>An optional heading above the message.</summary>
            public string Title;

            /// <summary>The question, in words a child understands ("Delete “My cruiser” for good?").</summary>
            public string Message;

            /// <summary>The confirming button — a verb, never "Yes" / "OK".</summary>
            public string YesLabel;

            /// <summary>The cancelling button.</summary>
            public string NoLabel;

            public System.Action OnYes;

            public System.Action OnNo;

            /// <summary><see cref="ButtonTier.Danger"/> for a permanent action (the default); <see cref="ButtonTier.Primary"/>
            /// for a safe choice such as "Continue your draft".</summary>
            public ButtonTier YesTier = ButtonTier.Danger;

            public ButtonTier NoTier = ButtonTier.Quiet;

            public string YesIcon = "btn_delete";

            public string NoIcon = "btn_close";

            /// <summary>What Esc / pad B choose: cancel (the default), or the confirming button when that is the safe one.</summary>
            public bool EscapeMeansYes;
        }

        /// <summary>A dialog opened by <see cref="Confirm"/>.</summary>
        public sealed class ConfirmDialog
        {
            internal ConfirmSpec Spec;

            /// <summary>The scrim; the whole dialog hangs off it.</summary>
            public GameObject Overlay { get; internal set; }

            public Button Yes { get; internal set; }

            public Button No { get; internal set; }

            /// <summary>True until a choice was made or it was closed.</summary>
            public bool IsOpen => Overlay != null && Overlay.activeSelf;

            /// <summary>Closes the dialog and runs the chosen button's action (after closing, so the action may open another
            /// dialog).</summary>
            public void Choose(bool yes)
            {
                if (!IsOpen)
                {
                    return;
                }

                Close();
                (yes ? Spec.OnYes : Spec.OnNo)?.Invoke();
            }

            /// <summary>Esc / pad B: the choice <see cref="ConfirmSpec.EscapeMeansYes"/> names.</summary>
            public void Escape() => Choose(Spec.EscapeMeansYes);

            /// <summary>Removes the dialog without a choice.</summary>
            public void Close()
            {
                if (Overlay == null)
                {
                    return;
                }

                var overlay = Overlay;
                Overlay = null;
                overlay.SetActive(false);
                if (Application.isPlaying)
                {
                    Object.Destroy(overlay);
                }
                else
                {
                    Object.DestroyImmediate(overlay);
                }
            }
        }

        /// <summary>
        /// Opens a confirmation over <paramref name="root"/> (the canvas root — the dialog is added as its LAST child, so it
        /// draws and takes clicks above every widget built before). The confirming button is built first: a
        /// <see cref="ButtonTier.Danger"/> one is never the pad's first focus (<see cref="UiNavFocus"/> skips it, the dialog
        /// opens on Cancel), a safe <see cref="ButtonTier.Primary"/> one is.
        /// </summary>
        public static ConfirmDialog Confirm(Transform root, ConfirmSpec spec)
        {
            var dialog = new ConfirmDialog { Spec = spec ?? new ConfirmSpec() };
            spec = dialog.Spec;
            float x = (1920f - ConfirmW) / 2f, y = (1080f - ConfirmH) / 2f;
            var (overlay, panel) = AddModalOverlay(root, x, y, ConfirmW, ConfirmH);
            overlay.name = "ConfirmDialog";
            dialog.Overlay = overlay;

            float textY = 28f;
            if (!string.IsNullOrEmpty(spec.Title))
            {
                var title = AddText(panel, 36f, 24f, ConfirmW - 72f, 36f, spec.Title, 26, Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                FitLabelSingleLine(title, ConfirmW - 72f, 18, 26);
                textY = 72f;
            }

            var message = AddText(panel, 36f, textY, ConfirmW - 72f, 196f - textY, spec.Message ?? string.Empty, 20, TextCol,
                TextAnchor.UpperLeft);
            FitLabel(message, 16, 20);

            const float buttonY = ConfirmH - 30f - 58f, buttonW = (ConfirmW - 72f - 24f) / 2f;
            dialog.Yes = AddTierButton(panel, 36f, buttonY, buttonW, 58f, spec.YesLabel ?? string.Empty, () => dialog.Choose(true),
                spec.YesTier, spec.YesIcon);
            dialog.Yes.name = ConfirmYesName;
            dialog.No = AddTierButton(panel, 36f + buttonW + 24f, buttonY, buttonW, 58f, spec.NoLabel ?? string.Empty,
                () => dialog.Choose(false), spec.NoTier, spec.NoIcon);
            dialog.No.name = ConfirmNoName;
            KeepLabelReadable(dialog.Yes);
            KeepLabelReadable(dialog.No);
            overlay.transform.SetAsLastSibling();
            OpenModal(overlay);
            return dialog;
        }

        /// <summary>A dialog button's label on one line at 16–22 px (AddButton's own fit may go down to 11 — too small for
        /// a question a child must read).</summary>
        private static void KeepLabelReadable(Button button)
        {
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                FitLabelSingleLine(label, label.rectTransform.sizeDelta.x, 16, 22);
            }
        }

        /// <summary>The common case: a permanent action (red <paramref name="dangerLabel"/>, bin icon) or Cancel.</summary>
        public static ConfirmDialog ConfirmDanger(Transform root, string message, string dangerLabel, System.Action onConfirm,
            string cancelLabel)
            => Confirm(root, new ConfirmSpec { Message = message, YesLabel = dangerLabel, OnYes = onConfirm, NoLabel = cancelLabel });
    }
}
