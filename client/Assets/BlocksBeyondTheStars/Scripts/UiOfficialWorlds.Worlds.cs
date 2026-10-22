// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Collections.Generic;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Client.Portal;
using BlocksBeyondTheStars.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    // The Official-worlds overlay's world dialogs: creating a world and managing one (password, lifecycle, saves, bans).
    internal sealed partial class UiOfficialWorlds
    {
        private async void DoCreateWorld(string worldName, string password, Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.CreateWorld(session, worldName, string.IsNullOrEmpty(password) ? null : password));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                if (r.Code == "terms_outdated") { CloseModal(); PromptReaccept(); return; }
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            CloseModal();
            _status.text = "";
            DoRefresh();
        }

        private void OpenCreateWorld()
        {
            var cDlg = OpenModalPanel(560f, 270f, 800f, 500f);
            UiKit.AddText(cDlg, 30f, 24f, 740f, 30f, _shell.L("ui.portal.new_world_title"), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            string[] worldName = { "" };
            string[] pw1 = { "" };
            string[] pw2 = { "" };
            UiKit.AddText(cDlg, 40f, 70f, 720f, 22f, _shell.L("ui.portal.world_name"), 15, UiKit.TextCol);
            UiKit.AddInput(cDlg, 40f, 94f, 720f, 38f, worldName[0], v => worldName[0] = v);
            UiKit.AddText(cDlg, 40f, 146f, 340f, 22f, _shell.L("ui.portal.world_password"), 15, UiKit.TextCol);
            var cp1 = UiKit.AddInput(cDlg, 40f, 170f, 340f, 38f, pw1[0], v => pw1[0] = v);
            cp1.contentType = InputField.ContentType.Password;
            UiKit.AddText(cDlg, 420f, 146f, 340f, 22f, _shell.L("ui.portal.password_repeat"), 15, UiKit.TextCol);
            var cp2 = UiKit.AddInput(cDlg, 420f, 170f, 340f, 38f, pw2[0], v => pw2[0] = v);
            cp2.contentType = InputField.ContentType.Password;
            var optional = UiKit.AddText(cDlg, 40f, 222f, 720f, 40f, _shell.L("ui.portal.world_password_optional"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            optional.horizontalOverflow = HorizontalWrapMode.Wrap;
            // In-flow pointer to the friends path (password now, list publicly later via Manage) —
            // the Manage dialog is the only other place that explains it, and first-timers skip it.
            var friends = UiKit.AddText(cDlg, 40f, 264f, 720f, 40f, _shell.L("ui.portal.create_friends_hint"), 13, UiKit.Cyan, TextAnchor.UpperLeft);
            friends.horizontalOverflow = HorizontalWrapMode.Wrap;
            var cWarn = UiKit.AddText(cDlg, 40f, 310f, 720f, 44f, "", 14, WarnCol, TextAnchor.UpperLeft, FontStyle.Bold);
            cWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(cDlg, 40f, 366f, 340f, 54f, _shell.L("ui.portal.create"), () =>
            {
                if (string.IsNullOrWhiteSpace(worldName[0])) { cWarn.text = _shell.L("ui.portal.err_world_name_invalid"); return; }
                if (pw1[0] != pw2[0]) { cWarn.text = _shell.L("ui.portal.err_password_mismatch"); return; }
                DoCreateWorld(worldName[0].Trim(), pw1[0], cWarn);
            }, "btn_join");
            UiKit.AddButton(cDlg, 420f, 366f, 340f, 54f, _shell.L("ui.menu.back"), CloseModal, "btn_exit");
        }

        private async void DoSetWorldPassword(string worldId, string password, Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.SetWorldPassword(session, worldId, password));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            warn.color = UiKit.Ok;
            warn.text = _shell.L("ui.portal.pw_updated");
            DoRefresh(); // the list's [PW] marker must follow
        }

        private async void DoSetVisibility(string worldId, bool makePublic, Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.SetWorldVisibility(session, worldId, makePublic));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            warn.color = UiKit.Ok;
            warn.text = _shell.L(makePublic ? "ui.portal.public_on" : "ui.portal.public_off");
            DoRefresh(); // the list's [PUB] marker + the manage toggle must follow on reopen
        }

        private async void DoStopWorld(string worldId, Text warn, Text statusLabel)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.StopWorld(session, worldId));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            warn.color = UiKit.Ok;
            warn.text = _shell.L("ui.portal.stopped");
            if (statusLabel != null) { statusLabel.text = "stopped"; }
            DoRefresh();
        }

        private async void DoDeleteWorld(PortalWorldInfo world, string typedName, Text warn)
        {
            if (typedName.Trim() != world.Name)
            {
                warn.color = WarnCol;
                warn.text = _shell.L("ui.portal.err_delete_name_mismatch");
                return;
            }

            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.DeleteWorld(session, world.Id));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            CloseModal();
            _status.text = _shell.L("ui.portal.deleted");
            DoRefresh();
        }

        private async void DoDownloadSave(string worldId, Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            string path = System.IO.Path.Combine(_saveBackupDir, worldId + "-world.db");
            var r = await Task.Run(() =>
            {
                var dl = portal.DownloadSave(session, worldId);
                if (dl.Ok)
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(_saveBackupDir);
                        System.IO.File.WriteAllBytes(path, dl.Bytes);
                    }
                    catch (System.Exception ex)
                    {
                        return new PortalSaveResult { Error = ex.Message };
                    }
                }

                return dl;
            });
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            warn.color = UiKit.Ok;
            warn.text = _shell.L("ui.portal.save_downloaded");
        }

        private async void DoUploadSave(string worldId, Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            string path = System.IO.Path.Combine(_saveBackupDir, worldId + "-world.db");
            var r = await Task.Run(() =>
            {
                try
                {
                    if (!System.IO.File.Exists(path))
                    {
                        return new PortalSimpleResult { Code = "save_file_missing", Error = "save_file_missing" };
                    }

                    return portal.UploadSave(session, worldId, System.IO.File.ReadAllBytes(path));
                }
                catch (System.Exception ex)
                {
                    return new PortalSimpleResult { Error = ex.Message };
                }
            });
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                if (r.Code == "terms_outdated") { CloseModal(); PromptReaccept(); return; }
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            warn.color = UiKit.Ok;
            warn.text = _shell.L("ui.portal.save_uploaded");
        }

        private void OpenSaveFolder()
        {
            System.IO.Directory.CreateDirectory(_saveBackupDir);
            Application.OpenURL("file:///" + _saveBackupDir.Replace('\\', '/'));
        }

        private void OpenManage(PortalWorldInfo world)
        {
            var mDlg = OpenModalPanel(460f, 150f, 1000f, 780f);
            UiKit.AddText(mDlg, 30f, 24f, 940f, 30f, world.Name, 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var mStatus = UiKit.AddText(mDlg, 40f, 58f, 920f, 24f, world.StatusLabel(_shell.L) + (world.HasPassword ? "  [PW]" : ""), 14,
                world.NeedsNewerServer ? UiKit.Warn : UiKit.CyanDim, TextAnchor.MiddleCenter); // #2500: "needs a server update"
            var mWarn = UiKit.AddText(mDlg, 76f, 596f, 884f, 56f, "", 14, WarnCol, TextAnchor.UpperLeft, FontStyle.Bold);
            mWarn.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddSpinner(mDlg, 42f, 596f, 26f, mWarn); // spins while a manage action is in progress

            // World join password (owner-only; empty remove keeps the world open).
            string[] mp1 = { "" };
            string[] mp2 = { "" };
            UiKit.AddText(mDlg, 40f, 100f, 920f, 22f, _shell.L("ui.portal.world_password"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            var mpi1 = UiKit.AddInput(mDlg, 40f, 126f, 300f, 38f, mp1[0], v => mp1[0] = v);
            mpi1.contentType = InputField.ContentType.Password;
            var mpi2 = UiKit.AddInput(mDlg, 352f, 126f, 300f, 38f, mp2[0], v => mp2[0] = v);
            mpi2.contentType = InputField.ContentType.Password;
            UiKit.AddButton(mDlg, 664f, 122f, 155f, 44f, _shell.L("ui.portal.pw_set"), () =>
            {
                if (mp1[0] != mp2[0]) { mWarn.color = WarnCol; mWarn.text = _shell.L("ui.portal.err_password_mismatch"); return; }
                if (string.IsNullOrEmpty(mp1[0])) { mWarn.color = WarnCol; mWarn.text = _shell.L("ui.portal.err_world_password_invalid"); return; }
                DoSetWorldPassword(world.Id, mp1[0], mWarn);
            }, "btn_join");
            UiKit.AddButton(mDlg, 827f, 122f, 133f, 44f, _shell.L("ui.portal.pw_remove"), () => DoSetWorldPassword(world.Id, "", mWarn), "btn_exit");

            // Lifecycle: stopping is also the precondition for save download/upload.
            UiKit.AddButton(mDlg, 40f, 192f, 300f, 50f, _shell.L("ui.portal.stop"), () => DoStopWorld(world.Id, mWarn, mStatus), "btn_settings");
            var stopHint = UiKit.AddText(mDlg, 360f, 200f, 600f, 40f, _shell.L("ui.portal.stop_hint"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            stopHint.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Save backup round-trip (browser-free): download to / upload from a fixed local folder.
            UiKit.AddText(mDlg, 40f, 262f, 920f, 22f, _shell.L("ui.portal.save_title"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            var saveHint = UiKit.AddText(mDlg, 40f, 288f, 920f, 40f, _shell.L("ui.portal.save_hint"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            saveHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(mDlg, 40f, 338f, 300f, 50f, _shell.L("ui.portal.save_download"), () => DoDownloadSave(world.Id, mWarn), "btn_join");
            UiKit.AddButton(mDlg, 352f, 338f, 300f, 50f, _shell.L("ui.portal.save_upload"), () => DoUploadSave(world.Id, mWarn), "btn_join");
            UiKit.AddButton(mDlg, 664f, 338f, 296f, 50f, _shell.L("ui.portal.save_folder"), OpenSaveFolder, "btn_credits");

            // Danger zone: deletion needs the world's name typed back — a click can't do it by accident.
            UiKit.AddText(mDlg, 40f, 412f, 920f, 22f, _shell.L("ui.portal.delete_world"), 15, UiKit.Warn, TextAnchor.MiddleLeft, FontStyle.Bold);
            var delHint = UiKit.AddText(mDlg, 40f, 438f, 920f, 24f, _shell.L("ui.portal.delete_world_hint"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            delHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            string[] typed = { "" };
            UiKit.AddInput(mDlg, 40f, 468f, 480f, 38f, typed[0], v => typed[0] = v, world.Name);
            UiKit.AddButton(mDlg, 552f, 464f, 408f, 46f, _shell.L("ui.portal.delete_world"), () => DoDeleteWorld(world, typed[0], mWarn), "btn_exit");

            // Public listing (opt-in). Requires a join password — public worlds stay password-gated, so
            // strangers who find the world in the browser still need the owner-shared password to join.
            UiKit.AddText(mDlg, 40f, 522f, 600f, 22f, _shell.L("ui.portal.public_title"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            var pubHint = UiKit.AddText(mDlg, 40f, 548f, 600f, 44f,
                _shell.L(world.HasPassword ? "ui.portal.public_hint" : "ui.portal.public_needs_pw"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            pubHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (world.HasPassword)
            {
                UiKit.AddButton(mDlg, 664f, 540f, 296f, 46f,
                    _shell.L(world.IsPublic ? "ui.portal.make_private" : "ui.portal.make_public"),
                    () => DoSetVisibility(world.Id, !world.IsPublic, mWarn), world.IsPublic ? "btn_exit" : "btn_join");
            }

            // Owner moderation (#497): the world owner's own ban list, the counterpart to the operator's
            // fleet ban. Its own dialog — this one is full, and blocking someone deserves room to read.
            UiKit.AddButton(mDlg, 150f, 700f, 340f, 54f, _shell.L("ui.portal.moderation"), () => OpenWorldBans(world), "btn_settings");
            UiKit.AddButton(mDlg, 510f, 700f, 340f, 54f, _shell.L("ui.menu.back"), CloseModal, "btn_exit");
        }

        // The owner's ban list for one world: who is blocked (with unblock), and who has played here
        // (with block/kick). Names come from the join grants the portal issued — the client has no
        // other way to know who visited, and typing names by hand is how you ban the wrong kid.
        private void OpenWorldBans(PortalWorldInfo world)
        {
            var bDlg = OpenModalPanel(460f, 150f, 1000f, 800f);
            UiKit.AddText(bDlg, 30f, 24f, 940f, 30f, _shell.L("ui.portal.bans_title") + " — " + world.Name, 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            var bHint = UiKit.AddText(bDlg, 40f, 62f, 920f, 44f, _shell.L("ui.portal.bans_hint"), 13, UiKit.CyanDim, TextAnchor.UpperLeft);
            bHint.horizontalOverflow = HorizontalWrapMode.Wrap;
            var bWarn = UiKit.AddText(bDlg, 76f, 110f, 884f, 30f, "", 14, WarnCol, TextAnchor.UpperLeft, FontStyle.Bold);
            UiKit.AddSpinner(bDlg, 42f, 110f, 26f, bWarn);
            string[] reason = { "" };
            var listRoot = UiKit.AddPanel(bDlg, 30f, 148f, 940f, 560f, new Color(0f, 0f, 0f, 0f)).transform;
            UiKit.AddButton(bDlg, 330f, 716f, 340f, 54f, _shell.L("ui.menu.back"), () => OpenManage(world), "btn_exit");

            async void Load()
            {
                bWarn.color = WarnCol;
                bWarn.text = _shell.L("ui.portal.working");
                var portal = new PortalClient(PortalBase());
                string session = _shell.Settings.PortalSessionToken;
                var r = await Task.Run(() => portal.ListWorldBans(session, world.Id));
                if (_overlay == null || listRoot == null) { return; }
                if (!r.Ok)
                {
                    bWarn.text = PortalErr(r.Code, r.Error);
                    return;
                }

                bWarn.text = "";
                for (int i = listRoot.childCount - 1; i >= 0; i--)
                {
                    Object.Destroy(listRoot.GetChild(i).gameObject);
                }

                float y = 0f;
                UiKit.AddText(listRoot, 10f, y, 900f, 24f, _shell.L("ui.portal.bans_title"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 30f;
                if (r.Bans.Count == 0)
                {
                    UiKit.AddText(listRoot, 10f, y, 900f, 24f, _shell.L("ui.portal.bans_none"), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
                    y += 30f;
                }

                foreach (var ban in r.Bans)
                {
                    long banId = ban.Id;
                    string label = ban.PlayerName + (ban.Reason.Length > 0 ? "  —  " + ban.Reason : string.Empty);
                    UiKit.AddText(listRoot, 10f, y, 700f, 34f, label, 14, UiKit.TextCol, TextAnchor.MiddleLeft);
                    UiKit.AddButton(listRoot, 730f, y, 190f, 34f, _shell.L("ui.portal.unblock"), () => DoWorldUnban(world, banId, bWarn, Load), "btn_join");
                    y += 40f;
                }

                y += 16f;
                UiKit.AddText(listRoot, 10f, y, 900f, 24f, _shell.L("ui.portal.bans_visitors"), 15, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 30f;
                var blocked = new System.Collections.Generic.HashSet<string>();
                foreach (var ban in r.Bans)
                {
                    blocked.Add(ban.PlayerName.ToLowerInvariant());
                }

                int shown = 0;
                foreach (var v in r.Visitors)
                {
                    if (blocked.Contains(v.PlayerName.ToLowerInvariant()) || y > 470f)
                    {
                        continue; // already blocked, or the dialog is full — the list is newest-first
                    }

                    string playerName = v.PlayerName;
                    string accountId = v.AccountId;
                    UiKit.AddText(listRoot, 10f, y, 520f, 34f, playerName, 14, UiKit.TextCol, TextAnchor.MiddleLeft);
                    UiKit.AddButton(listRoot, 540f, y, 180f, 34f, _shell.L("ui.portal.kick"),
                        () => DoWorldKick(world, playerName, bWarn), "btn_settings");
                    UiKit.AddButton(listRoot, 730f, y, 190f, 34f, _shell.L("ui.portal.block"),
                        () => DoWorldBan(world, playerName, accountId, reason[0], bWarn, Load), "btn_exit");
                    y += 40f;
                    shown++;
                }

                if (shown == 0 && r.Visitors.Count == 0)
                {
                    UiKit.AddText(listRoot, 10f, y, 900f, 24f, _shell.L("ui.portal.bans_no_visitors"), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
                    y += 30f;
                }

                y += 16f;
                UiKit.AddInput(listRoot, 10f, y, 910f, 38f, reason[0], v => reason[0] = v, _shell.L("ui.portal.block_reason"));
            }

            Load();
        }

        private async void DoWorldBan(PortalWorldInfo world, string playerName, string accountId, string reason, Text warn, System.Action done)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.AddWorldBan(session, world.Id, playerName, accountId, reason ?? ""));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            // The block always holds; the kick behind it only reaches someone who is in the world right
            // now (#502). Saying "blocked ✓" alone would imply they were thrown out — they weren't.
            warn.color = UiKit.Ok;
            warn.text = r.Kicked
                ? _shell.L("ui.portal.blocked_ok")
                : _shell.L("ui.portal.blocked_ok") + " " + _shell.L("ui.portal.kick_not_online");
            done?.Invoke();
        }

        private async void DoWorldUnban(PortalWorldInfo world, long banId, Text warn, System.Action done)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.RemoveWorldBan(session, world.Id, banId));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            warn.text = "";
            done?.Invoke();
        }

        private async void DoWorldKick(PortalWorldInfo world, string playerName, Text warn)
        {
            warn.color = WarnCol;
            warn.text = _shell.L("ui.portal.working");
            var portal = new PortalClient(PortalBase());
            string session = _shell.Settings.PortalSessionToken;
            var r = await Task.Run(() => portal.KickFromWorld(session, world.Id, playerName));
            if (_overlay == null || warn == null) { return; }
            if (!r.Ok)
            {
                warn.text = PortalErr(r.Code, r.Error);
                return;
            }

            // A kick that reached nobody is not a success story — the player may be offline, the world
            // asleep, or the instance still on an image without the kick endpoint (#502).
            warn.color = r.Kicked ? UiKit.Ok : WarnCol;
            warn.text = _shell.L(r.Kicked ? "ui.portal.kicked_ok" : "ui.portal.kick_not_online");
        }
    }
}
#endif
