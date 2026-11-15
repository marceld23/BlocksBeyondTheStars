// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3666: the settings screen became a menu screen with categories, a description card and a "Reset this category"
/// link. Guarded as source text (the Unity script cannot load headless): it follows the menu template, every setting the
/// old single list had is still on it, the new controls-line row is there, and every category has an icon that exists.
/// </summary>
public sealed class SettingsScreenLayoutTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static string Screen() => ClientTestPaths.ScriptPartsText("UiSettings");

    [Fact]
    public void TheScreen_FollowsTheMenuTemplate()
    {
        string screen = Screen();
        Assert.Contains("UiKit.AddScreenHeader(_root, L(\"ui.settings.title\")", screen);
        Assert.Contains("() => _shell.CloseSettings()", screen); // back to the main menu or the pause menu, whichever opened it
        Assert.Contains("UiNav.Enable(canvas.gameObject)", screen);
        Assert.Contains("UiKit.AddLinkButton(_root", screen);
        Assert.Contains("ButtonTier.Primary", screen);
        Assert.DoesNotContain("\"btn_exit\"", screen); // the power icon means "quit the game", never Back
    }

    /// <summary>Every setting the single list of the old screen offered — nothing may get lost in the categories.</summary>
    [Theory]
    [InlineData("S.Preset")]
    [InlineData("S.Brightness")]
    [InlineData("S.Cosiness")]
    [InlineData("S.DarkVisibility")]
    [InlineData("S.Window")]
    [InlineData("S.ViewDistanceChunks")]
    [InlineData("S.FarViewBlocks")]
    [InlineData("S.UiScale")]
    [InlineData("S.VSync")]
    [InlineData("S.FrameRateCap")]
    [InlineData("S.Smaa")]
    [InlineData("S.LensFlare")]
    [InlineData("S.MotionBlur")]
    [InlineData("S.DistanceHaze")]
    [InlineData("S.DistanceSoftness")]
    [InlineData("S.GroundDetails")]
    [InlineData("S.ApplyAtmosphereMode(")]
    [InlineData("\"fog_banks\", S.FogBanks")]
    [InlineData("\"lamp_shadows\", S.HighLampShadows")]
    [InlineData("S.MasterVolume")]
    [InlineData("S.MusicVolume")]
    [InlineData("S.SfxVolume")]
    [InlineData("S.MusicMode")]
    [InlineData("S.MuteAlarms")]
    [InlineData("S.VoiceEnabled")]
    [InlineData("S.VoiceVolume")]
    [InlineData("S.VoiceInputEnabled")]
    [InlineData("S.PushToTalkKey")]
    [InlineData("S.Unmute(who)")]
    [InlineData("S.MouseSensitivity")]
    [InlineData("S.FieldOfView")]
    [InlineData("S.InvertY")]
    [InlineData("InputMap.Remappable")]
    [InlineData("InputMap.FlightRemappable")]
    [InlineData("InputMap.VehicleRemappable")]
    [InlineData("InputMap.MenuRemappable")]
    [InlineData("S.ResetBindings()")]
    [InlineData("S.PadDeadzone")]
    [InlineData("S.PadLookX")]
    [InlineData("S.PadLookY")]
    [InlineData("S.PadInvertY")]
    [InlineData("S.PadTriggersMinePlace")]
    [InlineData("S.PadGlyphs")]
    [InlineData("S.PadVibration")]
    [InlineData("S.CameraMotion")]
    [InlineData("S.FestivalPreset")]
    [InlineData("UiFestivalPicker.Open(")]
    [InlineData("S.FestivalMood")]
    [InlineData("S.ScreenShake")]
    [InlineData("S.ReduceFlashes")]
    [InlineData("S.ReducedEffects")]
    [InlineData("S.StoryTextWaitForMe")]
    [InlineData("S.ShowAllObjectives")]
    [InlineData("S.AutoStowOnBoard")]
    [InlineData("S.ShowEnemyHealthBars")]
    [InlineData("S.ShowSessionTime")]
    [InlineData("S.ShowCoordinates")]
    [InlineData("S.ChatVisibility")]
    [InlineData("S.NpcCalls")]
    [InlineData("S.PlaytimeReminder")]
    [InlineData("S.ReminderMinutes")]
    [InlineData("S.SkinColor")]
    [InlineData("S.TorsoColor")]
    [InlineData("S.ArmColor")]
    [InlineData("S.LegColor")]
    [InlineData("S.UpdateFeedUrl")]
    [InlineData("S.UpdateCheckOnStart")]
    [InlineData("ClientUpdater.CheckForUpdates(")]
    [InlineData("S.Language = ")]
    public void EverySetting_OfTheOldList_IsStillOnTheScreen(string member) => Assert.Contains(member, Screen());

    [Fact]
    public void TheControlsLine_IsARowOfDisplayAndHud_WithThreeValues()
    {
        string screen = Screen();
        Assert.Contains("L(\"ui.settings.controls_line\")", screen);
        Assert.Contains("S.ControlsHint = Wrap(S.ControlsHintMode + dir, ClientSettings.ControlsHintOff + 1)", screen);
    }

    [Fact]
    public void EveryCategory_HasAnIcon_ThatExists()
    {
        var table = Regex.Match(Screen(), @"CategoryIcons =\s*\{(?<names>[^}]*)\}", RegexOptions.ExplicitCapture, RegexTimeout);
        Assert.True(table.Success, "the CategoryIcons table moved — keep this guard in step");
        var names = Regex.Matches(table.Groups["names"].Value, "\"(?<n>[a-z_]+)\"", RegexOptions.ExplicitCapture, RegexTimeout)
            .Select(m => m.Groups["n"].Value)
            .ToList();
        Assert.Equal(9, names.Count); // one per category
        string icons = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "icons");
        Assert.All(names, name => Assert.True(File.Exists(Path.Combine(icons, name + ".png")), $"icon {name}.png is missing"));
    }
}
