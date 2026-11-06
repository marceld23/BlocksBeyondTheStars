// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using BlocksBeyondTheStars.Client.Feedback;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Avatar Designer's skin file and its Submit button (#3262). <c>skin.json</c> carries the WHOLE look — the four
    /// base colours, the 32×32 face and the four body paintings (the Export used to drop all but the colours) — in the
    /// format <c>tools/gen_pioneer_skins.py --from-skin</c> reads, so an adopted skin becomes a pioneer's look in one
    /// step. A player's look has no hair (the suit's helmet covers it); the adopting developer gives the pioneer one
    /// with <c>--hair</c>. The picture comes from an <see cref="AvatarPreviewRig"/> (the figure alone, head to toe).
    /// </summary>
    public sealed partial class AvatarEditor
    {
        /// <summary>The skin file's format: 2 = colours + face + body paintings (1 was colours only).</summary>
        private const int SkinFormatVersion = 2;

        /// <summary>The preview rig's parking slot: the Character tab uses 0 and the appearance editor 1.</summary>
        private const int PreviewSlot = 3;

        private CreationSubmitDialog _submitDialog;
        private bool _capturingPreview;

        [Serializable]
        private sealed class SkinJson
        {
            public int formatVersion = SkinFormatVersion;
            public string key, name, skin, torso, arms, legs;
            public string facePixels = string.Empty, torsoPixels = string.Empty, armPixels = string.Empty,
                legPixels = string.Empty, helmetPixels = string.Empty;
        }

        /// <summary>Export and Submit side by side on the row Export had alone — each well over 140 px wide.</summary>
        private void AddExportAndSubmit(Transform panel, float y, float rowWidth)
        {
            const float gap = 10f;
            float width = (rowWidth - gap) / 2f;
            UiKit.AddFitButton(panel, 20f, y, width, 38f, L("ui.avatar.export"), Export);
            UiKit.AddFitButton(panel, 20f + width + gap, y, width, 38f, L("ui.submit.button"), Submit);
        }

        /// <summary>The designer's current look as the skin file (colours as #RRGGBB, paintings as the game stores them).</summary>
        private SkinJson BuildSkin(string key) => new SkinJson
        {
            key = key,
            name = _name,
            skin = Hex(_col[0]),
            torso = Hex(_col[1]),
            arms = Hex(_col[2]),
            legs = Hex(_col[3]),
            facePixels = _face ?? string.Empty,
            torsoPixels = _bodyPaint[BlocksBeyondTheStars.Shared.State.BodyPaint.Torso] ?? string.Empty,
            armPixels = _bodyPaint[BlocksBeyondTheStars.Shared.State.BodyPaint.Arms] ?? string.Empty,
            legPixels = _bodyPaint[BlocksBeyondTheStars.Shared.State.BodyPaint.Legs] ?? string.Empty,
            helmetPixels = _bodyPaint[BlocksBeyondTheStars.Shared.State.BodyPaint.Helmet] ?? string.Empty,
        };

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

            var payload = new CreationPayload().Add("skin.json", JsonUtility.ToJson(BuildSkin(key)));
            _capturingPreview = true;
            StartCoroutine(CapturePreview(png =>
            {
                _capturingPreview = false;
                OpenSubmitDialog(key, payload, png);
            }));
        }

        /// <summary>A throwaway <see cref="AvatarPreviewRig"/> in its own slot, dressed in the designer's look, read once.</summary>
        private IEnumerator CapturePreview(Action<byte[]> done)
        {
            var go = new GameObject("SubmitPreviewRig");
            go.transform.SetParent(transform, false);
            var rig = go.AddComponent<AvatarPreviewRig>();
            rig.EnsureBuilt(_col[0], _col[1], _col[2], _col[3], slot: PreviewSlot);
            rig.SetFace(_face);
            for (int part = 0; part < _bodyPaint.Length; part++)
            {
                rig.SetBodyPaint(part, _bodyPaint[part]);
            }

            rig.SetActive(true);
            byte[] png = null;
            yield return EditorPreviewCapture.FromRenderTexture(rig.Texture, bytes => png = bytes);
            Destroy(go);
            done(png);
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
