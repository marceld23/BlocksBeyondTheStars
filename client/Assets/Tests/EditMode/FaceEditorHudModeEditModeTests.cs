// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Client.HudFrames;
using BlocksBeyondTheStars.Shared.Festivals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The character editor's HUD mode (#3318): the "Character | HUD frame" switch commits the canvas being left, brings the
    /// other mode back as it was left (its own subjects and its own undo history), paints the frame as four 32 × 32 tiles
    /// on one canvas, and writes the frame into the player's settings — never into the body paint. Edit mode runs no Start,
    /// so the editor is started and driven by reflection, as the text-fit test does.
    /// </summary>
    public sealed class FaceEditorHudModeEditModeTests
    {
        private readonly List<GameObject> _spawned = new();
        private bool _hadEventSystem;
        private string _face = string.Empty;
        private readonly string[] _paints = new string[4];
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;
            LogAssert.ignoreFailingMessages = true; // edit mode refuses Destroy; the editor's rebuilds log it
            _dir = Path.Combine(Path.GetTempPath(), "bbts_hudmode_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ClientSettings.StorageDirOverride = _dir; // committing the frame saves the settings — never the real file
        }

        [TearDown]
        public void TearDown()
        {
            ClientSettings.StorageDirOverride = null;
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
                // best effort: a temp folder
            }

            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == "Face Editor UI")
                {
                    Object.DestroyImmediate(root);
                }
            }

            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
            LogAssert.ignoreFailingMessages = false;
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                Object.DestroyImmediate(es.gameObject);
            }
        }

        private FaceEditor NewEditor(ClientSettings settings)
        {
            var go = new GameObject("FaceEditorUnderTest");
            _spawned.Add(go);
            var editor = go.AddComponent<FaceEditor>();
            var colors = new[] { Color.white, Color.gray, Color.gray, Color.gray };
            editor.Subjects = AppearanceSubjects.Build(
                () => _face, f => _face = f ?? string.Empty,
                part => _paints[part] ?? string.Empty, (part, p) => _paints[part] = p ?? string.Empty,
                which => colors[which], (which, c) => colors[which] = c);
            editor.OutfitNames = () => new List<string>();
            editor.HudHost = new HudFrameEditorHost
            {
                Settings = settings,
                Templates = () => new List<HudFrameDefinition>(),
            };
            Call(editor, "Start");
            return editor;
        }

        private static void Call(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Invoke(target, null);

        private static T Get<T>(object target, string field)
            => (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        private static void Set(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        /// <summary>Paints one cell the way a stroke does: an undo step, the cell, the dirty flag.</summary>
        private static void Paint(FaceEditor editor, int cell, int index)
        {
            Call(editor, "BeginGridEdit");
            Get<int[]>(editor, "_grid")[cell] = index;
            Set(editor, "_dirty", true);
            Call(editor, "CommitGridEdit");
        }

        [Test]
        public void TheSwitch_CommitsTheModeBeingLeft_AndBringsTheOtherBackAsItWas()
        {
            // The framework resets the flag when the test body starts, so SetUp's value does not hold here (as in
            // AvatarPaintOrientationEditModeTests): the editor's subject reload calls Object.Destroy, which logs in edit mode.
            LogAssert.ignoreFailingMessages = true;
            var settings = new ClientSettings();
            var editor = NewEditor(settings);
            Assert.IsFalse(Get<bool>(editor, "_hudMode"));
            Paint(editor, 0, 12); // the face, top-left pixel blue

            Call(editor, "EnterHudMode");

            Assert.IsTrue(Get<bool>(editor, "_hudMode"));
            Assert.AreEqual('c', _face[0], "leaving the character mode committed the face");
            Assert.AreEqual(HudFrameDesign.EditorGridW * HudFrameDesign.EditorGridH, Get<int[]>(editor, "_grid").Length);
            Assert.AreEqual(string.Empty, settings.HudFrameDesign, "no frame until one is painted");
            Assert.IsFalse(Get<PixelEditHistory>(editor, "_history").CanUndo, "the HUD mode has its own, empty history");

            Paint(editor, HudFrameDesign.TileSide + 1, 25); // the top tile's second pixel
            Call(editor, "EnterCharacterMode");

            Assert.IsTrue(HudFrameDesign.TryParse(settings.HudFrameDesign, out var design));
            Assert.AreEqual(25, HudFrameDesign.ValueOf(design.Tile(HudFrameTile.Top)[1]));
            Assert.IsTrue(string.IsNullOrEmpty(_paints[0]), "the frame never becomes body paint");
            Assert.AreEqual(12, Get<int[]>(editor, "_grid")[0], "the face came back as it was left");
            Assert.IsTrue(Get<PixelEditHistory>(editor, "_history").CanUndo, "the character history survived the trip");

            Call(editor, "EnterHudMode");
            Assert.AreEqual(25, Get<int[]>(editor, "_grid")[HudFrameDesign.TileSide + 1]);
            Assert.IsTrue(Get<PixelEditHistory>(editor, "_history").CanUndo, "the HUD history survived too");
        }

        [Test]
        public void WithoutAHudHost_ThereIsNoHudMode()
        {
            var go = new GameObject("PaintEditorUnderTest");
            _spawned.Add(go);
            var editor = go.AddComponent<FaceEditor>();
            Call(editor, "Start");

            Call(editor, "EnterHudMode");

            Assert.IsFalse(Get<bool>(editor, "_hudMode"));
            Assert.IsNull(Get<RectTransform>(editor, "_hudLayer"));
        }
    }
}
