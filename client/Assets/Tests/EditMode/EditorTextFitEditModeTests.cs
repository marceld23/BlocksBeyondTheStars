// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
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
        /// <summary>A one-line text may overhang its box by this much (the glyphs' side bearings).</summary>
        private const float OverhangTolerance = 3f;

        private static readonly string[] Screens =
        {
            "hub", "station", "station-module", "settlement", "settlement-module", "kit-panel", "form-picker", "ship", "avatar",
            "texture", "form", "toollook", "material", "content", "shape", "paint",
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
            RequireFontRasterization();
            var shell = NewShell(locale);
            var before = new HashSet<GameObject>(Roots());
            string buildError = Build(screen, shell);
            var texts = Roots().Where(go => !before.Contains(go)).Select(Track)
                .SelectMany(go => go.GetComponentsInChildren<Text>(false)).ToList();
            if (texts.Count == 0)
            {
                Assert.Inconclusive($"'{screen}' could not be built in edit mode: {buildError ?? "no texts"}");
            }

            var problems = new List<string>();
            foreach (var text in texts)
            {
                string problem = Check(text);
                if (problem != null)
                {
                    problems.Add($"{HierarchyPath(text.transform)} \"{Shorten(text.text)}\": {problem}");
                }
            }

            Assert.IsEmpty(problems, $"'{screen}' in {locale.Code()}{(buildError != null ? " (built with: " + buildError + ")" : string.Empty)}:\n"
                                     + string.Join("\n", problems));
        }

        // ---------------------------------------------------------------- the rules

        /// <summary>The problem with one text, or null.</summary>
        private static string Check(Text text)
        {
            string s = text.text ?? string.Empty;
            if (s.Trim().Length == 0 || (text.supportRichText && s.IndexOf('<') >= 0) || text.GetComponentInParent<InputField>() != null)
            {
                return null; // empty, rich text (tags skew the indices) or a field the player types into
            }

            var rt = text.rectTransform;
            var settings = text.GetGenerationSettings(rt.rect.size);
            settings.scaleFactor = 1f; // reference units, as UiKit measures — independent of the test window
            var generator = new TextGenerator();
            generator.Populate(s, settings);
            int size = text.resizeTextForBestFit ? generator.fontSizeUsedForBestFit : text.fontSize;
            if (IsWords(s) && size < UiKit.EditorMinFontSize)
            {
                return $"{size} px — words are at least {UiKit.EditorMinFontSize} px in the editors";
            }

            if (text.horizontalOverflow == HorizontalWrapMode.Wrap)
            {
                if (text.verticalOverflow == VerticalWrapMode.Truncate && generator.characterCountVisible < s.Replace("\n", string.Empty).Length)
                {
                    return $"cut — only {generator.characterCountVisible} of {s.Length} characters fit {rt.rect.width:0}×{rt.rect.height:0}";
                }

                return BrokenWord(s, generator);
            }

            float width = generator.GetPreferredWidth(s, settings);
            return width > rt.rect.width + OverhangTolerance ? $"{width:0} px wide in a {rt.rect.width:0} px box at {size} px" : null;
        }

        /// <summary>A line that starts inside a word (not after a space or a line break, not at a CJK character).</summary>
        private static string BrokenWord(string s, TextGenerator generator)
        {
            var lines = new List<UILineInfo>();
            generator.GetLines(lines);
            for (int i = 1; i < lines.Count; i++)
            {
                int start = lines[i].startCharIdx;
                if (start <= 0 || start >= s.Length)
                {
                    continue;
                }

                char prev = s[start - 1], next = s[start];
                if (char.IsWhiteSpace(prev) || char.IsWhiteSpace(next) || ButtonLabelFit.HasCjk(prev.ToString()) || ButtonLabelFit.HasCjk(next.ToString())
                    || prev == '-' || prev == '/')
                {
                    continue;
                }

                return $"broken inside a word at \"{s.Substring(Math.Max(0, start - 8), Math.Min(16, s.Length - Math.Max(0, start - 8)))}\"";
            }

            return null;
        }

        /// <summary>True for a text with words: letters, more than three characters (a glyph, an arrow or "88°" is decoration).</summary>
        private static bool IsWords(string s) => s.Trim().Length > 3 && s.Count(char.IsLetter) >= 3;

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
            var localizer = new Localizer(locale, LocaleTable(locale), LocaleTable(GameLocale.English));
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

        private static Dictionary<string, string> LocaleTable(GameLocale locale)
        {
            string file = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "data", "locales", locale.Code() + ".json"));
            if (!File.Exists(file))
            {
                Assert.Ignore($"locale file not found: {file}");
            }

            return ContentLoader.ParseLocaleTable(File.ReadAllText(file));
        }

        private static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null && p.parent != null; p = p.parent)
            {
                sb.Insert(0, p.name + "/");
            }

            return sb.ToString();
        }

        private static string Shorten(string s) => s.Length <= 60 ? s.Replace("\n", "⏎") : s.Substring(0, 57).Replace("\n", "⏎") + "…";

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
