// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The Game category of the settings screen (#3666): how the game plays along (cargo, radio calls, festivals),
    /// the play-time display and break reminder, the character's colours and the software update.</summary>
    public sealed partial class UiSettings
    {
        private void GameRows(ref float y)
        {
            Kicker(ref y, L("ui.settings.group.play"));
            Toggle(ref y, L("ui.settings.auto_stow"), S.AutoStowOnBoard, () => { S.AutoStowOnBoard = !S.AutoStowOnBoard; Rebuild(); },
                L("ui.settings.auto_stow_info"));
            // NPC radio calls (#1119): the server initiates them, so a change is pushed to it right away (and again on every
            // join — see GameBootstrap's join sequence).
            Cycle(ref y, L("ui.settings.npc_calls"), L(NpcCallsLabel(S.NpcCalls)), dir =>
            {
                S.NpcCalls = Wrap(S.NpcCalls + dir, 3);
                SendNpcCalls();
                Rebuild();
            }, L("ui.settings.npc_calls_info"));

            FestivalRows(ref y);

            Kicker(ref y, L("ui.settings.group.playtime"));
            Toggle(ref y, L("ui.settings.show_session_time"), S.ShowSessionTime, () => { S.ShowSessionTime = !S.ShowSessionTime; Rebuild(); },
                L("ui.settings.show_session_time_info"));
            Toggle(ref y, L("ui.settings.playtime_reminder"), S.PlaytimeReminder, () => { S.PlaytimeReminder = !S.PlaytimeReminder; Rebuild(); },
                L("ui.settings.playtime_reminder_info"));
            // Reminder interval: 15-minute steps from 15 min up to 4 hours; without effect while the reminder is off.
            Stepper(ref y, L("ui.settings.reminder_interval"), (S.ReminderMinutes - 15) / 225f,
                () => { S.ReminderMinutes = Mathf.Clamp(S.ReminderMinutes - 15, 15, 240); Rebuild(); },
                () => { S.ReminderMinutes = Mathf.Clamp(S.ReminderMinutes + 15, 15, 240); Rebuild(); },
                S.ReminderMinutes + " min");

            Kicker(ref y, L("ui.settings.character"));
            ColorRow(ref y, L("ui.settings.skin"), S.SkinColor, c => S.SkinColor = c);
            ColorRow(ref y, L("ui.settings.torso"), S.TorsoColor, c => S.TorsoColor = c);
            ColorRow(ref y, L("ui.settings.arms"), S.ArmColor, c => S.ArmColor = c);
            ColorRow(ref y, L("ui.settings.legs"), S.LegColor, c => S.LegColor = c);

#if !UNITY_WEBGL || UNITY_EDITOR
            UpdateRows(ref y); // the browser loads the newest build with the page — there is no updater to set up (ClientUpdater)
#endif
        }

        /// <summary>Tells a joined game's server the NPC-calls choice (+ the VEGA hints mute, #2462).</summary>
        private void SendNpcCalls() => FindAnyObjectByType<GameBootstrap>()?.Network?.SendSetNpcCalls(S.NpcCalls, !S.VegaHints);

        /// <summary>#3021 "My festivals": which festivals of the real-world calendar this player celebrates — a preset (the
        /// row cycles big + my language → all → my language → none) or the own list of "Choose festivals…". Sent to a joined
        /// game's server at once; the menu card follows. #3004: the cosmetic mood they bring on this client.</summary>
        private void FestivalRows(ref float y)
        {
            Kicker(ref y, L("ui.settings.group.festivals"));
            Cycle(ref y, L("ui.settings.festivals"), L("ui.festival.preset." + (int)FestivalMood.Preset(S)), dir =>
            {
                S.FestivalPreset = Wrap((int)FestivalMood.Preset(S) + dir, 4); // the own list is chosen in the picker, not here
                FestivalMood.SendPrefs(S);
                _shell.RefreshMainMenu();
                Rebuild();
            }, L("ui.settings.festivals_hint"));
            var choose = Row(ref y, string.Empty, L("ui.settings.festivals_hint"));
            choose.GetComponent<UiSettingsRowInfo>().Label = L("ui.settings.festival_choose");
            UiKit.AddButton(choose, CtrlX, 0f, CtrlW, RowH, L("ui.settings.festival_choose"),
                () => UiFestivalPicker.Open(_shell, _root, () => { _shell.RefreshMainMenu(); Rebuild(); }));
            Toggle(ref y, L("ui.settings.festival_mood"), S.FestivalMood, () => { S.FestivalMood = !S.FestivalMood; FestivalMood.Enabled = S.FestivalMood; Rebuild(); });
        }

        /// <summary>The software update (Velopack): the feed, the check at startup, and a manual check with its status.</summary>
        private void UpdateRows(ref float y)
        {
            Kicker(ref y, L("ui.settings.updates"));
            var feed = Row(ref y, L("ui.settings.update_url"), null);
            UiKit.AddInput(feed, CtrlX, 4f, CtrlW, RowH - 8f, S.UpdateFeedUrl, v => S.UpdateFeedUrl = (v ?? string.Empty).Trim());
            Toggle(ref y, L("ui.settings.update_on_start"), S.UpdateCheckOnStart,
                () => { S.UpdateCheckOnStart = !S.UpdateCheckOnStart; Rebuild(); });

            var check = Row(ref y, string.Empty, null);
            check.GetComponent<UiSettingsRowInfo>().Label = L("ui.settings.update_check");
            UiKit.AddButton(check, 0f, 0f, LabelW, RowH,
                ClientUpdater.Busy ? L("ui.settings.update_checking") : L("ui.settings.update_check"),
                () => { if (!ClientUpdater.Busy) ClientUpdater.CheckForUpdates(S.UpdateFeedUrl, () => { if (this != null) Rebuild(); }); });
            var status = UiKit.AddText(check, CtrlX, 0f, CtrlW, RowH, UpdateStatusText(), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            // Download progress ticks up to 100 times — refresh this one label in place rather than rebuilding the whole
            // screen per percent (state changes still come through onChanged → Rebuild).
            int shownProgress = ClientUpdater.Progress;
            UiKit.EveryFrame(status.gameObject, () =>
            {
                if (ClientUpdater.Progress != shownProgress)
                {
                    shownProgress = ClientUpdater.Progress;
                    status.text = UpdateStatusText();
                }
            });
        }
    }
}
