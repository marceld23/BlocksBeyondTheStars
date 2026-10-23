// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Pins the main menu <see cref="UiMainMenu.Build"/> assembles (#2910). The golden is a dump of the whole canvas
    /// hierarchy — depth first, children in sibling order, one line per GameObject with its name, active flag,
    /// component types and rect, plus colour, size and string of every text and the limit and content type of every
    /// input field. Sibling order is behaviour on this screen: <c>UiKit.BootScreen</c> staggers the canvas's direct
    /// children by index, and draw order, raycast order and the gamepad focus order all follow the hierarchy.
    /// <para>
    /// Like <c>ChunkMesherGoldenEditModeTests</c> this is a golden: a deliberate menu change fails it, and re-baking is
    /// copying the <c>MainMenuHierarchy.actual.txt</c> the failing test writes (its path is in the failure message) over
    /// <c>MainMenuHierarchy.golden.txt</c>. The dump comes from the Editor, so it covers the native branch only
    /// (<c>UNITY_EDITOR</c> is always defined); the shell never ran Awake, so every text is its locale key, and its
    /// settings are a fresh <see cref="ClientSettings"/> (signed out of the portal). The version string is masked so a
    /// bundleVersion bump does not break it.
    /// </para>
    /// The wiring tests click the real buttons: the hierarchy cannot see a button whose lambda reads a field nothing ever
    /// assigned. They stop short of every path that reaches <c>ClientSettings.Save()</c> — that would write the
    /// developer's real settings file — so the play buttons are only clicked without a name.
    /// </summary>
    public sealed class MainMenuHierarchyEditModeTests
    {
        private const string GoldenFile = "MainMenuHierarchy.golden.txt";
        private const string ActualFile = "MainMenuHierarchy.actual.txt";
        private const string NeedName = "ui.webgl.need_name";

        // The canvas root's last three children, in build order: the connect dialog, the Official-worlds overlay and
        // the participate ("Mach mit") overlay — all hidden until their button opens them.
        private const int ConnectFromEnd = 3;
        private const int OfficialFromEnd = 2;
        private const int ParticipateFromEnd = 1;

        private readonly List<GameObject> _spawned = new();
        private bool _hadEventSystem;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;
            ClientSettings.LoadNoticeKey = ""; // the menu claims a pending settings-recovery notice — start without one
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();
            ClientSettings.LoadNoticeKey = "";

            // CreateCanvas makes an EventSystem when none exists — leave the scene as we found it.
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                Object.DestroyImmediate(es.gameObject);
            }
        }

        private T Track<T>(T go) where T : Object
        {
            _spawned.Add(go is Component c ? c.gameObject : go as GameObject);
            return go;
        }

        /// <summary>The real menu on an AppShell that never ran Awake: no localizer (texts are keys), no player name.</summary>
        private Transform BuildMenu()
        {
            var shell = Track(new GameObject("ShellUnderTest").AddComponent<AppShell>());
            shell.SetSettingsForTest(new ClientSettings());
            return Track(UiMainMenu.Build(shell)).transform;
        }

        [Test]
        public void Hierarchy_MatchesGolden()
        {
            string actual = Dump(BuildMenu());
            string goldenPath = Path.Combine(Application.dataPath, "Tests", "EditMode", GoldenFile);
            string golden = File.Exists(goldenPath) ? File.ReadAllText(goldenPath).Replace("\r\n", "\n") : "";
            if (actual == golden)
            {
                return;
            }

            string actualPath = Path.Combine(Application.temporaryCachePath, ActualFile);
            File.WriteAllText(actualPath, actual);
            string[] actualLines = actual.Split('\n');
            string[] goldenLines = golden.Split('\n');
            int i = 0;
            while (i < actualLines.Length && i < goldenLines.Length && actualLines[i] == goldenLines[i])
            {
                i++;
            }

            Assert.Fail($"The main menu differs from {GoldenFile} at line {i + 1}:\n" +
                        $"  golden: {(i < goldenLines.Length ? goldenLines[i] : "<end>")}\n" +
                        $"  actual: {(i < actualLines.Length ? actualLines[i] : "<end>")}\n" +
                        $"Full dump: {actualPath} — copy it over the golden only if the change is deliberate.");
        }

        [Test]
        public void Canvas_IsTheMainMenuScreen()
        {
            var root = BuildMenu();

            Assert.AreEqual("MainMenuUI", root.name);
            Assert.Greater(root.childCount, ConnectFromEnd);
            foreach (int fromEnd in new[] { ConnectFromEnd, OfficialFromEnd, ParticipateFromEnd })
            {
                Assert.IsFalse(Overlay(root, fromEnd).activeSelf, $"root child {root.childCount - fromEnd} is a hidden overlay");
            }
        }

        [TestCase("ui.menu.singleplayer")]
        [TestCase("ui.menu.host")]
        [TestCase("ui.menu.join")]
        [TestCase("ui.menu.official")]
        public void PlayButtons_WithoutAName_WarnAndOpenNothing(string label)
        {
            var root = BuildMenu();
            Assert.IsNull(TextShowing(root, NeedName), "no warning before the click");

            Click(ButtonIn(root, label));

            Assert.IsNotNull(TextShowing(root, NeedName), $"'{label}' without a name shows the name warning");
            Assert.IsFalse(Overlay(root, ConnectFromEnd).activeSelf, "the connect dialog stays shut");
            Assert.IsFalse(Overlay(root, OfficialFromEnd).activeSelf, "the Official-worlds overlay stays shut");
        }

        [Test]
        public void ContributeButton_OpensTheParticipateOverlay_AndBackClosesIt()
        {
            var root = BuildMenu();
            var participate = Overlay(root, ParticipateFromEnd);

            Click(ButtonIn(root, "ui.menu.contribute"));
            Assert.IsTrue(participate.activeSelf, "'Mach mit' opens the participate overlay");

            Click(ButtonIn(participate.transform, "ui.menu.back"));
            Assert.IsFalse(participate.activeSelf, "its Back button closes it again");
        }

        [Test]
        public void ConnectDialog_BackButtonClosesIt()
        {
            var connect = Overlay(BuildMenu(), ConnectFromEnd);
            connect.SetActive(true);

            Click(ButtonIn(connect.transform, "ui.menu.back"));

            Assert.IsFalse(connect.activeSelf);
        }

        [Test]
        public void OfficialWorlds_BackButtonClosesIt()
        {
            var official = Overlay(BuildMenu(), OfficialFromEnd);
            official.SetActive(true);

            Click(ButtonIn(official.transform, "ui.menu.back"));

            Assert.IsFalse(official.activeSelf);
        }

        [Test]
        public void NameReservedRetry_ShowsTheReplacementNameInTheMenuField()
        {
            // The Official-worlds join retry after "name reserved/blocked" hands its new name to the menu's write-back,
            // which must update the visible field too, not only the value CommitName reads (#2926). The retry itself
            // needs a portal answer, so this drives the write-back the overlay is given — on a Builder of its own,
            // reached by reflection (both are private to UiMainMenu).
            var shell = Track(new GameObject("ShellUnderTest").AddComponent<AppShell>());
            shell.SetSettingsForTest(new ClientSettings());
            shell.PlayerName = "Refused";
            var canvas = Track(UiKit.CreateCanvas("BuilderUnderTest"));
            var builderType = typeof(UiMainMenu).GetNestedType("Builder", BindingFlags.NonPublic);
            Assert.IsNotNull(builderType, "UiMainMenu.Builder");
            object builder = System.Activator.CreateInstance(builderType, shell, canvas.transform);
            builderType.GetMethod("BuildNativeMenu").Invoke(builder, null);
            var nameField = canvas.GetComponentsInChildren<InputField>(true).Single();
            Assert.AreEqual("Refused", nameField.text, "the menu field starts with the shell's name");

            var writeBack = builderType.GetMethod("SetMenuName", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(writeBack, "the menu's name write-back the Official-worlds overlay is given");
            writeBack.Invoke(builder, new object[] { "Nova" });

            Assert.AreEqual("Nova", nameField.text, "the visible name field shows the replacement name");
            Assert.AreEqual("Nova", builderType.GetField("_natName", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(builder),
                "the value CommitName reads is the replacement name");
        }

        private static GameObject Overlay(Transform root, int fromEnd) => root.GetChild(root.childCount - fromEnd).gameObject;

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

        private static Text TextShowing(Transform root, string text)
            => root.GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.text == text);

        private static string Dump(Transform root)
        {
            var sb = new StringBuilder();
            Append(sb, root, 0);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, Transform node, int depth)
        {
            var go = node.gameObject;
            sb.Append(' ', depth * 2).Append(go.name)
                .Append(" active=").Append(go.activeSelf ? "True" : "False")
                .Append(" comps=").Append(string.Join(",", go.GetComponents<Component>().Select(c => c == null ? "<missing>" : c.GetType().Name)));

            // The canvas root's own rect is driven by the screen (the Editor's game view) — only its children are layout.
            if (depth > 0 && node is RectTransform rt)
            {
                sb.Append(" pos=").Append(Vec(rt.anchoredPosition)).Append(" size=").Append(Vec(rt.sizeDelta));
            }

            var graphic = go.GetComponent<Graphic>();
            if (graphic != null)
            {
                sb.Append(" color=").Append(ColorUtility.ToHtmlStringRGBA(graphic.color));
            }

            var text = go.GetComponent<Text>();
            if (text != null)
            {
                sb.Append(" font=").Append(text.fontSize.ToString(CultureInfo.InvariantCulture))
                    .Append(" text=\"").Append(Mask(text.text)).Append('"');
            }

            var input = go.GetComponent<InputField>();
            if (input != null)
            {
                sb.Append(" limit=").Append(input.characterLimit.ToString(CultureInfo.InvariantCulture))
                    .Append(" content=").Append(input.contentType);
            }

            sb.Append('\n');
            for (int i = 0; i < node.childCount; i++)
            {
                Append(sb, node.GetChild(i), depth + 1);
            }
        }

        private static string Vec(Vector2 v)
            => "(" + v.x.ToString("F2", CultureInfo.InvariantCulture) + ", " + v.y.ToString("F2", CultureInfo.InvariantCulture) + ")";

        /// <summary>One line per node, and the build version (it moves with every release) masked.</summary>
        private static string Mask(string text)
        {
            string masked = (text ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
            return string.IsNullOrEmpty(AppShell.Version) ? masked : masked.Replace(AppShell.Version, "<ver>");
        }
    }
}
