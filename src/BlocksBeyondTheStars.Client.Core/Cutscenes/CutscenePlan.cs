// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>Where a cutscene plan gets its words: the localizer (core + pack tables), whether a key exists, and the
    /// device-aware resolver for <c>{key:Action}</c> tokens (<c>HudUi.KeyTokenGlyph</c> on the client — the one resolver,
    /// never a second one).</summary>
    public sealed class CutsceneTextSource
    {
        private readonly Func<string, string> _localize;
        private readonly Func<string, bool> _has;
        private readonly Func<string, string> _glyph;

        public CutsceneTextSource(Func<string, string> localize, Func<string, bool> has, Func<string, string>? glyph = null)
        {
            _localize = localize ?? throw new ArgumentNullException(nameof(localize));
            _has = has ?? throw new ArgumentNullException(nameof(has));
            _glyph = glyph ?? (action => action);
        }

        /// <summary>The text of <paramref name="key"/> (empty for an empty key).</summary>
        public string Localize(string? key) => string.IsNullOrEmpty(key) ? string.Empty : _localize(key!);

        /// <summary>Whether the tables know <paramref name="key"/>.</summary>
        public bool Has(string? key) => !string.IsNullOrEmpty(key) && _has(key!);

        /// <summary>What a <c>{key:Action}</c> token reads as.</summary>
        public string Glyph(string action) => _glyph(action);
    }

    /// <summary>One caption of a planned shot, its words already resolved (#2468).</summary>
    public sealed class PlannedCaption
    {
        internal PlannedCaption(string style, string key, string kicker, string text, string speaker, IReadOnlyList<string> pages,
            IReadOnlyList<float> pageSeconds, float at, float end, bool pageGated)
        {
            Style = style;
            Key = key;
            Kicker = kicker;
            Text = text;
            Speaker = speaker;
            Pages = pages;
            PageSeconds = pageSeconds;
            At = at;
            End = end;
            PageGated = pageGated;
        }

        /// <summary>The caption style (<see cref="CutsceneCaptionStyles"/>).</summary>
        public string Style { get; }

        /// <summary>The locale key (empty for a built-in card's composed text).</summary>
        public string Key { get; }

        /// <summary>The small line above a title card (or a reading frame's heading); empty = none.</summary>
        public string Kicker { get; }

        /// <summary>The whole composed text (tokens expanded, <c>{0}</c> filled).</summary>
        public string Text { get; }

        /// <summary>The narrator id of a speaker box; empty = the pack's narrator.</summary>
        public string Speaker { get; }

        /// <summary>The pages the caption shows one after the other (at least one).</summary>
        public IReadOnlyList<string> Pages { get; }

        /// <summary>Per page: how long it shows when timed, or its reading floor when page-gated.</summary>
        public IReadOnlyList<float> PageSeconds { get; }

        /// <summary>Seconds into the shot when the caption appears.</summary>
        public float At { get; }

        /// <summary>Timed: when it disappears (stretched to the reading floor). Page-gated: the point the shot holds at
        /// until the reader has turned the last page.</summary>
        public float End { get; }

        /// <summary>The caption waits for the reader ("Story text: wait for me"); else it is timed.</summary>
        public bool PageGated { get; }

        /// <summary>Whether the text types out (the speaker box).</summary>
        public bool Typewriter => Style == CutsceneCaptionStyles.Speaker;
    }

    /// <summary>One planned shot: the camera, the effective length and the resolved captions and cues (#2468).</summary>
    public sealed class PlannedShot
    {
        internal PlannedShot(int index, string camera, string anchor, string grade, float duration, float authoredDuration,
            IReadOnlyList<PlannedCaption> captions, IReadOnlyList<(string Cue, float At)> sfx)
        {
            Index = index;
            Camera = camera;
            Anchor = anchor;
            Grade = grade;
            Duration = duration;
            AuthoredDuration = authoredDuration;
            Captions = captions;
            Sfx = sfx;
        }

        public int Index { get; }

        /// <summary>The camera type (<see cref="CutsceneCameras"/>).</summary>
        public string Camera { get; }

        /// <summary>The anchor (<see cref="CutsceneAnchors"/>; never empty — the player by default).</summary>
        public string Anchor { get; }

        /// <summary>The colour grade (<see cref="CutsceneGrades"/>).</summary>
        public string Grade { get; }

        /// <summary>Seconds the shot runs when nothing waits for the reader — the authored length, stretched so every
        /// timed caption gets its reading floor.</summary>
        public float Duration { get; }

        /// <summary>The length the author wrote.</summary>
        public float AuthoredDuration { get; }

        public IReadOnlyList<PlannedCaption> Captions { get; }

        /// <summary>Sound cues (clip name, seconds into the shot).</summary>
        public IReadOnlyList<(string Cue, float At)> Sfx { get; }

        /// <summary>Whether the shot wants the camera.</summary>
        public bool TakesCamera => CutsceneCameras.TakesCamera(Camera);
    }

    /// <summary>
    /// The pure plan of one cutscene play (#2468, d4 §3.3): a <see cref="CutsceneDefinition"/> and the play's argument
    /// turned into shots with resolved captions, pages and timings. Unity-free so the timing rules are covered by plain
    /// .NET tests; the client's <c>CutsceneDirector</c> runs it with <see cref="CutscenePlayback"/>.
    /// <list type="bullet">
    /// <item>The argument (the captions' <c>{0}</c>) may be a locale key — a chapter binding sends the chapter's title key —
    /// or plain text; several values are joined by U+001F. A value the tables know is localized.</item>
    /// <item>Subtitles, speaker boxes and reading frames follow the "Story text: wait for me" setting: on, they wait for the
    /// reader page by page (the shot holds at the caption's end); off, each page runs for its reading floor
    /// <c>max(3 s, 1.2 s + chars / 12)</c>, at most 10 s. Title cards and credits are always timed. A banner (T1) never
    /// waits.</item>
    /// <item>A timed caption shorter than its reading floor is stretched, and its shot with it.</item>
    /// </list>
    /// </summary>
    public sealed class CutscenePlan
    {
        /// <summary>Reading speed assumed for 8–12-year-olds (d4 §2.3; to be tuned in playtests).</summary>
        public const float ReadingCharsPerSecond = 12f;

        /// <summary>The shortest a timed page shows.</summary>
        public const float ReadingFloorMin = 3f;

        /// <summary>The fixed part of the reading floor (finding the text).</summary>
        public const float ReadingFloorBase = 1.2f;

        /// <summary>The longest reading floor of one page.</summary>
        public const float ReadingFloorMax = 10f;

        /// <summary>Subtitle page budget — about two lines in the lower third.</summary>
        public const int SubtitlePageChars = 140;

        /// <summary>Speaker-box page budget — one VEGA panel page (the validator's 220).</summary>
        public const int SpeakerPageChars = 220;

        /// <summary>Reading-frame page budget (the validator's 600).</summary>
        public const int ReadingFramePageChars = 600;

        /// <summary>How fast a speaker box types (characters per second).</summary>
        public const float TypewriterCharsPerSecond = 42f;

        /// <summary>Length of the built-in recap card before it waits for the reader (or its floor runs out).</summary>
        public const float RecapSeconds = 8f;

        /// <summary>Length of a client-made banner ("Story moment saved").</summary>
        public const float BannerSeconds = 3.5f;

        private CutscenePlan(string packId, string cutsceneId, int tier, IReadOnlyList<PlannedShot> shots)
        {
            PackId = packId;
            CutsceneId = cutsceneId;
            DefinitionId = StoryCutsceneIds.DefinitionOf(cutsceneId);
            Tier = tier;
            Shots = shots;
        }

        public string PackId { get; }

        /// <summary>The (instance) id as received.</summary>
        public string CutsceneId { get; }

        /// <summary>The definition id (the part before <c>@</c>).</summary>
        public string DefinitionId { get; }

        /// <summary>The tier the plan plays at (a downgraded scene plays as a card).</summary>
        public int Tier { get; private set; }

        public IReadOnlyList<PlannedShot> Shots { get; private set; }

        /// <summary>A replay the player asked for (skippable at once, changes nothing).</summary>
        public bool Replay { get; private set; }

        /// <summary>The built-in recap card.</summary>
        public bool IsRecap { get; private set; }

        /// <summary>A client-made banner (no definition, nothing to confirm).</summary>
        public bool IsLocalBanner { get; private set; }

        /// <summary>The scene's music track; empty = the music keeps playing.</summary>
        public string Music { get; private set; } = string.Empty;

        /// <summary>Narrator lines the scene shows itself (dropped from the VEGA panel while it is owed).</summary>
        public IReadOnlyList<string> ReplaceLineKeys { get; private set; } = Array.Empty<string>();

        public bool Skippable { get; private set; } = true;

        public float MinDisplay { get; private set; }

        /// <summary>Hold a singleplayer world while the camera is taken.</summary>
        public bool HoldWorld { get; private set; }

        /// <summary>Freeze the controls.</summary>
        public bool LockInput { get; private set; }

        /// <summary>Hide the HUD.</summary>
        public bool HideHud { get; private set; }

        /// <summary>Whether any shot wants the camera.</summary>
        public bool TakesCamera => Shots.Any(s => s.TakesCamera);

        /// <summary>The length when nothing waits for the reader.</summary>
        public float TimedLength => Shots.Sum(s => s.Duration);

        /// <summary>Whether a caption style may wait for the reader.</summary>
        public static bool IsPageGatedStyle(string? style)
            => style == CutsceneCaptionStyles.Subtitle || style == CutsceneCaptionStyles.Speaker || style == CutsceneCaptionStyles.ReadingFrame;

        /// <summary>The page budget of a style (0 = one page).</summary>
        public static int PageCharsOf(string? style) => style switch
        {
            CutsceneCaptionStyles.Subtitle => SubtitlePageChars,
            CutsceneCaptionStyles.Speaker => SpeakerPageChars,
            CutsceneCaptionStyles.ReadingFrame => ReadingFramePageChars,
            _ => 0,
        };

        /// <summary>The reading floor of one page: <c>max(3 s, 1.2 s + chars / 12)</c>, at most 10 s — measured on the
        /// localized text, so a longer German line gets more time by itself.</summary>
        public static float ReadingSeconds(string? text)
        {
            int chars = string.IsNullOrEmpty(text) ? 0 : text!.Length;
            float s = ReadingFloorBase + chars / ReadingCharsPerSecond;
            return Math.Min(ReadingFloorMax, Math.Max(ReadingFloorMin, s));
        }

        /// <summary>The play's argument made readable: each U+001F-separated value that is a known locale key is
        /// localized (a chapter title key), anything else stays as it is (a body name). The values stay joined, so
        /// <see cref="LineText.Compose"/> fills <c>{0}</c>, <c>{1}</c>… from them.</summary>
        public static string ResolveArg(string? arg, CutsceneTextSource text)
        {
            if (string.IsNullOrEmpty(arg))
            {
                return string.Empty;
            }

            var values = LineText.SplitArgs(arg);
            for (int i = 0; i < values.Length; i++)
            {
                if (text.Has(values[i]))
                {
                    values[i] = text.Localize(values[i]);
                }
            }

            return string.Join(LineText.ArgSeparator.ToString(), values);
        }

        /// <summary>Plans a pack-defined cutscene.</summary>
        /// <param name="def">The definition (for an instance id: the template).</param>
        /// <param name="packId">The pack.</param>
        /// <param name="cutsceneId">The (instance) id as received.</param>
        /// <param name="arg">The play's argument (<c>CutscenePlay.Arg</c>).</param>
        /// <param name="text">The words.</param>
        /// <param name="waitForReader">The "Story text: wait for me" setting.</param>
        /// <param name="replay">A requested replay.</param>
        public static CutscenePlan Build(CutsceneDefinition def, string packId, string cutsceneId, string? arg,
            CutsceneTextSource text, bool waitForReader, bool replay = false)
        {
            if (def is null)
            {
                throw new ArgumentNullException(nameof(def));
            }

            string resolvedArg = ResolveArg(arg, text);
            int tier = CutsceneTiers.IsValid(def.Tier) ? def.Tier : CutsceneTiers.Card;
            bool gateAllowed = waitForReader && tier >= CutsceneTiers.Card;
            var shots = new List<PlannedShot>();
            var defShots = def.Shots ?? new List<CutsceneShot>();
            for (int i = 0; i < defShots.Count; i++)
            {
                var shot = defShots[i] ?? new CutsceneShot();
                var captions = new List<PlannedCaption>();
                float authored = shot.Duration > 0f ? shot.Duration : ReadingFloorMin;
                float length = authored;
                foreach (var c in shot.Captions ?? new List<CutsceneCaption>())
                {
                    if (c is null)
                    {
                        continue;
                    }

                    string style = string.IsNullOrEmpty(c.Style) ? CutsceneCaptionStyles.Subtitle : c.Style;
                    string body = LineText.Compose(text.Localize(c.Key), resolvedArg, text.Glyph);
                    string kicker = string.IsNullOrEmpty(c.KickerKey) ? string.Empty : LineText.Compose(text.Localize(c.KickerKey), resolvedArg, text.Glyph);
                    float at = Math.Max(0f, c.At);
                    float end = c.Hold > 0f ? at + c.Hold : authored;
                    var planned = PlanCaption(style, c.Key ?? string.Empty, kicker, body, c.Speaker ?? string.Empty, at, end,
                        gateAllowed && IsPageGatedStyle(style));
                    captions.Add(planned);
                    if (!planned.PageGated)
                    {
                        length = Math.Max(length, planned.End);
                    }
                }

                var sfx = (shot.Sfx ?? new List<CutsceneSfx>())
                    .Where(s => s != null && !string.IsNullOrEmpty(s.Cue))
                    .Select(s => (s.Cue, Math.Max(0f, s.At)))
                    .ToList();
                string camera = string.IsNullOrEmpty(shot.Camera?.Type) ? CutsceneCameras.PlayerView : shot.Camera!.Type;
                string anchor = string.IsNullOrEmpty(shot.Camera?.Anchor) ? CutsceneAnchors.Player : shot.Camera!.Anchor;
                string grade = string.IsNullOrEmpty(shot.Grade) ? CutsceneGrades.None : shot.Grade;
                shots.Add(new PlannedShot(i, camera, anchor, grade, length, authored, captions, sfx));
            }

            if (shots.Count == 0)
            {
                shots.Add(new PlannedShot(0, CutsceneCameras.PlayerView, CutsceneAnchors.Player, CutsceneGrades.None, ReadingFloorMin,
                    ReadingFloorMin, Array.Empty<PlannedCaption>(), Array.Empty<(string, float)>()));
            }

            var options = def.Options ?? new CutsceneOptions();
            return new CutscenePlan(packId, cutsceneId, tier, shots)
            {
                Replay = replay,
                Music = def.Music ?? string.Empty,
                ReplaceLineKeys = (def.ReplaceLineKeys ?? new List<string>()).Where(k => !string.IsNullOrEmpty(k)).ToArray(),
                Skippable = options.Skippable,
                MinDisplay = Math.Max(0f, options.MinDisplay),
                HoldWorld = def.EffectiveHoldWorld,
                LockInput = def.EffectiveLockInput,
                HideHud = def.EffectiveHideHud,
            };
        }

        /// <summary>Plans the built-in recap card ("The story so far", d4 §3.6): a reading frame over the live view with the
        /// pack's name and the reached chapters' titles, each ticked — titles only, no beat text. The argument carries the
        /// chapter title keys (U+001F-joined).</summary>
        public static CutscenePlan Recap(string packId, string? arg, string packNameKey, CutsceneTextSource text, bool waitForReader,
            bool replay = false)
        {
            string heading = text.Localize("ui.story.so_far");
            string packName = text.Has(packNameKey) ? text.Localize(packNameKey) : string.Empty;
            string kicker = packName.Length > 0 ? heading + " · " + packName : heading;
            var lines = RecapLines(arg, text);
            string body = string.Join("\n", lines);
            string hint = text.Localize("ui.story.so_far_hint");
            if (hint.Length > 0)
            {
                body = body.Length > 0 ? body + "\n\n" + hint : hint;
            }

            var caption = PlanCaption(CutsceneCaptionStyles.ReadingFrame, string.Empty, kicker, body, string.Empty, 0.4f, RecapSeconds,
                waitForReader, keepOnePage: true);
            float length = caption.PageGated ? RecapSeconds : Math.Max(RecapSeconds, caption.End);
            var shot = new PlannedShot(0, CutsceneCameras.PlayerView, CutsceneAnchors.Player, CutsceneGrades.None, length, RecapSeconds,
                new[] { caption }, Array.Empty<(string, float)>());
            return new CutscenePlan(packId, StoryCutsceneIds.Recap, CutsceneTiers.Card, new[] { shot })
            {
                Replay = replay,
                IsRecap = true,
            };
        }

        /// <summary>The recap card's rows: one "✓ title" per chapter title in the argument (a key is localized).</summary>
        public static IReadOnlyList<string> RecapLines(string? arg, CutsceneTextSource text)
        {
            var lines = new List<string>();
            foreach (string value in LineText.SplitArgs(arg))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                lines.Add("✓ " + (text.Has(value) ? text.Localize(value) : value));
            }

            return lines;
        }

        /// <summary>A banner the client makes itself (T1, nothing to confirm) — "Story moment saved" after an interrupted
        /// scene.</summary>
        public static CutscenePlan LocalBanner(string packId, string kicker, string line)
        {
            var caption = PlanCaption(CutsceneCaptionStyles.Subtitle, string.Empty, kicker ?? string.Empty, line ?? string.Empty,
                string.Empty, 0f, BannerSeconds, pageGated: false, keepOnePage: true);
            var shot = new PlannedShot(0, CutsceneCameras.PlayerView, CutsceneAnchors.Player, CutsceneGrades.None,
                Math.Max(BannerSeconds, caption.End), BannerSeconds, new[] { caption }, Array.Empty<(string, float)>());
            return new CutscenePlan(packId, string.Empty, CutsceneTiers.Banner, new[] { shot }) { IsLocalBanner = true };
        }

        /// <summary>The same scene as a card (the downgrade path, synthesis §6.2 "Abstieg"): every shot keeps its captions
        /// and timings but stays on the player's view, nothing holds the world, freezes the controls or hides the HUD. A
        /// banner or card is returned as it is.</summary>
        public CutscenePlan AsCard()
        {
            if (Tier <= CutsceneTiers.Card && !TakesCamera && !HoldWorld && !LockInput && !HideHud)
            {
                return this;
            }

            var shots = Shots.Select(s => new PlannedShot(s.Index, s.TakesCamera ? CutsceneCameras.PlayerView : s.Camera, s.Anchor, s.Grade,
                s.Duration, s.AuthoredDuration, s.Captions, s.Sfx)).ToList();
            return new CutscenePlan(PackId, CutsceneId, Math.Min(Tier, CutsceneTiers.Card), shots)
            {
                Replay = Replay,
                IsRecap = IsRecap,
                IsLocalBanner = IsLocalBanner,
                Music = Music,
                ReplaceLineKeys = ReplaceLineKeys,
                Skippable = Skippable,
                MinDisplay = MinDisplay,
            };
        }

        /// <summary>Pages and times one caption. Timed: each page runs for its reading floor or its share of the window,
        /// whichever is longer (the caption's end moves out when needed). Page-gated: the window stays as written; the
        /// floors are only the "auto" length of each page.</summary>
        private static PlannedCaption PlanCaption(string style, string key, string kicker, string body, string speaker, float at,
            float end, bool pageGated, bool keepOnePage = false)
        {
            var pages = keepOnePage || !IsPageGatedStyle(style) ? OnePage(body) : CaptionPager.Pages(body, PageCharsOf(style));
            if (pages.Count == 0)
            {
                pages = new[] { string.Empty };
            }

            var floors = pages.Select(p => ReadingSeconds(style == CutsceneCaptionStyles.TitleCard ? kicker + " " + p : p)).ToArray();
            float window = Math.Max(0f, end - at);
            if (pageGated)
            {
                return new PlannedCaption(style, key, kicker, body, speaker, pages, floors, at, at + window, pageGated: true);
            }

            float needed = floors.Sum();
            float effective = Math.Max(window, needed);
            float scale = needed > 0f ? effective / needed : 1f;
            var seconds = floors.Select(f => f * scale).ToArray();
            return new PlannedCaption(style, key, kicker, body, speaker, pages, seconds, at, at + effective, pageGated: false);
        }

        private static IReadOnlyList<string> OnePage(string body)
        {
            string t = (body ?? string.Empty).Trim();
            return new[] { t };
        }
    }
}
