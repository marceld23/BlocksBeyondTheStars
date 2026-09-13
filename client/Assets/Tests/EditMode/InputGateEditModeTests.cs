// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The text-entry gate (#1858): typing "E" into the feedback dialog docked the ship, "U" left the station,
    /// "V" flipped the camera. While a text field has the keys, every gameplay verb must read "not pressed"
    /// and only the two menu verbs (Esc / Tab) may pass — the rule lives in <see cref="InputGate"/> and is
    /// applied by <see cref="InputMap.Down"/> / <c>Held</c> / <c>Up</c>; the flight controls read the wider
    /// <see cref="InputGate.FlightControlAllowed"/>.
    /// </summary>
    public sealed class InputGateEditModeTests
    {
        [Test]
        public void NotTyping_EveryActionPasses()
        {
            foreach (InputAction action in Enum.GetValues(typeof(InputAction)))
            {
                Assert.IsTrue(InputGate.Allows(action, textFieldFocused: false), $"{action} must pass while no field is focused");
            }
        }

        [Test]
        public void Typing_SwallowsEveryGameplayVerb_ButNotTheMenuVerbs()
        {
            foreach (InputAction action in Enum.GetValues(typeof(InputAction)))
            {
                bool allowed = InputGate.Allows(action, textFieldFocused: true);
                if (action == InputAction.UiCancel || action == InputAction.UiMenu)
                {
                    Assert.IsTrue(allowed, $"{action} must still reach the dialog's close handler");
                }
                else
                {
                    Assert.IsFalse(allowed, $"{action} typed into a text field must stay a letter");
                }
            }
        }

        [Test]
        public void Typing_TheReportedVerbsAreSwallowed()
        {
            // The three from the report, spelled out so a future re-grouping of the enum cannot hide them.
            Assert.IsFalse(InputGate.Allows(InputAction.Interact, textFieldFocused: true), "E — dock / land / board");
            Assert.IsFalse(InputGate.Allows(InputAction.Disembark, textFieldFocused: true), "U — leave station / undock");
            Assert.IsFalse(InputGate.Allows(InputAction.ToggleThirdPerson, textFieldFocused: true), "V — camera");
        }

        [Test]
        public void MenuVerbs_AreExactlyEscapeAndTab()
        {
            CollectionAssert.AreEquivalent(
                new[] { InputAction.UiCancel, InputAction.UiMenu },
                Array.FindAll((InputAction[])Enum.GetValues(typeof(InputAction)), InputGate.IsMenuVerb),
                "the pass-through set must stay the two keyboard-locked menu verbs");
        }

        [Test]
        public void InputMap_HonoursTheGate_ThroughTheTestSeam()
        {
            // No EventSystem in EditMode, so the seam stands in for a focused field. With no key down the polls
            // are false either way — what this pins is that the gate short-circuits BEFORE the backends, i.e.
            // a typed key can never reach them.
            InputMap.TextEntryOverrideForTest = true;
            try
            {
                Assert.IsTrue(InputMap.TextEntryActive);
                Assert.IsFalse(InputMap.Down(InputAction.Interact));
                Assert.IsFalse(InputMap.Held(InputAction.Disembark));
                Assert.IsFalse(InputMap.Up(InputAction.ToggleThirdPerson));
                Assert.IsTrue(InputGate.Allows(InputAction.UiCancel, InputMap.TextEntryActive), "Esc still reaches the dialog");
            }
            finally
            {
                InputMap.TextEntryOverrideForTest = null;
            }

            InputMap.TextEntryOverrideForTest = false;
            try
            {
                Assert.IsFalse(InputMap.TextEntryActive);
            }
            finally
            {
                InputMap.TextEntryOverrideForTest = null;
            }
        }

        [Test]
        public void FlightControl_HeldStillByEveryOwner()
        {
            Assert.IsTrue(InputGate.FlightControlAllowed(menuOpen: false, awaitingRespawnConfirm: false, chatTyping: false, textFieldFocused: false));
            Assert.IsFalse(InputGate.FlightControlAllowed(menuOpen: true, awaitingRespawnConfirm: false, chatTyping: false, textFieldFocused: false), "Tab menu / chart / feedback dialog");
            Assert.IsFalse(InputGate.FlightControlAllowed(menuOpen: false, awaitingRespawnConfirm: true, chatTyping: false, textFieldFocused: false), "ship-destruction prompt");
            Assert.IsFalse(InputGate.FlightControlAllowed(menuOpen: false, awaitingRespawnConfirm: false, chatTyping: true, textFieldFocused: false), "chat box — the gap of #1858");
            Assert.IsFalse(InputGate.FlightControlAllowed(menuOpen: false, awaitingRespawnConfirm: false, chatTyping: false, textFieldFocused: true), "any other focused field");
        }
    }
}
