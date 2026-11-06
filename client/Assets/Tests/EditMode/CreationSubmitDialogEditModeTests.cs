// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The editors' "Submit to the developers" dialog — textures (#1965), ships, station/town modules and avatar skins
    /// (#3262) — built for real (Open → Build; edit mode runs no Update). Pins #2575 — the three consent sentences were
    /// inset with offsetMin/offsetMax on a POINT-anchored label, came out −24 wide and rendered as empty boxes, so
    /// nothing could be submitted — and #2576: Submit stays clickable and, with a box still open, says what is missing
    /// and rings the open boxes instead of being a dead button. The consent sentences of EVERY kind are checked in
    /// every game language: each must fit its box completely.
    /// </summary>
    public sealed class CreationSubmitDialogEditModeTests
    {
        private readonly List<GameObject> _spawned = new();
        private bool _hadEventSystem, _hadUiSound;

        private static IEnumerable<GameLocale> Locales => (GameLocale[])Enum.GetValues(typeof(GameLocale));

        private static readonly string[] Kinds =
        {
            CreationSubmission.KindTexture, CreationSubmission.KindShip, CreationSubmission.KindStructure, CreationSubmission.KindAvatar,
        };

        private static IEnumerable<object[]> LocalesAndKinds()
        {
            foreach (var locale in Locales)
            {
                foreach (string kind in Kinds)
                {
                    yield return new object[] { locale, kind };
                }
            }
        }

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
            _hadUiSound = GameObject.Find("UiSound") != null;
        }

        [TearDown]
        public void TearDown()
        {
            var canvas = GameObject.Find(CreationSubmitDialog.CanvasName);
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

        [TestCaseSource(nameof(LocalesAndKinds))]
        public void ConsentSentences_AreVisibleAndFitTheirBoxCompletely(GameLocale locale, string kind)
        {
            var table = LocaleTable(locale);
            var english = LocaleTable(GameLocale.English);
            OpenDialog(key => table.TryGetValue(key, out var text) || english.TryGetValue(key, out text) ? text : key, kind);
            RequireFontRasterization();

            for (int i = 1; i <= 3; i++)
            {
                var button = Part<Button>(CreationSubmitDialog.ConsentName + i);
                var box = ((RectTransform)button.transform).rect;
                var label = button.GetComponentInChildren<Text>();
                var rt = label.rectTransform;
                string key = CreationSubmitDialog.TextKey(kind, "check_" + i);
                Assert.IsTrue(english.ContainsKey(key), $"consent {i} of '{kind}' has an English sentence ({key})");
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
                    $"consent {i} of '{kind}' ({locale.Code()}): the sentence must fit its box completely, even at the smallest best-fit size — "
                    + "a consent the child cannot read in full is no consent");
            }
        }

        [Test]
        public void Submit_IsClickableBeforeAnyBoxIsTicked()
        {
            OpenDialog(key => key);
            Assert.IsTrue(Part<Button>(CreationSubmitDialog.SubmitName).interactable,
                "a Submit that is merely off looks clickable and does nothing — it must answer instead (#2576)");
        }

        [Test]
        public void SubmitWithAConsentMissing_SaysWhatIsMissingAndRingsOnlyTheOpenBoxes()
        {
            OpenDialog(key => key);
            Click(CreationSubmitDialog.ConsentName + 1);
            Click(CreationSubmitDialog.SubmitName);

            Assert.AreEqual("ui.tex.submit.need_consent", Part<Text>(CreationSubmitDialog.StatusName).text,
                "the status line names what is missing");
            Assert.IsFalse(Ring(1).enabled, "a ticked box gets no ring");
            Assert.IsTrue(Ring(2).enabled, "an open box is ringed");
            Assert.IsTrue(Ring(3).enabled, "an open box is ringed");
            Assert.IsTrue(Part<Button>(CreationSubmitDialog.SubmitName).interactable, "nothing was sent — Submit still answers");

            Click(CreationSubmitDialog.ConsentName + 2);
            Assert.IsFalse(Ring(2).enabled, "ticking a box takes its ring away");
            Assert.IsTrue(Ring(3).enabled);

            Click(CreationSubmitDialog.ConsentName + 3);
            Assert.IsFalse(Ring(3).enabled);
            Assert.AreEqual(string.Empty, Part<Text>(CreationSubmitDialog.StatusName).text, "all three ticked: the hint goes away");
            StringAssert.StartsWith("☑", Part<Button>(CreationSubmitDialog.ConsentName + 2).GetComponentInChildren<Text>().text);
        }

        [Test]
        public void EveryCreationKind_HasItsOwnTitleAndConsent_AndSharesTheAgeSentence()
        {
            foreach (string kind in Kinds)
            {
                if (kind == CreationSubmission.KindTexture)
                {
                    continue;
                }

                Assert.AreEqual($"ui.submit.{kind}.title", CreationSubmitDialog.TextKey(kind, "title"));
                Assert.AreEqual($"ui.submit.{kind}.check_1", CreationSubmitDialog.TextKey(kind, "check_1"));
                Assert.AreEqual("ui.tex.submit.check_3", CreationSubmitDialog.TextKey(kind, "check_3"));
                Assert.AreEqual("ui.submit.not_configured", CreationSubmitDialog.TextKey(kind, "not_configured"));
            }

            Assert.AreEqual("ui.tex.submit.title", CreationSubmitDialog.TextKey(CreationSubmission.KindTexture, "title"),
                "the texture keeps its approved wording");
        }

        [Test]
        public void ACreationDialog_ShowsItsKindsWording()
        {
            OpenDialog(key => key, CreationSubmission.KindShip);
            StringAssert.EndsWith("ui.submit.ship.check_1", Part<Button>(CreationSubmitDialog.ConsentName + 1).GetComponentInChildren<Text>().text);
            StringAssert.EndsWith("ui.tex.submit.check_3", Part<Button>(CreationSubmitDialog.ConsentName + 3).GetComponentInChildren<Text>().text);
        }

        [Test]
        public void Cancel_ClosesTheDialog()
        {
            var dialog = OpenDialog(key => key);
            Click(CreationSubmitDialog.CancelName);
            Assert.IsFalse(dialog.IsOpen);
            Assert.IsNull(GameObject.Find(CreationSubmitDialog.CanvasName));
        }

        // ---------------------------------------------------------------- helpers

        private CreationSubmitDialog OpenDialog(Func<string, string> localize, string kind = CreationSubmission.KindTexture)
        {
            var host = new GameObject("EditorUnderTest");
            _spawned.Add(host);
            var dialog = host.AddComponent<CreationSubmitDialog>();
            dialog.Localize = localize;
            if (kind == CreationSubmission.KindTexture)
            {
                dialog.Open("ash", "Ash", new[] { new byte[16] }, 0);
            }
            else
            {
                dialog.Open(new CreationSubmitRequest
                {
                    Kind = kind,
                    Key = "my_" + kind,
                    DisplayName = "Mine",
                    Payload = new CreationPayload { CellCount = 1 }.Add(kind + ".json", "{}"),
                });
            }

            Assert.IsTrue(dialog.IsOpen, "the dialog opened");
            return dialog;
        }

        private static T Part<T>(string name) where T : Component
        {
            var canvas = GameObject.Find(CreationSubmitDialog.CanvasName);
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

        private static Image Ring(int consent) => Part<Image>(CreationSubmitDialog.ConsentRingName + consent);

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
