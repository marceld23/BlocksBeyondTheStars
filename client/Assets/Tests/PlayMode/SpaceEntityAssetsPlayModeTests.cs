// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.PlayMode
{
    /// <summary>
    /// #2938 — a space entity's model takes the Materials and 64×64 block tiles ModelKit made for it along when it goes.
    /// Unity never garbage-collects a code-made Material or Texture2D, and the flight view's despawn only destroys the
    /// GameObject, so every drone, drop, pod or station that came and went left its materials and tiles behind until the
    /// return-to-menu sweep — a long flight grew memory for nothing. Each case runs the view's own builders
    /// (<c>BuildEntityModel</c>, the call <c>SyncEntities</c> makes for a new id, and <c>BuildRemoteAvatar</c>) on a
    /// stand-in scene root, destroys the model the way the view does and counts the live Material and Texture2D objects.
    /// A warm-up build first fills the static caches the builders share (the plume material and the like). Models are
    /// built and destroyed in the same frame, so none of their views ever runs an Update. PlayMode because OnDestroy never
    /// runs in edit mode.
    /// </summary>
    public sealed class SpaceEntityAssetsPlayModeTests
    {
        /// <summary>Every entity kind whose model is built from ModelKit parts (the anomaly and the wormhole use shared
        /// cached materials of their own). "Wreck" stands for the fallback cube.</summary>
        private static readonly string[] Kinds =
        {
            "SpaceStation", "Drone", "Ufo", "Cruiser", "BanditShip", "EscapePod", "DebrisField", "SalvageCapsule",
            "ResourceDrop", "Wreck",
        };

        private GameObject _host;
        private GameObject _sceneRoot;
        private SpaceView _view;

        [SetUp]
        public void SetUp()
        {
            // Inactive host: the view never wakes up (no Game, no camera) — only its builders are under test.
            _host = new GameObject("SpaceViewHost");
            _host.SetActive(false);
            _view = _host.AddComponent<SpaceView>();
            _sceneRoot = new GameObject("SpaceScene");
            var root = typeof(SpaceView).GetField("_root", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(root, "SpaceView._root is the scene root the builders parent to");
            root.SetValue(_view, _sceneRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (_sceneRoot != null)
            {
                Object.Destroy(_sceneRoot);
            }

            if (_host != null)
            {
                Object.Destroy(_host);
            }
        }

        [UnityTest]
        public IEnumerator EntityModels_TakeTheirMaterialsAndTilesAlong_WhenTheyDespawn()
        {
            DestroyAll(SpawnAll());
            yield return null;
            yield return null;
            int materials = Live<Material>(), tiles = Live<Texture2D>();

            for (int round = 0; round < 3; round++)
            {
                var models = SpawnAll();
                Assert.Greater(Live<Material>(), materials, "precondition: the entity builders make materials of their own");
                Assert.Greater(Live<Texture2D>(), tiles, "precondition: the entity builders load block tiles of their own");
                DestroyAll(models); // what SyncEntities does when an id leaves the snapshot
                yield return null;
                yield return null;
            }

            Assert.AreEqual(materials, Live<Material>(), "despawned entities must not leave their materials behind");
            Assert.AreEqual(tiles, Live<Texture2D>(), "despawned entities must not leave their block tiles behind");
        }

        /// <summary>An exploding hostile flings its cubes for a second or two after its model is gone
        /// (<c>SpaceFx.FlingParts</c>): a flung part keeps its material and tile while it flies, and they go with it.</summary>
        [UnityTest]
        public IEnumerator AFlungPart_KeepsItsLook_UntilItIsGoneToo()
        {
            Object.Destroy(Spawn("Drone"));
            yield return null;
            yield return null;
            int materials = Live<Material>(), tiles = Live<Texture2D>();

            var drone = Spawn("Drone");
            var part = drone.transform.Find("Core");
            Assert.IsNotNull(part, "precondition: the drone has a Core cube");
            var look = part.GetComponent<Renderer>().sharedMaterial;
            var tile = look.mainTexture;
            Assert.IsTrue(tile != null, "precondition: the Guardian plating is textured");
            part.SetParent(null, true);
            Object.Destroy(drone);
            yield return null;
            yield return null;

            Assert.IsTrue(look != null && part.GetComponent<Renderer>().sharedMaterial == look, "the flung part keeps its material while it flies");
            Assert.IsTrue(tile != null, "the flung part keeps its block tile while it flies");

            Object.Destroy(part.gameObject);
            yield return null;
            yield return null;
            Assert.AreEqual(materials, Live<Material>(), "the last part takes the model's materials along");
            Assert.AreEqual(tiles, Live<Texture2D>(), "the last part takes the model's block tiles along");
        }

        /// <summary>Another pilot is drawn with a placeholder hull until their real voxel ship arrives; the swap destroys
        /// the placeholder, and a pilot who leaves takes the whole avatar along.</summary>
        [UnityTest]
        public IEnumerator ARemotePilotsPlaceholderHull_TakesItsMaterialsAlong()
        {
            var build = typeof(SpaceView).GetMethod("BuildRemoteAvatar", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(build, "SpaceView.BuildRemoteAvatar builds another pilot's placeholder ship");
            Object.Destroy(AvatarPart(build.Invoke(_view, null), "Root"));
            yield return null;
            yield return null;
            int materials = Live<Material>(), tiles = Live<Texture2D>();

            var swapped = build.Invoke(_view, null);
            Assert.Greater(Live<Material>(), materials, "precondition: the placeholder hull has materials of its own");
            Object.Destroy(AvatarPart(swapped, "Ship")); // the pilot's real voxel hull arrived
            yield return null;
            yield return null;
            Assert.AreEqual(materials, Live<Material>(), "the swapped-out placeholder hull must take its materials along");
            Assert.AreEqual(tiles, Live<Texture2D>(), "the swapped-out placeholder hull must take its block tiles along");
            Object.Destroy(AvatarPart(swapped, "Root"));

            Object.Destroy(AvatarPart(build.Invoke(_view, null), "Root")); // the pilot left the instance
            yield return null;
            yield return null;
            Assert.AreEqual(materials, Live<Material>(), "a pilot who left must take the hull's materials along");
            Assert.AreEqual(tiles, Live<Texture2D>(), "a pilot who left must take the hull's block tiles along");
        }

        private GameObject Spawn(string kind)
        {
            var build = typeof(SpaceView).GetMethod("BuildEntityModel", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(build, "SpaceView.BuildEntityModel is the builder SyncEntities calls for a new entity");
            var go = (GameObject)build.Invoke(_view, new object[] { new NetCombatEntity { Id = "test-" + kind, Kind = kind } });
            Assert.IsTrue(go != null, $"precondition: a {kind} gets a model");
            return go;
        }

        private List<GameObject> SpawnAll()
        {
            var models = new List<GameObject>();
            foreach (string kind in Kinds)
            {
                models.Add(Spawn(kind));
            }

            return models;
        }

        private static void DestroyAll(List<GameObject> models)
        {
            foreach (var go in models)
            {
                Object.Destroy(go);
            }
        }

        private static GameObject AvatarPart(object avatar, string field)
        {
            var info = avatar.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(info, $"RemoteAvatar.{field} holds the part this test destroys");
            return (GameObject)info.GetValue(avatar);
        }

        private static int Live<T>() where T : Object => Resources.FindObjectsOfTypeAll<T>().Length;
    }
}
