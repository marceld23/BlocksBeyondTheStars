// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Feedback;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Avatar Designer's skin file and its Submit button (#3262). The file and the picture are
    /// <see cref="AppearanceSubmit"/>'s — the character editor's own Submit (#3319, in the menu AND in a world) sends the
    /// very same. A player's look has no hair (the suit's helmet covers it); the adopting developer gives the pioneer one
    /// with <c>tools/gen_pioneer_skins.py --hair</c>.
    /// </summary>
    public sealed partial class AvatarEditor
    {
        private CreationSubmitDialog _submitDialog;
        private bool _capturingPreview;

        /// <summary>Export and Submit side by side on the row Export had alone — each well over 140 px wide.</summary>
        private void AddExportAndSubmit(Transform panel, float y, float rowWidth)
        {
            const float gap = 10f;
            float width = (rowWidth - gap) / 2f;
            UiKit.AddFitButton(panel, 20f, y, width, 38f, L("ui.avatar.export"), Export);
            UiKit.AddFitButton(panel, 20f + width + gap, y, width, 38f, L("ui.submit.button"), Submit);
        }

        /// <summary>The designer's current scratch look.</summary>
        private FaceEditor.AppearanceSnapshot Look()
            => AppearanceSubjects.Snapshot(which => _col[which], () => _face, part => _bodyPaint[part]);

        /// <summary>The designer's current look as the skin file (colours as #RRGGBB, paintings as the game stores them).</summary>
        private string SkinFile(string key, bool pretty) => AppearanceSubmit.SkinFile(key, _name, Look(), pretty);

        /// <summary>Submit: the skin file, a picture of the figure, then the dialog with the consents.</summary>
        private void Submit()
        {
            if (_capturingPreview || CreationSubmitDialog.IsAnyOpen)
            {
                return;
            }

            string key = EditorSceneKit.Slug(_name);
            if (string.IsNullOrEmpty(key))
            {
                SetStatus(L("ui.avatar.need_name"));
                return;
            }

            var payload = new CreationPayload().Add("skin.json", SkinFile(key, pretty: false));
            _capturingPreview = true;
            StartCoroutine(AppearanceSubmit.CaptureSkinPicture(transform, Look(), png =>
            {
                _capturingPreview = false;
                OpenSubmitDialog(key, payload, png);
            }));
        }

        private void OpenSubmitDialog(string key, CreationPayload payload, byte[] png)
        {
            if (_submitDialog == null)
            {
                _submitDialog = gameObject.AddComponent<CreationSubmitDialog>();
                _submitDialog.Localize = L;
                _submitDialog.Settings = Shell != null ? Shell.Settings : null;
                _submitDialog.OnOutcome = (message, _) => SetStatus(message);
            }

            _submitDialog.Open(new CreationSubmitRequest
            {
                Kind = CreationSubmission.KindAvatar,
                Key = key,
                DisplayName = _name,
                Payload = payload,
                PreviewPng = png,
            });
        }
    }
}
