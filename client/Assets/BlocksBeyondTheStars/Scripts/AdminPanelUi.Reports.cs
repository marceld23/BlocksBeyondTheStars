// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Admin;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The admin panel's Reports tab (#3508): every report filed in this world — the open ones first, then the newest done
    /// ones, as the server sorts them. A row names the kind, who filed it and about whom, when and its state, the note, and
    /// the reported player's chat lines (collapsed). Its buttons: <b>Done</b> / <b>Reopen</b>, <b>Jump</b> to the reported
    /// player (when online) or to the reported place (the invisible admin mode), and <b>Chat with reporter</b>.
    /// </summary>
    public sealed partial class AdminPanelUi
    {
        private const float ReportTextW = 820f, ReportButtonsX = 856f, ReportGap = 10f;
        private const float DoneW = 180f, ReportJumpW = 180f, ReporterChatW = 240f;

        private readonly HashSet<long> _evidenceOpen = new HashSet<long>();

        private void BuildReportsTab()
        {
            _list = AddList("AdminReports", 0f);
        }

        private void RefreshReports(NetworkClient net)
        {
            ClearRows(_list);
            var reports = net.AdminReports.Reports;
            if (reports.Count == 0)
            {
                AddEmptyNote(_list, "ui.admin.reports_none");
                _list.sizeDelta = new Vector2(0f, 64f);
                return;
            }

            float y = 0f;
            foreach (var r in reports)
            {
                y += AddReportRow(net, r, y) + ReportGap;
            }

            _list.sizeDelta = new Vector2(0f, y);
        }

        /// <summary>One report; returns its height.</summary>
        private float AddReportRow(NetworkClient net, NetAdminReport r, float y)
        {
            float width = ContentW - 16f;
            string note = r.Note ?? string.Empty;
            float noteH = note.Length > 0 ? Mathf.Ceil(UiKit.MeasureHeight(note, ReportTextW, 16, FontStyle.Normal, false)) + 6f : 0f;
            bool hasEvidence = r.Evidence != null && r.Evidence.Length > 0;
            bool evidenceOpen = hasEvidence && _evidenceOpen.Contains(r.Id);
            float evidenceH = evidenceOpen ? EvidenceHeight(r.Evidence) : 0f;
            float h = Mathf.Max(112f, 70f + noteH + (hasEvidence ? 48f : 0f) + evidenceH + 10f);

            UiKit.AddPanel(_list, 0f, y, width, h, r.Done ? new Color(0.08f, 0.14f, 0.22f, 0.45f) : new Color(0.24f, 0.17f, 0.08f, 0.55f));
            AddDataText(_list, 14f, y + 8f, ReportTextW, 30f, AdminPanelText.ReportHeadline(r, _loc), 19, r.Done ? UiKit.CyanDim : UiKit.Warn,
                FontStyle.Bold);
            string state = r.Done ? AdminPanelText.Fill(L("ui.admin.report_done_by"), r.HandledBy) : L("ui.admin.report_open");
            AddDataText(_list, 14f, y + 40f, ReportTextW, 24f, $"{AdminPanelText.LocalDateTime(r.CreatedUnix)}  ·  {state}", 15, UiKit.CyanDim);
            float cy = y + 68f;
            if (note.Length > 0)
            {
                UiKit.AddMeasuredText(_list, 14f, cy, ReportTextW, note, 16, UiKit.TextCol, out _).supportRichText = false;
                cy += noteH;
            }

            if (hasEvidence)
            {
                long id = r.Id;
                string count = r.Evidence.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string label = evidenceOpen ? L("ui.admin.evidence_hide") : AdminPanelText.Fill(L("ui.admin.evidence"), count);
                UiKit.AddFitButton(_list, 14f, cy + 4f, 300f, 40f, label, () => ToggleEvidence(id));
                cy += 48f;
                if (evidenceOpen)
                {
                    AddEvidence(r.Evidence, cy);
                }
            }

            AddReportButtons(net, r, y);
            return h;
        }

        private static float EvidenceHeight(string[] lines)
        {
            float h = 0f;
            foreach (string line in lines)
            {
                h += Mathf.Ceil(UiKit.MeasureHeight(line ?? string.Empty, ReportTextW - 20f, 15, FontStyle.Normal, false)) + 2f;
            }

            return h;
        }

        private void AddEvidence(string[] lines, float y)
        {
            foreach (string line in lines)
            {
                var label = UiKit.AddMeasuredText(_list, 34f, y, ReportTextW - 20f, line ?? string.Empty, 15, UiKit.CyanDim, out float h);
                label.supportRichText = false;
                y += h + 2f;
            }
        }

        private void ToggleEvidence(long id)
        {
            if (!_evidenceOpen.Remove(id))
            {
                _evidenceOpen.Add(id);
            }

            Invalidate();
        }

        /// <summary>Done / Reopen, Jump (the reported player when online, else the reported place — the invisible mode only)
        /// and Chat with the reporter (when online).</summary>
        private void AddReportButtons(NetworkClient net, NetAdminReport r, float y)
        {
            long id = r.Id;
            bool done = r.Done;
            float x = ReportButtonsX, by = y + 10f;
            UiKit.AddFitButton(_list, x, by, DoneW, 46f, L(done ? "ui.admin.reopen" : "ui.admin.done"), () => net.SendAdminReportAction(id, !done));
            x += DoneW + ReportGap;

            if (Invisible)
            {
                string targetId = r.TargetId;
                if (!string.IsNullOrEmpty(targetId) && net.AdminPlayers.Find(targetId) != null)
                {
                    UiKit.AddFitButton(_list, x, by, ReportJumpW, 46f, L("ui.admin.jump"), () => JumpToPlayer(targetId));
                }
                else if (r.HasPosition)
                {
                    string location = r.LocationId;
                    float px = r.X, py = r.Y, pz = r.Z;
                    UiKit.AddFitButton(_list, x, by, ReportJumpW, 46f, L("ui.admin.jump_there"), () => JumpToPlace(location, px, py, pz));
                }
            }

            x += ReportJumpW + ReportGap;
            if (!string.IsNullOrEmpty(r.ReporterId) && net.AdminPlayers.Find(r.ReporterId) != null)
            {
                string reporterId = r.ReporterId, reporterName = r.ReporterName;
                UiKit.AddFitButton(_list, x, by, ReporterChatW, 46f, L("ui.admin.chat_reporter"), () => OpenConversation(reporterId, reporterName));
            }
        }
    }
}
