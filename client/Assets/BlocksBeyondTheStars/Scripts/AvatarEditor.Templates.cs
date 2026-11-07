// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Festivals;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the main-menu designer adds to the shared character editor beyond the outfits: the FREE skin templates in the
    /// outfit column (#3321 — a calendar skin belongs to a world, and the menu has none), the HUD mode with the free frame
    /// templates (#3318) and the editor's own Submit button (#3319). A worn skin template lands in the scratch look like a
    /// loaded outfit — the designer's Apply is still what makes it the player's look.
    /// </summary>
    public sealed partial class AvatarEditor
    {
        private void ConfigureTemplatesAndHud(FaceEditor editor)
        {
            var settings = Shell?.Settings;
            editor.SkinTemplateNames = () => AppearanceTemplates.Names(FreeSkins(), s => s.NameKey, L);
            editor.OnWearSkinTemplate = WearSkinTemplate;
            editor.SubmitSettings = settings;
            editor.LookName = () => _name;
            if (settings != null)
            {
                editor.HudHost = new HudFrameEditorHost
                {
                    Settings = settings,
                    Localize = L,
                    Templates = () => AppearanceTemplates.OfferedFrames(Shell?.Content, null), // free ones only
                };
            }
        }

        private List<SkinDefinition> FreeSkins() => AppearanceTemplates.OfferedSkins(Shell?.Content, null);

        /// <summary>Puts a skin template on the scratch look: its colours (an empty entry keeps the current one) and its
        /// four paintings; the face stays. Returns the outfit column's status line.</summary>
        private string WearSkinTemplate(int index)
        {
            var skins = FreeSkins();
            if (index < 0 || index >= skins.Count)
            {
                return string.Empty;
            }

            var look = CaptureScratch(_name);
            AppearanceTemplates.ApplySkin(skins[index], look);
            PutOnScratch(look);
            string status = L("ui.avatar.outfit_loaded").Replace("{name}", L(skins[index].NameKey));
            SetStatus(status);
            return status;
        }

        /// <summary>Makes <paramref name="o"/> the scratch look: colours, swatches, face, paintings and the figure behind the
        /// panel (the name field is the caller's business).</summary>
        private void PutOnScratch(AvatarOutfit o)
        {
            _col[0] = o.SkinColor;
            _col[1] = o.TorsoColor;
            _col[2] = o.ArmColor;
            _col[3] = o.LegColor;
            for (int i = 0; i < _swatch.Length; i++)
            {
                if (_swatch[i] != null)
                {
                    _swatch[i].color = _col[i];
                }
            }

            _face = o.FacePixels ?? string.Empty;
            for (int part = 0; part < _bodyPaint.Length; part++)
            {
                _bodyPaint[part] = o.GetBodyPaint(part);
            }

            if (_avatar != null)
            {
                _avatar.ApplyColors(_col[0], _col[1], _col[2], _col[3]);
                _avatar.SetFace(_face);
                for (int part = 0; part < _bodyPaint.Length; part++)
                {
                    _avatar.SetBodyPaint(part, _bodyPaint[part]);
                }
            }
        }
    }
}
