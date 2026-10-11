// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The glyph detection's pad rule (#2461, N7): the left stick and WASD share one pair of input axes, so the shared
/// axes count as pad activity only while no movement key is held — a keyboard walk with a pad plugged in used to flip
/// every hint to the pad's button names and back.
/// </summary>
public sealed class InputDeviceRulesTests
{
    private const float Deadzone = 0.2f;

    [Fact]
    public void AStickPushPastTheDeadzone_IsPadActivity()
    {
        Assert.True(InputDeviceRules.SharedMoveAxesArePad(0.6f, 0f, movementKeyHeld: false, Deadzone));
        Assert.True(InputDeviceRules.SharedMoveAxesArePad(0f, -0.3f, movementKeyHeld: false, Deadzone));
    }

    [Fact]
    public void AKeyboardWalk_IsNeverPadActivity()
    {
        // WASD drives the same axes to a full ±1 — with a movement key held that is the keyboard, whatever the value.
        Assert.False(InputDeviceRules.SharedMoveAxesArePad(1f, 0f, movementKeyHeld: true, Deadzone));
        Assert.False(InputDeviceRules.SharedMoveAxesArePad(-1f, 1f, movementKeyHeld: true, Deadzone));
    }

    [Fact]
    public void StickDriftInsideTheDeadzone_AndAStickAtRest_AreNoActivity()
    {
        Assert.False(InputDeviceRules.SharedMoveAxesArePad(0.1f, -0.15f, movementKeyHeld: false, Deadzone));
        Assert.False(InputDeviceRules.SharedMoveAxesArePad(0f, 0f, movementKeyHeld: false, Deadzone));
        Assert.False(InputDeviceRules.SharedMoveAxesArePad(0f, 0f, movementKeyHeld: false, deadzone: 0f)); // rest is rest
    }
}
