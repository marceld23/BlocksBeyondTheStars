// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    public static partial class UiMainMenu
    {
        /// <summary>
        /// The world the Continue button resumes (#3663): <see cref="ClientSettings.LastWorld"/> when its save still exists,
        /// with when it was last written and — once the background world list knows it — its game mode. One stat on the
        /// menu's frame; the full list is never needed for this.
        /// </summary>
        private sealed class LastWorld
        {
            public string Name;
            public DateTime LastUtc;

            /// <summary>The world, or null when there is no last world or its save is gone (deleted, another machine).</summary>
            public static LastWorld Find(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                try
                {
                    var db = new FileInfo(Path.Combine(LocalServerLauncher.SavesRoot, name, WorldListCache.WorldFile));
                    return db.Exists ? new LastWorld { Name = name, LastUtc = db.LastWriteTimeUtc } : null;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
                {
                    return null; // an unreadable or oddly named folder just means "no Continue"
                }
            }

            /// <summary>"World · Explorer · 5 min ago" — the mode only when the world list already read it.</summary>
            public string Line(Func<string, string> localize)
            {
                string mode = ModeKey();
                return Name + (mode.Length > 0 ? " · " + localize(mode) : string.Empty) + " · " + UiKit.TimeAgo(localize, LastUtc);
            }

            private string ModeKey()
            {
                var worlds = LocalServerLauncher.WorldList.TryGet();
                if (worlds == null)
                {
                    return string.Empty;
                }

                foreach (var world in worlds)
                {
                    if (string.Equals(world.Name, Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return world.ModeKey;
                    }
                }

                return string.Empty;
            }
        }

        /// <summary>
        /// Fills the What's-new card (#3663) with the newest release once the feed and the player's language have arrived
        /// (both load in the background, often after the menu is up), then removes itself. Before that the card shows
        /// "loading"; with an empty or failed feed it names the running version.
        /// </summary>
        private sealed class WhatsNewTeaser : MonoBehaviour
        {
            private AppShell _shell;
            private Text _kicker, _title, _teaser;
            private float _width;
            private string _code = "en";

            public void Init(AppShell shell, Text kicker, Text title, Text teaser, float width)
            {
                _shell = shell;
                _kicker = kicker;
                _title = title;
                _teaser = teaser;
                _width = width;
                _code = GameLocaleExtensions.Parse(shell.Settings?.Language).Code();
                // Filled from the first Update, not here: the menu's build stays the same whatever the feed's state (the
                // hierarchy golden of MainMenuHierarchyEditModeTests).
            }

            private void Update()
            {
                var entries = WhatsNew.Entries;
                if (entries == null || _shell == null)
                {
                    return;
                }

                if (entries.Count > 0 && !WhatsNew.LanguageReady(_code))
                {
                    WhatsNew.EnsureLanguage(_shell, _code);
                    return;
                }

                Fill(entries.Count > 0 ? entries[0] : null);
                Destroy(this);
            }

            private void Fill(WhatsNewEntry newest)
            {
                if (newest == null)
                {
                    _title.text = "VER. " + AppShell.Version;
                    _teaser.text = _shell.L("ui.whatsnew.empty");
                    return;
                }

                _kicker.text = (_shell.L("ui.menu.whatsnew") + " · VER. " + newest.version).ToUpperInvariant();
                _title.text = UiKit.Ellipsize(WhatsNew.Title(newest, _code), _width, 64f, _title.fontSize, FontStyle.Bold);
                _teaser.text = UiKit.Ellipsize(Plain(WhatsNew.Body(newest, _code)), _width, 112f, _teaser.fontSize);
            }

            /// <summary>The release body as one plain paragraph: markdown marks, links and headings stripped, lines joined.</summary>
            private static string Plain(string markdown)
            {
                string s = markdown ?? string.Empty;
                s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1");
                s = Regex.Replace(s, @"^\s*(#{1,6}|[-*>])\s*", string.Empty, RegexOptions.Multiline);
                s = s.Replace("**", string.Empty).Replace("*", string.Empty).Replace("`", string.Empty);
                s = Regex.Replace(s, @"\s+", " ");
                return s.Replace("<", "‹").Replace(">", "›").Trim();
            }
        }
    }
}
