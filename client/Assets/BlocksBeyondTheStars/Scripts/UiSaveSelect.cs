// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The singleplayer world picker (uGUI): lists existing save worlds to resume and lets the player
    /// start a brand-new world by name (a new name = a new, differently-seeded world), so different
    /// worlds can be tested without overwriting. Shown on <see cref="ShellPhase.SaveSelect"/> over the
    /// menu backdrop; AppShell spawns/destroys it per phase.
    /// </summary>
    public static class UiSaveSelect
    {
        /// <summary>Compact playtime for a save-list row: "12 h 30 min", "45 min", or "&lt;1 min" (h/min are
        /// understood in both German and English, so the figure needs no localization).</summary>
        private static string FormatPlaytime(long totalSeconds)
        {
            long minutes = totalSeconds / 60;
            if (minutes <= 0) return "<1 min";
            long h = minutes / 60, m = minutes % 60;
            return h > 0 ? $"{h} h {m} min" : $"{m} min";
        }

        /// <summary>
        /// Characters a world name may not contain. Deliberately the WINDOWS invalid-filename set on every
        /// platform rather than <c>Path.GetInvalidFileNameChars()</c>, which on Linux is only <c>/</c>: a save
        /// folder should mean the same thing wherever the game runs, and a <c>"</c> would additionally break
        /// the quoted <c>--world "…"</c> argument the launcher builds.
        /// <para>
        /// A player typed <c>Minecraft Wo bin ich?:(</c> and reported that he "cannot create a world with
        /// THIS name" — nothing rejected it, nothing warned him; the name was quietly mangled into
        /// <c>Minecraft Wo bin ich__(</c> deep inside the save layer, and the picker then listed a world he
        /// never named. Strip it here, in front of him, where he can see what he is getting.
        /// </para>
        /// </summary>
        private static readonly char[] IllegalNameChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

        /// <summary>The world name as it will actually be stored: illegal characters dropped, whitespace
        /// collapsed and trimmed. Empty when nothing usable is left.</summary>
        public static string SanitizeWorldName(string raw) => SanitizeWorldName(raw, trimEnd: true);

        /// <summary>
        /// <paramref name="trimEnd"/> is false while the player is still TYPING. A trailing space is illegal
        /// in a save name, but stripping it on every keystroke means the space between two words vanishes the
        /// moment you press it and the name can never grow past one word. Trailing whitespace is therefore
        /// only removed when the name is actually used.
        /// </summary>
        public static string SanitizeWorldName(string raw, bool trimEnd)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder(raw.Length);
            bool lastWasSpace = false;
            foreach (char c in raw)
            {
                if (char.IsControl(c) || System.Array.IndexOf(IllegalNameChars, c) >= 0)
                {
                    continue;
                }

                bool space = c == ' ';
                if (space && (lastWasSpace || sb.Length == 0))
                {
                    continue; // no leading or doubled spaces
                }

                lastWasSpace = space;
                sb.Append(c);
            }

            // A trailing dot or space names an unopenable file on Windows, so they go — at use time.
            return trimEnd ? sb.ToString().TrimEnd(' ', '.') : sb.ToString();
        }

        /// <summary>True when a save with this name already exists (case-insensitive, like the filesystem).</summary>
        private static bool WorldExists(string name)
        {
            foreach (string w in LocalServerLauncher.ListWorlds())
            {
                if (string.Equals(w, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("SaveSelectUI");
            var root = canvas.transform;
            UiNav.Enable(canvas.gameObject); // gamepad can pick a world (inert on KB/mouse)

            // Host mode ("Host Game" on the main menu): the same picker — any singleplayer save can be
            // hosted, "open to LAN" style — plus a host bar (max players + optional join password).
            bool host = shell.HostMode;
            int[] maxPlayers = { 12 };
            string[] hostPass = { "" };
            void Launch(string world, bool unlockAll = false, bool allShips = false, bool kit = false, bool sandbox = false,
                WorldCreationOptions options = null, bool flight = false)
            {
                if (host)
                {
                    shell.StartHostWorld(world, maxPlayers[0], hostPass[0], 0, unlockAll, allShips, kit, sandbox, options, flight);
                }
                else
                {
                    shell.StartSingleplayerWorld(world, 0, unlockAll, allShips, kit, sandbox, options, flight);
                }
            }

            UiKit.AddLogo(root, 360f, 70f, 900f, 96f, shell.L(host ? "ui.host.title" : "ui.save.title"), 56);
            UiKit.AddText(root, 364f, 180f, 1000f, 26f, shell.L(host ? "ui.host.subtitle" : "ui.save.subtitle"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);

            // ── Existing worlds (left) ────────────────────────────────────────────────────────
            var left = UiKit.AddPanel(root, 90f, 250f, 720f, 640f, UiKit.PanelFill).transform;
            UiKit.AddText(left, 20f, 16f, 680f, 26f, shell.L("ui.save.existing"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);

            // Delete-confirmation dialog (built below, shown on demand) — captured by the row buttons.
            GameObject confirm = null;
            Text confirmText = null;
            string[] target = { null };

            var worlds = LocalServerLauncher.ListWorlds();
            if (worlds.Length == 0)
            {
                UiKit.AddText(left, 20f, 70f, 680f, 28f, shell.L("ui.save.none"), 17, UiKit.TextCol, TextAnchor.MiddleLeft);
            }
            else
            {
                int shown = Mathf.Min(worlds.Length, 9);
                for (int i = 0; i < shown; i++)
                {
                    string w = worlds[i];
                    long played = LocalServerLauncher.ReadWorldPlaytimeSeconds(w);
                    string label = played > 0 ? $"▸  {w}    ({FormatPlaytime(played)})" : $"▸  {w}";
                    UiKit.AddButton(left, 20f, 56f + i * 62f, 612f, 54f, label, () => Launch(w), "btn_singleplayer");
                    UiKit.AddButton(left, 640f, 56f + i * 62f, 60f, 54f, "✕", () =>
                    {
                        target[0] = w;
                        if (confirmText != null) confirmText.text = shell.L("ui.save.delete_confirm").Replace("{world}", w);
                        if (confirm != null) confirm.SetActive(true);
                    }, "btn_exit");
                }
            }

            // ── New world (right) ─────────────────────────────────────────────────────────────
            var right = UiKit.AddPanel(root, 850f, 250f, 700f, 540f, UiKit.PanelFill).transform;
            UiKit.AddText(right, 20f, 16f, 660f, 26f, shell.L("ui.save.new"), 18, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            var nameLabel = UiKit.AddText(right, 20f, 54f, 660f, 24f, shell.L("ui.save.name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);

            // The name is sanitized AS IT IS TYPED, so the box always shows exactly what the save will be
            // called — no silent mangling three layers down, and a "?" simply never appears rather than
            // turning into an underscore behind the player's back. A one-line note says when something was
            // dropped, and Create refuses an empty or duplicate name out loud instead of half-working.
            // The label doubles as the message line — the panel has no spare row, and a warning right above
            // the box is where the eye already is.
            string[] name = { "new_world" };
            InputField nameInput = null;
            void SayName(string key, bool warn)
            {
                if (nameLabel == null)
                {
                    return;
                }

                nameLabel.text = shell.L(key);
                nameLabel.color = warn ? UiKit.Warn : UiKit.TextCol;
            }

            nameInput = UiKit.AddInput(right, 20f, 82f, 660f, 34f, name[0], v =>
            {
                string clean = SanitizeWorldName(v, trimEnd: false); // still typing — keep a trailing space
                name[0] = clean;
                if (clean != v && nameInput != null)
                {
                    // Re-entrant by design: the setter fires onChange again with `clean`, and SanitizeWorldName
                    // is idempotent, so the second pass changes nothing and the recursion stops there.
                    nameInput.text = clean;
                    SayName("ui.save.name_stripped", true);
                }
                else
                {
                    SayName("ui.save.name", false);
                }
            }, maxLength: 24);

            // Mode: Explorer (normal) vs Creative (everything unlocked + a starter set; survival mechanics
            // stay on) vs Sandbox (#662: free crafting, no oxygen/hunger, peaceful, everything unlocked).
            int[] mode = { 0 }; // 0 Explorer · 1 Creative · 2 Sandbox
            bool[] optBlueprints = { true };
            bool[] optShips = { true };
            bool[] optKit = { true };

            UiKit.AddText(right, 20f, 124f, 660f, 24f, shell.L("ui.save.mode"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            Button explorerBtn = null, creativeBtn = null, sandboxBtn = null;
            GameObject creativePanel = null;
            GameObject sandboxHint = null;
            void RefreshMode()
            {
                if (explorerBtn != null) explorerBtn.image.color = mode[0] == 0 ? UiKit.Cyan : UiKit.PanelFill;
                if (creativeBtn != null) creativeBtn.image.color = mode[0] == 1 ? UiKit.Cyan : UiKit.PanelFill;
                if (sandboxBtn != null) sandboxBtn.image.color = mode[0] == 2 ? UiKit.Cyan : UiKit.PanelFill;
                if (creativePanel != null) creativePanel.SetActive(mode[0] == 1);
                if (sandboxHint != null) sandboxHint.SetActive(mode[0] == 2);
            }

            explorerBtn = UiKit.AddButton(right, 20f, 152f, 210f, 46f, shell.L("ui.save.mode_explorer"), () => { mode[0] = 0; RefreshMode(); });
            creativeBtn = UiKit.AddButton(right, 245f, 152f, 210f, 46f, shell.L("ui.save.mode_creative"), () => { mode[0] = 1; RefreshMode(); });
            sandboxBtn = UiKit.AddButton(right, 470f, 152f, 210f, 46f, shell.L("ui.save.mode_sandbox"), () => { mode[0] = 2; RefreshMode(); });

            // Creative sub-options (a checklist; shown only when Creative is selected).
            // 166 tall, not 150: the three toggles end at y=134 and the flight line sits under them.
            var cp = UiKit.AddPanel(right, 20f, 206f, 660f, 166f, new Color(0.05f, 0.10f, 0.16f, 0.55f));
            creativePanel = cp.gameObject;
            void Toggle(float y, string label, bool[] state)
            {
                Button b = null;
                string Fmt() => (state[0] ? "[X]  " : "[  ]  ") + label;
                b = UiKit.AddButton(cp.transform, 12f, y, 636f, 38f, Fmt(), () =>
                {
                    state[0] = !state[0];
                    var t = b.GetComponentInChildren<Text>();
                    if (t != null) t.text = Fmt();
                });
            }

            Toggle(8f, shell.L("ui.save.opt_blueprints"), optBlueprints);
            Toggle(52f, shell.L("ui.save.opt_ships"), optShips);
            Toggle(96f, shell.L("ui.save.opt_kit"), optKit);

            // Flight is not a checkbox — it comes with both non-Explorer modes — but it has to be SAID, or it
            // stays as invisible as the /fly command it replaces. This is the line the player was missing.
            UiKit.AddText(cp.transform, 12f, 136f, 636f, 24f, shell.L("ui.save.creative_flight"), 14,
                UiKit.Cyan, TextAnchor.MiddleLeft);

            // Sandbox: no checklist — everything is on by design; a short hint says what the mode does.
            var sh = UiKit.AddText(right, 20f, 210f, 660f, 140f,
                shell.L("ui.save.sandbox_hint") + "\n" + shell.L("ui.save.creative_flight"), 15,
                UiKit.CyanDim, TextAnchor.UpperLeft);
            sh.horizontalOverflow = HorizontalWrapMode.Wrap;
            sandboxHint = sh.gameObject;
            RefreshMode(); // start on Explorer (sub-options hidden)

            // World options (sliders + presets): collected here, baked into the save at creation.
            var worldOptions = new WorldCreationOptions();
            GameObject optionsOverlay = null;
            UiKit.AddButton(right, 20f, 372f, 660f, 46f, shell.L("ui.worldopt.open"), () =>
            {
                optionsOverlay ??= UiWorldOptions.Build(shell, root, worldOptions);
                optionsOverlay.SetActive(true);
            });

            // Creative honours the checklist; Sandbox forces every grant on (free crafting makes blueprints
            // moot, but owning all ships + the kit keeps testing friction-free).
            bool UnlockAll() => mode[0] == 2 || (mode[0] == 1 && optBlueprints[0]);
            bool AllShips() => mode[0] == 2 || (mode[0] == 1 && optShips[0]);
            bool Kit() => mode[0] == 2 || (mode[0] == 1 && optKit[0]);

            // Both non-Explorer modes fly. Sandbox would get it from --game-mode anyway; Creative is the mode
            // a Minecraft player picks *in order to* fly, so it has to grant it explicitly.
            bool Flight() => mode[0] != 0;

            // Create validates instead of half-working: an empty name and a name that already exists both
            // say so, right above the box, and nothing is launched.
            void CreateNamed()
            {
                string world = SanitizeWorldName(name[0]);
                if (string.IsNullOrEmpty(world))
                {
                    SayName("ui.save.name_empty", true);
                    return;
                }

                if (WorldExists(world))
                {
                    SayName("ui.save.name_taken", true);
                    return;
                }

                Launch(world, UnlockAll(), AllShips(), Kit(), mode[0] == 2, worldOptions, Flight());
            }

            UiKit.AddButton(right, 20f, 428f, 320f, 50f, shell.L("ui.save.create"), CreateNamed, "btn_singleplayer");
            UiKit.AddButton(right, 360f, 428f, 320f, 50f, shell.L("ui.save.random"),
                () => Launch("world_" + Random.Range(1000, 999999), UnlockAll(), AllShips(), Kit(), mode[0] == 2, worldOptions, Flight()), "btn_join");
            UiKit.AddText(right, 20f, 486f, 660f, 50f, shell.L("ui.save.hint"), 14, UiKit.CyanDim, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;

            // ── Host options (host mode only): player cap + optional join password ───────────────
            if (host)
            {
                var bar = UiKit.AddPanel(root, 850f, 800f, 700f, 186f, UiKit.PanelFill).transform;
                UiKit.AddText(bar, 20f, 8f, 300f, 24f, shell.L("ui.host.max_players"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                Text count = null;
                UiKit.AddButton(bar, 20f, 38f, 46f, 46f, "-", () =>
                {
                    maxPlayers[0] = Mathf.Max(2, maxPlayers[0] - 1);
                    if (count != null) count.text = maxPlayers[0].ToString();
                });
                count = UiKit.AddText(bar, 74f, 38f, 60f, 46f, maxPlayers[0].ToString(), 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.AddButton(bar, 142f, 38f, 46f, 46f, "+", () =>
                {
                    maxPlayers[0] = Mathf.Min(16, maxPlayers[0] + 1);
                    if (count != null) count.text = maxPlayers[0].ToString();
                });
                UiKit.AddText(bar, 240f, 8f, 440f, 24f, shell.L("ui.host.password"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddInput(bar, 240f, 38f, 440f, 46f, hostPass[0], v => hostPass[0] = v);

                // The join address, HERE and not only in the chat once the world is up (#984): the host
                // reads it out (or copies it) while worldgen runs, so the friends can be typing already.
                // Resolved on build — the picker re-reads the live interface list, so unplugging the cable
                // and reopening this screen shows the Wi-Fi address instead of a stale one.
                string joinAddress = AppShell.LanJoinAddress();
                UiKit.AddText(bar, 20f, 92f, 400f, 24f, shell.L("ui.host.your_address"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddText(bar, 20f, 116f, 520f, 32f, joinAddress, 20, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                Text addressHint = null;
                UiKit.AddButton(bar, 552f, 112f, 128f, 40f, shell.L("ui.host.copy"), () =>
                {
                    GUIUtility.systemCopyBuffer = joinAddress;
                    if (addressHint != null)
                    {
                        // The hint line doubles as the confirmation — a clipboard copy is otherwise
                        // completely invisible, and the bar has no room for a toast.
                        addressHint.text = shell.L("ui.host.copied");
                        addressHint.color = UiKit.Cyan;
                    }
                });
                addressHint = UiKit.AddText(bar, 20f, 152f, 660f, 28f, shell.L("ui.host.address_hint"), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
            }

            UiKit.AddButton(root, 90f, 920f, 240f, 50f, shell.L("ui.menu.back"), () => shell.GoTo(ShellPhase.MainMenu), "btn_exit");

            // ── Delete confirmation (added last so it draws on top; hidden until a ✕ is pressed) ──────
            var (confirmOverlay, panel) = UiKit.AddModalOverlay(root, 610f, 420f, 700f, 250f);
            confirm = confirmOverlay;
            confirmText = UiKit.AddText(panel.transform, 30f, 30f, 640f, 80f, string.Empty, 20, UiKit.TextCol, TextAnchor.MiddleCenter);
            confirmText.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddButton(panel.transform, 40f, 160f, 290f, 58f, shell.L("ui.save.delete_yes"), () =>
            {
                if (!string.IsNullOrEmpty(target[0]))
                {
                    LocalServerLauncher.DeleteWorld(target[0]);
                }

                shell.RefreshSaveSelect(); // force a rebuild so the deleted world drops off the list (B59)
            }, "btn_exit");
            UiKit.AddButton(panel.transform, 370f, 160f, 290f, 58f, shell.L("ui.save.delete_no"), () => confirm.SetActive(false), "btn_singleplayer");
            confirm.SetActive(false);

            return canvas.gameObject;
        }
    }
}
