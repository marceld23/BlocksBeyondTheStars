// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPC dialogues (#1127) — the long-reserved "item 15" Dialog backend — and the story pack's authored
/// recurring characters (#1128). Press E near an NPC (outside any station block's reach) to talk: the
/// server picks the best matching dialogue (an authored character's own first, then role dialogues),
/// walks the node graph fully server-authoritatively, records the dormant <see cref="NpcInteractionKind.Dialog"/>
/// interaction, persists the choice as a milestone and applies the choice's consequence (standing bump,
/// story fragment, small gift, or a later radio call). All text goes out RESOLVED per player — dialogue
/// content is stage-gated and once-flagged, so it must never touch the shared greeting cache. Works fully
/// with <c>AiLevel.Off</c>; a future LLM hook may rephrase prose but never decides a branch.
/// </summary>
public sealed partial class GameServer
{
    private const float NpcTalkRange = 5f;

    private const string DialogDoneMilestonePrefix = "dialog:";      // "dialog:<key>:done" — once-per-player gate
    private const string DialogFlagMilestonePrefix = "dialogflag:";  // "dialogflag:<key>:<node>:<choice>" — the persisted decision

    /// <summary>The dialogue each player is currently in (npc, definition, node index). Runtime-only —
    /// walking away simply abandons it; the once-flag is written when the dialogue ENDS.</summary>
    private readonly Dictionary<string, (int NpcId, DialogDefinition Dialog, int Node)> _dialogSessions = new();

    /// <summary>"Later radio call" consequences waiting to fire (runtime-only — a restart drops the
    /// courtesy call, nothing of value is lost).</summary>
    private readonly List<(string PlayerId, string NpcKey, string NpcName, string Place, string BodyId, string LineKey, double Due)> _dialogRadioPending = new();

    private const double DialogRadioDelaySeconds = 90.0;

    /// <summary>Engine dialogues plus the active story pack's own (authored characters). Order matters:
    /// pack dialogues come FIRST so a character's authored dialogue wins over any generic role match.</summary>
    private IEnumerable<DialogDefinition> AllDialogs()
    {
        if (StoryActive && _story is not null)
        {
            foreach (var d in _story.Dialogs)
            {
                yield return d;
            }
        }

        foreach (var d in _content.Dialogs)
        {
            yield return d;
        }
    }

    /// <summary>The relationship-memory key for dialogue purposes: an authored character remembers the
    /// player GLOBALLY ("char:&lt;id&gt;" — the whole point of a recurring face), everyone else keeps the
    /// per-place role key.</summary>
    private string DialogNpcKey(PlayerSession session, ServerNpc npc)
        => !string.IsNullOrEmpty(npc.CharacterId) ? "char:" + npc.CharacterId : NpcKeyForNpc(session, npc);

    /// <summary>Best dialogue for this NPC + player right now, or null (→ the ordinary greeting bubble).
    /// An authored character offers their own dialogue first; once that is done they fall back to the
    /// generic dialogues of the role they occupy.</summary>
    private DialogDefinition? PickDialog(PlayerSession session, ServerNpc npc, string npcKey)
    {
        var p = session.State;
        int rel = p.NpcMemory.TryGetValue(npcKey, out var r) ? r.Value : 0;
        int stage = StageRank(RelationshipTier(rel));

        foreach (var d in AllDialogs())
        {
            bool matches = !string.IsNullOrEmpty(d.Character)
                ? string.Equals(d.Character, npc.CharacterId, StringComparison.OrdinalIgnoreCase)
                : string.IsNullOrEmpty(d.Role) || string.Equals(d.Role, npc.Role, StringComparison.OrdinalIgnoreCase);
            if (!matches
                || d.Nodes.Count == 0
                || StageRank(d.MinStage) > stage
                || (d.OncePerPlayer && p.Milestones.Contains(DialogDoneMilestonePrefix + d.Key + ":done")))
            {
                continue;
            }

            return d;
        }

        return null;
    }

    /// <summary>The player talks to an NPC (E in reach). A matching dialogue opens the panel; none →
    /// the ordinary greeting bubble, which makes settlers greet-able for the first time too.</summary>
    private void HandleTalkToNpc(PlayerSession session, TalkToNpcIntent intent)
    {
        var npc = _npcs.FirstOrDefault(n => n.Id == intent.NpcId);
        if (npc is null || npc.Pos.DistanceSquared(session.State.Position) > NpcTalkRange * NpcTalkRange)
        {
            return;
        }

        string npcKey = DialogNpcKey(session, npc);
        var dialog = PickDialog(session, npc, npcKey);
        if (dialog is null)
        {
            EmitGreeting(session, npc); // nothing to decide — the familiar over-head line
            return;
        }

        _dialogSessions[session.State.PlayerId] = (npc.Id, dialog, 0);
        var node = dialog.Nodes[0];
        Send(session, new NpcDialogState
        {
            NpcId = npc.Id,
            Name = npc.Name,
            Text = Localize(session.Locale, node.PromptKey),
            Choices = node.Choices.Select(c => Localize(session.Locale, c.TextKey)).ToArray(),
            End = node.Choices.Count == 0,
        });
    }

    /// <summary>The player picks a reply. The server owns the walk: validate against ITS node, record the
    /// (finally producing) Dialog interaction, persist the decision, apply the consequence, then answer
    /// with the response — plus the follow-up node's prompt when the dialogue continues.</summary>
    private void HandleNpcDialogChoice(PlayerSession session, NpcDialogChoiceIntent intent)
    {
        string playerId = session.State.PlayerId;
        if (!_dialogSessions.TryGetValue(playerId, out var active))
        {
            return;
        }

        var npc = _npcs.FirstOrDefault(n => n.Id == active.NpcId);
        var node = active.Node >= 0 && active.Node < active.Dialog.Nodes.Count ? active.Dialog.Nodes[active.Node] : null;
        if (npc is null || node is null || intent.ChoiceIndex < 0 || intent.ChoiceIndex >= node.Choices.Count)
        {
            _dialogSessions.Remove(playerId);
            return;
        }

        var choice = node.Choices[intent.ChoiceIndex];
        string npcKey = DialogNpcKey(session, npc);
        var p = session.State;

        RecordNpcInteraction(p, npcKey, npc.Name, npc.Role, NpcInteractionKind.Dialog, NpcPlaceFor(p));
        p.Milestones.Add(DialogFlagMilestonePrefix + active.Dialog.Key + ":" + active.Node + ":" + intent.ChoiceIndex);
        ApplyDialogConsequence(session, npc, npcKey, choice.Consequence);

        bool end = choice.Next < 0 || choice.Next >= active.Dialog.Nodes.Count;
        if (end && active.Dialog.OncePerPlayer && !choice.KeepOpen)
        {
            p.Milestones.Add(DialogDoneMilestonePrefix + active.Dialog.Key + ":done");
        }

        _repo.SavePlayer(p);
        SendNpcStandings(session); // the Dialog interaction (and a standing consequence) may have moved the tier

        if (end)
        {
            _dialogSessions.Remove(playerId);
            Send(session, new NpcDialogState
            {
                NpcId = npc.Id,
                Name = npc.Name,
                Text = Localize(session.Locale, choice.ResponseKey),
                End = true,
            });
            return;
        }

        _dialogSessions[playerId] = (active.NpcId, active.Dialog, choice.Next);
        var next = active.Dialog.Nodes[choice.Next];
        Send(session, new NpcDialogState
        {
            NpcId = npc.Id,
            Name = npc.Name,
            Text = Localize(session.Locale, choice.ResponseKey) + "\n\n" + Localize(session.Locale, next.PromptKey),
            Choices = next.Choices.Select(c => Localize(session.Locale, c.TextKey)).ToArray(),
            End = next.Choices.Count == 0,
        });
    }

    /// <summary>Applies a choice's consequence. Unknown/malformed strings are ignored — a data typo must
    /// never break the conversation itself.</summary>
    private void ApplyDialogConsequence(PlayerSession session, ServerNpc npc, string npcKey, string consequence)
    {
        if (string.IsNullOrEmpty(consequence))
        {
            return;
        }

        var parts = consequence.Split(':');
        switch (parts[0])
        {
            case "standing" when parts.Length >= 2 && int.TryParse(parts[1], out int bump):
                if (session.State.NpcMemory.TryGetValue(npcKey, out var rel))
                {
                    rel.Value = Math.Clamp(rel.Value + bump, -100, 100);
                }

                break;

            case "fragment" when parts.Length >= 2 && StoryActive && _story is not null:
                if (_story.Fragments.FirstOrDefault(f => f.Key == parts[1]) is { } frag)
                {
                    Send(session, new NetFragmentRevealed { Category = frag.Category, TextKey = frag.TextKey });
                    RecordStoryFragment(frag.Key); // shared-arc dedupe — a re-gifted fragment only re-reads
                }

                break;

            case "gift" when parts.Length >= 3 && int.TryParse(parts[2], out int count) && count > 0
                && _content.GetItem(parts[1]) is not null:
                Serve(session);
                var pool = new MaterialPool(_content, session.State, _ship);
                pool.Add(parts[1], count);
                SendInventory(session);
                break;

            case "radio" when parts.Length >= 2:
                // "I'll call you" — and a little later, they do (through every normal radio gate).
                _dialogRadioPending.Add((session.State.PlayerId, npcKey, npc.Name, NpcPlaceFor(session.State),
                    session.CurrentLocationId, parts[1], _uptime + DialogRadioDelaySeconds));
                break;
        }
    }

    /// <summary>Fires due "later radio call" consequences (piggybacks on the greeting tick's cadence).</summary>
    private void TickDialogRadio()
    {
        if (_dialogRadioPending.Count == 0)
        {
            return;
        }

        for (int i = _dialogRadioPending.Count - 1; i >= 0; i--)
        {
            var pending = _dialogRadioPending[i];
            if (_uptime < pending.Due)
            {
                continue;
            }

            _dialogRadioPending.RemoveAt(i);
            if (FindSessionByPlayerId(pending.PlayerId) is { Joined: true } owner)
            {
                TryNpcRadioCall(owner, pending.NpcKey, pending.NpcName, pending.Place, pending.BodyId,
                    "dialog:" + pending.LineKey, pending.LineKey, string.Empty, isMission: false);
            }
        }
    }

    /// <summary>Lets a story pack's authored character (#1128) claim a freshly spawned NPC: a deterministic
    /// hash over (seed, place, character) picks roughly one in <see cref="StoryCharacter.OneIn"/> eligible
    /// slots, so the same save always meets them at the same spots. The claim fixes name, face, body and
    /// outfit — the same person everywhere — and never touches quartermasters (their coined name is baked
    /// into their board's mission texts).</summary>
    private void ApplyAuthoredCharacter(ServerNpc npc, string placeKind, string placeKey)
    {
        if (!StoryActive || _story is null)
        {
            return;
        }

        foreach (var ch in _story.Characters)
        {
            if (string.IsNullOrEmpty(ch.Id) || string.IsNullOrEmpty(ch.Name)
                || npc.Role == "quartermaster"
                || !ch.Roles.Contains(npc.Role)
                || (!string.IsNullOrEmpty(ch.Places) && ch.Places != placeKind))
            {
                continue;
            }

            uint h = (uint)(_meta.Seed ^ WorldGenerator.StableHash("char:" + ch.Id + "|" + placeKey));
            if (h % (uint)Math.Max(1, ch.OneIn) != 0)
            {
                continue;
            }

            var look = new Random(unchecked((int)WorldGenerator.StableHash("charlook:" + ch.Id)));
            npc.CharacterId = ch.Id;
            npc.Name = ch.Name;
            npc.IsRobot = ch.Robot;
            npc.Size = 1.0f;
            npc.SkinRgb = ch.Robot
                ? new uint[] { 0xBFC7CF, 0xD5DBE1, 0xA8B2BC, 0xC9CCB8 }[look.Next(4)]
                : new uint[] { 0xFFE0C4, 0xD9A066, 0xA9713A, 0x8D5524 }[look.Next(4)];
            npc.OutfitRgb = new uint[] { 0x3D7EBF, 0x8A63BF, 0x2FA48E, 0xC24B5A, 0xB3A05C, 0x6B8FA3 }[look.Next(6)];
            npc.LegsRgb = new uint[] { 0x4A4E57, 0x5C5346, 0x3E4A5C, 0x6B5C4A }[look.Next(4)];
            return; // first matching character wins — at most one face per slot
        }
    }

    /// <summary>The stable face seed an authored character carries onto the wire (0 = not authored).</summary>
    private static int CharacterFaceVariant(ServerNpc npc)
        => string.IsNullOrEmpty(npc.CharacterId) ? 0 : unchecked((int)WorldGenerator.StableHash("charface:" + npc.CharacterId));

    // ---------------- Test hooks ----------------

    /// <summary>Test seam: talks to the given NPC as the player (mirrors <see cref="TalkToNpcIntent"/>).</summary>
    public void TalkToNpcForTest(string playerId, int npcId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleTalkToNpc(s, new TalkToNpcIntent { NpcId = npcId });
        }
    }

    /// <summary>Test seam: picks a reply in the player's active dialogue.</summary>
    public void ChooseDialogForTest(string playerId, int choiceIndex)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleNpcDialogChoice(s, new NpcDialogChoiceIntent { ChoiceIndex = choiceIndex });
        }
    }

    /// <summary>Test/inspection: the key + node of the player's active dialogue, or null.</summary>
    public (string Key, int Node)? ActiveDialogForTest(string playerId)
        => _dialogSessions.TryGetValue(playerId, out var s) ? (s.Dialog.Key, s.Node) : null;

    /// <summary>Test/inspection: NPC ids + names + roles + character ids on the active world.</summary>
    public IReadOnlyList<(int Id, string Name, string Role, string CharacterId)> NpcRosterForTest()
        => _npcs.Select(n => (n.Id, n.Name, n.Role, n.CharacterId)).ToList();

    /// <summary>Test seam: runs the due-radio drain immediately.</summary>
    public void TickDialogRadioForTest() => TickDialogRadio();
}
