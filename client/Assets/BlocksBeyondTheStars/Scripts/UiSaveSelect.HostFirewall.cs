// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The host screen's firewall step (#3708): see <see cref="HostFirewall"/>.</summary>
    public static partial class UiSaveSelect
    {
        /// <summary>
        /// Starts the hosted world through <paramref name="host"/> — at once when Windows Firewall already lets
        /// friends reach the bundled server (or there is nothing to ask about), otherwise after the dialog that
        /// explains the one permission hosting needs. The dialog is built on demand, after every other widget of
        /// the screen, so it draws and takes clicks on top.
        /// </summary>
        private static void HostThroughFirewall(AppShell shell, Transform root, System.Action host)
        {
            string exe = LocalServerLauncher.ServerExePath();
            int port = LocalServerLauncher.DefaultPort;
            var state = HostFirewall.Skipped ? HostFirewallState.Allowed : HostFirewall.Check(exe, port);
            if (state == HostFirewallState.Allowed)
            {
                host();
                return;
            }

            HostFirewallPrompt.Open(shell, root, state, exe, port, host);
        }
    }

    /// <summary>
    /// The dialog in front of a hosted world that Windows Firewall would not let anybody into (#3708): what is
    /// needed and who can give it, then "allow now" (one elevation prompt, off the main thread), "host anyway"
    /// or back. After "allow now" the rules are read again — the rule list decides, not the helper's exit.
    /// </summary>
    internal sealed class HostFirewallPrompt : MonoBehaviour
    {
        private AppShell _shell;
        private string _exe;
        private int _port;
        private System.Action _host;
        private Text _status;
        private bool _busy;
        private volatile bool _answered;

        public static void Open(AppShell shell, Transform root, HostFirewallState state, string exe, int port, System.Action host)
        {
            var (overlay, panel) = UiKit.AddModalOverlay(root, 460f, 320f, 1000f, 440f);
            var prompt = overlay.AddComponent<HostFirewallPrompt>();
            prompt._shell = shell;
            prompt._exe = exe;
            prompt._port = port;
            prompt._host = host;

            UiKit.AddText(panel, 30f, 22f, 940f, 40f, shell.L("ui.host.firewall_title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            string body = shell.L(state == HostFirewallState.Blocked ? "ui.host.firewall_blocked" : "ui.host.firewall_none");
            UiKit.AddText(panel, 40f, 76f, 920f, 132f, body, 20, UiKit.TextCol, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Wrap;
            string detail = shell.L("ui.host.firewall_detail").Replace("{port}", port.ToString());
            UiKit.AddText(panel, 40f, 214f, 920f, 56f, detail, 15, UiKit.CyanDim, TextAnchor.MiddleCenter).horizontalOverflow = HorizontalWrapMode.Wrap;
            prompt._status = UiKit.AddText(panel, 40f, 278f, 920f, 30f, string.Empty, 16, UiKit.Warn, TextAnchor.MiddleCenter);

            UiKit.AddButton(panel, 40f, 336f, 290f, 58f, shell.L("ui.host.firewall_allow"), prompt.Allow, "btn_singleplayer");
            UiKit.AddButton(panel, 355f, 336f, 290f, 58f, shell.L("ui.host.firewall_skip"), prompt.HostAnyway, "btn_join");
            UiKit.AddButton(panel, 670f, 336f, 290f, 58f, shell.L("ui.menu.back"), prompt.Close, "btn_exit");
            UiKit.OpenModal(overlay);
        }

        private void Allow()
        {
            if (_busy)
            {
                return;
            }

            _busy = true;
            _answered = false;
            _status.text = _shell.L("ui.host.firewall_waiting");

            // The elevation prompt blocks until it is answered — never on the main thread.
            string exe = _exe;
            int port = _port;
            var thread = new Thread(() =>
            {
                HostFirewall.RequestAllow(exe, port);
                _answered = true;
            })
            {
                IsBackground = true,
                Name = "HostFirewall",
            };
            try
            {
                thread.SetApartmentState(ApartmentState.STA); // what the Windows shell prefers for an elevation request
            }
            catch (System.Exception)
            {
                // Not supported on this runtime: the default apartment starts an .exe just as well.
            }

            thread.Start();
        }

        private void Update()
        {
            if (!_busy || !_answered)
            {
                return;
            }

            _busy = false;
            if (HostFirewall.Check(_exe, _port) == HostFirewallState.Allowed)
            {
                Launch();
            }
            else
            {
                _status.text = _shell.L("ui.host.firewall_failed");
            }
        }

        private void HostAnyway()
        {
            if (_busy)
            {
                return;
            }

            HostFirewall.Skipped = true; // asked and answered: not again in this session
            Launch();
        }

        private void Launch()
        {
            var host = _host;
            Destroy(gameObject);
            host?.Invoke();
        }

        private void Close()
        {
            if (!_busy)
            {
                Destroy(gameObject);
            }
        }
    }
}
