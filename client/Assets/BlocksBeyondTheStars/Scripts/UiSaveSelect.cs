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

        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("SaveSelectUI");
            var root = canvas.transform;

            // Host mode ("Host Game" on the main menu): the same picker — any singleplayer save can be
            // hosted, "open to LAN" style — plus a host bar (max players + optional join password).
            bool host = shell.HostMode;
            int[] maxPlayers = { 4 };
            string[] hostPass = { "" };
            void Launch(string world, bool unlockAll = false, bool allShips = false, bool kit = false, WorldCreationOptions options = null)
            {
                if (host)
                {
                    shell.StartHostWorld(world, maxPlayers[0], hostPass[0], 0, unlockAll, allShips, kit, options);
                }
                else
                {
                    shell.StartSingleplayerWorld(world, 0, unlockAll, allShips, kit, options);
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
            UiKit.AddText(right, 20f, 54f, 660f, 24f, shell.L("ui.save.name"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);

            string[] name = { "new_world" };
            UiKit.AddInput(right, 20f, 82f, 660f, 34f, name[0], v => name[0] = v, maxLength: 24);

            // Mode: Explorer (normal) vs Creative (everything unlocked + a starter set; survival mechanics stay on).
            bool[] creative = { false };
            bool[] optBlueprints = { true };
            bool[] optShips = { true };
            bool[] optKit = { true };

            UiKit.AddText(right, 20f, 124f, 660f, 24f, shell.L("ui.save.mode"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            Button explorerBtn = null, creativeBtn = null;
            GameObject creativePanel = null;
            void RefreshMode()
            {
                if (explorerBtn != null) explorerBtn.image.color = creative[0] ? UiKit.PanelFill : UiKit.Cyan;
                if (creativeBtn != null) creativeBtn.image.color = creative[0] ? UiKit.Cyan : UiKit.PanelFill;
                if (creativePanel != null) creativePanel.SetActive(creative[0]);
            }

            explorerBtn = UiKit.AddButton(right, 20f, 152f, 320f, 46f, shell.L("ui.save.mode_explorer"), () => { creative[0] = false; RefreshMode(); });
            creativeBtn = UiKit.AddButton(right, 360f, 152f, 320f, 46f, shell.L("ui.save.mode_creative"), () => { creative[0] = true; RefreshMode(); });

            // Creative sub-options (a checklist; shown only when Creative is selected).
            var cp = UiKit.AddPanel(right, 20f, 206f, 660f, 150f, new Color(0.05f, 0.10f, 0.16f, 0.55f));
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
            RefreshMode(); // start on Explorer (sub-options hidden)

            // World options (sliders + presets): collected here, baked into the save at creation.
            var worldOptions = new WorldCreationOptions();
            GameObject optionsOverlay = null;
            UiKit.AddButton(right, 20f, 372f, 660f, 46f, shell.L("ui.worldopt.open"), () =>
            {
                optionsOverlay ??= UiWorldOptions.Build(shell, root, worldOptions);
                optionsOverlay.SetActive(true);
            });

            UiKit.AddButton(right, 20f, 428f, 320f, 50f, shell.L("ui.save.create"),
                () => Launch(name[0], creative[0] && optBlueprints[0], creative[0] && optShips[0], creative[0] && optKit[0], worldOptions), "btn_singleplayer");
            UiKit.AddButton(right, 360f, 428f, 320f, 50f, shell.L("ui.save.random"),
                () => Launch("world_" + Random.Range(1000, 999999), creative[0] && optBlueprints[0], creative[0] && optShips[0], creative[0] && optKit[0], worldOptions), "btn_join");
            UiKit.AddText(right, 20f, 486f, 660f, 50f, shell.L("ui.save.hint"), 14, UiKit.CyanDim, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;

            // ── Host options (host mode only): player cap + optional join password ───────────────
            if (host)
            {
                var bar = UiKit.AddPanel(root, 850f, 800f, 700f, 100f, UiKit.PanelFill).transform;
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
            }

            UiKit.AddButton(root, 90f, 920f, 240f, 50f, shell.L("ui.menu.back"), () => shell.GoTo(ShellPhase.MainMenu), "btn_exit");

            // ── Delete confirmation (added last so it draws on top; hidden until a ✕ is pressed) ──────
            var dim = UiKit.AddImage(root, 0f, 0f, 1920f, 1080f, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0.6f));
            confirm = dim.gameObject;
            dim.raycastTarget = true; // swallow clicks behind the dialog
            var panel = UiKit.AddPanel(confirm.transform, 610f, 420f, 700f, 250f, UiKit.Panel).transform;
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
