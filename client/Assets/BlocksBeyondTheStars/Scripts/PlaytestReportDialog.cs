// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the Test Pilot dialogs share: which one is open, and who owns the cancel key. The shell's Esc / pad B also
    /// backs out of the page underneath and an editor's own keys keep working under a dialog — both ask here first.
    /// The frame a dialog closed in still counts, so the SAME press does not also close what lies under it, whichever
    /// Update runs first (the rule of the submit dialog).
    /// </summary>
    public static class PlaytestModal
    {
        private static int _open;
        private static int _closedFrame = -1;

        /// <summary>True while a Test Pilot dialog is on screen.</summary>
        public static bool AnyOpen => _open > 0;

        /// <summary>True while a Test Pilot dialog owns the cancel key (see the class summary).</summary>
        public static bool OwnsCancel => _open > 0 || _closedFrame == Time.frameCount;

        public static void Opened() => _open++;

        public static void Closed()
        {
            _open = Mathf.Max(0, _open - 1);
            _closedFrame = Time.frameCount;
        }

        /// <summary>Forgets the open dialogs — for an edit-mode test, whose dialogs are destroyed without their OnDestroy.</summary>
        public static void ResetForTest() => _open = 0;

        /// <summary>A dialog canvas above the page and the editors (and above the submit dialog's order), with its scrim.</summary>
        public static (Canvas Canvas, Transform Panel) Build(string name, float w, float h)
        {
            var canvas = UiKit.CreateCanvas(name);
            canvas.sortingOrder = 75;
            UiNav.Enable(canvas.gameObject);
            var dim = UiKit.AddModalDim(canvas.transform);
            return (canvas, UiKit.AddDialogPanel(dim.transform, (1920f - w) / 2f, (1080f - h) / 2f, w, h));
        }
    }

    /// <summary>
    /// The closing report of a playtest task: the task's fixed questions (stars or a choice — all mandatory), an
    /// optional line in the tester's own words, an optional nickname, and — from an editor — the offer to send the
    /// work along. Send stays clickable: pressed with a question open, it marks that question instead of doing nothing.
    /// The body is built by <see cref="PlaytestReport"/> and goes out through the ordinary feedback channel. No e-mail
    /// field on purpose: answers come back inside the game.
    /// </summary>
    public sealed class PlaytestReportDialog : MonoBehaviour
    {
        private const float PanelW = 1100f, PanelH = 940f, M = 40f, InnerW = PanelW - (2f * M);
        private static PlaytestReportDialog _instance;
        private static string _lastNickname = string.Empty;

        private AppShell _shell;
        private PlaytestTask _task;
        private Func<FeedbackAttachment> _work;
        private Canvas _canvas;
        private InputField _free, _nick;
        private Text _status, _workLabel;
        private bool _sendWork, _sending, _done;
        private readonly Dictionary<string, string> _answers = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Text> _questionLabels = new Dictionary<string, Text>(StringComparer.Ordinal);
        private readonly Dictionary<string, Text> _missingHints = new Dictionary<string, Text>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<(string Value, Button Button)>> _choices = new Dictionary<string, List<(string, Button)>>(StringComparer.Ordinal);
        private Task<FeedbackUploadResult> _upload;
        private FeedbackReport _report;
        private string _json;

        /// <summary>True while the report dialog is on screen.</summary>
        public static bool IsOpen => _instance != null;

        /// <summary>Opens the dialog for <paramref name="task"/>. <paramref name="work"/> is an editor's "send my work"
        /// source; null hides the offer.</summary>
        public static void Open(AppShell shell, PlaytestTask task, Func<FeedbackAttachment> work = null)
        {
            if (_instance != null || PlaytestHub.Instance == null || task == null)
            {
                return;
            }

            var go = new GameObject("PlaytestReportDialog");
            _instance = go.AddComponent<PlaytestReportDialog>();
            _instance._shell = shell;
            _instance._task = task;
            _instance._work = work;
            _instance.Build();
        }

        private string L(string key) => _shell.L(key);

        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private void Build()
        {
            var hub = PlaytestHub.Instance;
            var entry = hub.Progress.Get(_task.Id);
            if (entry != null)
            {
                foreach (var pair in entry.Answers)
                {
                    _answers[pair.Key] = pair.Value;
                }
            }

            Transform panel;
            (_canvas, panel) = PlaytestModal.Build("PlaytestReportUI", PanelW, PanelH);
            PlaytestModal.Opened();
            string lang = _shell.Settings.Language;
            UiKit.AddText(panel, M, 22f, InnerW, 38f, L("ui.testpilot.report.title"), 28, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var name = UiKit.AddText(panel, M, 62f, InnerW, 28f, _task.Title.For(lang), 19, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(name, InnerW, 13, 19);

            const float viewTop = 104f, viewH = PanelH - viewTop - 150f;
            var content = UiKit.AddScrollViewport(panel, "Form", M, viewTop, InnerW, viewH, out var scroll);
            float w = InnerW - 22f, y = 0f;
            foreach (var question in _task.Questions)
            {
                y = AddQuestion(content, question, y, w, lang);
            }

            y = AddFreeFields(content, entry != null ? entry.FreeText : string.Empty, y, w);
            content.sizeDelta = new Vector2(0f, Mathf.Max(viewH, y));
            if (y > viewH)
            {
                UiKit.AddVerticalScrollbar(panel, scroll, PanelW - M - 12f, viewTop, 12f, viewH);
            }

            _status = UiKit.AddText(panel, M, PanelH - 138f, InnerW, 44f, string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            _status.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddTierButton(panel, PanelW - M - 280f, PanelH - 84f, 280f, 60f, L("ui.testpilot.report.send"), OnSend, ButtonTier.Primary, "btn_feedback");
            UiKit.AddTierButton(panel, PanelW - M - 280f - 16f - 220f, PanelH - 84f, 220f, 60f, L("ui.testpilot.cancel"), Close, ButtonTier.Quiet, "btn_back");
            Refresh();
        }

        /// <summary>One fixed question: its text, the stars or the choice, and the "still missing" line. Returns the next y.</summary>
        private float AddQuestion(Transform content, PlaytestQuestion question, float y, float w, string lang)
        {
            var label = UiKit.AddMeasuredText(content, 0f, y, w, question.Text.For(lang), 20, UiKit.TextCol, out float h);
            _questionLabels[question.Id] = label;
            y += h + 8f;
            var buttons = new List<(string, Button)>();
            string id = question.Id;
            if (question.Type == PlaytestQuestionTypes.Stars)
            {
                for (int n = 1; n <= 5; n++)
                {
                    string value = n.ToString();
                    var star = UiKit.AddButton(content, (n - 1) * 70f, y, 62f, 58f, "★", () => SetAnswer(id, value));
                    UiKit.StyleTier(star, ButtonTier.Quiet);
                    star.GetComponentInChildren<Text>().fontSize = 34;
                    buttons.Add((value, star));
                }
            }
            else
            {
                float x = 0f;
                foreach (var option in question.Options)
                {
                    string value = option.Id, text = option.Text.For(lang);
                    float bw = Mathf.Clamp(UiKit.MeasureWidth(text, 19) + 44f, 120f, w);
                    if (x + bw > w)
                    {
                        x = 0f;
                        y += 58f;
                    }

                    buttons.Add((value, UiKit.AddFitButton(content, x, y, bw, 50f, text, () => SetAnswer(id, value))));
                    x += bw + 10f;
                }
            }

            _choices[question.Id] = buttons;
            y += 62f;
            _missingHints[question.Id] = UiKit.AddText(content, 0f, y - 4f, w, 22f, L("ui.testpilot.report.missing"), 15, UiKit.Warn, TextAnchor.MiddleLeft);
            _missingHints[question.Id].gameObject.SetActive(false);
            return y + 24f;
        }

        /// <summary>The free text, the nickname, the work offer and what is sent. Returns the next y.</summary>
        private float AddFreeFields(Transform content, string draft, float y, float w)
        {
            UiKit.AddText(content, 0f, y, w, 26f, L("ui.testpilot.report.free_label"), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            y += 30f;
            _free = UiKit.AddInput(content, 0f, y, w, 110f, draft, _ => SaveDraft(), string.Empty, 2000, 18, multiline: true);
            y += 124f;

            UiKit.AddText(content, 0f, y, 320f, 44f, L("ui.testpilot.report.nick_label"), 18, UiKit.TextCol, TextAnchor.MiddleLeft);
            _nick = UiKit.AddInput(content, 330f, y, 320f, 44f, _lastNickname, null, string.Empty, 40);
            y += 58f;

            if (_work != null)
            {
                var toggle = UiKit.AddButton(content, 0f, y, w, 48f, string.Empty, () =>
                {
                    _sendWork = !_sendWork;
                    Refresh();
                });
                UiKit.StyleTier(toggle, ButtonTier.Quiet);
                _workLabel = toggle.GetComponentInChildren<Text>();
                _workLabel.alignment = TextAnchor.MiddleLeft;
                y += 58f;
            }

            UiKit.AddMeasuredText(content, 0f, y, w, L("ui.testpilot.report.what_is_sent"), 15, UiKit.CyanDim, out float h);
            return y + h + 10f;
        }

        private void SetAnswer(string questionId, string value)
        {
            _answers[questionId] = value;
            SaveDraft();
            Refresh();
        }

        private void SaveDraft()
            => PlaytestHub.Instance?.Progress.SetDraft(_task, _answers, _free != null ? _free.text : string.Empty, Now);

        /// <summary>Shows what is chosen: stars up to the picked one in gold, the picked option as the primary button.</summary>
        private void Refresh()
        {
            foreach (var question in _task.Questions)
            {
                _answers.TryGetValue(question.Id, out string picked);
                bool stars = question.Type == PlaytestQuestionTypes.Stars;
                int.TryParse(picked, out int count);
                foreach (var (value, button) in _choices[question.Id])
                {
                    if (stars)
                    {
                        button.GetComponentInChildren<Text>().color = int.Parse(value) <= count ? UiTestPilot.Gold : new Color(0.45f, 0.55f, 0.66f);
                    }
                    else
                    {
                        UiKit.StyleTier(button, value == picked ? ButtonTier.Primary : ButtonTier.Quiet);
                    }
                }
            }

            if (_workLabel != null)
            {
                _workLabel.text = (_sendWork ? "☑  " : "☐  ") + L("ui.testpilot.report.work");
            }
        }

        private void OnSend()
        {
            var hub = PlaytestHub.Instance;
            if (_sending || _done || hub == null)
            {
                return;
            }

            if (!hub.Configured)
            {
                SetStatus(L("ui.testpilot.not_configured"), UiKit.Warn);
                return;
            }

            var form = new PlaytestReportForm { FreeText = _free.text, Nickname = _nick.text };
            foreach (var pair in _answers)
            {
                form.Answers[pair.Key] = pair.Value;
            }

            if (_sendWork && _work != null)
            {
                form.Work = _work();
            }

            var built = hub.BuildReport(_task, form);
            MarkMissing(built.MissingQuestions);
            if (built.Report == null)
            {
                SetStatus(L(built.Refusal == PlaytestReportRefusal.QuestionsMissing ? "ui.testpilot.report.need_answers" : "ui.testpilot.report.steps_open"), UiKit.Warn);
                return;
            }

            _lastNickname = (_nick.text ?? string.Empty).Trim();
            _report = built.Report;
            _json = FeedbackUploader.Serialize(_report, null);
            _sending = true;
            SetStatus(L("ui.feedback.sending"), UiKit.CyanDim);
            string json = _json;
            _upload = Task.Run(() => hub.Upload(json));
        }

        private void MarkMissing(IReadOnlyList<string> missing)
        {
            foreach (var pair in _questionLabels)
            {
                bool open = false;
                foreach (string id in missing)
                {
                    open |= id == pair.Key;
                }

                pair.Value.color = open ? UiKit.Warn : UiKit.TextCol;
                _missingHints[pair.Key].gameObject.SetActive(open);
            }
        }

        private void Update()
        {
            if (_upload != null && _upload.IsCompleted)
            {
                var task = _upload;
                _upload = null;
                _sending = false;
                var result = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                bool sent = PlaytestHub.Instance != null && PlaytestHub.Instance.OnUploaded(_task, _report, _json, result);
                if (sent)
                {
                    _done = true;
                    SetStatus(L(result != null && result.Ok ? "ui.testpilot.report.sent" : "ui.testpilot.report.queued"), UiKit.Ok);
                    Invoke(nameof(CloseNow), 1.6f);
                }
                else
                {
                    SetStatus(L("ui.testpilot.report.failed"), UiKit.Warn);
                }
            }

            if (_canvas != null && !_sending && !_done && InputMap.Down(InputAction.UiCancel) && !UiKit.TextFieldFocused())
            {
                Close();
            }
        }

        private void SetStatus(string text, Color color)
        {
            _status.text = text;
            _status.color = color;
        }

        private void Close()
        {
            if (!_sending)
            {
                CloseNow();
            }
        }

        /// <summary>Closes the open dialog (the draft is kept); nothing happens while a report is on its way.</summary>
        public static void CloseOpen()
        {
            if (_instance != null)
            {
                _instance.Close();
            }
        }

        private void CloseNow()
        {
            if (!_done)
            {
                SaveDraft(); // a closed dialog loses nothing
            }

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                UiKit.ReleaseTextFieldFocus(_canvas.transform);
                Destroy(_canvas.gameObject);
                _canvas = null;
                PlaytestModal.Closed();
            }

            if (_instance == this)
            {
                _instance = null;
            }

            if (_done && _shell != null)
            {
                _shell.RebuildTestPilot();
            }
        }
    }

    /// <summary>The small question behind "Did not work": what went wrong, in a line. The note is optional — the step
    /// closes either way.</summary>
    public sealed class PlaytestNotePrompt : MonoBehaviour
    {
        private static PlaytestNotePrompt _instance;
        private Canvas _canvas;
        private InputField _input;
        private Action<string> _onOk;

        public static void Open(AppShell shell, Action<string> onOk)
        {
            if (_instance != null)
            {
                return;
            }

            _instance = new GameObject("PlaytestNotePrompt").AddComponent<PlaytestNotePrompt>();
            _instance._onOk = onOk;
            _instance.Build(shell);
        }

        private void Build(AppShell shell)
        {
            const float w = 820f, h = 330f, m = 36f;
            Transform panel;
            (_canvas, panel) = PlaytestModal.Build("PlaytestNoteUI", w, h);
            PlaytestModal.Opened();
            UiKit.AddText(panel, m, 24f, w - (2f * m), 36f, shell.L("ui.testpilot.note_title"), 24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var hint = UiKit.AddText(panel, m, 66f, w - (2f * m), 50f, shell.L("ui.testpilot.note_hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            _input = UiKit.AddInput(panel, m, 126f, w - (2f * m), 90f, string.Empty, null, string.Empty, 200, 18, multiline: true);
            UiKit.AddTierButton(panel, w - m - 240f, h - 82f, 240f, 56f, shell.L("ui.testpilot.step_failed"), Ok, ButtonTier.Primary);
            UiKit.AddTierButton(panel, w - m - 240f - 16f - 200f, h - 82f, 200f, 56f, shell.L("ui.testpilot.cancel"), () => Destroy(gameObject), ButtonTier.Quiet, "btn_back");
        }

        private void Ok()
        {
            var done = _onOk;
            string note = _input != null ? _input.text : string.Empty;
            Destroy(gameObject);
            done?.Invoke(note);
        }

        private void Update()
        {
            if (_canvas != null && InputMap.Down(InputAction.UiCancel) && !UiKit.TextFieldFocused())
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                UiKit.ReleaseTextFieldFocus(_canvas.transform);
                Destroy(_canvas.gameObject);
                _canvas = null;
                PlaytestModal.Closed();
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }
    }

    /// <summary>"Your test report was confirmed": the gold badge, the task, the developers' line and how far the next rank
    /// is. Shown in a menu, never over the running world. OK marks the developers' line as read.</summary>
    public sealed class PlaytestConfirmedDialog : MonoBehaviour
    {
        private static PlaytestConfirmedDialog _instance;
        private Canvas _canvas;
        private PlaytestConfirmation _confirmation;

        /// <summary>True while the dialog is on screen.</summary>
        public static bool IsOpen => _instance != null;

        public static void Open(AppShell shell, PlaytestConfirmation confirmation)
        {
            if (_instance != null || PlaytestHub.Instance == null || confirmation == null)
            {
                return;
            }

            _instance = new GameObject("PlaytestConfirmedDialog").AddComponent<PlaytestConfirmedDialog>();
            _instance._confirmation = confirmation;
            _instance.Build(shell, PlaytestHub.Instance);
        }

        private void Build(AppShell shell, PlaytestHub hub)
        {
            const float w = 800f, h = 730f, m = 40f, inner = w - (2f * m);
            Transform panel;
            (_canvas, panel) = PlaytestModal.Build("PlaytestConfirmedUI", w, h);
            PlaytestModal.Opened();
            var task = hub.Tracker.Find(_confirmation.TaskId);
            var gold = UiTestPilot.Gold;
            UiKit.AddKicker(panel, m, 28f, inner, shell.L("ui.testpilot.confirmed.title"), gold).alignment = TextAnchor.MiddleCenter;
            UiKit.AddIcon(panel, (w - 230f) / 2f, 64f, 230f, "pt_badge_gold");
            var title = UiKit.AddText(panel, m, 306f, inner, 44f, task != null ? task.Title.For(shell.Settings.Language) : _confirmation.TaskId, 30,
                UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.FitLabelSingleLine(title, inner, 16, 30);

            float y = 364f;
            if (_confirmation.Text.Length > 0)
            {
                var text = UiKit.AddText(panel, m, y, inner, 110f, string.Format(shell.L("ui.testpilot.confirmed.devs"), _confirmation.Text), 19,
                    UiKit.TextCol, TextAnchor.UpperCenter);
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.supportRichText = false; // the developers' line is shown verbatim
                y += 118f;
            }

            if (_confirmation.FixedInVersion.Length > 0)
            {
                UiKit.AddText(panel, m, y, inner, 28f, string.Format(shell.L("ui.testpilot.confirmed.fixed"), _confirmation.FixedInVersion), 17,
                    UiKit.CyanDim, TextAnchor.MiddleCenter);
                y += 34f;
            }

            var (badges, confirmed) = hub.Progress.BadgeCount();
            confirmed = Mathf.Max(1, confirmed); // this dialog IS a confirmed report — never "gold badge no. 0"
            badges = Mathf.Max(badges, confirmed);
            int next = PlaytestRanks.NextThreshold(badges);
            string progress = next > 0
                ? string.Format(shell.L("ui.testpilot.confirmed.progress"), confirmed, next - badges,
                    shell.L("ui.testpilot.rank" + (PlaytestRanks.RankFor(badges) + 1)))
                : string.Format(shell.L("ui.testpilot.confirmed.progress_top"), confirmed);
            var line = UiKit.AddText(panel, m, y, inner, 28f, progress, 17, UiKit.CyanDim, TextAnchor.MiddleCenter);
            UiKit.FitLabelSingleLine(line, inner, 12, 17);

            var ok = UiKit.AddTierButton(panel, (w - 260f) / 2f, h - 92f, 260f, 62f, shell.L("ui.testpilot.ok"), () => Destroy(gameObject), ButtonTier.Primary);
            if (ok.GetComponentInChildren<Text>() is { } okLabel)
            {
                okLabel.alignment = TextAnchor.MiddleCenter; // a lone button without an icon: its word in the middle
            }
        }

        private void Update()
        {
            if (_canvas != null && InputMap.Down(InputAction.UiCancel))
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
                PlaytestModal.Closed();
                PlaytestHub.Instance?.Acknowledge(_confirmation);
            }

            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
