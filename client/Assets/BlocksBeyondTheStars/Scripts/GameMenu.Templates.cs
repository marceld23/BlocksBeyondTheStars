// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Festivals;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the in-game appearance screen adds to the shared character editor beyond the outfits: the skin templates of
    /// this world in the outfit column (#3321 — the free ones and those a calendar door unlocked here, <c>skin:&lt;key&gt;</c>),
    /// the HUD mode with the frame templates of this world (#3318, <c>hudframe:&lt;key&gt;</c>) and the editor's Submit
    /// button (#3319). Wearing a skin template is wearing an outfit: the colours and the four paintings go down their
    /// normal paths — the figure and the settings at once, the paintings through the paced appearance queue (the server
    /// takes one every 2 s) — and the face stays the player's own.
    /// </summary>
    public sealed partial class GameMenu
    {
        private void ConfigureTemplatesAndHud(FaceEditor editor)
        {
            editor.SkinTemplateNames = () => AppearanceTemplates.Names(WorldSkins(), s => s.NameKey, L);
            editor.OnWearSkinTemplate = WearSkinTemplate;
            editor.SubmitSettings = Settings;
            editor.UseVoiceOf(Settings, ApplyAppearance); // #3767: the voice sliders — saved and sent with the colours
            editor.HudHost = new HudFrameEditorHost
            {
                Settings = Settings,
                Localize = L,
                Templates = () => AppearanceTemplates.OfferedFrames(Game?.Content, Unlocked),
            };
        }

        /// <summary>
        /// The appearance screen on the editor shell (#3693), looking and working like the main menu's Avatar Designer: Back
        /// top left, Undo / Redo, Submit, Save ("on your character" — a commit reaches the figure, the settings and, paced,
        /// the server; colours apply at once), the tab bar with the Figure tab (colour rows, gear preview, the figure; no
        /// skin name or Export — that is the designer's) and the HUD frame. Its own canvas sits above the console, which
        /// steps aside while it is up (see Update), and Esc / pad B reach it through <see cref="HandleEditorCancel"/>.
        /// </summary>
        private void ConfigureAppearanceFrame(FaceEditor editor)
        {
            editor.EditorKey = "appearance";
            editor.ShellTitleKey = "ui.appearance.title";
            editor.SaveSublineKey = "ui.editor.save_sub.appearance";
            editor.FigureTabKey = "ui.avatar.figure_tab";
            editor.OnClosed = () =>
            {
                if (ReferenceEquals(_faceEditor, editor))
                {
                    _faceEditor = null; // Back destroys the editor; the console comes back next frame
                }
            };
        }

        /// <summary>Whether this world unlocked the cosmetic <paramref name="key"/> (<c>skin:christmas</c>).</summary>
        private bool Unlocked(string key) => Game?.Network != null && Game.Network.HasCosmetic(key);

        private List<SkinDefinition> WorldSkins() => AppearanceTemplates.OfferedSkins(Game?.Content, Unlocked);

        /// <summary>Puts a skin template on: its colours (an empty entry keeps the player's own), its four paintings, the
        /// player's face. Returns the outfit column's status line.</summary>
        private string WearSkinTemplate(int index)
        {
            var skins = WorldSkins();
            if (Settings == null || index < 0 || index >= skins.Count)
            {
                return string.Empty;
            }

            var look = Settings.CaptureOutfit(string.Empty);
            AppearanceTemplates.ApplySkin(skins[index], look);
            Settings.ApplyOutfit(look);
            ApplyAppearance(); // colours: figure + save + one message
            for (int part = 0; part < BodyPaintKit.PartCount; part++)
            {
                ApplyBodyPaint(part, look.GetBodyPaint(part)); // each painting through the paced queue
            }

            if (skins[index].HasFace)
            {
                ApplyFace(look.FacePixels); // #3770: a template with a face brings the whole look
            }

            return L(AppearanceTemplates.WornKey(skins[index])).Replace("{name}", L(skins[index].NameKey));
        }
    }
}
