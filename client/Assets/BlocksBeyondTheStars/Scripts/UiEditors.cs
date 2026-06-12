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

            UiKit.AddLogo(root, 360f, 90f, 800f, 96f, shell.L("ui.editors.title"), 60);
            UiKit.AddText(root, 364f, 200f, 900f, 26f, shell.L("ui.editors.subtitle"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);

            const float bx = 360f, bw = 520f, bh = 64f, gap = 80f;
            float by = 300f;
            UiKit.AddButton(root, bx, by, bw, bh, shell.L("ui.menu.ship_editor"), shell.OpenShipEditor, "btn_singleplayer");
            UiKit.AddButton(root, bx, by + gap, bw, bh, shell.L("ui.menu.avatar_editor"), shell.OpenAvatarEditor, "btn_settings");
            UiKit.AddButton(root, bx, by + gap * 2f, bw, bh, shell.L("ui.menu.station_editor"), shell.OpenStationEditor, "btn_singleplayer");
            UiKit.AddButton(root, bx, by + gap * 3f, bw, bh, shell.L("ui.menu.settlement_editor"), shell.OpenSettlementEditor, "btn_singleplayer");
            UiKit.AddButton(root, bx, by + gap * 4f, bw, bh, shell.L("ui.menu.content_editor"), shell.OpenContentEditor, "btn_settings");
            UiKit.AddButton(root, bx, by + gap * 5f, bw, bh, shell.L("ui.menu.material_editor"), shell.OpenMaterialEditor, "btn_settings");
            UiKit.AddButton(root, bx, by + gap * 6f + 16f, bw, bh, shell.L("ui.menu.back"), () => shell.GoTo(ShellPhase.MainMenu), "btn_exit");

            // Description panel (right) explaining what the editors export.
            UiKit.AddPanel(root, 980f, 300f, 560f, 360f, UiKit.PanelFill);
            UiKit.AddText(root, 1004f, 318f, 520f, 24f, shell.L("ui.editors.info_title"), 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(root, 1004f, 356f, 520f, 280f, shell.L("ui.editors.info_body"), 16, UiKit.TextCol, TextAnchor.UpperLeft).horizontalOverflow = HorizontalWrapMode.Wrap;

            return canvas.gameObject;
        }
    }
}
