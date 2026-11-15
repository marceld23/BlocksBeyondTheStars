// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The "last played" picture of a local world (<see cref="WorldPreviewFrame"/>): "My worlds" shows it at the top of the
    /// selected world's detail pane — 16:9, above the name and Play — and a neutral placeholder while the world has none;
    /// the main menu's Continue card shows it in the icon's place, and only when there is one. Both screens are built on a
    /// temp saves folder (<see cref="LocalServerLauncher.SavesRootOverrideForTest"/>), never on the player's saves. The
    /// world list arrives through the list's own Start (invoked here — EditMode runs no player loop).
    /// </summary>
    public sealed class WorldPreviewEditModeTests
    {
        private const string WithPicture = "Alpha";
        private const string WithoutPicture = "Beta";
        private const int PictureW = 64, PictureH = 36;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private bool _hadEventSystem;
        private string _saves;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;
            _saves = Path.Combine(Application.temporaryCachePath, "world-preview-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saves);
            LocalServerLauncher.SavesRootOverrideForTest = _saves;
            ClientSettings.LoadNoticeKey = "";
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go == null)
                {
                    continue;
                }

                foreach (var frame in go.GetComponentsInChildren<WorldPreviewFrame>(true))
                {
                    frame.Clear(); // EditMode calls no OnDestroy: free the loaded pictures by hand
                }

                Object.DestroyImmediate(go);
            }

            _spawned.Clear();
            LocalServerLauncher.SavesRootOverrideForTest = null;
            try
            {
                Directory.Delete(_saves, recursive: true);
            }
            catch (IOException)
            {
                // The background world-list scan may still hold the folder for a moment; the temp cache is swept anyway.
            }

            var es = Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                Object.DestroyImmediate(es.gameObject);
            }
        }

        [Test]
        public void MyWorlds_DetailPane_HasThePictureOnTop_In16By9()
        {
            var root = BuildMyWorlds("");
            var frame = root.GetComponentInChildren<WorldPreviewFrame>(true);

            Assert.IsNotNull(frame, "My worlds has a picture frame");
            Assert.AreEqual("WorldDetail", frame.transform.parent.name, "it belongs to the selected world's detail pane");
            var rect = (RectTransform)frame.transform;
            Assert.AreEqual(16f / 9f, rect.sizeDelta.x / rect.sizeDelta.y, 0.01f, "16:9");
            Assert.AreEqual(0f, rect.anchoredPosition.x - 32f, 0.01f, "on the pane's left content edge");

            var play = (RectTransform)ButtonIn(frame.transform.parent, "ui.save.play").transform;
            float frameBottom = -rect.anchoredPosition.y + rect.sizeDelta.y;
            Assert.Less(frameBottom, -play.anchoredPosition.y, "the picture sits above Play (and the name)");
            Assert.IsFalse(frame.ShowsPicture, "nothing is shown before a world is selected");
        }

        [Test]
        public void MyWorlds_ShowsTheSelectedWorldsPicture_AndThePlaceholderWithoutOne()
        {
            AddWorld(WithPicture, picture: true);
            AddWorld(WithoutPicture, picture: false);
            var root = BuildMyWorlds(WithPicture);
            var frame = root.GetComponentInChildren<WorldPreviewFrame>(true);
            ArriveList(root); // selects the last-played world

            Assert.IsTrue(frame.ShowsPicture, $"'{WithPicture}' has a picture — it is shown");
            Assert.IsFalse(frame.Placeholder.activeSelf, "no placeholder over a picture");
            Assert.AreEqual(PictureW, frame.Picture.texture.width);
            Assert.AreEqual(PictureH, frame.Picture.texture.height);
            var first = frame.Picture.texture;

            Click(ButtonIn(root, WithoutPicture));
            Assert.IsFalse(frame.ShowsPicture, $"'{WithoutPicture}' has none");
            Assert.IsTrue(frame.Placeholder.activeSelf, "the placeholder stands in");

            Click(ButtonIn(root, WithPicture));
            Assert.IsTrue(frame.ShowsPicture);
            Assert.AreSame(first, frame.Picture.texture, "a picture shown before is not read again");
        }

        [Test]
        public void Frame_ReadsANewerPictureOverTheLoadedOne()
        {
            AddWorld(WithPicture, picture: true);
            var frame = Track(WorldPreviewFrame.Add(NewParent(), 0f, 0f, 480f, 270f));
            frame.SavesRoot = _saves;
            frame.Show(WithPicture);
            Assert.AreEqual(PictureW, frame.Picture.texture.width);

            string path = WorldPreviewFiles.PathFor(_saves, WithPicture);
            WritePicture(path, 32, 18);
            File.SetLastWriteTimeUtc(path, System.DateTime.UtcNow.AddMinutes(1));
            frame.Show(WithPicture);

            Assert.AreEqual(32, frame.Picture.texture.width, "the picture written since replaces the loaded one");
        }

        [Test]
        public void ContinueCard_ShowsThePicture_InTheIconsPlace()
        {
            AddWorld(WithPicture, picture: true);
            var root = BuildMainMenu(WithPicture);
            var card = ButtonIn(root, "ui.menu.continue");

            var frame = card.GetComponentInChildren<WorldPreviewFrame>(true);
            Assert.IsNotNull(frame, "the Continue card carries the world's picture");
            Assert.IsTrue(frame.ShowsPicture);
            Assert.IsNull(frame.Placeholder, "the card's picture has no placeholder");
            Assert.IsFalse(card.GetComponentsInChildren<Image>(false).Any(i => i != card.targetGraphic),
                "the picture takes the icon's place");

            var picture = (RectTransform)frame.transform;
            var title = (RectTransform)card.GetComponentsInChildren<Text>(true).First(t => t.text == "ui.menu.continue").transform;
            Assert.LessOrEqual(picture.anchoredPosition.x + picture.sizeDelta.x, title.anchoredPosition.x, "the title starts right of it");
        }

        [Test]
        public void ContinueCard_WithoutAPicture_StaysAsItWas()
        {
            AddWorld(WithoutPicture, picture: false);
            var root = BuildMainMenu(WithoutPicture);
            var card = ButtonIn(root, "ui.menu.continue");

            Assert.IsNull(root.GetComponentInChildren<WorldPreviewFrame>(true), "no picture, no frame");
            Assert.IsTrue(card.GetComponentsInChildren<Image>(false).Any(i => i != card.targetGraphic), "the icon stays");
        }

        private Transform BuildMyWorlds(string lastWorld) => Track(UiSaveSelect.Build(NewShell(lastWorld))).transform;

        private Transform BuildMainMenu(string lastWorld) => Track(UiMainMenu.Build(NewShell(lastWorld))).transform;

        private AppShell NewShell(string lastWorld)
        {
            var shell = Track(new GameObject("ShellUnderTest").AddComponent<AppShell>());
            shell.SetSettingsForTest(new ClientSettings { LastWorld = lastWorld });
            shell.PlayerName = "";
            return shell;
        }

        private Transform NewParent() => Track(new GameObject("PreviewParent", typeof(RectTransform))).transform;

        private T Track<T>(T obj) where T : Object
        {
            _spawned.Add(obj is Component c ? c.gameObject : obj as GameObject);
            return obj;
        }

        /// <summary>A save folder (a world.db makes it a world) and, with <paramref name="picture"/>, its preview.jpg.</summary>
        private void AddWorld(string name, bool picture)
        {
            string dir = Path.Combine(_saves, name);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, WorldListCache.WorldFile), new byte[] { 0 });
            if (picture)
            {
                WritePicture(WorldPreviewFiles.PathFor(_saves, name), PictureW, PictureH);
            }
        }

        private static void WritePicture(string path, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, mipChain: false);
            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(200, 120, 40, 255);
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToJPG(WorldPreviewFiles.JpegQuality));
            Object.DestroyImmediate(tex);
        }

        /// <summary>Waits for the background world list of the temp folder, then runs the list's Start: the rows are built
        /// and the last-played world is selected, as on the screen's first frame.</summary>
        private static void ArriveList(Transform root)
        {
            var deadline = System.DateTime.UtcNow.AddSeconds(10);
            while (LocalServerLauncher.WorldList.TryGet() == null)
            {
                Assert.Less(System.DateTime.UtcNow, deadline, "the world list was read in time");
                System.Threading.Thread.Sleep(10);
            }

            var list = root.GetComponents<MonoBehaviour>().Single(b => b.GetType().Name == "WorldListView");
            var start = list.GetType().GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(start, "the world list builds in Start");
            start.Invoke(list, null);
        }

        /// <summary>The one button under <paramref name="scope"/> whose label reads <paramref name="label"/>.</summary>
        private static Button ButtonIn(Transform scope, string label)
        {
            var matches = scope.GetComponentsInChildren<Button>(true)
                .Where(b => b.GetComponentsInChildren<Text>(true).Any(t => t.text == label))
                .ToList();
            Assert.AreEqual(1, matches.Count, $"exactly one '{label}' button under {scope.name}");
            return matches[0];
        }

        private static void Click(Button button)
        {
            Assert.IsTrue(button.interactable, $"'{button.gameObject.name}' can be clicked");
            button.onClick.Invoke();
        }
    }
}
