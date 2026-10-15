// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Localization;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Unity-free logic behind VEGA's speech panel and the re-readable tips log (#736, #737): splitting an
    /// over-long line into pages that fit the fixed panel, and reconstructing the tips a player has already
    /// received from their server-persisted <c>vega:*</c> milestones. Both live here (not in the
    /// MonoBehaviours) so the paging and mapping rules are covered by plain .NET tests.
    /// </summary>
    public static class VegaText
    {
        /// <summary>Advisor hints in teaching order (vitals first, then situational), followed by the context
        /// tips (#1077) in the order they tend to matter: gear, materials, places, space.</summary>
        private static readonly string[] HintOrder =
        {
            "o2", "breathe", "energy", "cold", "heat", "hunger", "shipfood", "base_hunger", "invfull", "night", "poi",
            "medkit", "hull_low",
            "lamp_off", "lamp_missing", "torch_underground", "eat_now", "wrong_tool", "tier_gate", "scanner_idle", "scanner_unknown", "speeder_far", "ship_far",
            "rare_ore_near", "needed_ore_near", "data_cache_near", "craftable_now", "blueprint_affordable",
            "settlement_near", "ruin_near", "factory_near", "treasure_near", "trader_near", "tameable_near", "player_near",
            "asteroid_near", "asteroid_no_tool", "station_near", "wreck_signal", "wreck_signal_manual", "debris_signal", "debris_recorder", "jump_ready",
        };

        /// <summary>Repeat / retire markers the server appends to a context-tip milestone (<c>vega:hint:lamp_off#2</c>,
        /// <c>#done</c>). They are pacing state, not tips — the log lists a tip once.</summary>
        private const char TipRepeatMarker = '#';

        /// <summary>Separator between several <c>{n}</c> arguments packed into one <c>ShipAiLine.LineArg</c>
        /// (mirrors the server's <c>VegaArgSeparator</c>; the one definition is <see cref="LineText.ArgSeparator"/>).</summary>
        public const char ArgSeparator = LineText.ArgSeparator;

        /// <summary>Splits a packed line argument into the <c>{0}</c>, <c>{1}</c>… values.</summary>
        public static string[] SplitArgs(string lineArg) => LineText.SplitArgs(lineArg);

        /// <summary>
        /// Replaces <c>{key:Action}</c> tokens in a localized line with the on-screen name of the control bound to
        /// that action (keyboard key, pad button or touch wording — whatever <paramref name="glyph"/> returns for
        /// the action name). A token whose action the resolver does not know is dropped to its action name so a
        /// stale locale never shows raw braces. Lines without tokens are returned untouched. Since #2466 the one
        /// implementation lives in Shared (<see cref="LineText"/>), so the server composes the same way.
        /// </summary>
        public static string ExpandKeyTokens(string text, Func<string, string> glyph) => LineText.ExpandKeyTokens(text, glyph);

        /// <summary>The label of the menu button on the touch layout — a symbol, like the ◄ ► arrows beside it, so it
        /// has no locale key (TouchControlsUi draws the same character).</summary>
        public const string TouchMenuLabel = "≡";

        /// <summary>The quick-bar arrows of the touch layout (TouchControlsUi draws ◄ and ►) — what <c>{key:Hotbar}</c> reads
        /// as on a tablet.</summary>
        public const string TouchHotbarLabel = "◄ ►";

        /// <summary>
        /// The pseudo-actions a <c>{key:…}</c> token may name besides the Unity <c>InputAction</c>s (#2469, synthesis §3.6):
        /// the continuous core of the controls (move, look, jump, crouch, mine, place, scan, the quick bar) that is no
        /// rebindable action. <c>HudUi.KeyTokenGlyph</c> resolves them per device; <see cref="TouchControlLabel"/> names their
        /// touch buttons; the server's neutral words are <c>ui.vega.act.&lt;Name&gt;</c>.
        /// </summary>
        public static readonly IReadOnlyList<string> PseudoActions = new[] { "Move", "Look", "Jump", "Crouch", "Mine", "Place", "Scan", "Hotbar" };

        /// <summary>Whether <paramref name="action"/> is one of the <see cref="PseudoActions"/>.</summary>
        public static bool IsPseudoAction(string? action)
        {
            foreach (string pseudo in PseudoActions)
            {
                if (string.Equals(pseudo, action, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// What a <c>{key:Action}</c> token reads as on a tablet (#2463): the label of the touch layout's own button for
        /// that action (USE, MAP, FEED, ≡ …), resolved through <paramref name="localize"/> (<c>ui.touch.*</c> keys), or
        /// null when the layout has no button for it — the caller then names the on-screen action menu. Keyed by the
        /// <c>InputAction</c> name because this assembly cannot see the Unity enum; the pairs mirror the action wiring in
        /// <c>TouchControlsUi.Build</c>.
        /// </summary>
        public static string? TouchControlLabel(string action, Func<string, string> localize)
        {
            string? key = action switch
            {
                "Interact" => "ui.touch.use",
                "PlanetMap" or "FlightMap" => "ui.touch.map",
                "FeedCreature" => "ui.touch.feed",
                "PrimaryFire" => "ui.touch.attack",
                "ToggleThirdPerson" => "ui.touch.view",
                "RotateShape" => "ui.touch.rotate",
                "FlightAutopilot" => "ui.touch.auto",
                "FlightPadChooser" => "ui.touch.land",
                "FlightEnterInterior" => "ui.touch.ship",
                "FlightTargetNext" => "ui.touch.target",
                "EvaDeployStation" => "ui.touch.deploy",
                "SpeederBoost" => "ui.touch.boost",
                "SpeederExit" => "ui.touch.exit",
                "SpeederRefuel" => "ui.touch.fuel",
                "VegaContinue" => "ui.touch.next",
                "ContextActions" => "ui.touch.actions",
                // #2469: the pseudo-actions — the layout's own buttons, the stick and the look pad.
                "Jump" => "ui.touch.jump",
                "Crouch" => "ui.touch.down",
                "Mine" or "Scan" => "ui.touch.mine",
                "Place" => "ui.touch.place",
                "Move" => "ui.key.pad_left_stick",
                "Look" => "ui.key.touch_look",
                _ => null,
            };

            if (key != null)
            {
                return localize(key);
            }

            return action switch
            {
                "UiMenu" => TouchMenuLabel,
                "Hotbar" => TouchHotbarLabel,
                _ => null,
            };
        }

        /// <summary>The locale key VEGA's lines name an action by on a tablet when the touch layout has no button of its own
        /// for it (<see cref="TouchControlLabel"/> is null): "the on-screen action menu" — the verb waits in that list.</summary>
        public const string TouchMenuFallbackKey = "ui.vega.key_touch";

        /// <summary>The same fallback for the HUD's short "X: verb" prompts and buttons (#2502): the label of the ACT button that
        /// opens the list — "ACT: undock", "Store all materials (ACT)" — instead of a phrase that does not fit a prompt.</summary>
        public const string TouchPromptFallbackKey = "ui.touch.actions";

        /// <summary>Which fallback a tablet reads for an action without its own touch button: <see cref="TouchPromptFallbackKey"/>
        /// in a HUD prompt (<paramref name="prompt"/>), <see cref="TouchMenuFallbackKey"/> in VEGA's sentences.</summary>
        public static string TouchFallbackKey(bool prompt) => prompt ? TouchPromptFallbackKey : TouchMenuFallbackKey;

        /// <summary>
        /// #2469 (E11, synthesis G12): the client-only VEGA lessons the basic training teaches itself in its flight steps —
        /// the chart, the ship scanner and holding its trigger, scanning an anomaly. While the tutorial runs for the player
        /// (<c>ShipAiLine.TutorialActive</c>) they are not spoken; the step lines carry them, per player on the server, so a
        /// shared PC no longer decides who heard them. A deep-dive course covers a lesson through the step that names it in
        /// its <c>hintRefs</c> (<see cref="LessonCoveredByStoryStep"/>, #2459): the planet card, target lock, wormhole,
        /// climbing and bio-lab lessons; the pad/touch context list stays a client lesson.
        /// </summary>
        public static bool LessonCoveredByTutorial(string? lineKey) => lineKey switch
        {
            "vega.hint.chart_waypoint" or "vega.hint.ship_scanner" or "vega.hint.ship_scan_hold" or "vega.hint.anomaly_scan" => true,
            _ => false,
        };

        /// <summary>The prefix of a VEGA hint's line key; the rest is the hint id a step names in its <c>hintRefs</c>.</summary>
        private const string HintKeyPrefix = "vega.hint.";

        /// <summary>
        /// #2459 (G12, one teaching voice): whether a client-only lesson (<c>vega.hint.&lt;id&gt;</c>, spoken by the client's
        /// VEGA panel) is a story step's to teach right now — a chapter of a player pack runs or is replayed for this player
        /// (<paramref name="states"/>, the server's Journal view) and one of its steps names the lesson in its <c>hintRefs</c>
        /// (<paramref name="packs"/>, this client's copy of the packs) and is active or still ahead: the pilot course's chart,
        /// planet-scan and target-lock steps, the navigator's wormhole, the climbing step, the bio lab. A step the chapter
        /// already passed (done or skipped — it stands before the chapter's first active step) no longer covers it; with no
        /// active step known the whole running chapter does. Optional chapters only offered, not started, cover nothing — the
        /// lesson is then the only teaching. False for an older server (no states) and for a key that is no VEGA hint.
        /// </summary>
        public static bool LessonCoveredByStoryStep(
            string? lineKey,
            BlocksBeyondTheStars.Networking.Messages.StoryPackStates? states,
            IEnumerable<BlocksBeyondTheStars.Shared.Story.StoryDefinition>? packs)
        {
            if (lineKey is null || !lineKey.StartsWith(HintKeyPrefix, StringComparison.Ordinal) || states?.Packs is null || packs is null)
            {
                return false;
            }

            string id = lineKey.Substring(HintKeyPrefix.Length);
            foreach (var net in states.Packs)
            {
                if (net?.Chapters is null)
                {
                    continue;
                }

                BlocksBeyondTheStars.Shared.Story.StoryDefinition? def = null;
                foreach (var pack in packs)
                {
                    if (pack != null && string.Equals(pack.Id, net.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        def = pack;
                        break;
                    }
                }

                if (def is null)
                {
                    continue;
                }

                foreach (var chapter in net.Chapters)
                {
                    if (chapter is null
                        || (chapter.State != BlocksBeyondTheStars.Networking.Messages.StoryChapterStates.Active
                            && chapter.State != BlocksBeyondTheStars.Networking.Messages.StoryChapterStates.Replay))
                    {
                        continue;
                    }

                    var chapterDef = def.Chapters.Find(c => string.Equals(c.Id, chapter.Id, StringComparison.Ordinal));
                    if (chapterDef != null && ChapterStepAheadNames(chapterDef, net.Steps, id))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Whether a step of a running chapter at or after its first active step names the hint id.</summary>
        private static bool ChapterStepAheadNames(
            BlocksBeyondTheStars.Shared.Story.StoryChapter chapter,
            BlocksBeyondTheStars.Networking.Messages.NetStoryStep[]? active,
            string hintId)
        {
            int firstActive = -1;
            for (int i = 0; i < chapter.Steps.Count && firstActive < 0; i++)
            {
                foreach (var step in active ?? Array.Empty<BlocksBeyondTheStars.Networking.Messages.NetStoryStep>())
                {
                    if (step != null && string.Equals(step.ChapterId, chapter.Id, StringComparison.Ordinal)
                        && string.Equals(step.Id, chapter.Steps[i].Id, StringComparison.Ordinal))
                    {
                        firstActive = i;
                        break;
                    }
                }
            }

            for (int i = Math.Max(0, firstActive); i < chapter.Steps.Count; i++)
            {
                if (chapter.Steps[i].HintRefs is { } refs && refs.Contains(hintId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>#2471: lines that point at the Journal (or set a target) rather than say something worth re-reading — they
        /// are shown, but never kept in VEGA's notes: the join's "N new entries in your Journal" (B09) and the dock lesson's
        /// "I set the station as your flight target" — and (#2570) "I've marked your goal", whose marker goes with its step.</summary>
        public static bool IsTransientLine(string? lineKey) => lineKey switch
        {
            BlocksBeyondTheStars.Shared.Story.StoryNarratorLines.JournalNews => true,
            BlocksBeyondTheStars.Shared.Story.StoryNarratorLines.StepMarkedStation => true,
            BlocksBeyondTheStars.Shared.Story.StoryNarratorLines.StepMarkedSpot => true,
            BlocksBeyondTheStars.Shared.Story.StoryNarratorLines.ChapterNothingLeft => true, // #2500: the answer to a click
            _ => false,
        };

        /// <summary>What a <c>{0}</c> slot reads as when a line is shown without its argument (the tips log, #2364:
        /// the milestones remember the key, not the wreck's name).</summary>
        public const string MissingArg = LineText.MissingArg;

        /// <summary>
        /// Composes a VEGA line for display (#2363): the <c>{key:Action}</c> tokens are expanded FIRST, then the
        /// <c>{0}</c>, <c>{1}</c>… slots are filled from the packed <paramref name="lineArg"/>. The order is the fix —
        /// <c>string.Format</c> reads <c>{key:…}</c> as a malformed placeholder and throws, which is how
        /// <c>vega.hint.debris_signal</c> silently lost every pilot's debris-field tip in 2026.10.8. A format that still
        /// fails (a damaged community translation) never throws: the token-expanded line is shown as it is, and the
        /// locale test that composes every VEGA line catches the leftover braces. Without an argument the slots read
        /// as <see cref="MissingArg"/> ("The wreck … drifts"), for the tips log. Delegates to <see cref="LineText.Compose"/>
        /// (#2466).
        /// </summary>
        public static string Compose(string? text, string? lineArg, Func<string, string> glyph) => LineText.Compose(text, lineArg, glyph);

        private static readonly string[] WorldOrder =
            { "asteroid", "ocean", "corrupted", "fungal", "ice", "volcanic", "gds" };

        /// <summary>
        /// Groups wrapped text lines into pages that fit a fixed-height box. Inputs are the wrap layout the
        /// UI text engine produced: the first character index and the height of each rendered line.
        /// Returns (start, length) character ranges, one per page; a single range covering everything when
        /// it already fits. A line taller than <paramref name="maxHeight"/> still gets a page of its own —
        /// pages never come back empty, so paging can never lose text (the bug this replaces, #736).
        /// </summary>
        public static List<(int Start, int Length)> PageRanges(
            IReadOnlyList<int> lineStarts, IReadOnlyList<float> lineHeights, int textLength, float maxHeight)
        {
            var pages = new List<(int, int)>();
            if (textLength <= 0)
            {
                return pages;
            }

            int count = Math.Min(lineStarts.Count, lineHeights.Count);
            if (count == 0)
            {
                pages.Add((0, textLength)); // no layout info — behave like the unpaged panel
                return pages;
            }

            int pageStart = 0; // index into lineStarts
            float pageHeight = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i > pageStart && pageHeight + lineHeights[i] > maxHeight)
                {
                    pages.Add(Range(lineStarts, pageStart, i, textLength));
                    pageStart = i;
                    pageHeight = 0f;
                }

                pageHeight += lineHeights[i];
            }

            pages.Add(Range(lineStarts, pageStart, count, textLength));
            return pages;
        }

        private static (int, int) Range(IReadOnlyList<int> lineStarts, int firstLine, int endLine, int textLength)
        {
            int start = lineStarts[firstLine];
            int end = endLine < lineStarts.Count ? lineStarts[endLine] : textLength;
            return (start, Math.Max(0, end - start));
        }

        /// <summary>Rebuilds the "VEGA tips" log without a tutorial pack (see the overload).</summary>
        public static List<string> JournalKeys(IEnumerable<string> milestones) => JournalKeys(milestones, null);

        /// <summary>
        /// Rebuilds the "VEGA tips" log from a player's persisted milestones (the server's <c>VegaJournal</c>): first the
        /// lessons of the basic training they have done — since #2469 the tutorial pack's steps (<c>story:&lt;pack&gt;:step:&lt;id&gt;</c>,
        /// done, not skipped), in the pack's order, each as its start line and its done line, read from the tutorial packs
        /// among <paramref name="packs"/> (the hard-coded copy of the old nine-stage chain is gone) — then every advisor
        /// hint they have already received, as locale keys in canonical order. Story and memory content (<c>vega:mem:*</c>)
        /// is excluded — the Story tab logs those live (Kind 2 lines). Unknown hint ids still map generically so future server
        /// hints appear without a client update (the UI drops keys with no translation).
        /// </summary>
        public static List<string> JournalKeys(IEnumerable<string> milestones, IEnumerable<BlocksBeyondTheStars.Shared.Story.StoryDefinition>? packs)
        {
            var set = new HashSet<string>(milestones, StringComparer.Ordinal);
            var keys = new List<string>();

            foreach (var pack in packs ?? Array.Empty<BlocksBeyondTheStars.Shared.Story.StoryDefinition>())
            {
                if (!string.Equals(pack.Kind, BlocksBeyondTheStars.Shared.Story.StoryRegistry.KindTutorial, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var chapter in pack.Chapters)
                {
                    foreach (var step in chapter.Steps)
                    {
                        if (!set.Contains(BlocksBeyondTheStars.Shared.Story.StoryPackKeys.StepDone(pack.Id, step.Id)))
                        {
                            continue;
                        }

                        foreach (string? key in new[] { step.Explain?.StartKey, step.Explain?.DoneKey })
                        {
                            if (key is { Length: > 0 } && !keys.Contains(key))
                            {
                                keys.Add(key);
                            }
                        }
                    }
                }
            }

            foreach (var id in HintOrder)
            {
                if (set.Remove("vega:hint:" + id))
                {
                    keys.Add("vega.hint." + id);
                }
            }

            foreach (var id in WorldOrder)
            {
                if (set.Remove("vega:hint:world:" + id))
                {
                    keys.Add("vega.hint.world." + id);
                }
            }

            // The bandit briefing burns its once-flag directly (GameServerBanditShips), with a line key
            // that does not follow the vega.hint.<id> pattern.
            if (set.Remove("vega:hint:bandit_brief"))
            {
                keys.Add("vega.brief.bandits");
            }

            // Anything else under vega:hint: maps generically (future hints; world types beyond the list).
            // Sorted — set iteration order is arbitrary and the log should be stable across joins.
            var rest = new List<string>();
            foreach (var m in set)
            {
                if (m.StartsWith("vega:hint:", StringComparison.Ordinal) && m.IndexOf(TipRepeatMarker) < 0)
                {
                    rest.Add("vega.hint." + m["vega:hint:".Length..].Replace(':', '.'));
                }
            }

            rest.Sort(StringComparer.Ordinal);
            keys.AddRange(rest);

            return keys;
        }
    }
}
