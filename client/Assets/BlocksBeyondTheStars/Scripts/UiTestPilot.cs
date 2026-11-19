// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The "Test Pilot" page (desktop builds): the playtest tasks the report inbox offers, this install's rank and
    /// badges, and — one level in — a task with its steps to tick off and the way to its closing report. Reached from
    /// the main menu's "Join in" dialog and from the pause menu. The page only shows what <see cref="PlaytestHub"/>
    /// holds; it is rebuilt whenever the hub's <see cref="PlaytestHub.Revision"/> moves (AppShell.TestPilot.cs).
    /// The texts of a TASK come from the fetched catalogue (German and English); everything else is localized as usual.
    /// </summary>
    public static class UiTestPilot
    {
        /// <summary>The task whose view is open; null or empty = the list.</summary>
        public static string OpenTaskId;

        private const float Top = UiKit.MenuContentTop, CardH = 740f, Pad = 28f;
        private const float ListW = 1150f, SideX = UiKit.MenuLeft + ListW + 30f, SideW = UiKit.MenuRight - SideX;
        private const float RowH = 96f, RowGap = 10f;
        private const float AboutW = 620f, StepsX = UiKit.MenuLeft + AboutW + 30f, StepsW = UiKit.MenuRight - StepsX;

        public static GameObject Build(AppShell shell)
        {
            var hub = PlaytestHub.Instance;
            var canvas = UiKit.CreateCanvas("TestPilotUI");
            UiNav.Enable(canvas.gameObject);
            var task = hub != null ? hub.Tracker.Find(OpenTaskId) : null;
            if (task != null)
            {
                BuildTask(canvas.transform, shell, hub, task);
            }
            else
            {
                OpenTaskId = null;
                BuildList(canvas.transform, shell, hub);
            }

            return canvas.gameObject;
        }

        // ---------------------------------------------------------------- the list

        private static void BuildList(Transform root, AppShell shell, PlaytestHub hub)
        {
            var back = UiKit.AddScreenHeader(root, shell.L("ui.testpilot.title"), shell.L("ui.testpilot.subtitle"),
                shell.CloseTestPilot, shell.L("ui.menu.back"));

            var list = UiKit.AddCard(root, UiKit.MenuLeft, Top, ListW, CardH);
            UiKit.AddKicker(list, Pad, 20f, ListW - (2f * Pad), shell.L("ui.testpilot.tasks"));
            if (hub == null || hub.Tracker.Tasks.Count == 0)
            {
                string why = hub == null || !hub.Configured ? "ui.testpilot.not_configured"
                    : hub.Fetching ? "ui.testpilot.fetching"
                    : "ui.testpilot.empty";
                var note = UiKit.AddText(list, Pad, 70f, ListW - (2f * Pad), 120f, shell.L(why), 20, UiKit.CyanDim, TextAnchor.UpperLeft);
                note.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            else
            {
                AddTaskRows(list, shell, hub);
            }

            if (hub != null)
            {
                AddSide(root, shell, hub);
            }

            back.transform.SetAsLastSibling(); // a pad's first focus is the first task, not the way out
        }

        private static void AddTaskRows(Transform list, AppShell shell, PlaytestHub hub)
        {
            float viewH = CardH - 70f - Pad, rowW = ListW - (2f * Pad) - 22f;
            var content = UiKit.AddScrollViewport(list, "Tasks", Pad, 60f, ListW - (2f * Pad), viewH, out var scroll);
            float y = 0f;
            foreach (var task in hub.Tracker.Tasks)
            {
                AddTaskRow(content, shell, hub, task, y, rowW);
                y += RowH + RowGap;
            }

            content.sizeDelta = new Vector2(0f, Mathf.Max(viewH, y));
            if (y > viewH)
            {
                UiKit.AddVerticalScrollbar(list, scroll, ListW - Pad - 12f, 60f, 12f, viewH);
            }
        }

        private static void AddTaskRow(Transform content, AppShell shell, PlaytestHub hub, PlaytestTask task, float y, float rowW)
        {
            string id = task.Id;
            var row = UiKit.AddButton(content, 0f, y, rowW, RowH, task.Title.For(shell.Settings.Language), () => Open(id));
            UiKit.StyleTier(row, ButtonTier.Secondary);
            var label = row.GetComponentInChildren<Text>();
            const float chipW = 250f;
            float textW = rowW - chipW - 60f;
            UiKit.Place(label.gameObject, 24f, 12f, textW, 40f);
            label.alignment = TextAnchor.MiddleLeft;
            UiKit.FitLabelSingleLine(label, textW, 16, 24);

            var sub = UiKit.AddText(row.transform, 24f, 54f, textW, 28f, WhereLine(shell, task), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(sub, textW, 12, 16);

            var status = hub.Progress.StatusOf(task);
            AddChip(row.transform, rowW - chipW - 20f, (RowH - 32f) / 2f, chipW, StatusText(shell, hub, task, status), StatusColor(status));
        }

        /// <summary>"In the game · about 10 minutes · brings a test world".</summary>
        private static string WhereLine(AppShell shell, PlaytestTask task)
        {
            string where = task.Area == PlaytestAreas.Editor ? shell.L(EditorNameKey(task.Editor)) : shell.L("ui.testpilot.where_ingame");
            if (task.Minutes > 0)
            {
                where += " · " + string.Format(shell.L("ui.testpilot.minutes"), task.Minutes);
            }

            if (task.World != null && task.Area != PlaytestAreas.Editor)
            {
                where += " · " + shell.L("ui.testpilot.has_world");
            }

            return where;
        }

        /// <summary>The menu name of an editor a task is about.</summary>
        public static string EditorNameKey(string editor)
        {
            switch (editor)
            {
                case PlaytestEditorEvents.Ship: return "ui.menu.ship_editor";
                case PlaytestEditorEvents.Avatar: return "ui.menu.avatar_editor";
                case PlaytestEditorEvents.Station: return "ui.menu.station_editor";
                case PlaytestEditorEvents.Settlement: return "ui.menu.settlement_editor";
                case PlaytestEditorEvents.Texture: return "ui.menu.texture_editor";
                case PlaytestEditorEvents.Form: return "ui.menu.form_editor";
                case PlaytestEditorEvents.ToolLook: return "ui.menu.tool_looks";
                default: return "ui.menu.editors";
            }
        }

        private static string StatusText(AppShell shell, PlaytestHub hub, PlaytestTask task, PlaytestTaskStatus status)
        {
            switch (status)
            {
                case PlaytestTaskStatus.InProgress:
                    return string.Format(shell.L("ui.testpilot.status.in_progress"), task.Steps.Count - hub.Progress.OpenSteps(task), task.Steps.Count);
                case PlaytestTaskStatus.ReadyToReport: return shell.L("ui.testpilot.status.ready");
                case PlaytestTaskStatus.Submitted: return shell.L("ui.testpilot.status.submitted");
                case PlaytestTaskStatus.Confirmed: return shell.L("ui.testpilot.status.confirmed");
                case PlaytestTaskStatus.Retest: return shell.L("ui.testpilot.status.retest");
                default: return shell.L("ui.testpilot.status.new");
            }
        }

        private static Color StatusColor(PlaytestTaskStatus status)
        {
            switch (status)
            {
                case PlaytestTaskStatus.Submitted: return UiKit.Ok;
                case PlaytestTaskStatus.Confirmed: return Gold;
                case PlaytestTaskStatus.InProgress:
                case PlaytestTaskStatus.ReadyToReport: return UiKit.Cyan;
                default: return UiKit.Warn;
            }
        }

        /// <summary>The gold of a confirmed report (the achievement toast's gold).</summary>
        public static readonly Color Gold = new Color(1f, 0.82f, 0.3f);

        /// <summary>A pill with a short text in <paramref name="color"/>.</summary>
        public static void AddChip(Transform parent, float x, float y, float w, string text, Color color)
        {
            UiKit.AddImage(parent, x, y, w, 32f, UiKit.PillSprite, new Color(color.r, color.g, color.b, 0.16f), Image.Type.Sliced);
            var label = UiKit.AddText(parent, x + 10f, y, w - 20f, 32f, text, 16, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.FitLabelSingleLine(label, w - 20f, 11, 16);
        }

        // ---------------------------------------------------------------- rank and badges

        private static void AddSide(Transform root, AppShell shell, PlaytestHub hub)
        {
            var side = UiKit.AddCard(root, SideX, Top, SideW, CardH);
            float w = SideW - (2f * Pad);
            var (badges, confirmed) = hub.Progress.BadgeCount();
            int rank = PlaytestRanks.RankFor(badges);
            int next = PlaytestRanks.NextThreshold(badges);

            UiKit.AddKicker(side, Pad, 20f, w, shell.L("ui.testpilot.rank"));
            UiKit.AddIcon(side, Pad, 56f, 120f, "pt_rank" + Mathf.Max(1, rank)).color = rank > 0 ? Color.white : new Color(1f, 1f, 1f, 0.3f);
            var name = UiKit.AddText(side, Pad + 140f, 66f, w - 140f, 44f, shell.L("ui.testpilot.rank" + rank), 28, UiKit.TextCol,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(name, w - 140f, 16, 28);
            string progress = next > 0
                ? string.Format(shell.L("ui.testpilot.rank_progress"), badges, next - badges, shell.L("ui.testpilot.rank" + (rank + 1)))
                : string.Format(shell.L("ui.testpilot.rank_top"), badges);
            var line = UiKit.AddText(side, Pad + 140f, 112f, w - 140f, 52f, progress, 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            line.horizontalOverflow = HorizontalWrapMode.Wrap;

            // The way to the next rank, counted from the first badge on: a bar that starts empty again with every rank
            // looked like nothing had been earned right after the first badge.
            var bar = UiKit.AddProgressBar(side, Pad, 194f, w, 12f);
            bar.Set(next > 0 ? Mathf.Clamp01(badges / (float)next) : 1f);

            UiKit.AddKicker(side, Pad, 236f, w, shell.L("ui.testpilot.badges"));
            AddBadgeGrid(side, w, badges, confirmed);

            var legend = UiKit.AddText(side, Pad, 540f, w, 56f, shell.L("ui.testpilot.legend"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            legend.horizontalOverflow = HorizontalWrapMode.Wrap;

            string fetched = hub.Fetching ? shell.L("ui.testpilot.fetching")
                : hub.FetchedUnix > 0 ? string.Format(shell.L("ui.testpilot.fetched"),
                    UiKit.TimeAgo(shell.L, DateTimeOffset.FromUnixTimeSeconds(hub.FetchedUnix).UtcDateTime))
                : shell.L("ui.testpilot.never");
            var when = UiKit.AddText(side, Pad, CardH - 96f, w, 24f, fetched, 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(when, w, 11, 15);
            UiKit.AddTierButton(side, Pad, CardH - 66f, 260f, 44f, shell.L("ui.testpilot.reload"), () => hub.Refresh(playerAsked: true),
                ButtonTier.Quiet);
        }

        /// <summary>Eight places, four per row: gold for a confirmed report, cyan for a sent one, a faint ring for a
        /// place still free. More than eight badges show as the last place's number.</summary>
        private static void AddBadgeGrid(Transform side, float w, int badges, int confirmed)
        {
            const int Columns = 4, Places = 8;
            const float size = 104f;
            float gap = (w - (Columns * size)) / (Columns - 1);
            for (int i = 0; i < Places; i++)
            {
                float x = Pad + ((i % Columns) * (size + gap)), y = 272f + ((i / Columns) * (size + 18f));
                bool earned = i < badges;
                var icon = UiKit.AddIcon(side, x, y, size, earned && i < confirmed ? "pt_badge_gold" : "pt_badge");
                icon.color = earned ? Color.white : new Color(1f, 1f, 1f, 0.14f);
            }

            if (badges > Places)
            {
                UiKit.AddText(side, Pad, 272f + (2f * (size + 18f)) - 14f, w, 24f, "+" + (badges - Places), 18, UiKit.Cyan,
                    TextAnchor.MiddleRight, FontStyle.Bold);
            }
        }

        // ---------------------------------------------------------------- one task

        /// <summary>Opens a task's view; a task that can still be worked on becomes the one being worked on.</summary>
        public static void Open(string taskId)
        {
            OpenTaskId = taskId;
            var hub = PlaytestHub.Instance;
            if (hub != null)
            {
                hub.Activate(taskId); // refused for a task whose report is sent — its view is then read-only
            }
        }

        private static void BuildTask(Transform root, AppShell shell, PlaytestHub hub, PlaytestTask task)
        {
            string lang = shell.Settings.Language;
            var back = UiKit.AddScreenHeader(root, task.Title.For(lang), WhereLine(shell, task), () =>
            {
                OpenTaskId = null;
                shell.RebuildTestPilot();
            }, shell.L("ui.menu.back"));

            var status = hub.Progress.StatusOf(task);
            bool sent = status == PlaytestTaskStatus.Submitted || status == PlaytestTaskStatus.Confirmed;
            AddAbout(root, shell, hub, task, status, sent);
            AddSteps(root, shell, hub, task, sent);
            back.transform.SetAsLastSibling();
        }

        private static void AddAbout(Transform root, AppShell shell, PlaytestHub hub, PlaytestTask task, PlaytestTaskStatus status, bool sent)
        {
            string lang = shell.Settings.Language;
            var card = UiKit.AddCard(root, UiKit.MenuLeft, Top, AboutW, CardH);
            float w = AboutW - (2f * Pad), y = 20f;
            UiKit.AddKicker(card, Pad, y, w, shell.L("ui.testpilot.about"));
            y += 34f;
            UiKit.AddMeasuredText(card, Pad, y, w, task.Intro.For(lang), 19, UiKit.TextCol, out float introH);
            y += Mathf.Min(introH, 300f) + 24f;

            if (!sent && task.World != null && task.Area != PlaytestAreas.Editor)
            {
                y = AddWorldBlock(card, shell, hub, task, y, w);
            }

            if (sent)
            {
                string note = status == PlaytestTaskStatus.Confirmed ? "ui.testpilot.confirmed_note" : "ui.testpilot.sent_note";
                var done = UiKit.AddText(card, Pad, CardH - 130f, w, 96f, shell.L(note), 20, status == PlaytestTaskStatus.Confirmed ? Gold : UiKit.Ok,
                    TextAnchor.LowerLeft, FontStyle.Bold);
                done.horizontalOverflow = HorizontalWrapMode.Wrap;
                return;
            }

            int open = hub.Progress.OpenSteps(task);
            string openText = open == 0 ? shell.L("ui.testpilot.open_none")
                : open == 1 ? shell.L("ui.testpilot.open_one")
                : string.Format(shell.L("ui.testpilot.open_many"), open);
            UiKit.AddText(card, Pad, CardH - 124f, w, 26f, openText, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            var report = UiKit.AddTierButton(card, Pad, CardH - 90f, w, 62f, shell.L("ui.testpilot.report"), () =>
            {
                if (hub.Progress.OpenSteps(task) == 0)
                {
                    PlaytestReportDialog.Open(shell, task);
                }
            }, ButtonTier.Primary, "btn_feedback");
            report.interactable = open == 0;
        }

        /// <summary>The test world's lines and its button; returns the next free y.</summary>
        private static float AddWorldBlock(Transform card, AppShell shell, PlaytestHub hub, PlaytestTask task, float y, float w)
        {
            UiKit.AddKicker(card, Pad, y, w, shell.L("ui.testpilot.world"));
            y += 30f;
            var refusal = hub.WorldRefusal(task);
            bool inWorld = shell.CurrentBoot != null;
            string hintKey = refusal == PlaytestWorldRefusal.UnknownPlanet ? "ui.testpilot.world_unknown_planet"
                : inWorld ? "ui.testpilot.world_leave_first"
                : "ui.testpilot.world_hint";
            UiKit.AddMeasuredText(card, Pad, y, w, shell.L(hintKey), 16, UiKit.CyanDim, out float hintH);
            y += hintH + 12f;
            var create = UiKit.AddTierButton(card, Pad, y, w, 56f, shell.L("ui.testpilot.world_create"), () => hub.StartTestWorld(task),
                ButtonTier.Secondary, "btn_singleplayer");
            create.interactable = refusal == PlaytestWorldRefusal.None && !inWorld;
            return y + 76f;
        }

        private static void AddSteps(Transform root, AppShell shell, PlaytestHub hub, PlaytestTask task, bool sent)
        {
            var card = UiKit.AddCard(root, StepsX, Top, StepsW, CardH);
            UiKit.AddKicker(card, Pad, 20f, StepsW - (2f * Pad), shell.L("ui.testpilot.steps"));
            float viewH = CardH - 60f - Pad, rowW = StepsW - (2f * Pad) - 22f;
            var content = UiKit.AddScrollViewport(card, "Steps", Pad, 60f, StepsW - (2f * Pad), viewH, out var scroll);
            float y = 0f;
            for (int i = 0; i < task.Steps.Count; i++)
            {
                y += PlaytestStepRow.Add(content, shell, hub, task, task.Steps[i], i + 1, y, rowW, sent) + RowGap;
            }

            content.sizeDelta = new Vector2(0f, Mathf.Max(viewH, y));
            if (y > viewH)
            {
                UiKit.AddVerticalScrollbar(card, scroll, StepsW - Pad - 12f, 60f, 12f, viewH);
            }
        }
    }

    /// <summary>
    /// One step of a task as a row — on the Test Pilot page and in the editors' list alike: its number or tick, the
    /// text, and what closes it. A step the game recognises shows its count and closes by itself; one it cannot
    /// recognise (or one for a server that does not report events) has the two buttons "Done" and "Did not work".
    /// </summary>
    public static class PlaytestStepRow
    {
        /// <summary>Adds the row at <paramref name="y"/> and returns its height.</summary>
        public static float Add(Transform parent, AppShell shell, PlaytestHub hub, PlaytestTask task, PlaytestStep step, int number,
            float y, float rowW, bool readOnly, bool compact = false)
        {
            string lang = shell.Settings.Language;
            var state = hub.Progress.StepOf(task, step.Id);
            bool open = state.State == PlaytestStepStates.Open;
            bool failed = state.State == PlaytestStepStates.Failed;
            bool recognisable = step.Check != null && !(IsServerEvent(step.Check.Event) && hub.ServerReports(step.Check.Event) == false);
            bool buttons = open && !readOnly && !recognisable;
            int size = compact ? 16 : 19;
            float buttonsW = buttons ? (compact ? 0f : 340f) : 0f, chipW = buttons ? 0f : (compact ? 0f : 230f);
            float textX = compact ? 44f : 70f, textW = rowW - textX - 20f - buttonsW - chipW;
            float textH = UiKit.MeasureHeight(step.Text.For(lang), textW, size, FontStyle.Normal, false);
            string sub = SubLine(shell, state, open, failed, recognisable);
            float subH = sub.Length > 0 ? UiKit.MeasureHeight(sub, textW, 14, FontStyle.Normal, false) + 4f : 0f;
            float h = Mathf.Max(compact ? 44f : 84f, textH + subH + (compact ? 14f : 30f)) + (buttons && compact ? 50f : 0f);

            var row = compact ? AddPlainRow(parent, y, rowW, h) : UiKit.AddCard(parent, 0f, y, rowW, h);
            AddTick(row, compact ? 6f : 18f, compact ? 6f : (h - 40f) / 2f, compact ? 30f : 40f, open ? number.ToString() : failed ? "✕" : "✔",
                open ? UiKit.CyanDim : failed ? UiKit.DangerCol : UiKit.Ok);
            float ty = compact ? 6f : 14f;
            var text = UiKit.AddText(row, textX, ty, textW, textH + 4f, step.Text.For(lang), size, UiKit.TextCol, TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (sub.Length > 0)
            {
                var subText = UiKit.AddText(row, textX, ty + textH + 4f, textW, subH, sub, 14, UiKit.CyanDim, TextAnchor.UpperLeft);
                subText.horizontalOverflow = HorizontalWrapMode.Wrap;
            }

            if (buttons)
            {
                AddButtons(row, shell, hub, task, step, compact ? textX : rowW - buttonsW - 10f, compact ? h - 48f : (h - 52f) / 2f, compact);
            }
            else if (!compact)
            {
                AddState(row, shell, hub, task, step, state, open, failed, readOnly, rowW - chipW - 16f, (h - 32f) / 2f, chipW);
            }

            return h;
        }

        private static bool IsServerEvent(string key)
            => !PlaytestEditorEvents.IsEditorEvent(key) && !BlocksBeyondTheStars.Shared.Story.GameEventKeys.IsUiEvent(key);

        private static string SubLine(AppShell shell, PlaytestStepProgress state, bool open, bool failed, bool recognisable)
        {
            if (failed && state.Note.Length > 0)
            {
                return string.Format(shell.L("ui.testpilot.step_note"), state.Note);
            }

            if (!open)
            {
                return string.Empty;
            }

            return shell.L(recognisable ? "ui.testpilot.step_auto_hint" : "ui.testpilot.step_manual_hint");
        }

        private static Transform AddPlainRow(Transform parent, float y, float w, float h)
        {
            var go = new GameObject("Step", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, 0f, y, w, h);
            return go.transform;
        }

        private static void AddTick(Transform row, float x, float y, float size, string mark, Color color)
        {
            UiKit.AddImage(row, x, y, size, size, UiKit.PillSprite, new Color(color.r, color.g, color.b, 0.18f), Image.Type.Sliced);
            UiKit.AddText(row, x, y, size, size, mark, size > 34f ? 20 : 15, color, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        private static void AddButtons(Transform row, AppShell shell, PlaytestHub hub, PlaytestTask task, PlaytestStep step, float x, float y, bool compact)
        {
            float w = compact ? 150f : 160f, h = compact ? 42f : 52f;
            string stepId = step.Id;
            UiKit.AddTierButton(row, x, y, w, h, shell.L("ui.testpilot.step_done"),
                () => hub.SetStep(task, stepId, PlaytestStepStates.Done, null), ButtonTier.Secondary);
            UiKit.AddTierButton(row, x + w + 10f, y, w, h, shell.L("ui.testpilot.step_failed"),
                () => PlaytestNotePrompt.Open(shell, note => hub.SetStep(task, stepId, PlaytestStepStates.Failed, note)), ButtonTier.Quiet);
        }

        private static void AddState(Transform row, AppShell shell, PlaytestHub hub, PlaytestTask task, PlaytestStep step,
            PlaytestStepProgress state, bool open, bool failed, bool readOnly, float x, float y, float w)
        {
            if (open)
            {
                if (step.Check != null && step.Check.Required > 1)
                {
                    UiTestPilot.AddChip(row, x, y, w, string.Format(shell.L("ui.testpilot.step_count"), state.Count, step.Check.Required), UiKit.Cyan);
                }

                return;
            }

            string text = failed ? shell.L("ui.testpilot.step_failed")
                : state.Auto ? shell.L("ui.testpilot.step_recognised_chip")
                : shell.L("ui.testpilot.step_done");
            UiTestPilot.AddChip(row, x, y - (state.Auto || readOnly ? 0f : 18f), w, text, failed ? UiKit.DangerCol : UiKit.Ok);
            if (!state.Auto && !readOnly)
            {
                // What the player ticked, the player may take back.
                string stepId = step.Id;
                UiKit.AddLinkButton(row, x, y + 20f, w, 28f, shell.L("ui.testpilot.step_undo"),
                    () => hub.SetStep(task, stepId, PlaytestStepStates.Open, null));
            }
        }
    }
}
