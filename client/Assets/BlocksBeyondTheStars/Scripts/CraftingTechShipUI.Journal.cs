// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BlocksBeyondTheStars.Client.Portal;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Story tab's Journal of <see cref="CraftingTechShipUI"/> (#2471): one page per story, the world story
    /// (VEGA) with its story-track block and replays, VEGA's notes and the story-log entries.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        // --- The Journal (#2471): the Story tab grew into it — one page per story, VEGA's notes, the player's own notes ---

        /// <summary>The Journal page of the selected category: a player story (the tutorial, a side story — chapters, tasks,
        /// switches), the world story (VEGA — <see cref="BuildStoryList"/>), VEGA's notes or the player's own notes. Laid out
        /// in absolute rows like the rest of the tab (no LayoutGroup — the wrapped-text overflow trap).</summary>
        private float BuildJournalPage()
        {
            if (_category == JournalCategories.NotesId)
            {
                return BuildNotesList();
            }

            if (JournalCategories.TryPackId(_category, out string packId))
            {
                var story = Game?.Story;
                if (story != null && story.Active && string.Equals(story.StoryId, packId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return BuildStoryList();
                }

                var pack = JournalCategories.Find(Game?.StoryPackStates, _category);
                if (pack != null)
                {
                    return BuildPackPage(pack);
                }
            }

            return BuildVegaNotes();
        }

        private bool HasLoc(string key) => Game?.Localizer != null && !string.IsNullOrEmpty(key) && Game.Localizer.Has(key);

        /// <summary>A locale key's text, or <paramref name="fallback"/> when this client has no text for it.</summary>
        private string LocOr(string key, string fallback) => HasLoc(key) ? L(key) : fallback;

        /// <summary>A Journal text resolves its <c>{key:…}</c> tokens through the one resolver the VEGA panel uses.</summary>
        private string ComposeJournalText(string text)
            => VegaPanel.Instance != null ? VegaPanel.Instance.ComposeLine(text, null) : VegaText.Compose(text, null, a => a);

        /// <summary>A player story's page (#2471, synthesis §5.8): name, description, status and the story's switches (Track,
        /// Start, Skip all, Restart from the beginning, Switch off / on); "What to do now" — the running parts' tasks ("Step 4 of
        /// 8: …" with live progress, Skip step, More in the Codex) and the part's Skip / Start over; then every part with its
        /// state and its buttons (Start / Later for an offer, Replay for a finished one); then the story's scenes to watch
        /// again. The rules are <see cref="JournalPackPage"/> (Client.Core, unit-tested).</summary>
        private float BuildPackPage(NetStoryPack pack)
        {
            float y = 8f;
            var live = Game?.Objectives;
            UiKit.AddText(_listContent, 8, y, 760, 36, LocOr(pack.NameKey, pack.Id) + (pack.Badge ? "  ★" : string.Empty), 24,
                pack.Badge ? UiKit.Ok : UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 42f;
            if (HasLoc(pack.DescriptionKey))
            {
                y = JournalRow(y, 8f, 760f, L(pack.DescriptionKey), 16, UiKit.CyanDim, FontStyle.Normal);
            }

            string status = JournalPackPage.Status(pack, L);
            if (status.Length > 0)
            {
                y = JournalRow(y, 8f, 760f, status, 18, pack.Badge ? UiKit.Ok : UiKit.TextCol, FontStyle.Bold);
            }

            y = JournalButtons(y + 4f, 0f, pack.Id, JournalPackPage.PackActions(pack));

            // What to do now: the running parts' tasks, the chip's first.
            var running = pack.Chapters.Where(c => c.State == StoryChapterStates.Active || c.State == StoryChapterStates.Replay).ToList();
            if (running.Count > 0 && pack.Enabled)
            {
                y += 6f;
                y = AllianceSection(L("ui.journal.now"), y);
                foreach (var ch in running)
                {
                    y = JournalRow(y, 8f, 760f, JournalPackPage.Icon(ch.State) + "  " + JournalPackPage.ChapterLabel(pack, ch, L, HasLoc), 18,
                        UiKit.Cyan, FontStyle.Bold);
                    foreach (var step in JournalPackPage.StepsOf(pack, ch.Id))
                    {
                        var stepActions = JournalPackPage.StepActions(step);
                        float top = y;
                        float width = stepActions.Count > 0 ? 780f - 32f - (stepActions.Count * 196f) : 748f;
                        y = JournalRow(y, 32f, width, JournalPackPage.StepLine(pack, step, live, L, ComposeJournalText, HasLoc), 17,
                            step.OnChip ? UiKit.TextCol : UiKit.CyanDim, FontStyle.Normal);
                        float bx = 780f;
                        for (int i = stepActions.Count - 1; i >= 0; i--)
                        {
                            bx -= 196f;
                            var chosen = stepActions[i];
                            UiKit.AddButton(_listContent, bx + 6f, top, 190f, 34f, L(chosen.LabelKey), () => RunJournalAction(pack.Id, chosen));
                        }

                        y = Mathf.Max(y, top + 40f);
                    }

                    y = JournalButtons(y, 32f, pack.Id, JournalPackPage.RunningChapterActions(ch));
                }
            }

            // Every part with its state.
            y += 6f;
            y = AllianceSection(L(JournalPackPage.IsTutorial(pack) ? "ui.journal.parts" : "ui.story.chapters"), y);
            foreach (var ch in pack.Chapters)
            {
                var actions = JournalPackPage.ChapterActions(pack, ch); // #2500: none when nothing is left to practise
                bool active = ch.State == StoryChapterStates.Active || ch.State == StoryChapterStates.Replay;
                var color = active ? UiKit.Cyan : ch.State == StoryChapterStates.Done || ch.State == StoryChapterStates.Offered || ch.NothingLeft ? UiKit.TextCol : UiKit.CyanDim;
                string row = JournalPackPage.ChapterIcon(ch) + "  " + JournalPackPage.ChapterLabel(pack, ch, L, HasLoc)
                             + "   ·   " + JournalPackPage.StateText(ch, L, HasLoc);
                float top = y;
                float width = actions.Count > 0 ? 780f - 8f - (actions.Count * 150f) : 760f;
                y = JournalRow(y, 8f, width, row, 17, color, active ? FontStyle.Bold : FontStyle.Normal);
                float bx = 780f;
                for (int i = actions.Count - 1; i >= 0; i--)
                {
                    bx -= 150f;
                    var chosen = actions[i];
                    UiKit.AddButton(_listContent, bx + 6f, top, 144f, 32f, L(chosen.LabelKey), () => RunJournalAction(pack.Id, chosen));
                }

                y = Mathf.Max(y, top + (actions.Count > 0 ? 38f : 0f));
            }

            y = JournalReplaySection(y, pack.Id);
            return y + 12f;
        }

        /// <summary>A Journal button: the "More" opens the Codex; everything else is the player's own <see cref="StoryPackIntent"/>
        /// (the server validates it and answers with the new stories at once — the page redraws when they arrive).</summary>
        private void RunJournalAction(string packId, JournalAction action)
        {
            if (action.OpensCodex)
            {
                Menu?.OpenWiki(JournalPackPage.CodexChapter(action.Codex));
                return;
            }

            Game?.Network?.SendStoryPack(packId, action.Action, action.ChapterId, action.StepId);
        }

        /// <summary>A row of Journal buttons from <paramref name="x"/>, wrapping onto the next row when they do not fit; returns
        /// the next y (unchanged without buttons).</summary>
        private float JournalButtons(float y, float x, string packId, List<JournalAction> actions)
        {
            if (actions == null || actions.Count == 0)
            {
                return y;
            }

            const float H = 40f, Gap = 10f, Right = 780f;
            float cx = x;
            foreach (var action in actions)
            {
                string label = L(action.LabelKey);
                float w = Mathf.Clamp(ButtonWidth(label), 140f, Right - x);
                if (cx + w > Right && cx > x)
                {
                    cx = x;
                    y += H + 8f;
                }

                var chosen = action;
                UiKit.AddButton(_listContent, cx, y, w, H, label, () => RunJournalAction(packId, chosen));
                cx += w + Gap;
            }

            return y + H + 10f;
        }

        /// <summary>The width a button needs for its label at the button font (UiKit.AddButton: 22 bold, 18 left, 10 right).</summary>
        private static float ButtonWidth(string label)
        {
            var settings = new TextGenerationSettings
            {
                font = UiKit.Font,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                richText = false,
                scaleFactor = 1f,
                lineSpacing = 1f,
                horizontalOverflow = HorizontalWrapMode.Overflow,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(2000f, 40f),
                textAnchor = TextAnchor.MiddleLeft,
                pivot = new Vector2(0f, 1f),
                color = Color.white,
            };
            return StoryMeasurer.GetPreferredWidth(label ?? string.Empty, settings) + 40f;
        }

        /// <summary>One wrapped Journal text row, measured (the story-log rule) — returns the next y.</summary>
        private float JournalRow(float y, float x, float width, string text, int size, Color color, FontStyle style)
        {
            var settings = new TextGenerationSettings
            {
                font = UiKit.Font,
                fontSize = size,
                fontStyle = style,
                richText = false,
                scaleFactor = 1f,
                lineSpacing = 1f,
                horizontalOverflow = HorizontalWrapMode.Wrap,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(width, 0f),
                textAnchor = TextAnchor.UpperLeft,
                pivot = new Vector2(0f, 1f),
                color = Color.white,
            };
            float h = 6f + StoryMeasurer.GetPreferredHeight(text ?? string.Empty, settings);
            var t = UiKit.AddText(_listContent, x, y, width, h, text ?? string.Empty, size, color, TextAnchor.UpperLeft, style);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return y + h + 6f;
        }

        /// <summary>Watch again (#2468) for one story: the scenes of that story this player has seen and may replay — the
        /// prologue on the VEGA page, the tutorial's cards on the tutorial's. The server re-sends one on request (spoiler guard:
        /// only what was shown); the director plays it once this menu is closed. #2497: <paramref name="withoutEnding"/> leaves
        /// the ending's two scenes out while the page shows "Watch the ending again" (which replays both); a scene that will
        /// wait for the player to land says so in a short banner.</summary>
        private float JournalReplaySection(float y, string packId, bool withoutEnding = false)
        {
            var director = CutsceneDirector.Instance;
            var replays = director != null ? director.ReplayEntries(withoutEnding) : null;
            var mine = replays?.Where(e => string.Equals(e.Pack.Id, packId, System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (mine == null || mine.Count == 0)
            {
                return y;
            }

            y += 10f;
            y = AllianceSection(L("ui.story.replay"), y);
            foreach (var entry in mine)
            {
                float rowTop = y;
                y = StoryEntry(y, director.ReplayLabel(entry), 660f); // keep clear of the Watch button's lane
                var chosen = entry;
                UiKit.AddButton(_listContent, 688, rowTop, 92, 30, L("ui.story.replay_button"), () =>
                {
                    Game?.Network?.SendReplayCutscene(chosen.Pack.Id, chosen.CutsceneId);
                    CutsceneDirector.Instance?.NoteReplayWaits(chosen.Pack.Id, new[] { chosen.CutsceneId }); // #2497: in flight
                    Hide();
                });
            }

            return y;
        }

        /// <summary>VEGA's notes (#2471): every lesson, tip and narrator line VEGA has said that is not a story beat — rebuilt
        /// from the server's milestone snapshot (#737), the world story's notes (<c>NoteKeys</c>: the Guardian reveal, the
        /// insights, the resolution line) and the relay insights, plus what arrived this session. A dismissed line used to be
        /// gone for good; the non-beat narrator lines used to live only until the next join.</summary>
        private float BuildVegaNotes()
        {
            float y = 8f;
            var hint = UiKit.AddText(_listContent, 8, y, 760, 44, L("ui.journal.tips_hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            y += Mathf.Max(30f, hint.preferredHeight + 10f);
            bool anyTip = false;
            if (Game?.VegaLogKeys != null)
            {
                foreach (var key in Game.VegaLogKeys)
                {
                    if (Game?.Localizer == null || !Game.Localizer.Has(key))
                    {
                        continue; // a future server hint this client has no translation for — no raw "[key]" (#428)
                    }

                    // Composed like the speech panel (#2364): key glyphs resolved, a lost {0} reads as "…".
                    string line = VegaPanel.Instance != null ? VegaPanel.Instance.ComposeLine(L(key), null) : VegaText.Compose(L(key), null, a => a);
                    y = StoryEntry(y, line);
                    anyTip = true;
                }
            }

            if (!anyTip)
            {
                UiKit.AddText(_listContent, 12, y, 756, 28, L("ui.story.vega.none"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 34f;
            }

            return y + 12f;
        }

        /// <summary>The world story's Journal page (#2471: the VEGA page — the Story Log before): the star-network meter with
        /// its bar, the Track switch, the ending to watch again, the story-track block (Start the story now, the crew's
        /// chapter, The story so far), the chapter list with the heard beats, fragments, memories and field records, and the
        /// story's scenes to watch again (the prologue "Prologue — Awakening" among them).</summary>
        private float BuildStoryList()
        {
            float y = 8f;
            var s = Game?.Story;
            bool storyOn = s != null && s.Active;
            bool endingButton = false; // #2497: "Watch the ending again" shown — "Watch again" then leaves the ending out

            if (storyOn)
            {
                int pct = s.ProgressTarget > 0 ? Mathf.Clamp(Mathf.RoundToInt(100f * s.Progress / s.ProgressTarget), 0, 100) : 0;
                UiKit.AddText(_listContent, 8, y, 760, 36, L("ui.story.meter") + ": " + pct + "%", 24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 40f;
                // #2471: the meter as a bar too (the achievement rows' look).
                float frac = s.ProgressTarget > 0 ? Mathf.Clamp01((float)s.Progress / s.ProgressTarget) : 0f;
                UiKit.AddImage(_listContent, 8, y, 744f, 10f, null, new Color(1f, 1f, 1f, 0.10f));
                if (frac > 0f)
                {
                    UiKit.AddImage(_listContent, 8, y, 744f * frac, 10f, null, UiKit.Cyan);
                }

                y += 20f;
                UiKit.AddText(_listContent, 8, y, 760, 28,
                    L("ui.story.fragments") + ": " + s.FragmentsFound + "   ·   " + L("ui.story.kills") + ": " + s.MachineKills + "   ·   " + L("ui.story.beats") + ": " + s.BeatsRevealed,
                    16, UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 40f;

                // #2471: pin the story's lead to the objective display — once the player's own story has started.
                var entry = Game?.StoryPackStates?.Packs.FirstOrDefault(p => string.Equals(p.Id, s.StoryId, System.StringComparison.OrdinalIgnoreCase));
                if (entry != null && entry.Started)
                {
                    y = JournalButtons(y, 0f, entry.Id, new List<JournalAction>
                    {
                        entry.Tracked
                            ? new JournalAction("ui.journal.tracked", StoryPackActions.Untrack)
                            : new JournalAction("ui.journal.track", StoryPackActions.Track),
                    });
                }

                // #2470: the player's story track. A current server always names the world's chapter; before the track
                // starts (VEGA begins after the basic training) the story is held for this player.
                var pack = Game.StoryPack;
                bool trackKnown = !string.IsNullOrEmpty(s.WorldChapter);
                bool started = !trackKnown || s.Started;

                // The ending, re-viewable (#1124): once the story is resolved, the resolution cinematic can
                // be replayed any time. The server re-sends it on request; the cinematic itself waits until
                // this menu is closed, so the button both asks for it and gets the menu out of the way. Not offered
                // before the player's own story has started — it would spoil what is waiting for them (#2470). #2497: on a
                // current server it replays the whole ending (the stand-down, then the ending), which "Watch again" below then
                // leaves out — the ending is listed once.
                endingButton = s.GuardianDefeated && started;
                if (endingButton)
                {
                    string storyId = s.StoryId;
                    var endingIds = BlocksBeyondTheStars.Client.Cutscenes.CutsceneCatalog.EndingIds(pack);
                    UiKit.AddButton(_listContent, 0, y, 780, 56, L("ui.story.view_ending"), () =>
                    {
                        Game?.Network?.SendRequestStoryResolution();
                        CutsceneDirector.Instance?.NoteReplayWaits(storyId, endingIds); // #2497: "Plays when you land" in flight
                        Hide();
                    });
                    y += 66f;
                }

                y = StoryTrackBlock(y, s, pack, started);

                // #2476: the chapter's story missions VEGA handed out — running (●) or done (✔), each with its steps (done ✔, on the
                // chip ●, ahead ○) and "Skip mission" under a running one. Sent once the player's story track started; an older
                // server sends none.
                var missions = JournalPackPage.MissionsOf(entry);
                if (missions.Count > 0 && entry != null)
                {
                    y = AllianceSection(L("ui.journal.missions"), y);
                    foreach (var mission in missions)
                    {
                        bool running = mission.State == StoryChapterStates.Active;
                        y = JournalRow(y, 8f, 760f, JournalPackPage.MissionTitle(mission, L, HasLoc), 18,
                            running ? UiKit.Cyan : UiKit.Ok, FontStyle.Bold);
                        foreach (var step in mission.Steps)
                        {
                            y = JournalRow(y, 32f, 736f, JournalPackPage.MissionStepLine(step, L, HasLoc), 17,
                                step.OnChip ? UiKit.Cyan : step.State == StoryChapterStates.Done ? UiKit.Ok : UiKit.CyanDim,
                                step.OnChip ? FontStyle.Bold : FontStyle.Normal);
                        }

                        y = JournalButtons(y, 32f, entry.Id, JournalPackPage.MissionActions(mission));
                    }

                    y += 8f;
                }

                // #2475: the current chapter's leads as tasks — followed (✔), on the chip now (●), open (○). The server sends them
                // only once the player's story track started; an older server sends none.
                var leads = JournalPackPage.LeadsOf(entry);
                if (leads.Count > 0)
                {
                    y = AllianceSection(L("ui.journal.leads"), y);
                    foreach (var lead in leads)
                    {
                        y = JournalRow(y, 8f, 760f, JournalPackPage.LeadLine(lead, L, HasLoc), 17,
                            lead.Current ? UiKit.Cyan : lead.Done ? UiKit.Ok : UiKit.CyanDim, lead.Current ? FontStyle.Bold : FontStyle.Normal);
                    }

                    y += 8f;
                }

                var chapters = BlocksBeyondTheStars.Client.Core.StoryChapterList.Build(pack, Game.StoryHeardBeats, s.Chapter, started, s.WorldChapter);
                if (chapters.Count > 0)
                {
                    // The chapter list (#2470): reached chapters titled with their heard beats, future ones "???".
                    y = AllianceSection(L("ui.story.chapters"), y);
                    foreach (var row in chapters)
                    {
                        var color = row.State == BlocksBeyondTheStars.Client.Core.StoryChapterRowState.Current ? UiKit.Cyan
                            : row.State == BlocksBeyondTheStars.Client.Core.StoryChapterRowState.Done ? UiKit.TextCol : UiKit.CyanDim;
                        UiKit.AddText(_listContent, 8, y, 760, 28, BlocksBeyondTheStars.Client.Core.StoryChapterList.Label(row, L), 18, color,
                            TextAnchor.MiddleLeft, row.State == BlocksBeyondTheStars.Client.Core.StoryChapterRowState.Current ? FontStyle.Bold : FontStyle.Normal);
                        y += 32f;
                        foreach (string key in row.BeatKeys)
                        {
                            y = StoryEntry(y, StoryText(key));
                        }
                    }
                }
                else
                {
                    y = AllianceSection(L("ui.story.beats"), y);
                    y = StoryEntries(y, Game.StoryLogBeats);
                }

                y += 10f;
                y = AllianceSection(L("ui.story.fragments"), y);
                if (Game.StoryLogFragments == null || Game.StoryLogFragments.Count == 0)
                {
                    y = StoryEmpty(y);
                }
                else
                {
                    foreach (var (cat, key) in Game.StoryLogFragments)
                    {
                        y = StoryReadableEntry(y, L("ui.reader.fragment"), IdLabel("lore.cat.", cat), key);
                    }
                }

                y += 10f;
                y = AllianceSection(L("ui.story.memories"), y);
                if (Game.StoryLogMemories == null || Game.StoryLogMemories.Count == 0)
                {
                    y = StoryEmpty(y);
                }
                else
                {
                    foreach (var key in Game.StoryLogMemories)
                    {
                        y = StoryReadableEntry(y, L("ui.reader.memory"), string.Empty, key);
                    }
                }

                // Field records (#1111): rune inscriptions, wreck logs, ruin notes — what the world itself
                // told this player. Re-readable like the fragments.
                y += 10f;
                y = AllianceSection(L("ui.story.lore"), y);
                if (Game.StoryLogLore == null || Game.StoryLogLore.Count == 0)
                {
                    y = StoryEmpty(y);
                }
                else
                {
                    foreach (var (site, key) in Game.StoryLogLore)
                    {
                        y = StoryReadableEntry(y, L("ui.lore.site." + site), string.Empty, key);
                    }
                }
            }
            else
            {
                UiKit.AddText(_listContent, 8, y, 760, 34, L("ui.story.off"), 20, UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 44f;
            }

            // Watch again (#2468): this story's cutscenes the player has seen and may replay — the prologue, chapter openers.
            // Each story's page lists its own scenes since #2471 (the tutorial's cards are on the tutorial's page); VEGA's tips
            // moved to "VEGA's notes".
            if (storyOn)
            {
                y = JournalReplaySection(y, s.StoryId, withoutEnding: endingButton);
            }

            return y + 12f;
        }

        /// <summary>The story-track block of the Story tab (#2470): before the player's track starts a hint and "Start the story
        /// now" (when the pack allows an early start — the server starts the track on <c>startPack</c>); the crew's chapter as a
        /// status line where the server says so (a player still waiting, a hosted or arcade newcomer in their personal chapter
        /// 1); from chapter 2 on "The story so far" on demand — the recap card, played on this client alone.</summary>
        private float StoryTrackBlock(float y, StoryStateMessage s, BlocksBeyondTheStars.Shared.Story.StoryDefinition pack, bool started)
        {
            if (pack == null)
            {
                return y;
            }

            if (!started)
            {
                var hint = UiKit.AddText(_listContent, 8, y, 760, 48, L("ui.story.start_now_hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                hint.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += Mathf.Max(30f, hint.preferredHeight + 8f);
                if (pack.Start != null && pack.Start.EarlyOptIn)
                {
                    string startId = pack.Id;
                    UiKit.AddButton(_listContent, 0, y, 780, 56, L("ui.story.start_now"), () =>
                    {
                        Game?.Network?.SendStoryPack(startId, StoryPackActions.StartPack);
                        Hide(); // the story's first moments play once the menu is out of the way
                    });
                    y += 66f;
                }
            }

            if (s.ShowCrewChapter)
            {
                string crew = BlocksBeyondTheStars.Client.Core.StoryChapterList.CrewLine(pack, s.WorldChapter, L);
                if (crew.Length > 0)
                {
                    UiKit.AddText(_listContent, 8, y, 760, 28, crew, 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
                    y += 34f;
                }
            }

            string recap = started ? BlocksBeyondTheStars.Client.Core.StoryChapterList.RecapArg(pack, s.Chapter) : string.Empty;
            if (recap.Length > 0 && CutsceneDirector.Instance != null)
            {
                string recapId = pack.Id;
                UiKit.AddButton(_listContent, 0, y, 780, 48, L("ui.story.so_far"), () =>
                {
                    CutsceneDirector.Instance?.PlayLocal(recapId, BlocksBeyondTheStars.Shared.Story.StoryCutsceneIds.Recap, recap);
                    Hide();
                });
                y += 58f;
            }

            return y;
        }

        private float StoryEntries(float y, System.Collections.Generic.List<string> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                return StoryEmpty(y);
            }

            foreach (var k in keys)
            {
                y = StoryEntry(y, StoryText(k));
            }

            return y;
        }

        /// <summary>A story-log text: the localized line, or a short "needs a newer version" note when this client has no
        /// text for the key (#2462, N10) — a server with a newer pack sends keys this client does not know, and the raw
        /// "[key]" used to land in the list.</summary>
        private string StoryText(string key)
            => Game?.Localizer != null && Game.Localizer.Has(key) ? L(key) : L("ui.story.entry_missing");

        private static readonly TextGenerator StoryMeasurer = new TextGenerator();

        private float StoryEntry(float y, string text, float width = 756f)
        {
            // Measured height instead of the old length/64 guess (which underestimated long German lines),
            // and wrap actually enabled — the UiKit default (Overflow) let a long entry run past the pane.
            string row = "• " + text;
            var settings = new TextGenerationSettings
            {
                font = UiKit.Font,
                fontSize = 17,
                fontStyle = FontStyle.Normal,
                richText = false,
                scaleFactor = 1f,
                lineSpacing = 1f,
                horizontalOverflow = HorizontalWrapMode.Wrap,
                verticalOverflow = VerticalWrapMode.Overflow,
                generationExtents = new Vector2(width, 0f),
                textAnchor = TextAnchor.UpperLeft,
                pivot = new Vector2(0f, 1f),
                color = Color.white,
            };
            float h = 8f + StoryMeasurer.GetPreferredHeight(row, settings);
            var t = UiKit.AddText(_listContent, 12, y, width, h, row, 17, UiKit.TextCol, TextAnchor.UpperLeft);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return y + h + 6f;
        }

        /// <summary>A story-log row with a Read button (#1110): the bullet text as before, plus the reader —
        /// long archive texts get a real page instead of squinting at a list row.</summary>
        private float StoryReadableEntry(float y, string title, string label, string textKey)
        {
            string body = StoryText(textKey); // never a raw "[key]" (#2462, N10)
            string text = string.IsNullOrEmpty(label) ? body : "[" + label + "] " + body;
            float rowTop = y;
            y = StoryEntry(y, text, 660f); // keep clear of the Read button's lane

            UiKit.AddButton(_listContent, 688, rowTop, 92, 30, L("ui.story.read"), () =>
            {
                StoryReaderUi.Instance?.OpenOverMenu(title, label, body);
            });
            return y;
        }

        private float StoryEmpty(float y)
        {
            UiKit.AddText(_listContent, 12, y, 756, 28, L("ui.story.none"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
            return y + 34f;
        }
    }
}
