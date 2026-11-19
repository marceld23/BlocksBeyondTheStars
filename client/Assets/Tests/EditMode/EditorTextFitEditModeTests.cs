// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Localization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The editors' texts in Unity itself (#3263), the counterpart of the headless <c>EditorTextFitTests</c>: every editor
    /// screen is built for real in every game language and every <see cref="Text"/> on it is measured with Unity's own
    /// <see cref="TextGenerator"/> — words are at least 13 px (a glyph or a number may be smaller), a wrapping text breaks
    /// only between words (or between CJK characters) and is not cut, and a one-line text stays inside its box. Edit mode
    /// runs no Start, so the screens are started by reflection; a screen that cannot be built without a running game is
    /// reported inconclusive instead of failing.
    /// </summary>
    public sealed class EditorTextFitEditModeTests
    {
        private static readonly string[] Screens =
        {
            "hub", "station", "station-module", "settlement", "settlement-module", "kit-panel", "form-picker", "ship", "avatar",
            "texture", "form", "toollook", "material", "content", "shape", "paint", "appearance", "appearance-hud",
        };

        private readonly List<GameObject> _spawned = new();
        private bool _hadEventSystem;
        private Func<string, string> _previousLocalize;

        private static IEnumerable<object[]> ScreensAndLocales()
        {
            foreach (string screen in Screens)
            {
                foreach (GameLocale locale in Enum.GetValues(typeof(GameLocale)))
                {
                    yield return new object[] { screen, locale };
                }
            }
        }

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null;
            _previousLocalize = UiKit.Localize;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
            UiKit.Localize = _previousLocalize;
            LogAssert.ignoreFailingMessages = false;
            var es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                UnityEngine.Object.DestroyImmediate(es.gameObject);
            }
        }

        [TestCaseSource(nameof(ScreensAndLocales))]
        public void EveryText_IsReadableAndFitsItsBox(string screen, GameLocale locale)
        {
            UiTextFit.RequireFontRasterization();
            var shell = NewShell(locale);
            var before = new HashSet<GameObject>(Roots());
            string buildError = Build(screen, shell);
            var texts = Roots().Where(go => !before.Contains(go)).Select(Track)
                .SelectMany(go => go.GetComponentsInChildren<Text>(false)).ToList();
            if (texts.Count == 0)
            {
                Assert.Inconclusive($"'{screen}' could not be built in edit mode: {buildError ?? "no texts"}");
            }

            var problems = UiTextFit.Problems(texts, UiKit.EditorMinFontSize);

            Assert.IsEmpty(problems, $"'{screen}' in {locale.Code()}{(buildError != null ? " (built with: " + buildError + ")" : string.Empty)}:\n"
                                     + string.Join("\n", problems));
        }

        // ---------------------------------------------------------------- building the screens

        /// <summary>Builds <paramref name="screen"/>; returns the error of a build that threw part-way (its texts are still
        /// checked), else null.</summary>
        private string Build(string screen, AppShell shell)
        {
            LogAssert.ignoreFailingMessages = true; // scene set-up logs (Destroy, missing content) are not this test's subject
            try
            {
                switch (screen)
                {
                    case "hub": Track(UiEditors.Build(shell)); break;
                    case "station": Start<StructureEditor>(shell, e => e.EditorMode = StructureEditor.Mode.Station); break;
                    case "station-module": Start<StructureEditor>(shell, e => { e.EditorMode = StructureEditor.Mode.Station; Set(e, "_moduleMode", true); Set(e, "_function", "hub"); }); break;
                    case "settlement": Start<StructureEditor>(shell, e => e.EditorMode = StructureEditor.Mode.Settlement); break;
                    case "settlement-module": Start<StructureEditor>(shell, e => { e.EditorMode = StructureEditor.Mode.Settlement; Set(e, "_moduleMode", true); Set(e, "_function", "house"); }); break;
                    case "kit-panel": Call(Start<StructureEditor>(shell, null), "OpenKitPanel"); break;
                    case "form-picker": Call(Start<StructureEditor>(shell, null), "OpenFormPicker"); break;
                    case "ship": Start<ShipEditor>(shell, null); break;
                    case "avatar": Start<AvatarEditor>(shell, null); break;
                    case "texture": Start<TextureEditor>(shell, null); break;
                    case "form": Start<FormEditor>(shell, null); break;
                    case "toollook": Start<ToolLookEditor>(shell, null); break;
                    case "material": Start<MaterialEditor>(shell, null); break;
                    case "content": Start<ContentEditor>(shell, null); break;
                    case "shape": Start<ShapeEditor>(shell, e => e.Localizer = shell.L); break;
                    case "paint": Start<FaceEditor>(shell, e => e.Localizer = shell.L); break;
                    case "appearance": Start<FaceEditor>(shell, e => Appearance(e, shell)); break;
                    case "appearance-hud": Call(Start<FaceEditor>(shell, e => Appearance(e, shell)), "EnterHudMode"); break;
                }

                return null;
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
                return inner.GetType().Name + ": " + inner.Message;
            }
        }

        /// <summary>Adds the editor, sets its shell (and whatever <paramref name="prepare"/> sets) and runs its Start.</summary>
        private T Start<T>(AppShell shell, Action<T> prepare) where T : MonoBehaviour
        {
            var editor = Track(new GameObject(typeof(T).Name + "UnderTest")).AddComponent<T>();
            typeof(T).GetField("Shell", BindingFlags.Public | BindingFlags.Instance)?.SetValue(editor, shell);
            prepare?.Invoke(editor);
            Call(editor, "Start");
            return editor;
        }

        /// <summary>The character editor as both avatar hosts build it (#3318): the five part tabs, the outfit column with a
        /// skin template, the HUD mode. No live figure — its preview rig needs a running player.</summary>
        private static void Appearance(FaceEditor editor, AppShell shell)
        {
            var colors = new[] { Color.white, Color.gray, Color.gray, Color.gray };
            var paints = new string[4];
            string face = string.Empty;
            editor.Localizer = shell.L;
            editor.Subjects = AppearanceSubjects.Build(() => face, f => face = f, part => paints[part] ?? string.Empty,
                (part, p) => paints[part] = p, which => colors[which], (which, c) => colors[which] = c);
            editor.OutfitNames = () => new List<string> { "Mein Skin" };
            editor.SkinTemplateNames = () => new List<string> { shell.L("ui.ed.default.skin") };
            editor.HudHost = new HudFrameEditorHost
            {
                Settings = shell.Settings,
                Localize = shell.L,
                Templates = () => new List<BlocksBeyondTheStars.Shared.Festivals.HudFrameDefinition>(),
            };
        }

        private static void Call(object target, string method)
            => target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.Invoke(target, null);

        private static void Set(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(target, value);

        /// <summary>An AppShell that never ran Awake, with settings and the locale's table (English behind it) — what the
        /// editors read their texts from.</summary>
        private AppShell NewShell(GameLocale locale)
        {
            var shell = Track(new GameObject("ShellUnderTest")).AddComponent<AppShell>();
            shell.SetSettingsForTest(new ClientSettings());
            var localizer = new Localizer(locale, UiTextFit.LocaleTable(locale), UiTextFit.LocaleTable(GameLocale.English));
            typeof(AppShell).GetProperty(nameof(AppShell.Localizer))?.SetValue(shell, localizer);
            UiKit.Localize = shell.L;
            return shell;
        }

        private GameObject Track(GameObject go)
        {
            if (go != null && !_spawned.Contains(go))
            {
                _spawned.Add(go);
            }

            return go;
        }

        private static IEnumerable<GameObject> Roots()
            => UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
    }
}
