// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Festivals;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What a host gives the character editor's HUD mode (#3318): the settings that hold the worn frame, its switches and
    /// the player's own frame library, plus the templates this host may offer (<see cref="AppearanceTemplates.OfferedFrames"/>
    /// — free ones in the main menu, unlocked ones too in a world). Both avatar hosts (the main-menu designer and the in-game
    /// appearance screen) build one; the block-paint tool does not, and so has no HUD mode. The frame is the player's own
    /// client setting in both: there is no server to tell, and no scratch copy to apply.
    /// </summary>
    public sealed class HudFrameEditorHost
    {
        public ClientSettings Settings;
        public Func<string, string> Localize;

        /// <summary>The templates on offer right now (asked again whenever the column is rebuilt).</summary>
        public Func<List<HudFrameDefinition>> Templates;

        /// <summary>The worn design string ("" = no frame); setting it saves the settings.</summary>
        public string Design
        {
            get => Settings?.HudFrameDesign ?? string.Empty;
            set
            {
                if (Settings == null)
                {
                    return;
                }

                Settings.HudFrameDesign = value ?? string.Empty;
                Settings.Save();
            }
        }

        private List<HudFrameDesignEntry> Library
        {
            get
            {
                if (Settings == null)
                {
                    return new List<HudFrameDesignEntry>();
                }

                Settings.HudFrameDesigns ??= new List<HudFrameDesignEntry>();
                return Settings.HudFrameDesigns;
            }
        }

        public List<string> DesignNames()
        {
            var names = new List<string>();
            foreach (var entry in Library)
            {
                names.Add(entry?.Name ?? string.Empty);
            }

            return names;
        }

        /// <summary>The saved design at <paramref name="index"/>, or null.</summary>
        public HudFrameDesignEntry DesignAt(int index)
        {
            var library = Library;
            return index >= 0 && index < library.Count ? library[index] : null;
        }

        /// <summary>Saves <paramref name="design"/> under a name — over the entry that already carries it (case-insensitive),
        /// otherwise as a new one up to <see cref="ClientSettings.MaxHudFrameDesigns"/>.</summary>
        public string SaveDesign(string name, string design)
        {
            if (Settings == null)
            {
                return string.Empty;
            }

            name = (name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return L("ui.hudframe.need_name");
            }

            var library = Library;
            int existing = library.FindIndex(e => e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                library[existing].Design = design ?? string.Empty;
                Settings.Save();
                return L("ui.hudframe.updated").Replace("{name}", library[existing].Name);
            }

            if (library.Count >= ClientSettings.MaxHudFrameDesigns)
            {
                return L("ui.hudframe.limit").Replace("{max}", ClientSettings.MaxHudFrameDesigns.ToString());
            }

            library.Add(new HudFrameDesignEntry { Name = name, Design = design ?? string.Empty });
            Settings.Save();
            return L("ui.hudframe.saved").Replace("{name}", name);
        }

        /// <summary>Renames the saved frame at <paramref name="index"/> (its pixels stay put).</summary>
        public string RenameDesign(int index, string name)
        {
            var library = Library;
            if (index < 0 || index >= library.Count)
            {
                return L("ui.hudframe.select_first");
            }

            name = (name ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                return L("ui.hudframe.need_name");
            }

            int clash = library.FindIndex(e => e != null && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (clash >= 0 && clash != index)
            {
                return L("ui.hudframe.name_taken").Replace("{name}", library[clash].Name);
            }

            library[index].Name = name;
            Settings.Save();
            return L("ui.hudframe.renamed").Replace("{name}", name);
        }

        /// <summary>Drops a saved frame. The worn frame is a separate copy, so nothing on screen changes.</summary>
        public string DeleteDesign(int index)
        {
            var library = Library;
            if (index < 0 || index >= library.Count)
            {
                return string.Empty;
            }

            string name = library[index]?.Name ?? string.Empty;
            library.RemoveAt(index);
            Settings.Save();
            return L("ui.hudframe.deleted").Replace("{name}", name);
        }

        /// <summary>The templates on offer.</summary>
        public List<HudFrameDefinition> OfferedTemplates() => Templates?.Invoke() ?? new List<HudFrameDefinition>();

        public string L(string key) => Localize != null ? Localize(key) : key;
    }
}
