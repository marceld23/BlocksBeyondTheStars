using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// The singleplayer world picker (uGUI): lists existing save worlds to resume and lets the player
    /// start a brand-new world by name (a new name = a new, differently-seeded world), so different
    /// worlds can be tested without overwriting. Shown on <see cref="ShellPhase.SaveSelect"/> over the
    /// menu backdrop; AppShell spawns/destroys it per phase.
    /// </summary>
    public static class UiSaveSelect
    {
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("SaveSelectUI");
            var root = canvas.transform;

            UiKit.AddLogo(root, 360f, 70f, 900f, 96f, shell.L("ui.save.title"), 56);
            UiKit.AddText(root, 364f, 180f, 1000f, 26f, shell.L("ui.save.subtitle"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);

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
                    UiKit.AddButton(left, 20f, 56f + i * 62f, 612f, 54f, $"▸  {w}", () => shell.StartSingleplayerWorld(w), "btn_singleplayer");
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

            UiKit.AddButton(right, 20f, 372f, 320f, 50f, shell.L("ui.save.create"),
                () => shell.StartSingleplayerWorld(name[0], 0, creative[0] && optBlueprints[0], creative[0] && optShips[0], creative[0] && optKit[0]), "btn_singleplayer");
            UiKit.AddButton(right, 360f, 372f, 320f, 50f, shell.L("ui.save.random"),
                () => shell.StartSingleplayerWorld("world_" + Random.Range(1000, 999999), 0, creative[0] && optBlueprints[0], creative[0] && optShips[0], creative[0] && optKit[0]), "btn_join");
            UiKit.AddText(right, 20f, 436f, 660f, 90f, shell.L("ui.save.hint"), 14, UiKit.CyanDim, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;

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
