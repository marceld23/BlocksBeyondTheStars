// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.HudFrames;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The character editor's Submit button (#3319), in the main menu AND in a world: in the character mode it sends the
    /// look being worn as a skin (<c>skin.json</c> + a picture of the figure — the same file the Avatar Designer's own
    /// Submit sends, <see cref="AppearanceSubmit"/>), in the HUD mode the frame (<c>hud.json</c>: the four tiles, the
    /// twinkle, the size + a picture of the frame). The canvas is committed first, so what the player sees is what is sent;
    /// the dialog then asks for the three consents and the nickname as for every other submission.
    /// </summary>
    public sealed partial class FaceEditor
    {
        /// <summary>The settings a submission's reply key comes from (#3319). Null = no Submit button.</summary>
        public ClientSettings SubmitSettings;

        /// <summary>The host's name for the look being worn (the main-menu designer's name field); null = the outfit name
        /// typed in the column, else a default name.</summary>
        public Func<string> LookName;

        private CreationSubmitDialog _submitDialog;
        private bool _capturing;

        /// <summary>Right of Back on the button row — only in a host that can send (both avatar hosts).</summary>
        private void BuildSubmitButton(Transform panel, float buttonsY)
        {
            if (SubmitSettings == null || PreviewState == null)
            {
                return;
            }

            UiKit.AddFitButton(panel, 692f, buttonsY, 180f, 56f, L("ui.submit.button"), SubmitPressed);
        }

        private void SubmitPressed()
        {
            if (_capturing || CreationSubmitDialog.IsAnyOpen)
            {
                return;
            }

            EndStroke();
            CommitActive(); // what is on the canvas is what is sent
            if (_hudMode)
            {
                SubmitHudFrame();
            }
            else
            {
                SubmitSkin();
            }
        }

        private void SubmitSkin()
        {
            string name = SubmitName(LookName?.Invoke(), _outfitShelf, "ui.ed.default.skin");
            string key = CreationSubmission.SafeKey(EditorSceneKit.Slug(name), CreationSubmission.KindAvatar);
            var look = PreviewState();
            var payload = new CreationPayload().Add("skin.json", AppearanceSubmit.SkinFile(key, name, look, pretty: false));
            _capturing = true;
            StartCoroutine(AppearanceSubmit.CaptureSkinPicture(transform, look, png =>
            {
                _capturing = false;
                OpenSubmitDialog(CreationSubmission.KindAvatar, key, name, payload, png);
            }));
        }

        private void SubmitHudFrame()
        {
            var design = HudFrameDesign.Parse(HudHost?.Design);
            if (design.IsEmpty)
            {
                SetShelfStatus(_hudShelf, L("ui.hudframe.empty"));
                return;
            }

            string name = SubmitName(null, _hudShelf, "ui.hudframe.default_name");
            string key = CreationSubmission.SafeKey(EditorSceneKit.Slug(name), CreationSubmission.KindHud);
            int size = HudHost.Settings != null ? HudHost.Settings.HudFrameSizeLevel : HudFrameLayout.SizeMedium;
            var payload = new CreationPayload { BasedOn = _hudTemplateKey ?? string.Empty }
                .Add(HudFrameSubmissionFile.FileName, HudFrameSubmissionFile.Json(design, size, key, name));
            OpenSubmitDialog(CreationSubmission.KindHud, key, name, payload, AppearanceSubmit.HudFramePicture(design, size));
        }

        /// <summary>The name a submission is offered under (the dialog lets the player change it): the host's, the one
        /// typed into the column, or the default.</summary>
        private string SubmitName(string hostName, Shelf shelf, string defaultKey)
        {
            if (!string.IsNullOrWhiteSpace(hostName))
            {
                return hostName.Trim();
            }

            string typed = shelf?.Typed;
            return string.IsNullOrWhiteSpace(typed) ? L(defaultKey) : typed.Trim();
        }

        private void OpenSubmitDialog(string kind, string key, string name, CreationPayload payload, byte[] png)
        {
            if (_submitDialog == null)
            {
                _submitDialog = gameObject.AddComponent<CreationSubmitDialog>();
                _submitDialog.Localize = L;
                _submitDialog.Settings = SubmitSettings;
                _submitDialog.OnOutcome = (message, _) => SetShelfStatus(_hudMode ? _hudShelf : _outfitShelf, message);
            }

            _submitDialog.Open(new CreationSubmitRequest
            {
                Kind = kind,
                Key = key,
                DisplayName = name,
                Payload = payload,
                PreviewPng = png,
            });
        }
    }
}
