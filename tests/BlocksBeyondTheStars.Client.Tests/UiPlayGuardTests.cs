// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3921 — "sometimes the space bar opens the pause menu": the hidden Tab menu kept a control selected, W walked the
/// invisible selection up to its "Pause menu" button and Space (uGUI's Submit) clicked it. In play nothing is selected
/// and the keyboard walks no menu; a gamepad keeps its navigation (its chat keyboard works with the cursor locked).
/// The Unity side is pinned as text — PR CI never compiles it.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class UiPlayGuardTests
{
    private static string Script(string name)
        => System.IO.File.ReadAllText(System.IO.Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", name));

    [Fact]
    public void BackInPlay_TheSelectionIsDropped_OnceAsTheCursorLocks()
    {
        Assert.True(UiPlayGuard.ClearsSelection(cursorWasFree: true, cursorFree: false));  // the last menu closed
        Assert.False(UiPlayGuard.ClearsSelection(cursorWasFree: false, cursorFree: false)); // playing on: nothing to drop
        Assert.False(UiPlayGuard.ClearsSelection(cursorWasFree: true, cursorFree: true));   // a menu is open: its selection stays
        Assert.False(UiPlayGuard.ClearsSelection(cursorWasFree: false, cursorFree: true));  // a menu opens
    }

    [Fact]
    public void InPlay_TheKeyboardWalksNoMenu_ButAGamepadKeepsItsNavigation()
    {
        Assert.False(UiPlayGuard.NavigationWanted(cursorFree: false, gamepadActive: false)); // W/A/S/D and Space are the game's
        Assert.True(UiPlayGuard.NavigationWanted(cursorFree: false, gamepadActive: true));   // the pad's chat keyboard, cursor locked
        Assert.True(UiPlayGuard.NavigationWanted(cursorFree: true, gamepadActive: false));   // any menu, any dialog
        Assert.True(UiPlayGuard.NavigationWanted(cursorFree: true, gamepadActive: true));
    }

    [Fact]
    public void TheCursorArbiter_AppliesBothRules_AndTheMainMenuGetsItsNavigationBack()
    {
        string menus = Script("GameBootstrap.Menus.cs");
        Assert.Contains("KeepMenusOutOfPlay(free);", menus); // every frame, where the cursor lock is decided
        Assert.Contains("if (UiPlayGuard.ClearsSelection(_cursorWasFree, cursorFree))", menus);
        Assert.Contains("events.SetSelectedGameObject(null);", menus);
        Assert.Contains("events.sendNavigationEvents = UiPlayGuard.NavigationWanted(cursorFree, InputMap.ActiveDevice == InputDeviceKind.Gamepad);", menus);
        Assert.Contains("private bool _cursorWasFree = true;", menus); // the first locked frame of a world drops the menu's leftover
        Assert.Contains("events.sendNavigationEvents = true;", menus);
        Assert.Contains("RestoreMenuNavigation();", Script("GameBootstrap.cs"));
    }

    [Fact]
    public void TheTabMenu_LetsItsSelectionGo_BeforeItsCanvasGoes()
    {
        string tab = Script("CraftingTechShipUI.cs");
        int drop = tab.IndexOf("UiKit.DropSelectionUnder(_canvas.transform);", System.StringComparison.Ordinal);
        int hide = tab.IndexOf("_canvas.enabled = false;", drop < 0 ? 0 : drop, System.StringComparison.Ordinal);
        Assert.True(drop > 0 && hide > drop, "the selection is dropped before the canvas is switched off");
        Assert.Contains("selected.transform.IsChildOf(root)", Script("UiKit.cs")); // only the screen's own controls
    }
}
