// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The texture editor's "Submit to the developers" dialog (#1965), built for real (Open → Build; edit mode runs no
    /// Update). Pins #2575 — the three consent sentences were inset with offsetMin/offsetMax on a POINT-anchored label,
    /// came out −24 wide and rendered as empty boxes, so no texture could be submitted — and #2576: Submit stays
    /// clickable and, with a box still open, says what is missing and rings the open boxes instead of being a dead
    /// button. The consent sentences are checked in every game language: each must fit its box completely.
    /// </summary>
    public sealed class TextureSubmitDialogEditModeTests
    {
        private readonly List<GameObject> _spawned = new();
        private bool _hadEventSystem, _hadUiSound;

        private static IEnumerable<GameLocale> Locales => (GameLocale[])Enum.GetValues(typeof(GameLocale));

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
            _hadUiSound = GameObject.Find("UiSound") != null;
        }

        [TearDown]
        public void TearDown()
        {
            var canvas = GameObject.Find(TextureSubmitDialog.CanvasName);
            if (canvas != null)
            {
                UnityEngine.Object.DestroyImmediate(canvas);
            }

            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();

            // CreateCanvas makes an EventSystem and a click makes the UiSound source — leave the scene as we found it.
            var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                UnityEngine.Object.DestroyImmediate(es.gameObject);
            }

            var sound = GameObject.Find("UiSound");
            if (!_hadUiSound && sound != null)
            {
                UnityEngine.Object.DestroyImmediate(sound);
            }
        }

        [TestCaseSource(nameof(Locales))]
        public void ConsentSentences_AreVisibleAndFitTheirBoxCompletely(GameLocale locale)
        {
            var table = LocaleTable(locale);
            var english = LocaleTable(GameLocale.English);
            OpenDialog(key => table.TryGetValue(key, out var text) || english.TryGetValue(key, out text) ? text : key);
            RequireFontRasterization();

            for (int i = 1; i <= 3; i++)
            {
                var button = Part<Button>(TextureSubmitDialog.ConsentName + i);
                var box = ((RectTransform)button.transform).rect;
                var label = button.GetComponentInChildren<Text>();
                var rt = label.rectTransform;
                string key = "ui.tex.submit.check_" + i;
                string sentence = table.TryGetValue(key, out var own) ? own : english[key];

                Assert.Greater(rt.rect.width, 0f, $"consent {i}: the label must have a width — it was −24 and drew nothing (#2575)");
                Assert.GreaterOrEqual(rt.anchoredPosition.x, 0f, $"consent {i}: label starts inside its box");
                Assert.LessOrEqual(rt.anchoredPosition.x + rt.rect.width, box.width, $"consent {i}: label ends inside its box");
                Assert.LessOrEqual(rt.rect.height, box.height, $"consent {i}: label is no taller than its box");
                Assert.AreEqual("☐  " + sentence, label.text, $"consent {i}: an unticked box shows ☐ and its whole sentence");

                // Measured in reference units (scale 1), like UiKit.MeasureWidth — independent of the test run's window.
                var settings = label.GetGenerationSettings(rt.rect.size);
                settings.scaleFactor = 1f;
                var generator = new TextGenerator();
                generator.Populate(label.text, settings);
                Assert.GreaterOrEqual(generator.characterCountVisible, label.text.Length,
                    $"consent {i} ({locale.Code()}): the sentence must fit its box completely, even at the smallest best-fit size — "
                    + "a consent the child cannot read in full is no consent");
            }
        }

        [Test]
        public void Submit_IsClickableBeforeAnyBoxIsTicked()
        {
            OpenDialog(key => key);
            Assert.IsTrue(Part<Button>(TextureSubmitDialog.SubmitName).interactable,
                "a Submit that is merely off looks clickable and does nothing — it must answer instead (#2576)");
        }

        [Test]
        public void SubmitWithAConsentMissing_SaysWhatIsMissingAndRingsOnlyTheOpenBoxes()
        {
            OpenDialog(key => key);
            Click(TextureSubmitDialog.ConsentName + 1);
            Click(TextureSubmitDialog.SubmitName);

            Assert.AreEqual("ui.tex.submit.need_consent", Part<Text>(TextureSubmitDialog.StatusName).text,
                "the status line names what is missing");
            Assert.IsFalse(Ring(1).enabled, "a ticked box gets no ring");
            Assert.IsTrue(Ring(2).enabled, "an open box is ringed");
            Assert.IsTrue(Ring(3).enabled, "an open box is ringed");
            Assert.IsTrue(Part<Button>(TextureSubmitDialog.SubmitName).interactable, "nothing was sent — Submit still answers");

            Click(TextureSubmitDialog.ConsentName + 2);
            Assert.IsFalse(Ring(2).enabled, "ticking a box takes its ring away");
            Assert.IsTrue(Ring(3).enabled);

            Click(TextureSubmitDialog.ConsentName + 3);
            Assert.IsFalse(Ring(3).enabled);
            Assert.AreEqual(string.Empty, Part<Text>(TextureSubmitDialog.StatusName).text, "all three ticked: the hint goes away");
            StringAssert.StartsWith("☑", Part<Button>(TextureSubmitDialog.ConsentName + 2).GetComponentInChildren<Text>().text);
        }

        [Test]
        public void Cancel_ClosesTheDialog()
        {
            var dialog = OpenDialog(key => key);
            Click(TextureSubmitDialog.CancelName);
            Assert.IsFalse(dialog.IsOpen);
            Assert.IsNull(GameObject.Find(TextureSubmitDialog.CanvasName));
        }

        // ---------------------------------------------------------------- helpers

        private TextureSubmitDialog OpenDialog(Func<string, string> localize)
        {
            var host = new GameObject("TextureEditorUnderTest");
            _spawned.Add(host);
            var dialog = host.AddComponent<TextureSubmitDialog>();
            dialog.Localize = localize;
            dialog.Open("ash", "Ash", new[] { new byte[16] }, 0);
            Assert.IsTrue(dialog.IsOpen, "the dialog opened");
            return dialog;
        }

        private static T Part<T>(string name) where T : Component
        {
            var canvas = GameObject.Find(TextureSubmitDialog.CanvasName);
            Assert.IsNotNull(canvas, "the dialog canvas exists");
            foreach (var part in canvas.GetComponentsInChildren<T>(true))
            {
                if (part.gameObject.name == name)
                {
                    return part;
                }
            }

            Assert.Fail($"no {typeof(T).Name} named '{name}' in the dialog");
            return null;
        }

        private static Image Ring(int consent) => Part<Image>(TextureSubmitDialog.ConsentRingName + consent);

        private static void Click(string name) => Click(Part<Button>(name));

        private static void Click(Button button)
        {
            Assert.IsTrue(button.interactable, $"'{button.gameObject.name}' can be clicked");
            button.onClick.Invoke();
        }

        /// <summary>The locale table straight from the repository's <c>data/locales</c> — the source of truth, also
        /// in a checkout whose StreamingAssets copy was never synced.</summary>
        private static Dictionary<string, string> LocaleTable(GameLocale locale)
        {
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "locales", locale.Code() + ".json"));
            if (!File.Exists(file))
            {
                Assert.Ignore($"locale file not found: {file}");
            }

            return ContentLoader.ParseLocaleTable(File.ReadAllText(file));
        }

        /// <summary>The fit check measures glyphs; a run that cannot rasterize the font cannot judge it.</summary>
        private static void RequireFontRasterization()
        {
            var probe = new TextGenerator();
            probe.Populate("Ab", new TextGenerationSettings
            {
                font = UiKit.Font,
                fontSize = 15,
                scaleFactor = 1f,
                lineSpacing = 1f,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(200f, 50f),
                textAnchor = TextAnchor.MiddleLeft,
                pivot = new Vector2(0f, 1f),
                color = Color.white,
            });
            if (probe.characterCount == 0 || probe.lineCount == 0)
            {
                Assert.Inconclusive("this run cannot rasterize the UI font — the fit check needs glyph metrics");
            }
        }
    }
}
