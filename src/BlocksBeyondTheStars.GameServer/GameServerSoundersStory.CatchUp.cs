// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3425: a skip never strands a player in the Sounders' Song. A kid skipped chapters 1 and 2 and stood at "decode the first
/// Song Disc" without the spindle's first disc — chapter 2's reward is handed out only when the shrine's echo game is solved,
/// and the note table stayed empty because only decoding fills it. So, once a step (or a whole chapter) of the pack was
/// SKIPPED, the server hands over what playing it would have left with the player — once, guarded by the milestones the
/// normal play writes (a player who played it has them and never gets a second one):
/// <list type="bullet">
/// <item>chapter 2 (the shrine): the Song Spindle (unless one is carried) and disc 1 — <c>snd:shrine_reward</c>;</item>
/// <item>s3.2 / s3.4 / s4.5 (decode a disc): the disc counts as decoded (<c>snd:disc:&lt;disc&gt;</c>) — its letters fill the
/// note table, its Song Archive page opens, the hidden blueprint it carries shows up in research and the story loot moves
/// on to the next disc, exactly as a decode would; s3.4 also hands over the builders and the hearing disc (the hearing disc
/// is the chest socket's disc chapters 4 and 5 need), s4.4 (open a sound chamber) the Great Song disc — a story disc only
/// when the player neither decoded it nor has it at hand (<see cref="NextMissingDisc"/>), and once
/// (<c>snd:catchup:&lt;disc&gt;</c>: a disc sitting in a spindle is not at hand, yet must not come again);</item>
/// <item>s4.2 (research): the Resonance Blaster and obsidian armour blueprints; s4.3 / s4.6 (craft): one Resonance Blaster /
/// obsidian chest piece, unless one is carried (<c>snd:catchup:&lt;item&gt;</c> makes it once);</item>
/// <item>chapter 6: the Song of Return is learned (<c>snd:song_learned</c>, Ornemis on the star map); s8.1: Roll is convinced;
/// s8.2: the Bridge Song counts as sung (the sing duel can start).</item>
/// </list>
/// Only while the story still runs — a chapter of it is ahead — so "Skip all" hands out nothing, and the sing duel's Soundi
/// (the last step) stays the prize of playing it. VEGA says what was handed over (<c>srv.sounders.catch_up_items</c>) or that
/// the gaps were filled (<c>srv.sounders.catch_up</c>). Runs in the 1 Hz story-pack pass (<see cref="TickStoryPacksFor"/>), so
/// an old save that is already stuck heals on its next join as well.
/// <para>A full backpack never loses a handed item (Justus 2026-11-15: skipped s3.4 with a full pack, "now I don't have the
/// item"): what a skip hands over goes into the backpack only — VEGA's line says she packed it — and while the backpack has no
/// room for it, it waits. Nothing is written for it (its once-milestone, and the decode of a disc still to come: a decoded disc
/// is never "found" again), VEGA asks for room (<c>srv.sounders.catch_up_room</c>, at most once a minute) and the next pass
/// packs it. A catch-up is no event in the world — nothing to drop where it happened, unlike a mined block — so it never goes
/// to the hold or the ground, where a child never found it.</para>
/// <para>And a story item a later step needs is never lost for good (<see cref="SoundersNeeds"/>): the spindle and disc 1 of
/// chapter 3, and the hearing disc the chest's socket (s4.7) and the Echoes (chapter 5) need, come again, into the backpack,
/// while that step runs and the player has none at hand (<c>srv.sounders.need_item</c>) — however it went (a skip's hand-over
/// or the shrine's reward lying on the ground, a crate, a friend). A spare is harmless.</para>
/// <para>And a disc that is no story disc (a socket disc, a collectible) sung on a spindle while a decode step runs says it only
/// sings a tune and names the disc the story waits for (<see cref="HintStoryDisc"/>).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Prefix of the catch-up's own once-milestones (a disc or a crafted piece handed over for a skipped step).</summary>
    internal const string SoundersCatchUpPrefix = "snd:catchup:";

    private enum SoundersCatchUpKind
    {
        /// <summary>Chapter 2's reward: the Song Spindle and disc 1.</summary>
        ShrineReward,

        /// <summary>A story disc the step would have found (unless decoded or at hand).</summary>
        FindDisc,

        /// <summary>A story disc the step would have decoded.</summary>
        DecodeDisc,

        /// <summary>A blueprint the step would have researched.</summary>
        Blueprint,

        /// <summary>An item the step would have crafted (once, unless one is carried).</summary>
        Craft,

        /// <summary>A pack milestone the step would have written.</summary>
        Milestone,
    }

    /// <summary>What each skipped step (a chapter id: the whole chapter, for steps with stand-ins) would have left with the
    /// player, in story order — a disc is found before it is decoded.</summary>
    private static readonly (string Producer, SoundersCatchUpKind Kind, string Key)[] SoundersCatchUps =
    {
        ("s2", SoundersCatchUpKind.ShrineReward, string.Empty),
        ("s3.2", SoundersCatchUpKind.DecodeDisc, SoundersIds.DiscBeginning),
        ("s3.4", SoundersCatchUpKind.FindDisc, SoundersIds.DiscBuilders),
        ("s3.4", SoundersCatchUpKind.FindDisc, SoundersIds.DiscHearing),
        ("s3.4", SoundersCatchUpKind.DecodeDisc, SoundersIds.DiscBuilders),
        ("s3.4", SoundersCatchUpKind.DecodeDisc, SoundersIds.DiscHearing),
        ("s4.2", SoundersCatchUpKind.Blueprint, SoundersIds.BlueprintResonance),
        ("s4.2", SoundersCatchUpKind.Blueprint, SoundersIds.BlueprintObsidian),
        ("s4.3", SoundersCatchUpKind.Craft, SoundersIds.ResonanceBlaster),
        ("s4.4", SoundersCatchUpKind.FindDisc, SoundersIds.DiscGreatSong),
        ("s4.5", SoundersCatchUpKind.DecodeDisc, SoundersIds.DiscGreatSong),
        ("s4.6", SoundersCatchUpKind.Craft, SoundersIds.ObsidianChest),
        ("s6", SoundersCatchUpKind.Milestone, SoundersIds.MilestoneSongLearned),
        ("s8.1", SoundersCatchUpKind.Milestone, SoundersRollConvincedMilestone),
        ("s8.2", SoundersCatchUpKind.Milestone, SoundersBridgeDoneMilestone),
    };

    /// <summary>What a later step needs at hand, while it runs (a step id: while the step is active; a chapter id: while the
    /// chapter runs) — handed again when the player has none (see the type's summary). Only items nothing else gives again:
    /// the spindle is no recipe before disc 1 is decoded, the relic caches never hand out disc 1, and the hearing disc is
    /// decoded by then (the story loot never hands a decoded disc out twice). The Great Song disc is not here: the next sound
    /// chamber brings it again while it is not decoded.</summary>
    private static readonly (string Needer, string Item)[] SoundersNeeds =
    {
        ("s3.1", SoundersIds.SongSpindle), // to place it — the shrine's reward may lie at the shrine (a full backpack)
        ("s3.2", SoundersIds.DiscBeginning), // to decode it
        ("s4.7", SoundersIds.DiscHearing), // into the obsidian chest's socket
        ("s5", SoundersIds.DiscHearing), // the Echoes come only to a wearer of the hearing disc
    };

    /// <summary>Seconds between two "make room for it" lines while handed items wait for room in the backpack.</summary>
    private const double SoundersRoomLineSeconds = 60.0;

    /// <summary>When each player may hear the "make room" line again (RAM only: after a restart VEGA simply asks again).</summary>
    private readonly Dictionary<string, double> _soundersRoomLineAt = new(StringComparer.Ordinal);

    /// <summary>One catch-up pass: the names of what went into the backpack or the research list (for a skip, and handed again
    /// because a step needs it), and what waits for room — by name for VEGA's line, by key so its decode waits as well.</summary>
    private sealed class SoundersCatchUpPass
    {
        public readonly List<string> Handed = new();
        public readonly List<string> Supplied = new();
        public readonly List<string> Waiting = new();
        public readonly HashSet<string> WaitingKeys = new(StringComparer.Ordinal);
    }

    /// <summary>The catch-up (see the type's summary): hands over, once, what skipped Sounders steps would have given, and again
    /// what a running step needs and the player no longer has.</summary>
    private void CatchUpSoundersSkips(PlayerSession s)
    {
        if (!s.Joined || s.Spectating || !IsStoryPackActive(s, SoundersIds.PackId) || !SoundersStoryRuns(s))
        {
            return;
        }

        var pass = new SoundersCatchUpPass();
        bool caughtUp = false;
        foreach (var (producer, kind, key) in SoundersCatchUps)
        {
            if (SoundersProducerSkipped(s.State.Milestones, producer))
            {
                caughtUp |= ApplySoundersCatchUp(s, kind, key, pass);
            }
        }

        bool supplied = SupplySoundersNeeds(s, pass);
        AskRoomForSoundersItems(s, pass);
        if (!caughtUp && !supplied)
        {
            return;
        }

        MarkJournalDirty(s);
        _repo.SavePlayer(s.State);
        SendInventory(s); // the backpack and the research list
        if (caughtUp)
        {
            Send(s, new ServerMessage
            {
                Text = pass.Handed.Count > 0 ? "@srv.sounders.catch_up_items:" + string.Join(", ", pass.Handed) : "@srv.sounders.catch_up",
            });
        }

        if (supplied)
        {
            Send(s, new ServerMessage { Text = "@srv.sounders.need_item:" + string.Join(", ", pass.Supplied) });
        }
    }

    /// <summary>Whether a chapter of the Sounders' Song still runs for the player (one is ahead of them).</summary>
    private bool SoundersStoryRuns(PlayerSession s)
    {
        foreach (string chapter in SoundersChapters)
        {
            if (InSoundersChapter(s, chapter))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="producer"/> was passed without being played: a chapter id (s2, s6 — their steps have
    /// stand-ins, so a skipped step alone says nothing) once the chapter is through, its outcome guarding the rest; a step id
    /// once it is skipped and not done.</summary>
    private static bool SoundersProducerSkipped(HashSet<string> milestones, string producer)
        => producer.IndexOf('.') < 0
            ? SoundersChapterThrough(milestones, producer)
            : milestones.Contains(StoryPackKeys.StepSkipped(SoundersIds.PackId, producer))
              && !milestones.Contains(StoryPackKeys.StepDone(SoundersIds.PackId, producer));

    /// <summary>One catch-up entry; true when it changed the player. Names of handed items and blueprints go to the pass; an
    /// item without room in the backpack waits there and the entry writes nothing (the next pass tries again).</summary>
    private bool ApplySoundersCatchUp(PlayerSession s, SoundersCatchUpKind kind, string key, SoundersCatchUpPass pass)
    {
        var p = s.State;
        switch (kind)
        {
            case SoundersCatchUpKind.ShrineReward:
                return CatchUpShrineReward(s, pass);
            case SoundersCatchUpKind.FindDisc:
                if (!CatchUpDiscDue(p, key) || !HandSoundersItems(s, pass, pass.Handed, key))
                {
                    return false;
                }

                p.Milestones.Add(SoundersCatchUpPrefix + key);
                return true;
            case SoundersCatchUpKind.DecodeDisc:
                // Not while the disc waits for room: a decoded disc counts as had, and would never be handed over then.
                return !pass.WaitingKeys.Contains(key) && p.Milestones.Add(SoundersIds.DiscMilestone(key));
            case SoundersCatchUpKind.Blueprint:
                if (_content.GetBlueprint(key) is not { } bp || !p.UnlockedBlueprints.Add(key))
                {
                    return false;
                }

                pass.Handed.Add(LocalizedName(s.Locale, bp.NameKey, key));
                return true;
            case SoundersCatchUpKind.Craft:
                if (p.Milestones.Contains(SoundersCatchUpPrefix + key)
                    || (!CarriesSounderItem(p, key) && !HandSoundersItems(s, pass, pass.Handed, key)))
                {
                    return false;
                }

                p.Milestones.Add(SoundersCatchUpPrefix + key);
                return true;
            default:
                return p.Milestones.Add(key);
        }
    }

    /// <summary>Chapter 2's reward for a skipper: the Song Spindle (unless one is carried) and disc 1 — both, or both wait.</summary>
    private bool CatchUpShrineReward(PlayerSession s, SoundersCatchUpPass pass)
    {
        var p = s.State;
        if (p.Milestones.Contains(SoundersShrineRewardMilestone))
        {
            return false;
        }

        var items = new List<string>();
        if (!CarriesSounderItem(p, SoundersIds.SongSpindle))
        {
            items.Add(SoundersIds.SongSpindle);
        }

        bool disc = CatchUpDiscDue(p, SoundersIds.DiscBeginning);
        if (disc)
        {
            items.Add(SoundersIds.DiscBeginning);
        }

        if (!HandSoundersItems(s, pass, pass.Handed, items.ToArray()))
        {
            return false;
        }

        p.Milestones.Add(SoundersShrineRewardMilestone);
        if (disc)
        {
            p.Milestones.Add(SoundersCatchUpPrefix + SoundersIds.DiscBeginning);
        }

        return true;
    }

    /// <summary>Whether a skipped step still owes the player <paramref name="disc"/>: not handed by a catch-up before (once — a
    /// disc sitting in a spindle is not "at hand" and must not come again every second), not decoded and not at hand.</summary>
    private bool CatchUpDiscDue(Shared.State.PlayerState p, string disc)
        => !p.Milestones.Contains(SoundersCatchUpPrefix + disc) && NextMissingDisc(p, new[] { disc }) is not null;

    /// <summary>
    /// One of each of <paramref name="keys"/> into the backpack — all of them, or none: when the backpack has no room for all,
    /// they wait (named in the pass, their keys hold their decode back) and false comes back, so the caller writes nothing.
    /// Never the hold or the ground (see the type's summary). The names of what was packed go to <paramref name="names"/>.
    /// </summary>
    private bool HandSoundersItems(PlayerSession s, SoundersCatchUpPass pass, List<string> names, params string[] keys)
    {
        var known = keys.Where(k => _content.GetItem(k) is not null).ToList();
        var trial = s.State.Inventory.Clone();
        if (known.Any(k => trial.Add(k, 1, _content.MaxStackOf(k)) > 0))
        {
            foreach (string k in known)
            {
                if (pass.WaitingKeys.Add(k))
                {
                    pass.Waiting.Add(SounderItemName(s, k));
                }
            }

            return false;
        }

        foreach (string k in known)
        {
            GiveSoundersItem(s, k, 1); // fits the backpack (the trial above), which the pool fills first
            names.Add(SounderItemName(s, k));
        }

        return true;
    }

    private string SounderItemName(PlayerSession s, string itemKey)
        => LocalizedName(s.Locale, _content.GetItem(itemKey)?.NameKey, itemKey);

    /// <summary>Hands again what a running step needs (<see cref="SoundersNeeds"/>) when the player has none at hand; true when
    /// something was packed.</summary>
    private bool SupplySoundersNeeds(PlayerSession s, SoundersCatchUpPass pass)
    {
        bool supplied = false;
        foreach (var (needer, item) in SoundersNeeds)
        {
            if (!pass.WaitingKeys.Contains(item) && SoundersNeedRuns(s, needer) && !SounderItemAtHand(s.State, item))
            {
                supplied |= HandSoundersItems(s, pass, pass.Supplied, item);
            }
        }

        return supplied;
    }

    /// <summary>Whether <paramref name="needer"/> runs: a step id while that step is active, a chapter id while the chapter runs.</summary>
    private bool SoundersNeedRuns(PlayerSession s, string needer)
    {
        if (needer.IndexOf('.') < 0)
        {
            return InSoundersChapter(s, needer);
        }

        return SoundersPack is { } pack
               && pack.Chapters.SelectMany(c => c.Steps).FirstOrDefault(st => st.Id == needer) is { } step
               && StepActive(s, step);
    }

    /// <summary>Whether the player has <paramref name="item"/> at hand: in the backpack, worn (a disc in the socket) or sitting
    /// in a Song Spindle of this world (where a disc is decoded — taking it out is the player's move, no spare needed).</summary>
    private bool SounderItemAtHand(Shared.State.PlayerState p, string item)
        => CarriesSounderItem(p, item)
           || _containers.Any(c => c.Kind == SpindleContainerKind && c.Items.Any(i => string.Equals(i.Item, item, StringComparison.Ordinal)));

    /// <summary>VEGA asks for room while handed items wait for it — at once, then at most once a minute.</summary>
    private void AskRoomForSoundersItems(PlayerSession s, SoundersCatchUpPass pass)
    {
        string pid = s.State.PlayerId;
        if (pass.Waiting.Count == 0)
        {
            _soundersRoomLineAt.Remove(pid); // the next wait asks at once
            return;
        }

        if (_soundersRoomLineAt.TryGetValue(pid, out double at) && _uptime < at)
        {
            return;
        }

        _soundersRoomLineAt[pid] = _uptime + SoundersRoomLineSeconds;
        Send(s, new ServerMessage { Text = "@srv.sounders.catch_up_room:" + string.Join(", ", pass.Waiting) });
    }

    // ---------------- A tune instead of the story's disc ----------------

    /// <summary>
    /// A Song Spindle sang <paramref name="disc"/> for this player without opening the note-key minigame: when it is a disc
    /// that is no story disc (a socket disc, a collectible) and a decode step of the Sounders' Song runs, the player learns it
    /// only sings a tune — and which disc the story waits for (<c>srv.sounders.wrong_disc</c> when they carry it,
    /// <c>srv.sounders.wrong_disc_find</c> when it is still to be found).
    /// </summary>
    private void HintStoryDisc(PlayerSession session, string disc)
    {
        if (!SongDiscs.IsDisc(disc) || SongDiscs.IsStoryDisc(disc) || WantedStoryDisc(session) is not { } wanted)
        {
            return;
        }

        string key = CarriesSounderItem(session.State, wanted) ? "@srv.sounders.wrong_disc:" : "@srv.sounders.wrong_disc_find:";
        Send(session, new ServerMessage { Text = key + LocalizedName(session.Locale, _content.GetItem(wanted)?.NameKey, wanted) });
    }

    /// <summary>The story disc a running decode step (<c>disc:decoded</c>) of the player's Sounders pack waits for — one they
    /// carry first — or null when no decode step runs.</summary>
    internal string? WantedStoryDisc(PlayerSession s)
    {
        if (!SoundersActive(s) || SoundersPack is not { } pack)
        {
            return null;
        }

        string? wanted = null;
        foreach (var chapter in pack.Chapters)
        {
            foreach (var step in chapter.Steps)
            {
                if (!string.Equals(step.Event, GameEventKeys.DiscDecoded, StringComparison.Ordinal) || !StepActive(s, step))
                {
                    continue;
                }

                foreach (string disc in step.Args)
                {
                    if (s.State.Milestones.Contains(SoundersIds.DiscMilestone(disc)))
                    {
                        continue;
                    }

                    if (CarriesSounderItem(s.State, disc))
                    {
                        return disc;
                    }

                    wanted ??= disc;
                }
            }
        }

        return wanted;
    }

    /// <summary>Test seam: the catch-up for a player now (it runs in the 1 Hz story-pack pass).</summary>
    internal void CatchUpSoundersSkipsForTest(PlayerSession s) => CatchUpSoundersSkips(s);
}
