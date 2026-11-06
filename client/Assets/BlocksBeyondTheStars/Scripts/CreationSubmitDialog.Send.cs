// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Build;
using BlocksBeyondTheStars.Client.Feedback;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The sending half of the submit dialog: build the report, post it (Task / WebGL request), and on no
    /// connection park it in the shared feedback spool — the texture's flow (#1965), now for every kind (#3262).</summary>
    public sealed partial class CreationSubmitDialog
    {
        private void OnSubmitClicked()
        {
            if (_sending || _done)
            {
                return;
            }

            if (!AllConsentsGiven)
            {
                _consentMissingShown = true;
                RefreshLabels();
                SetStatus(T("need_consent"), UiKit.Warn);
                return;
            }

            string nickname = _noName || _nickInput == null ? string.Empty : _nickInput.text;
            var report = BuildReport(nickname, out string refusal);
            if (report == null)
            {
                SetStatus(refusal, UiKit.Warn);
                return;
            }

            var uploader = new FeedbackUploader(FeedbackUploader.DefaultEndpoint, BugReportBuildSecrets.ApiKey);
            if (!uploader.IsConfigured)
            {
                // A build without the inbox key (a local or fork build) cannot submit — saving/exporting still works.
                SetStatus(T("not_configured"), UiKit.Warn);
                return;
            }

            if (!_noName)
            {
                _lastNickname = (nickname ?? string.Empty).Trim();
            }

            StartUpload(uploader, report);
        }

        /// <summary>The report for the open request, or null with the sentence that says why not.</summary>
        private FeedbackReport BuildReport(string nickname, out string refusal)
        {
            refusal = T("failed");
            string name = _nameInput != null ? _nameInput.text : string.Empty;
            string note = _noteInput != null ? _noteInput.text : string.Empty;
            if (_request.IsTexture)
            {
                return BuildTextureReport(name, nickname, note);
            }

            var form = new CreationSubmissionForm
            {
                Kind = _request.Kind,
                Key = _request.Key,
                Name = name,
                Nickname = nickname,
                Note = note,
                MadeMyself = _checked[0],
                GrantsUse = _checked[1],
                OldEnoughOrParentsAgreed = _checked[2],
            };
            var result = CreationSubmission.Build(form, _request.Payload, _request.PreviewPng, AppShell.Version,
                FeedbackUi.ReplyKeyFor(Settings), SessionId, Application.platform.ToString(), DateTime.UtcNow);
            refusal = result.Refusal switch
            {
                CreationRefusal.TooManyCells => string.Format(L("ui.submit.too_many_cells"), _request.Payload.CellCount, CreationSubmission.MaxCells(_request.Kind)),
                CreationRefusal.TooLarge => L("ui.submit.too_large"),
                _ => T("failed"),
            };
            return result.Report;
        }

        private FeedbackReport BuildTextureReport(string name, string nickname, string note)
        {
            var form = new TextureSubmissionForm
            {
                Key = _request.Key,
                Kind = _request.Icon ? TextureSubmission.KindIcon : TextureSubmission.KindTile,
                Name = name,
                Nickname = nickname,
                Note = note,
                PaintedMyself = _checked[0],
                GrantsUse = _checked[1],
                OldEnoughOrParentsAgreed = _checked[2],
            };

            // Built on the main thread (PNG encoding is a Unity call); only the POST leaves the game loop.
            byte[] png = null;
            try
            {
                png = TexturePackFolder.EncodeStrip(_request.Frames);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CreationSubmit] no PNG preview: " + e.Message); // the raw tile is what counts
            }

            return TextureSubmission.Build(form, _request.Frames, _request.Fps, png, AppShell.Version,
                FeedbackUi.ReplyKeyFor(Settings), SessionId, Application.platform.ToString(), DateTime.UtcNow);
        }

        private void StartUpload(FeedbackUploader uploader, FeedbackReport report)
        {
            _sending = true;
            RefreshLabels();
            SetStatus(L("ui.feedback.sending"), UiKit.CyanDim);

            string json = FeedbackUploader.Serialize(report, null);
            _pendingJson = json;
            _pendingTitle = report.Title;
#if UNITY_WEBGL && !UNITY_EDITOR
            // No sockets and no threads in the browser — the same body goes out via UnityWebRequest.
            StartCoroutine(FeedbackWebGlTransport.PostJson(json, OnUploadFinished));
#else
            _uploadTask = Task.Run(() => uploader.UploadRawJson(json));
#endif
        }

        private void OnUploadFinished(FeedbackUploadResult result)
        {
            _sending = false;
            string body = _pendingJson;
            string title = _pendingTitle ?? string.Empty;
            _pendingJson = null;
            _pendingTitle = null;

            if (result != null && result.Ok)
            {
                RememberSent(result.ReportId, title);
                Finish(T("sent"));
            }
            else if (result != null && result.StatusCode >= 400 && result.StatusCode < 500)
            {
                // The inbox refused this body — sending the same again cannot work, so it is not queued.
                SetStatus(T("failed"), UiKit.Warn);
                RefreshLabels();
            }
            else if (!string.IsNullOrEmpty(body) && new FeedbackSpool(Path.Combine(AppPaths.Root, "feedback")).Write(body) != null)
            {
                WebGlStorage.Sync();
                Finish(T("queued"));
            }
            else
            {
                SetStatus(T("failed"), UiKit.Warn);
                RefreshLabels();
            }
        }

        /// <summary>The reply poll only runs while the install remembers a sent report (#1328) — a submission
        /// counts, or the developers' answer would never be fetched.</summary>
        private static void RememberSent(string reportId, string title)
        {
            if (string.IsNullOrEmpty(reportId))
            {
                return;
            }

            var log = new SentReportsLog(Path.Combine(AppPaths.Root, "feedback", "sent.json"));
            if (log.Record(reportId, title, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            {
                WebGlStorage.Sync();
            }
        }

        private void Finish(string message)
        {
            _done = true; // sent is sent — no second copy by a double click while the dialog fades out
            SetStatus(message, UiKit.Ok);
            RefreshLabels();
            OnOutcome?.Invoke(message, UiKit.Ok);
            Invoke(nameof(DestroyDialog), 1.8f);
        }
    }
}
