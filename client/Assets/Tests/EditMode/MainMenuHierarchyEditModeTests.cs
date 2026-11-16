// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
            shell.SetSettingsForTest(NoLastWorld());
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
            Assert.Greater(root.childCount, ParticipateFromEnd);
            Assert.IsFalse(Overlay(root, ParticipateFromEnd).activeSelf, "the participate overlay starts hidden");
        }

        [Test]
        public void Decoration_IsGone()
        {
            // #3663: the SYSTEM CHECK panel (five fixed "OK"s) and the static "world / server info" panel (always "Mode:
            // Survival", a mode the game does not offer) made room for real content.
            var root = BuildMenu();
            Assert.IsNull(TextShowing(root, "ui.menu.system_check"));
            Assert.IsNull(TextShowing(root, "ui.sys.ok"));
            Assert.IsNull(TextShowing(root, "ui.info.mode_title"));
        }

        [Test]
        public void ProfileChip_HoldsTheOnlyNameField()
        {
            var shell = Track(new GameObject("ShellUnderTest").AddComponent<AppShell>());
            shell.SetSettingsForTest(NoLastWorld());
            shell.PlayerName = "Nova";
            var root = Track(UiMainMenu.Build(shell)).transform;

            var field = root.GetComponentsInChildren<InputField>(true).Single();
            Assert.AreEqual("Nova", field.text, "the chip shows the shell's player name");
        }

        [Test]
        public void MultiplayerIsOneEntry()
        {
            // #3663: Host game, Join server and Official worlds were three top-level buttons; one Multiplayer entry now.
            var root = BuildMenu();
            ButtonIn(root, "ui.menu.multiplayer");
            Assert.IsNull(TextShowing(root, "ui.menu.host"));
            Assert.IsNull(TextShowing(root, "ui.menu.join"));
            Assert.IsNull(TextShowing(root, "ui.menu.official"));
        }

        [TestCase("ui.menu.singleplayer")]
        [TestCase("ui.menu.multiplayer")]
        public void PlayButtons_WithoutAName_WarnAndOpenNothing(string label)
        {
            var shell = Track(new GameObject("ShellUnderTest").AddComponent<AppShell>());
            shell.SetSettingsForTest(NoLastWorld());
            var root = Track(UiMainMenu.Build(shell)).transform;
            var before = shell.Phase;
            Assert.IsNull(TextShowing(root, NeedName), "no warning before the click");

            Click(ButtonIn(root, label));

            Assert.IsNotNull(TextShowing(root, NeedName), $"'{label}' without a name shows the name warning");
            Assert.AreEqual(before, shell.Phase, "no screen opens without a name");
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
        public void CommunityRow_IsFlushWithTheSideRowAboveIt()
        {
            // #3716: Feedback / Join in / Website sit on the grid of Settings / Credits / Quit — the same left edges and the
            // same widths, so the row ends where the button column ends (it used to run 114 px past it).
            var root = BuildMenu();
            string[] above = { "ui.menu.settings", "ui.menu.credits", "ui.menu.quit" };
            string[] row = { "ui.menu.feedback", "ui.menu.contribute", "ui.menu.website" };
            for (int i = 0; i < row.Length; i++)
            {
                var upper = (RectTransform)ButtonIn(root, above[i]).transform;
                var lower = (RectTransform)ButtonIn(root, row[i]).transform;
                Assert.AreEqual(upper.anchoredPosition.x, lower.anchoredPosition.x, 0.01f, $"'{row[i]}' starts where '{above[i]}' starts");
                Assert.AreEqual(upper.sizeDelta.x, lower.sizeDelta.x, 0.01f, $"'{row[i]}' is as wide as '{above[i]}'");
            }
        }

        [Test]
        public void FeedbackButton_WithoutTheShellsForm_DoesNothing()
        {
            // #3715: the button hands over to the shell's own FeedbackUi (FeedbackUi.OpenFromMenu). The menu under test has
            // none — its shell never ran Awake — so the click must be a quiet no-op, not a null reference.
            var root = BuildMenu();
            Assert.DoesNotThrow(() => Click(ButtonIn(root, "ui.menu.feedback")));
        }

        /// <summary>Settings without a last world: whether a save of that name exists on this machine must not change the
        /// menu under test (Continue shows only for an existing save, #3663).</summary>
        private static ClientSettings NoLastWorld() => new ClientSettings { LastWorld = "" };

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
