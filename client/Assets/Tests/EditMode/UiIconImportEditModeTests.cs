// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The UI icons import as 2D textures (#2924). Fifteen of them — the main-menu button icons, the system check and the
    /// world-info rows — were committed with a <c>.meta</c> that held only a guid. Unity reads the missing TextureImporter
    /// block with <c>textureShape: TextureCube</c>, so <c>Resources.Load&lt;Texture2D&gt;</c> returned null (the untyped
    /// load a Cubemap) and <see cref="UiKit.AddIcon"/> silently drew nothing — in the Editor and in every player built
    /// from that import.
    /// </summary>
    public sealed class UiIconImportEditModeTests
    {
        /// <summary>The icons the shell screens ask for by name: the button icons handed to <see cref="UiKit.AddButton"/>,
        /// the system-check rows (main menu and loading screen) and the main menu's world-info rows.</summary>
        private static readonly string[] ShellIcons =
        {
            "btn_credits", "btn_exit", "btn_feedback", "btn_host", "btn_join", "btn_settings", "btn_singleplayer",
            "sys_comms", "sys_engines", "sys_life", "sys_nav", "sys_shields",
            "info_mode", "info_multiplayer", "info_procedural",
        };

        [TearDown]
        public void TearDown() => UiKit.ClearIconCache();

        [TestCaseSource(nameof(ShellIcons))]
        public void ShellIcon_LoadsAsA2DTextureAndASprite(string name)
        {
            UiKit.ClearIconCache(); // another test may have cached the icon before this one ran

            Assert.IsNotNull(Resources.Load<Texture2D>("icons/" + name), $"icons/{name} loads as a Texture2D");
            Assert.IsNotNull(UiKit.Icon(name), $"UiKit.Icon(\"{name}\") yields a sprite, so AddIcon draws it");
        }

        [Test]
        public void EveryIconInResources_LoadsAsA2DTexture()
        {
            string dir = Path.Combine(Application.dataPath, "Resources", "icons");
            var names = Directory.GetFiles(dir, "*.png").Select(Path.GetFileNameWithoutExtension).ToList();
            var broken = names.Where(n => Resources.Load<Texture2D>("icons/" + n) == null).OrderBy(n => n).ToList();

            Assert.Greater(names.Count, ShellIcons.Length, "the icon folder was found");
            Assert.IsEmpty(broken, "icons that do not load as a Texture2D (check their .meta importer settings): " + string.Join(", ", broken));
        }
    }
}
