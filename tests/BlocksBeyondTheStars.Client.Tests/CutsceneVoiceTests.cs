// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2495 — VEGA's voice in the cutscene speaker box and the ambience ducking, Unity-free: whose clips a speaker box chatters
/// with (<see cref="CutsceneVoice.VoiceOf"/>: the pack's narrator or speaker, VEGA's default, none for the Guardian core),
/// when it chatters (<see cref="CutsceneVoice.ShouldChatter(CaptionView, float, bool)"/>: while a speaker box types, the
/// prologue's narration subtitles silent, a closed box silent), the next clip (never the same twice in a row), pitch and
/// volume, the epilogue's blip, and how far a scene ducks the ambience (<see cref="CutsceneRules.AmbienceDuck"/>).
/// </summary>
public sealed class CutsceneVoiceTests
{
    private const string V = StoryRegistry.DefaultStoryId;
    private const float Dt = 1f / 30f;

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    private static StoryDefinition Vega => Content.Value.Stories[V];

    private static CutscenePlan Plan(string id, bool waitForReader = true)
    {
        var loc = Content.Value.CreateLocalizer(GameLocale.English);
        var def = Vega.FindCutscene(id)!;
        return CutscenePlan.Build(def, V, id, CutsceneArgs.For(Vega, def, id), new CutsceneTextSource(loc.Get, loc.Has, a => a), waitForReader);
    }

    // ---------------- Whose voice ----------------

    [Fact]
    public void VegasPack_SpeaksWithItsNarratorVoice_TheSameClipsHerPanelUses()
    {
        Assert.Equal(new[] { "vega_chatter_1", "vega_chatter_2", "vega_chatter_3" }, CutsceneVoice.VegaDefault);
        Assert.Equal(CutsceneVoice.VegaDefault, Vega.Narrator.Voice); // the pack declares what the panel plays
        Assert.Equal(CutsceneVoice.VegaDefault, CutsceneVoice.VoiceOf(Vega, string.Empty));
        Assert.Equal(CutsceneVoice.VegaDefault, CutsceneVoice.VoiceOf(Vega, StoryNarrator.DefaultId));
        Assert.Same(Vega.Narrator.Voice, CutsceneVoice.VoiceOf(Vega, string.Empty)); // no copy per frame
    }

    [Fact]
    public void TheGuardianCore_HasNoVoice_AndAnUnknownSpeakerIsSilent()
    {
        Assert.Empty(CutsceneVoice.VoiceOf(Vega, "core")); // its line-start hum only (d4: no typewriter chatter for the core)
        Assert.Empty(CutsceneVoice.VoiceOf(Vega, "yara"));
    }

    [Fact]
    public void WithoutAVoiceInThePack_VegaSpeaksWithHerDefault_AnyOtherNarratorIsSilent()
    {
        var tutorial = Content.Value.Stories["tutorial"]; // "narrator": { "id": "vega" } — no voice of its own
        Assert.Empty(tutorial.Narrator.Voice);
        Assert.Equal(CutsceneVoice.VegaDefault, CutsceneVoice.VoiceOf(tutorial, string.Empty));
        Assert.Equal(CutsceneVoice.VegaDefault, CutsceneVoice.VoiceOf(null, string.Empty)); // no pack: the default narrator
        Assert.Equal(CutsceneVoice.VegaDefault, CutsceneVoice.VoiceOf(null, "vega"));

        var other = new StoryDefinition { Narrator = new StoryNarrator { Id = "yara" } };
        Assert.Empty(CutsceneVoice.VoiceOf(other, string.Empty));
        Assert.Equal(CutsceneVoice.VegaDefault, CutsceneVoice.VoiceOf(other, "vega")); // VEGA as a guest keeps her voice

        var own = new StoryDefinition { Narrator = new StoryNarrator { Id = "yara", Voice = { "yara_hum_1", string.Empty, "yara_hum_2" } } };
        own.Speakers.Add(new StoryNarrator { Id = "bot", Voice = { "bot_beep" } });
        Assert.Equal(new[] { "yara_hum_1", "yara_hum_2" }, CutsceneVoice.VoiceOf(own, string.Empty)); // an empty name is left out
        Assert.Equal(new[] { "bot_beep" }, CutsceneVoice.VoiceOf(own, "bot"));
    }

    // ---------------- When it chatters ----------------

    [Fact]
    public void ShouldChatter_OnlyASpeakerBoxWithAVoice_WhileItTypes()
    {
        const string speaker = CutsceneCaptionStyles.Speaker;
        Assert.True(CutsceneVoice.ShouldChatter(speaker, pageGated: true, closing: false, visibleChars: 10, textLength: 40, pageAge: 0.3f, hasVoice: true));
        Assert.False(CutsceneVoice.ShouldChatter(speaker, true, false, 40, 40, 0.3f, true));  // typed out (or fast-forwarded)
        Assert.False(CutsceneVoice.ShouldChatter(speaker, true, true, 10, 40, 0.3f, true));   // closed: fading out in silence
        Assert.False(CutsceneVoice.ShouldChatter(speaker, true, false, 10, 40, 0.3f, false)); // no voice (the core)
        Assert.False(CutsceneVoice.ShouldChatter(speaker, true, false, 0, 0, 0f, true));      // nothing to say

        foreach (string style in CutsceneCaptionStyles.All.Where(s => s != speaker))
        {
            Assert.False(CutsceneVoice.ShouldChatter(style, true, false, 10, 40, 0.3f, true));
            Assert.False(CutsceneVoice.ShouldChatter(style, false, false, 40, 40, 0.3f, true));
        }

        // A timed box shows its page whole: it speaks for as long as the page would take to type.
        float speaking = CutsceneVoice.SpeakingSeconds(84);
        Assert.Equal(2f, speaking, 3);
        Assert.True(CutsceneVoice.ShouldChatter(speaker, false, false, 84, 84, 0f, true));
        Assert.True(CutsceneVoice.ShouldChatter(speaker, false, false, 84, 84, speaking - 0.05f, true));
        Assert.False(CutsceneVoice.ShouldChatter(speaker, false, false, 84, 84, speaking, true));
        Assert.False(CutsceneVoice.ShouldChatter(speaker, false, true, 84, 84, 0.5f, true)); // closed by N
    }

    /// <summary>The VEGA prologue read with "wait for me": the three narration subtitles ("You wake…") never chatter — the
    /// silence, the blip as the console wakes, then VEGA's voice in shots 5 and 6 while her box types; N completing the typing
    /// stops it.</summary>
    [Fact]
    public void TheVegaPrologue_NarrationIsSilent_VegaChattersWhileHerBoxTypes()
    {
        var plan = Plan(StoryCutsceneIds.Prologue);
        var play = new CutscenePlayback(plan);
        var chatteredIn = new HashSet<int>();
        bool fastForwarded = false;
        PlannedCaption? last = null;
        int lastPage = -1;
        float pageAt = 0f;
        while (!play.Done && play.Elapsed < 200f)
        {
            foreach (var v in play.Visible())
            {
                if (v.Caption.Style != CutsceneCaptionStyles.Speaker)
                {
                    Assert.False(CutsceneVoice.ShouldChatter(v, 0f, hasVoice: true));
                    continue;
                }

                if (v.Caption != last || v.Page != lastPage)
                {
                    (last, lastPage, pageAt) = (v.Caption, v.Page, play.Elapsed);
                }

                bool chatter = CutsceneVoice.ShouldChatter(v, play.Elapsed - pageAt, CutsceneVoice.VoiceOf(Vega, v.Caption.Speaker).Count > 0);
                Assert.Equal(v.VisibleChars < v.Text.Length && !v.Closing, chatter);
                if (chatter)
                {
                    chatteredIn.Add(play.ShotIndex);
                    if (play.ShotIndex == 5 && !fastForwarded && v.VisibleChars > 5)
                    {
                        play.Continue(); // N while typing: the page completes — and the voice stops with it
                        fastForwarded = true;
                        var after = play.Visible().First(x => x.Caption.Style == CutsceneCaptionStyles.Speaker);
                        Assert.False(CutsceneVoice.ShouldChatter(after, play.Elapsed - pageAt, true));
                    }
                }
            }

            if (play.ReaderCanContinue)
            {
                play.Continue();
            }

            play.Tick(Dt);
        }

        Assert.True(play.Done);
        Assert.True(fastForwarded);
        Assert.Equal(new[] { 4, 5 }, chatteredIn.OrderBy(i => i)); // the two speaker shots, never the narration
    }

    /// <summary>"Wait for me" off: the speaker box shows its page whole and VEGA speaks for its typing time from the page's
    /// start; N on the last page closes it — the fading box is silent.</summary>
    [Fact]
    public void ATimedSpeakerBox_SpeaksForItsTypingTime_AndFallsSilentWhenClosed()
    {
        var plan = Plan(StoryCutsceneIds.Prologue, waitForReader: false);
        var play = new CutscenePlayback(plan);
        while (!play.Done && play.ShotIndex < 4)
        {
            play.Tick(Dt);
        }

        play.Tick(Dt);
        var view = play.Visible().Single(v => v.Caption.Style == CutsceneCaptionStyles.Speaker);
        Assert.False(view.Caption.PageGated);
        Assert.Equal(view.Text.Length, view.VisibleChars); // timed: no typewriter
        Assert.True(CutsceneVoice.ShouldChatter(view, 0f, true));
        Assert.False(CutsceneVoice.ShouldChatter(view, CutsceneVoice.SpeakingSeconds(view.Text.Length) + 0.01f, true));

        while (play.Visible().Any(v => v.Caption.Style == CutsceneCaptionStyles.Speaker && !v.Closing))
        {
            play.Continue(); // pages ahead, the last page closes it
        }

        var closing = play.Visible().Single(v => v.Caption.Style == CutsceneCaptionStyles.Speaker);
        Assert.True(closing.Closing);
        Assert.False(CutsceneVoice.ShouldChatter(closing, 0f, true));
    }

    // ---------------- Which clip, how loud ----------------

    [Fact]
    public void PickClip_NeverRepeatsTheLastOne_AndReachesEveryOther()
    {
        Assert.Equal(-1, CutsceneVoice.PickClip(0, -1, 0.5f));
        Assert.Equal(0, CutsceneVoice.PickClip(1, 0, 0.9f)); // one clip: no choice
        for (int previous = -1; previous < 3; previous++)
        {
            var seen = new HashSet<int>();
            for (int i = 0; i <= 100; i++)
            {
                int pick = CutsceneVoice.PickClip(3, previous, i / 100f);
                Assert.InRange(pick, 0, 2);
                Assert.NotEqual(previous, pick);
                seen.Add(pick);
            }

            Assert.Equal(previous < 0 ? 3 : 2, seen.Count);
        }

        Assert.InRange(CutsceneVoice.PickClip(3, 1, 1.5f), 0, 2); // a roll out of range is clamped
        Assert.InRange(CutsceneVoice.PickClip(3, 7, -1f), 0, 2);  // a stale previous index is ignored

        var random = new Random(2495);
        int last = -1;
        for (int i = 0; i < 500; i++)
        {
            int next = CutsceneVoice.PickClip(3, last, (float)random.NextDouble());
            Assert.NotEqual(last, next);
            last = next;
        }
    }

    [Fact]
    public void PitchAndVolume_KeepThePanelsLevels()
    {
        Assert.Equal(0.94f, CutsceneVoice.Pitch(0f), 4);
        Assert.Equal(1.08f, CutsceneVoice.Pitch(1f), 4);
        Assert.Equal(1.01f, CutsceneVoice.Pitch(0.5f), 4);
        Assert.Equal(0.94f, CutsceneVoice.Pitch(-3f), 4);

        Assert.Equal(0.4f, CutsceneVoice.Volume(0.8f, 1f), 4); // master × SFX × 0.5 (#761)
        Assert.Equal(0.32f, CutsceneVoice.Volume(0.8f, 0.8f), 4);
        Assert.Equal(0.5f, CutsceneVoice.Volume(2f, 1f), 4);   // clamped
        Assert.Equal(0f, CutsceneVoice.Volume(0f, 1f), 4);     // muted with the master slider
    }

    // ---------------- The epilogue's blip ----------------

    [Fact]
    public void TheEpilogue_ChirpsAsVegasReadingFrameAppears()
    {
        var shot = Plan(StoryCutsceneIds.Resolution).Shots.Last();
        var frame = Assert.Single(shot.Captions);
        Assert.Equal(CutsceneCaptionStyles.ReadingFrame, frame.Style);
        Assert.Equal("story.vega.beat13", frame.Key);
        Assert.Contains(shot.Sfx, s => s.Cue == "ai_blip" && Math.Abs(s.At - frame.At) < 0.001f);
    }

    // ---------------- Ambience ducking ----------------

    [Fact]
    public void AScene_DucksTheAmbience_ByItsTier()
    {
        Assert.Equal(1f, CutsceneRules.AmbienceDuck(null, capped: false));
        Assert.Equal(1f, CutsceneRules.AmbienceDuck(CutscenePlan.LocalBanner(V, "Saved", "Story moment saved"), false)); // a banner

        var card = Plan("b01");
        Assert.Equal(CutsceneTiers.Card, card.Tier);
        Assert.Equal(0.7f, CutsceneRules.AmbienceDuck(card, false));

        var scene = Plan(StoryCutsceneIds.Prologue);
        Assert.True(scene.Tier >= CutsceneTiers.Scene);
        Assert.Equal(0.4f, CutsceneRules.AmbienceDuck(scene, false));
        Assert.Equal(0.7f, CutsceneRules.AmbienceDuck(scene, capped: true)); // MP: after 30 s the captions go on as a card
        Assert.Equal(0.7f, CutsceneRules.AmbienceDuck(scene.AsCard(), false)); // played as a card
        Assert.Equal(0.4f, CutsceneRules.AmbienceDuck(Plan(StoryCutsceneIds.Resolution), false)); // the ending (T4)
    }
}
