// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.PlayMode
{
    /// <summary>
    /// #2936 — the rig's prompts and screens take their canvases along when the rig is torn down. <c>UiKit.CreateCanvas</c>
    /// makes a scene-root object, so destroying the rig root alone left it behind: an open launch question stayed over the
    /// main menu with buttons that called into a destroyed GameBootstrap, and every world join left hidden canvases
    /// behind (the <see cref="UiPanelPlayModeTests"/> leak class, #2303/#2925). Each case builds the component's canvas the
    /// way the component does, destroys the component's GameObject (the rig root), and expects the canvas gone. This
    /// lives in PlayMode because OnDestroy never runs in edit mode; the Client.Tests twin
    /// (<c>RigCanvasCleanupGuardTests</c>) keeps a new rig prompt from forgetting it in PR CI.
    /// </summary>
    public sealed class RigCanvasCleanupPlayModeTests
    {
        private GameObject _host;
        private readonly List<GameObject> _canvases = new List<GameObject>();
        private bool _hadEventSystem;

        [SetUp]
        public void SetUp()
        {
            _hadEventSystem = Object.FindAnyObjectByType<EventSystem>() != null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _canvases.Where(go => go != null))
            {
                Object.Destroy(go);
            }

            _canvases.Clear();
            if (_host != null)
            {
                Object.Destroy(_host);
            }

            // CreateCanvas makes an EventSystem when none exists — leave the scene as we found it.
            var es = Object.FindAnyObjectByType<EventSystem>();
            if (!_hadEventSystem && es != null)
            {
                Object.Destroy(es.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator LaunchPrompt_TakesItsCanvasAlong() => BuiltCanvasGoesWithTheComponent<LaunchPrompt>("EnsureUi");

        [UnityTest]
        public IEnumerator VendorChoicePrompt_TakesItsCanvasAlong() => BuiltCanvasGoesWithTheComponent<VendorChoicePrompt>("EnsureUi");

        [UnityTest]
        public IEnumerator RespawnPrompt_TakesItsCanvasAlong() => BuiltCanvasGoesWithTheComponent<RespawnPrompt>("EnsureUi");

        [UnityTest]
        public IEnumerator MaintenanceUi_TakesItsCanvasAlong() => BuiltCanvasGoesWithTheComponent<MaintenanceUi>("EnsureUi");

        [UnityTest]
        public IEnumerator DisconnectScreen_TakesItsCanvasAlong() => BuiltCanvasGoesWithTheComponent<DisconnectScreen>("EnsureUi");

        [UnityTest]
        public IEnumerator BanditDemandUi_TakesItsCanvasAlong() => BuiltCanvasGoesWithTheComponent<BanditDemandUi>("EnsureBuilt");

        [UnityTest]
        public IEnumerator NpcDialogUi_TakesItsCanvasAlong() => BuiltCanvasGoesWithTheComponent<NpcDialogUi>("EnsureBuilt");

        /// <summary>Built twice: the <c>_flash != null</c> early-out still holds, one canvas — and that one goes along.</summary>
        [UnityTest]
        public IEnumerator DeathFx_BuildsOneCanvas_AndTakesItAlong() => BuiltCanvasGoesWithTheComponent<DeathFx>("EnsureUi", builds: 2);

        [UnityTest]
        public IEnumerator TouchControlsUi_TakesItsCanvasAlong() => BuiltCanvasGoesWithTheComponent<TouchControlsUi>("Build");

        [UnityTest]
        public IEnumerator VendorTradeUI_TakesItsCanvasAlong() => BuiltCanvasGoesWithTheComponent<VendorTradeUI>("Build");

        /// <summary>The star chart's canvas lives only while the chart is open — torn down open, it must go along. Its
        /// build reads the live star map, so the open chart's canvas is handed in.</summary>
        [UnityTest]
        public IEnumerator SpaceMap_TakesAnOpenChartsCanvasAlong() => HeldCanvasesGoWithTheComponent<SpaceMap>("_canvas");

        /// <summary>The Tab menu's confirms and the suit-slot picker are top-level canvases of their own; closing the menu
        /// drops them, the rig teardown did not. Their builds need a world, so the open dialogs' canvases are handed in.</summary>
        [UnityTest]
        public IEnumerator CraftingTechShipUI_TakesItsOpenDialogsAlong()
            => HeldCanvasesGoWithTheComponent<CraftingTechShipUI>("_storySwitchDialog", "_discardDialog", "_pickerCanvas");

        /// <summary>Runs the component's own canvas builder <paramref name="builds"/> times, then destroys the rig root.</summary>
        private IEnumerator BuiltCanvasGoesWithTheComponent<T>(string builder, int builds = 1) where T : MonoBehaviour
        {
            var component = AddToRig<T>();
            var before = new HashSet<Canvas>(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include));
            var build = typeof(T).GetMethod(builder, BindingFlags.Instance | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null);
            Assert.IsNotNull(build, $"{typeof(T).Name}.{builder}() is the canvas builder this test drives");
            for (int i = 0; i < builds; i++)
            {
                build.Invoke(component, null);
            }

            var built = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include)
                .Where(c => !before.Contains(c) && c.transform.parent == null)
                .Select(c => c.gameObject)
                .ToList();
            _canvases.AddRange(built);
            Assert.AreEqual(1, built.Count, $"precondition: {typeof(T).Name}.{builder} builds one scene-root canvas");

            Object.Destroy(_host);
            yield return null;
            yield return null;

            Assert.IsTrue(built[0] == null, $"the {typeof(T).Name} canvas must go with the rig, not outlive it");
        }

        /// <summary>Hands a scene-root canvas into each named field (a Canvas or its GameObject), then destroys the rig
        /// root.</summary>
        private IEnumerator HeldCanvasesGoWithTheComponent<T>(params string[] fields) where T : MonoBehaviour
        {
            var component = AddToRig<T>();
            var held = new List<GameObject>();
            foreach (string name in fields)
            {
                var field = typeof(T).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(field, $"{typeof(T).Name}.{name} holds the canvas this test hands in");
                var canvas = UiKit.CreateCanvas(typeof(T).Name + name);
                held.Add(canvas.gameObject);
                field.SetValue(component, field.FieldType == typeof(GameObject) ? (object)canvas.gameObject : canvas);
            }

            _canvases.AddRange(held);
            Object.Destroy(_host);
            yield return null;
            yield return null;

            for (int i = 0; i < fields.Length; i++)
            {
                Assert.IsTrue(held[i] == null, $"the canvas in {typeof(T).Name}.{fields[i]} must go with the rig, not outlive it");
            }
        }

        /// <summary>The component on a stand-in rig root. Disabled: there is no Game here, and only the build and the
        /// teardown are under test (OnDestroy still runs — the GameObject was active).</summary>
        private T AddToRig<T>() where T : MonoBehaviour
        {
            _host = new GameObject("Game");
            var component = _host.AddComponent<T>();
            component.enabled = false;
            return component;
        }
    }
}
