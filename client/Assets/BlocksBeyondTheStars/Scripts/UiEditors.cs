using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The "Editors" submenu (uGUI): a single main-menu entry opens this screen, which gathers the
    /// content-creation tools — the ship designer, avatar designer, station editor and town editor —
    /// each on its own framed button, plus Back. Shown over the animated <see cref="MenuBackground"/>;
    /// AppShell spawns it on the <see cref="ShellPhase.Editors"/> phase and destroys it on leaving.
    /// </summary>
    public static class UiEditors
    {
        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("EditorsUI");
            var root = canvas.transform;

            UiKit.AddLogo(root, 360f, 80f, 800f, 96f, shell.L("ui.editors.title"), 60);
            UiKit.AddText(root, 364f, 184f, 900f, 26f, shell.L("ui.editors.subtitle"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);

            const float bx = 360f, bw = 520f, bh = 58f, gap = 66f;

            // Group 1 — in-game creator tools (their output flows into your own game/worlds).
            float y = 244f;
            UiKit.AddText(root, bx, y, bw, 24f, shell.L("ui.editors.group.creator"), 15, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
            UiKit.AddButton(root, bx, y, bw, bh, shell.L("ui.menu.ship_editor"), shell.OpenShipEditor, "btn_singleplayer"); y += gap;
            UiKit.AddButton(root, bx, y, bw, bh, shell.L("ui.menu.avatar_editor"), shell.OpenAvatarEditor, "btn_singleplayer"); y += gap;
            UiKit.AddButton(root, bx, y, bw, bh, shell.L("ui.menu.station_editor"), shell.OpenStationEditor, "btn_singleplayer"); y += gap;
            UiKit.AddButton(root, bx, y, bw, bh, shell.L("ui.menu.settlement_editor"), shell.OpenSettlementEditor, "btn_singleplayer"); y += gap + 14f;

            // Group 2 — developer content tools (need a merge + rebuild; not applied to your current game).
            UiKit.AddText(root, bx, y, bw, 24f, shell.L("ui.editors.group.dev"), 15, UiKit.Warn, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 30f;
            UiKit.AddButton(root, bx, y, bw, bh, shell.L("ui.menu.content_editor"), shell.OpenContentEditor, "btn_settings"); y += gap;
            UiKit.AddButton(root, bx, y, bw, bh, shell.L("ui.menu.material_editor"), shell.OpenMaterialEditor, "btn_settings"); y += gap + 14f;

            UiKit.AddButton(root, bx, y, bw, bh, shell.L("ui.menu.back"), () => shell.GoTo(ShellPhase.MainMenu), "btn_exit");

            // Description panel (right): what the editors export + the developer-tool caveat.
            UiKit.AddPanel(root, 980f, 244f, 560f, 420f, UiKit.PanelFill);
            UiKit.AddText(root, 1004f, 262f, 520f, 24f, shell.L("ui.editors.info_title"), 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 1004f, 300f, 520f, 250f, shell.L("ui.editors.info_body"), 16, UiKit.TextCol, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.AddText(root, 1004f, 560f, 520f, 90f, shell.L("ui.editors.dev.note"), 14, UiKit.Warn, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;

            return canvas.gameObject;
        }
    }
}
