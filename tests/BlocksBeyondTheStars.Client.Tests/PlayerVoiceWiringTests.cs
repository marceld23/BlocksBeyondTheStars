// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3767 on the client: the figure's own mouth and breath sounds play in the chosen voice, the editor's Figure tab has the
/// two sliders with the three quick buttons, the voice goes out with the colours, and another player's hit and fall are
/// heard in that player's voice. The rule itself is the server suite's (<c>PlayerVoiceTests</c>); here the Unity wiring is
/// guarded as source text (the scripts cannot load headless — the <see cref="ClientQuirksWiringTests"/> pattern).
/// </summary>
public sealed class PlayerVoiceWiringTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    [Fact]
    public void TheOwnVoiceSounds_GoThroughThePitchedPlayer()
    {
        string mix = Script("ClientAudio.Mix.cs");
        Assert.Contains("int voice = PlayerVoice.IsVoiceClip(id) ? OwnVoice : 0;", mix);
        Assert.Contains("pitch * PlayerVoice.Factor(voice)", mix);
        Assert.Contains("clip = VoiceTimbre(clip, voice);", mix);

        // The cues that used to go through Cue — which cannot pitch — come through Voice now.
        Assert.Contains("Voice(\"hurt_player\");", Script("ClientAudio.cs"));
        Assert.DoesNotContain("Cue(\"hurt_player\")", Script("ClientAudio.cs"));
        Assert.DoesNotContain("Cue(\"player_death\")", Script("DeathFx.cs"));
        Assert.Contains("audio.Voice(\"climb_strain\",", Script("PlayerController.Climb.cs"));
        Assert.Contains("audio.Voice(\"climb_strain\",", Script("PlayerController.Sprint.cs"));
        Assert.Contains("public int OwnVoice => Settings != null ? Settings.VoicePacked : 0;", Script("ClientAudio.Voice.cs"));
    }

    [Fact]
    public void TheVoice_GoesOutWithTheColours_AndTheOthersHearAHitAndAFallInIt()
    {
        Assert.Contains("HullRgb, Settings != null ? Settings.VoicePacked : 0);", Script("GameBootstrap.cs")); // at the join
        Assert.Contains("Rgb(Settings.HullColor), Settings.VoicePacked);", Script("GameMenu.cs"));               // on a change
        string remote = Script("RemotePlayers.Feel.cs");
        Assert.Contains("m.Emote is PlayerEmotes.Hurt or PlayerEmotes.Down", remote);
        Assert.Contains("VoiceAt(m.Emote == PlayerEmotes.Hurt ? \"hurt_player\" : \"player_death\", at, m.Voice,", remote);
    }

    [Fact]
    public void TheFigureTab_HasTwoSlidersAndThreeQuickButtons_InBothHosts()
    {
        string column = Script("FaceEditor.Voice.cs");
        Assert.Contains("PlayerVoice.Low, PlayerVoice.Middle, PlayerVoice.High", column);
        Assert.Contains("slider.maxValue = PlayerVoice.Max / PlayerVoice.Step;", column);
        Assert.Contains("slider.wholeNumbers = true;", column);
        Assert.Contains("BuildVoiceColumn(card);", Script("FaceEditor.Figure.cs"));
        Assert.Contains("editor.UseVoiceOf(settings, () => settings.Save());", Script("AvatarEditor.Templates.cs"));
        Assert.Contains("editor.UseVoiceOf(Settings, ApplyAppearance);", Script("GameMenu.Templates.cs"));

        // The settings start at the middle, so a player who never opens the editor sounds as before.
        string settings = Script("ClientSettings.Voice.cs");
        Assert.Contains("public int VoicePitch = PlayerVoice.Middle;", settings);
        Assert.Contains("public int VoiceAge = PlayerVoice.Middle;", settings);
        Assert.Equal(PlayerVoice.Middle, (PlayerVoice.Low + PlayerVoice.High) / 2);
    }

    [Fact]
    public void TheColumnsTexts_ExistInEnglishAndGerman()
    {
        foreach (string code in new[] { "en", "de" })
        {
            var table = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")));
            foreach (string key in new[]
                     {
                         "ui.avatar.voice", "ui.avatar.voice_low", "ui.avatar.voice_middle", "ui.avatar.voice_high",
                         "ui.avatar.voice_pitch", "ui.avatar.voice_age", "ui.avatar.voice_listen",
                         "ui.avatar.voice_1", "ui.avatar.voice_2",
                     })
            {
                Assert.True(table.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text), code + ": " + key);
            }
        }
    }

    // ---------------- #3826: the second recorded voice ----------------

    private static string AudioDir() => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "audio");

    [Fact]
    public void EveryVoiceSound_IsBundledInBothRecordedVoices()
    {
        var first = Directory.GetFiles(AudioDir(), "*.mp3").Select(Path.GetFileNameWithoutExtension)
            .Where(id => PlayerVoice.IsVoiceClip(id) && !id!.StartsWith(PlayerVoice.SecondVoicePrefix, StringComparison.Ordinal))
            .Select(id => id!)
            .ToList();
        Assert.True(first.Count >= 11, "the first voice lost a clip: " + string.Join(", ", first));

        var missing = first.Where(id => !File.Exists(Path.Combine(AudioDir(), PlayerVoice.ClipFor(id, 1) + ".mp3"))).ToList();
        Assert.True(missing.Count == 0, "voice sounds without a second-voice recording: " + string.Join(", ", missing));

        // And no second-voice clip without a first one — it could never be played.
        var orphans = Directory.GetFiles(AudioDir(), PlayerVoice.SecondVoicePrefix + "*.mp3").Select(Path.GetFileNameWithoutExtension)
            .Where(id => !first.Contains(id!.Substring(PlayerVoice.SecondVoicePrefix.Length)))
            .ToList();
        Assert.True(orphans.Count == 0, "second-voice clips without a first-voice twin: " + string.Join(", ", orphans));

        // The rotating takes rotate in both voices.
        Assert.Contains(PlayerVoice.ClipFor("hurt_player", 1), ClipTakes.Families);
        Assert.Contains(PlayerVoice.ClipFor("breath_effort", 1), ClipTakes.Families);
    }

    [Fact]
    public void TheChosenVoice_PicksTheClip_ForTheOwnFigureTheOthersAndTheSample()
    {
        string voice = Script("ClientAudio.Voice.cs");
        Assert.Contains("PlayOwn(Take(VoiceClip(id, OwnVoice)), vol, pitch);", voice);       // the own grunts and breaths of effort
        Assert.Contains("Take(VoiceClip(id, packedVoice))", voice);                           // another player's hit and fall
        Assert.Contains("VoiceClip(\"breath_exhale\", packedVoice)", voice);                  // the editor's sample in a world
        Assert.Contains("PlayerVoice.ClipFor(id, PlayerVoice.KindOf(packedVoice))", voice);
        Assert.Contains("return _clips.ContainsKey(clip) ? clip : id;", voice);               // a missing clip falls back, never silence
        Assert.Contains("id = VoiceClip(id, OwnVoice);", Script("ClientAudio.Mix.cs"));       // every own body sound that is a voice sound
        Assert.Contains("PlayerVoice.ClipFor(\"breath_exhale\", PlayerVoice.KindOf(packedVoice))", Script("UiSound.cs")); // the menu's designer

        string settings = Script("ClientSettings.Voice.cs");
        Assert.Contains("public int VoiceKind;", settings); // 0 from an older settings file: the first voice
        Assert.Contains("PlayerVoice.Pack(VoicePitch, VoiceAge, VoiceKind)", settings);

        string column = Script("FaceEditor.Voice.cs");
        Assert.Contains("L(\"ui.avatar.voice_1\"), L(\"ui.avatar.voice_2\")", column);
        Assert.Contains("PlayerVoice.ClampKind(_voiceSettings.VoiceKind), ChangeVoiceKind", column);
        Assert.Contains("_voiceSettings.VoiceKind = PlayerVoice.ClampKind(kind);", column);
    }
}
