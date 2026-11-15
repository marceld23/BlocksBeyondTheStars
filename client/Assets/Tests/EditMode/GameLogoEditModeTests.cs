// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The game's one logo in the Unity shell (#3706): <see cref="GameLogo"/> loads the official logo (rocket icon + wordmark)
    /// from Resources, and <see cref="UiKit.AddGameLogo"/> fits it into the title box of the splash, the menu and the
    /// loading screen — aspect kept, the mark on the box's left edge (or centred), never a click target.
    /// </summary>
    public sealed class GameLogoEditModeTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        [Test]
        public void Texture_IsTheOfficialLogo_WithMipMaps()
        {
            var tex = GameLogo.Texture;

            Assert.IsNotNull(tex, "the logo must load from Resources/" + GameLogo.ResourcePath);
            Assert.AreEqual(1600, tex.width, "the shipped 1600 px rendering of the print logo");
            Assert.Greater(tex.width / (float)tex.height, 6f, "icon + one-line wordmark: a wide mark");
            Assert.Greater(tex.mipmapCount, 1, "shown smaller than its 1600 px: it needs mips to stay calm");
            Assert.IsTrue(tex.isReadable, "the poster capture reads its pixels");
            Assert.AreSame(tex, GameLogo.Texture, "one texture per session");
        }

        [Test]
        public void Raster_KeepsTheAspect_AtTheAskedWidth()
        {
            var img = GameLogo.Raster(1200);

            Assert.IsNotNull(img);
            Assert.AreEqual(1200, img.Width);
            Assert.AreEqual(GameLogo.Texture.width / (float)GameLogo.Texture.height, img.Width / (float)img.Height, 0.05f);
        }

        [Test]
        public void AddGameLogo_FitsTheBox_WithTheLettersOnItsLeftEdge()
        {
            _root = new GameObject("Root", typeof(RectTransform));
            var img = UiKit.AddGameLogo(_root.transform, 90f, 22f, 560f, 140f);

            Assert.IsNotNull(img);
            Assert.AreSame(GameLogo.Texture, img.texture);
            Assert.IsFalse(img.raycastTarget);
            var rt = img.rectTransform;
            Assert.LessOrEqual(rt.sizeDelta.x, 560.01f);
            Assert.LessOrEqual(rt.sizeDelta.y, 140.01f);
            Assert.AreEqual(GameLogo.Texture.width / (float)GameLogo.Texture.height, rt.sizeDelta.x / rt.sizeDelta.y, 0.01f);
            float margin = rt.sizeDelta.x * GameLogo.MarginFraction;
            Assert.AreEqual(90f - margin, rt.anchoredPosition.x, 0.01f, "the mark starts on the column");
        }

        [Test]
        public void AddGameLogo_Centered_SitsInTheMiddleOfTheBox()
        {
            _root = new GameObject("Root", typeof(RectTransform));
            var img = UiKit.AddGameLogo(_root.transform, 0f, 0f, 1100f, 270f, centered: true);

            Assert.IsNotNull(img);
            var rt = img.rectTransform;
            Assert.AreEqual(550f, rt.anchoredPosition.x + rt.sizeDelta.x * 0.5f, 0.01f);
            Assert.AreEqual(-135f, rt.anchoredPosition.y - rt.sizeDelta.y * 0.5f, 0.01f);
        }
    }
}
