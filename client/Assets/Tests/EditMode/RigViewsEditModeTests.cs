// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Pins the one rule the rig's view registry (#2914) relies on: a <see cref="RigViews"/> getter returns the registered
    /// component, null while nothing is registered, and a TRUE null once the component is destroyed — so
    /// <c>Game.Views.Doors?.X()</c> skips a destroyed view exactly like the former <c>DoorView.Instance?.X()</c> did after
    /// its OnDestroy cleared the static. DoorView is a plain view (no <see cref="UiPanel"/>); its Awake/OnDestroy never run
    /// in edit mode, so a bare component is safe to create and destroy here.
    /// </summary>
    public sealed class RigViewsEditModeTests
    {
        [Test]
        public void UnsetViewIsNull()
        {
            Assert.That(new RigViews().Doors, Is.Null);
        }

        [Test]
        public void RegisteredViewIsReturned()
        {
            var views = new RigViews();
            var go = new GameObject("doors");
            try
            {
                var door = go.AddComponent<DoorView>();
                views.Doors = door;
                Assert.That(views.Doors, Is.SameAs(door));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DestroyedViewReadsAsNull()
        {
            var views = new RigViews();
            var go = new GameObject("doors");
            views.Doors = go.AddComponent<DoorView>();
            Object.DestroyImmediate(go);

            Assert.That(views.Doors, Is.Null);
            Assert.That((object)views.Doors, Is.Null); // a true null, not a destroyed wrapper — what makes ?. skip it
        }
    }
}
