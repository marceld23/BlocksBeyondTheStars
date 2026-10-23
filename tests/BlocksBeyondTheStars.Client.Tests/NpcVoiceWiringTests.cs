// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2592/#2593/#2597/#2605 — the Unity half of the NPC voices, gestures, bubbles and the achievement banner, guarded as
/// source text (the Unity scripts cannot load headless — the UI layout guards' pattern): every NPC sound goes through the
/// voice budget, the bubble wraps, an AI upgrade never plays twice, the dialog panel speaks, VEGA's voice tells the NPCs
/// to hush, the HUD drains the achievement queue, and the new texts exist in both mandatory languages.
/// </summary>
public sealed class NpcVoiceWiringTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    private static JsonElement Locale(string code)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void NpcView_SpeaksThroughTheVoiceBudget_AndDrawsAWrappedBubble()
    {
        string src = Script("NpcView.cs");
        Assert.Contains("NpcVoice.VoiceFor(", src);
        Assert.Contains("NpcVoice.BabblePlan(", src);
        Assert.Contains("_voiceGate.RequestBabble(", src);
        Assert.Contains("_voiceGate.RequestReaction(", src);
        Assert.Contains("labels.Bubble(", src);
        Assert.Contains("NpcBubbleText.Clamp(", src);
        Assert.Contains("_greetings.OnGreeting(", src);

        // No NPC sound bypasses the budget: the pooled one-shot paths are not used for voices.
        Assert.DoesNotContain(".At(NpcVoice", src);
        Assert.DoesNotContain("Cue(NpcVoice", src);

        // An AI upgrade returns before any gesture or voice.
        int upgrade = src.IndexOf("decision == NpcGreetingDecision.Upgrade", StringComparison.Ordinal);
        int gesture = src.IndexOf("Gesture(m.NpcId, gesture)", StringComparison.Ordinal);
        int babble = src.IndexOf("QueueBabble(m.NpcId, NpcVoiceGate.PriorityOf(m.Kind)", StringComparison.Ordinal);
        Assert.True(upgrade > 0 && gesture > upgrade && babble > upgrade, "the upgrade must return before the voice and the gesture");
    }

    [Fact]
    public void ASettlerWithoutText_NoLongerOffersToTrade()
    {
        string src = Script("NpcView.cs");
        Assert.Contains("\"npc.greet.settler\"", src);
        Assert.Contains("\"npc.greet.passing\"", src);
    }

    [Fact]
    public void TheDialogPanel_LetsTheNpcSpeakEachLine()
        => Assert.Contains("NpcView.Instance.DialogLine(m.NpcId, m.Text, opening)", Script("NpcDialogUi.cs"));

    [Fact]
    public void VegasVoice_TellsTheNpcsToHush()
    {
        Assert.Contains("public static bool AnySpeaking", Script("CutsceneScreen.cs"));
        Assert.Contains("NarratorVoice.AnySpeaking", Script("NpcView.cs"));
    }

    [Fact]
    public void TheAvatar_HasTheGreetingGestures_WithRestingRolls()
    {
        string src = Script("PlayerAvatar.cs");
        Assert.Contains("public void Wave(bool leftHand = false)", src);
        Assert.Contains("public void Nod()", src);
        Assert.Contains("public void Cheer()", src);
        Assert.Contains("public void SetLookYaw(float yaw)", src);
        Assert.Contains("Quaternion.Euler(armR, yawR, rollR)", src);
        Assert.Contains("float rollL = 0f, rollR = 0f, yawL = 0f, yawR = 0f, elbowRollL = 0f, elbowRollR = 0f;", src);
    }

    [Fact]
    public void TheHud_CelebratesAchievements_AfterResearch_NotInACutscene()
    {
        string hud = ClientTestPaths.ScriptPartsText("HudUi"); // HudUi.cs + its widget parts (#2909)
        Assert.Contains("Game.AchievementsUnlocked.Dequeue()", hud);
        Assert.Contains("\"ui.achv.banner\"", hud);
        Assert.Contains("Cue(\"ui_success\"", hud);
        int research = hud.IndexOf("StartResearchToast(Game.ResearchAvailable.Dequeue())", StringComparison.Ordinal);
        int achievement = hud.IndexOf("StartAchievementToast(Game.AchievementsUnlocked.Dequeue())", StringComparison.Ordinal);
        Assert.True(research > 0 && achievement > research, "research drains first");

        // The unlock handler only queues — no status line, no sound at the unlock any more.
        string boot = Script("GameBootstrap.PlayerState.cs"); // the achievement handlers' GameBootstrap part (#2912)
        int handler = boot.IndexOf("AchievementUnlockedReceived += m =>", StringComparison.Ordinal);
        Assert.True(handler > 0);
        string body = boot.Substring(handler, boot.IndexOf("};", handler, StringComparison.Ordinal) - handler);
        Assert.Contains("AchievementsUnlocked.Enqueue(m.Key)", body);
        Assert.DoesNotContain("LastMessage", body);
        Assert.DoesNotContain("Cue(", body);
    }

    [Theory]
    [InlineData("en", "Achievement unlocked!", "Hello!", "Hello, pilot!")]
    [InlineData("de", "Erfolg freigeschaltet!", "Hallo!", "Hallo, Pilot!")]
    public void TheNewTexts_ExistInBothMandatoryLanguages(string code, string banner, string passing, string settler)
    {
        var loc = Locale(code);
        Assert.Equal(banner, loc.GetProperty("ui.achv.banner").GetString());
        Assert.Equal(passing, loc.GetProperty("npc.greet.passing").GetString());
        Assert.Equal(settler, loc.GetProperty("npc.greet.settler").GetString());
    }
}
