// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The "Editors" hub (uGUI, #3667). The menu template's header (Back to the main menu, the title, the subtitle), then
    /// the creator tools as a grid of cards — each with its own icon, its name, one line on what it makes and, where the
    /// result can be sent to the developers, a "Can be submitted" badge — and in the grid's last cell a help card with the
    /// controls the editors share. Below it, set apart on its own panel, the two developer tools (item &amp; recipe, material)
    /// with their warning said once. Every card opens its editor exactly as the old button column did. Shown over the
    /// animated <see cref="MenuBackground"/>; AppShell spawns it on the <see cref="ShellPhase.Editors"/> phase and destroys
    /// it on leaving.
    /// </summary>
    public static class UiEditors
    {
        /// <summary>A card of the grid: four per row across the template's content width (<see cref="UiKit.MenuWidth"/>).</summary>
        public const float CardW = 420f, CardH = 184f, CardGap = 20f;

        /// <summary>Where the grid starts: under the section label at the template's content top.</summary>
        public const float GridTop = UiKit.MenuContentTop + 32f;

        // Inside a card: the icon top left, the name beside it, the description under both, the badge at the foot.
        private const float CardPad = 22f, CardIcon = 64f, NameX = CardPad + CardIcon + 18f, NameW = CardW - NameX - 18f, NameH = 64f;
        private const float DescY = 96f, DescW = CardW - (2f * CardPad), DescH = 46f, BadgeY = 148f, BadgeH = 24f;

        // The help card's lines.
        private const float HelpTitleX = CardPad + 48f, HelpTitleW = CardW - (2f * CardPad) - 48f, HelpLineY = 64f, HelpLineH = 28f;

        /// <summary>The developer section: one opaque panel under the grid holding its label, the warning and two cards.</summary>
        public const float DevTop = GridTop + (2f * CardH) + CardGap + 30f, DevH = 262f, DevCardH = 150f, DevPad = 24f;

        private const float DevInnerW = UiKit.MenuWidth - (2f * DevPad);

        private static readonly string[] HelpLines = { "ui.editors.help.look", "ui.editors.help.fly", "ui.editors.help.place", "ui.editors.help.remove" };

        public static GameObject Build(AppShell shell)
        {
            var canvas = UiKit.CreateCanvas("EditorsUI");
            var root = canvas.transform;
            UiNav.Enable(canvas.gameObject); // a gamepad walks the cards (inert on keyboard/mouse)

            var back = UiKit.AddScreenHeader(root, shell.L("ui.editors.title"), shell.L("ui.editors.subtitle"),
                () => shell.GoTo(ShellPhase.MainMenu), shell.L("ui.menu.back"));
            UiKit.FitButtonLabel(back.GetComponentInChildren<Text>()); // the editors' rule: ≥ 14 px on one line (#3263)

            AddCreatorGrid(root, shell);
            AddDeveloperSection(root, shell);

            // Last in the hierarchy, so a gamepad's first focus is the first editor, not the way out (B leaves anyway).
            back.transform.SetAsLastSibling();
            return canvas.gameObject;
        }

        /// <summary>The seven creator tools, four per row, and the help card in the eighth cell.</summary>
        private static void AddCreatorGrid(Transform root, AppShell shell)
        {
            UiKit.AddKicker(root, UiKit.MenuLeft, UiKit.MenuContentTop, UiKit.MenuWidth, shell.L("ui.editors.group.creator"));
            string submit = shell.L("ui.editors.badge_submit");

            // What can be sent to the developers: a ship, a skin, a station, a village and a texture ("Submit" in each).
            AddEditorCard(root, Cell(0), Row(0), CardH, "cat_buildship", shell.L("ui.menu.ship_editor"), shell.L("ui.editors.desc.ship"), submit,
                shell.OpenShipEditor);
            AddEditorCard(root, Cell(1), Row(0), CardH, "cat_suit", shell.L("ui.menu.avatar_editor"), shell.L("ui.editors.desc.avatar"), submit,
                shell.OpenAvatarEditor);
            AddEditorCard(root, Cell(2), Row(0), CardH, "map_station", shell.L("ui.menu.station_editor"), shell.L("ui.editors.desc.station"), submit,
                shell.OpenStationEditor);
            AddEditorCard(root, Cell(3), Row(0), CardH, "map_settlement", shell.L("ui.menu.settlement_editor"), shell.L("ui.editors.desc.settlement"),
                submit, shell.OpenSettlementEditor);
            AddEditorCard(root, Cell(0), Row(1), CardH, "ed_texture", shell.L("ui.menu.texture_editor"), shell.L("ui.editors.desc.texture"), submit,
                shell.OpenTextureEditor);
            AddEditorCard(root, Cell(1), Row(1), CardH, "ed_shape", shell.L("ui.menu.form_editor"), shell.L("ui.editors.desc.form"), null,
                shell.OpenFormEditor);
            AddEditorCard(root, Cell(2), Row(1), CardH, "cat_tools", shell.L("ui.menu.tool_looks"), shell.L("ui.editors.desc.tool_looks"), null,
                shell.OpenToolLookEditor);
            AddHelpCard(root, Cell(3), Row(1), shell);
        }

        /// <summary>The developer tools on their own, quieter panel: their output needs a merge + rebuild, so the warning
        /// stands once above the two cards.</summary>
        private static void AddDeveloperSection(Transform root, AppShell shell)
        {
            var panel = UiKit.AddCard(root, UiKit.MenuLeft, DevTop, UiKit.MenuWidth, DevH);
            UiKit.AddKicker(panel, DevPad, 16f, DevInnerW, shell.L("ui.editors.group.dev"), UiKit.Warn);
            var note = UiKit.AddText(panel, DevPad, 44f, DevInnerW, 40f, shell.L("ui.editors.dev.note"), 15, UiKit.Warn, TextAnchor.UpperLeft);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;

            float y = 96f;
            var content = AddEditorCard(panel, DevPad, y, DevCardH, "cat_components", shell.L("ui.menu.content_editor"),
                shell.L("ui.editors.desc.content"), null, shell.OpenContentEditor);
            var material = AddEditorCard(panel, DevPad + CardW + CardGap, y, DevCardH, "cat_blocks", shell.L("ui.menu.material_editor"),
                shell.L("ui.editors.desc.material"), null, shell.OpenMaterialEditor);
            Subdue(content);
            Subdue(material);
        }

        /// <summary>The x of grid column <paramref name="column"/>.</summary>
        private static float Cell(int column) => UiKit.MenuLeft + (column * (CardW + CardGap));

        /// <summary>The y of grid row <paramref name="row"/>.</summary>
        private static float Row(int row) => GridTop + (row * (CardH + CardGap));

        /// <summary>
        /// One editor as a card: the whole card is the button (mouse and pad alike) on the template's opaque secondary fill,
        /// its icon top left, the name beside it (one line, two at the most), one line below on what the editor makes, and
        /// the <paramref name="badge"/> at the foot when there is one.
        /// </summary>
        private static Button AddEditorCard(Transform parent, float x, float y, float h, string icon, string name, string description,
            string badge, System.Action open)
        {
            var card = UiKit.AddButton(parent, x, y, CardW, h, name, open);
            UiKit.StyleTier(card, ButtonTier.Secondary); // first: the tier recolours every child it finds
            var label = card.GetComponentInChildren<Text>();
            UiKit.Place(label.gameObject, NameX, CardPad, NameW, NameH);
            UiKit.FitLabelLines(label, 24, ButtonLabelFit.SingleLineMin, ButtonLabelFit.TwoLineMin);

            UiKit.AddIcon(card.transform, CardPad, CardPad, CardIcon, icon);
            var desc = UiKit.AddText(card.transform, CardPad, DescY, DescW, DescH, description, 16, UiKit.TextCol, TextAnchor.UpperLeft);
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            if (!string.IsNullOrEmpty(badge))
            {
                AddBadge(card.transform, CardPad, BadgeY, badge);
            }

            return card;
        }

        /// <summary>A small green pill: "Can be submitted".</summary>
        private static void AddBadge(Transform card, float x, float y, string text)
        {
            float w = Mathf.Min(DescW, UiKit.MeasureWidth(text, 14) + 28f);
            UiKit.AddImage(card, x, y, w, BadgeH, UiKit.PillSprite, new Color(UiKit.Ok.r, UiKit.Ok.g, UiKit.Ok.b, 0.16f), Image.Type.Sliced);
            UiKit.AddText(card, x, y, w, BadgeH, text, 14, UiKit.Ok, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        /// <summary>The developer cards step back: a dimmer name, icon and description than the creator cards.</summary>
        private static void Subdue(Button card)
        {
            foreach (var text in card.GetComponentsInChildren<Text>(true))
            {
                text.color = new Color(0.62f, 0.72f, 0.82f);
            }

            foreach (var image in card.GetComponentsInChildren<Image>(true))
            {
                if (image != card.targetGraphic)
                {
                    image.color = new Color(1f, 1f, 1f, 0.6f);
                }
            }
        }

        /// <summary>The grid's eighth cell: how every editor is steered (look, fly, place, remove) — not a button.</summary>
        private static void AddHelpCard(Transform root, float x, float y, AppShell shell)
        {
            var card = UiKit.AddCard(root, x, y, CardW, CardH);
            UiKit.AddIcon(card, CardPad, 18f, 36f, "set_controls");
            var title = UiKit.AddText(card, HelpTitleX, 18f, HelpTitleW, 36f, shell.L("ui.editors.help.title"), 18, UiKit.Cyan,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.FitLabelSingleLine(title, HelpTitleW, UiKit.EditorMinFontSize, 18);

            float ly = HelpLineY;
            foreach (string key in HelpLines)
            {
                var line = UiKit.AddText(card, CardPad, ly, DescW, HelpLineH, shell.L(key), 16, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.FitLabelSingleLine(line, DescW, UiKit.EditorMinFontSize, 16);
                ly += HelpLineH;
            }
        }
    }
}
