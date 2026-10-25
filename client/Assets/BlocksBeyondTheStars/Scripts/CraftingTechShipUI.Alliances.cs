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
    /// The Alliances tab of <see cref="CraftingTechShipUI"/>: alliances and allies, finding, muting and reporting
    /// players, the crew (#1216) and the radio (Funk) chat with its live log.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        // Alliances tab: the radio (Funk) input draft + the live scrollback Text (refreshed each frame so new
        // messages appear without rebuilding the pane, which would drop the input's focus while typing).
        private string _funkDraft = string.Empty;
        private string _crewNameDraft = string.Empty; // crew create/rename input (#1216)
        private string _crewStamp = string.Empty;     // change detector so an open Crew view refreshes itself
        private Text _funkLog;

        // --- Alliances tab (player alliances + radio/Funk chat) ---

        private float BuildAlliancesList()
        {
            _funkLog = null; // only the Funk view owns the live scrollback Text
            switch (_category)
            {
                case "crew": return BuildCrewList();
                case "find": return BuildAllianceFindList();
                case "funk": return BuildFunkLog();
                default: return BuildAllyList();
            }
        }

        /// <summary>The roster view: incoming requests (accept/decline), current allies (end), and outgoing requests.</summary>
        private float BuildAllyList()
        {
            var a = Game.Alliances ?? new AllianceList();
            float y = BuildDuelSection(0f); // #2972: a challenge, the running duel, or whom to challenge

            if (a.Incoming != null && a.Incoming.Length > 0)
            {
                y = AllianceSection(L("ui.alliance.incoming"), y);
                foreach (var r in a.Incoming)
                {
                    string id = r.PlayerId;
                    UiKit.AddText(_listContent, 8, y + 8, 420, 40, AllianceName(r.PlayerName, r.PlayerId), 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    var acc = UiKit.AddButton(_listContent, 440, y + 6, 150, 44, L("ui.alliance.accept"), () => Game.Network?.SendAllianceResponse(id, true));
                    acc.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
                    UiKit.AddButton(_listContent, 600, y + 6, 150, 44, L("ui.alliance.decline"), () => Game.Network?.SendAllianceResponse(id, false));
                    y += 58f;
                }

                y += 10f;
            }

            y = AllianceSection(L("ui.alliance.cat_allies"), y);
            if (a.Allies == null || a.Allies.Length == 0)
            {
                UiKit.AddText(_listContent, 8, y, 760, 30, L("ui.alliance.none"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 40f;
            }
            else
            {
                foreach (var al in a.Allies)
                {
                    string id = al.PartnerId;
                    string dot = al.Online ? "<color=#52E0A0>●</color> " : "<color=#6A7480>●</color> ";
                    UiKit.AddText(_listContent, 8, y + 8, 400, 40, dot + AllianceName(al.PartnerName, al.PartnerId), 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    MuteButton(420, y + 6, 170, id, al.PartnerName);
                    if (al.Crew)
                    {
                        // A crew-derived ally (#1216): there is nothing to "end" here — crew access is left
                        // through the Crew view, and a manual alliance with the same player is a separate row.
                        UiKit.AddText(_listContent, 600, y + 6, 150, 44, L("ui.crew.crew_tag"), 18, UiKit.CyanDim, TextAnchor.MiddleCenter);
                    }
                    else
                    {
                        UiKit.AddButton(_listContent, 600, y + 6, 150, 44, L("ui.alliance.end"), () => Game.Network?.SendDissolveAlliance(id));
                    }
                    y += 58f;
                }
            }

            if (a.Outgoing != null && a.Outgoing.Length > 0)
            {
                y += 10f;
                y = AllianceSection(L("ui.alliance.outgoing"), y);
                foreach (var r in a.Outgoing)
                {
                    UiKit.AddText(_listContent, 8, y, 760, 36, AllianceName(r.PlayerName, r.PlayerId) + "  —  " + L("ui.alliance.waiting"), 18, UiKit.CyanDim, TextAnchor.MiddleLeft);
                    y += 40f;
                }
            }

            return y;
        }

        /// <summary>A Mute / Unmute toggle for one player row on the Alliances tab (#1290). Reads and writes the
        /// same <see cref="ClientSettings.MutedPlayers"/> list the Settings screen and <c>/mute</c> use, saves,
        /// and rebuilds the list so the label flips. Player id == display name in this game, but both are
        /// matched on unmute so an entry added under either spelling goes away.</summary>
        private void MuteButton(float x, float y, float w, string playerId, string displayName)
        {
            var s = Menu?.Settings;
            if (s == null || string.IsNullOrEmpty(playerId))
            {
                return;
            }

            bool muted = s.IsMuted(playerId, displayName);
            var b = UiKit.AddButton(_listContent, x, y, w, 44, L(muted ? "ui.mute.unmute" : "ui.mute.mute"), () =>
            {
                bool changed;
                if (muted)
                {
                    changed = s.Unmute(playerId);
                    changed |= s.Unmute(displayName);
                }
                else
                {
                    changed = s.Mute(playerId);
                }

                if (changed)
                {
                    s.Save();
                }

                RebuildList();
            });
            if (muted)
            {
                b.GetComponent<Image>().color = new Color(0.5f, 0.3f, 0.24f); // a muted row shows warm, like other "undo" buttons
            }
        }

        /// <summary>The "find players" picker: online players you can still propose an alliance to (self, current
        /// allies and pending requests are filtered out). Player id == display name in this game, so the name is
        /// the target id. <see cref="StarMapData.Players"/> carries every online player, not just nearby ones.</summary>
        private float BuildAllianceFindList()
        {
            float y = AllianceSection(L("ui.alliance.cat_find"), 0f);
            var a = Game.Alliances ?? new AllianceList();

            var taken = new HashSet<string>();
            if (a.Allies != null) foreach (var al in a.Allies) taken.Add(al.PartnerId);
            if (a.Incoming != null) foreach (var r in a.Incoming) taken.Add(r.PlayerId);
            if (a.Outgoing != null) foreach (var r in a.Outgoing) taken.Add(r.PlayerId);

            string me = Game.LocalPlayerId ?? string.Empty;
            var players = Game.StarMap?.Players ?? System.Array.Empty<NetPlayerLocation>();
            var seen = new HashSet<string>();
            int shown = 0;
            foreach (var p in players)
            {
                string id = p.Name; // player id == name
                if (string.IsNullOrEmpty(id) || id == me || taken.Contains(id) || !seen.Add(id))
                {
                    continue;
                }

                // On official hosted worlds a "report player" button sits next to each name — kids need a
                // one-tap way to flag misbehaviour (reviewed by the operators; nobody is auto-punished).
                // Every row also carries a Mute / Unmute toggle (#1290) — the same list the Settings screen
                // and /mute edit, so a loud stranger can be silenced right where their name shows up.
                bool canReport = !string.IsNullOrEmpty(Game.HostedToken) && !string.IsNullOrEmpty(Game.PortalSession);
                float nameW = canReport ? 220 : 300;
                UiKit.AddText(_listContent, 8, y + 8, nameW, 40, AllianceName(p.Name, id), 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                var btn = UiKit.AddButton(_listContent, canReport ? 236 : 320, y + 6, canReport ? 180 : 230, 44,
                    L("ui.alliance.propose"), () => Game.Network?.SendRequestAlliance(id));
                btn.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.7f);
                if (canReport)
                {
                    Button reportBtn = null;
                    reportBtn = UiKit.AddButton(_listContent, 424, y + 6, 160, 44, L("ui.portal.report"), () => ReportPlayer(id, reportBtn));
                    reportBtn.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.2f);
                    MuteButton(592, y + 6, 160, id, p.Name);
                }
                else
                {
                    MuteButton(560, y + 6, 190, id, p.Name);
                }

                y += 58f;
                shown++;
            }

            if (shown == 0)
            {
                UiKit.AddText(_listContent, 8, y, 760, 30, L("ui.alliance.no_players"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 40f;
            }

            return y;
        }

        /// <summary>Files a player report against the worlds portal (official hosted worlds only — the button
        /// exists only when a portal session + hosted join are present). One tap, category "other"; the
        /// button itself becomes the confirmation. Reports are reviewed by the operators, never auto-punish.</summary>
        private async void ReportPlayer(string playerName, Button button)
        {
            string portalUrl = Game.PortalUrl;
            string session = Game.PortalSession;
            string worldId = Game.HostedWorldId;
            var portal = new PortalClient(portalUrl);
            var result = await Task.Run(() => portal.Report(session, playerName, "other", "in-game report", worldId));
            if (button == null)
            {
                return; // menu closed while the request ran
            }

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = result.Ok ? L("ui.portal.reported") : L("ui.portal.report_failed");
            }

            if (result.Ok)
            {
                button.interactable = false; // one report per player per menu visit is plenty
            }
        }

        /// <summary>The radio (Funk) scrollback in the list pane — a live Text refreshed each frame (see Update),
        /// reusing the existing global chat feed. The input + send button live in the detail pane.</summary>
        private float BuildFunkLog()
        {
            _funkLog = UiKit.AddText(_listContent, 0, 0, 780, 700, ComposeFunkLog(), 18, UiKit.TextCol, TextAnchor.UpperLeft);
            _funkLog.horizontalOverflow = HorizontalWrapMode.Wrap;
            _funkLog.verticalOverflow = VerticalWrapMode.Overflow;
            _funkLog.supportRichText = true;
            return 700f;
        }

        private string ComposeFunkLog()
        {
            var chat = Game?.RecentChat;
            if (chat == null || chat.Count == 0)
            {
                return L("ui.funk.empty");
            }

            var sb = new System.Text.StringBuilder();
            int from = Mathf.Max(0, chat.Count - 30);
            for (int i = from; i < chat.Count; i++)
            {
                sb.AppendLine($"<b>{chat[i].Sender}:</b> {chat[i].Text}");
            }

            return sb.ToString();
        }

        /// <summary>The Crew view (#1216): open invites, then either the "found a crew" form or the member
        /// roster with the owner's management verbs. Every button is a plain UiKit button, so the stick walks
        /// the whole view like the rest of the menu.</summary>
        private float BuildCrewList()
        {
            var crew = Game.Crew ?? new CrewList();
            float y = 0f;

            if (crew.Invites != null && crew.Invites.Length > 0)
            {
                y = AllianceSection(L("ui.crew.invites"), y);
                foreach (var inv in crew.Invites)
                {
                    string cid = inv.CrewId;
                    string line = L("ui.crew.invite_from").Replace("{name}", inv.FromName).Replace("{crew}", inv.CrewName);
                    UiKit.AddText(_listContent, 8, y + 8, 420, 40, line, 18, UiKit.TextCol, TextAnchor.MiddleLeft);
                    var acc = UiKit.AddButton(_listContent, 440, y + 6, 150, 44, L("ui.crew.accept"), () => Game.Network?.SendCrewAction("accept", cid));
                    acc.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
                    UiKit.AddButton(_listContent, 600, y + 6, 150, 44, L("ui.crew.decline"), () => Game.Network?.SendCrewAction("decline", cid));
                    y += 58f;
                }

                y += 10f;
            }

            if (string.IsNullOrEmpty(crew.CrewId))
            {
                y = AllianceSection(L("ui.crew.title"), y);
                UiKit.AddText(_listContent, 8, y, 760, 30, L("ui.crew.none_yet"), 18, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 34f;
                UiKit.AddText(_listContent, 8, y, 760, 30, L("ui.crew.create_hint"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                y += 42f;
                UiKit.AddInput(_listContent, 8, y, 480, 46, _crewNameDraft, v => _crewNameDraft = v, L("ui.crew.name_placeholder"));
                var mk = UiKit.AddButton(_listContent, 500, y, 250, 46, L("ui.crew.create"), () =>
                {
                    Game.Network?.SendCrewAction("create", _crewNameDraft);
                    _crewNameDraft = string.Empty;
                });
                mk.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
                y += 60f;
                return y;
            }

            bool owner = crew.OwnerId == Game.LocalPlayerId;
            int count = crew.Members?.Length ?? 0;
            y = AllianceSection(crew.Name + "  —  " + L("ui.crew.members").Replace("{0}", count.ToString()), y);
            foreach (var m in crew.Members ?? System.Array.Empty<NetCrewMember>())
            {
                string dot = m.Online ? "<color=#52E0A0>●</color> " : "<color=#6A7480>●</color> ";
                string tag = m.IsOwner ? "  ·  " + L("ui.crew.owner_tag") : string.Empty;
                UiKit.AddText(_listContent, 8, y + 8, 400, 40, dot + AllianceName(m.Name, m.PlayerId) + tag, 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                if (m.PlayerId != Game.LocalPlayerId)
                {
                    MuteButton(420, y + 6, 170, m.PlayerId, m.Name); // (#1290) — never offered on yourself
                }

                if (owner && !m.IsOwner)
                {
                    string target = m.PlayerId;
                    UiKit.AddButton(_listContent, 600, y + 6, 150, 44, L("ui.crew.kick"), () => Game.Network?.SendCrewAction("kick", targetPlayerId: target));
                }

                y += 58f;
            }

            y += 10f;
            if (owner)
            {
                UiKit.AddInput(_listContent, 8, y, 480, 46, _crewNameDraft, v => _crewNameDraft = v, L("ui.crew.name_placeholder"));
                UiKit.AddButton(_listContent, 500, y, 250, 46, L("ui.crew.rename"), () =>
                {
                    Game.Network?.SendCrewAction("rename", _crewNameDraft);
                    _crewNameDraft = string.Empty;
                });
                y += 56f;
            }

            var leave = UiKit.AddButton(_listContent, 8, y, 250, 46, L("ui.crew.leave"), () => Game.Network?.SendCrewAction("leave"));
            leave.GetComponent<Image>().color = new Color(0.5f, 0.3f, 0.24f);
            if (owner)
            {
                var dis = UiKit.AddButton(_listContent, 500, y, 250, 46, L("ui.crew.disband"), () => Game.Network?.SendCrewAction("disband"));
                dis.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.2f);
            }

            y += 62f;

            if (owner && count < 8)
            {
                y = AllianceSection(L("ui.crew.invite_someone"), y);
                var inCrew = new HashSet<string>((crew.Members ?? System.Array.Empty<NetCrewMember>()).Select(m => m.PlayerId));
                string me = Game.LocalPlayerId ?? string.Empty;
                var players = Game.StarMap?.Players ?? System.Array.Empty<NetPlayerLocation>();
                var seen = new HashSet<string>();
                int shown = 0;
                foreach (var pl in players)
                {
                    string id = pl.Name; // player id == name
                    if (string.IsNullOrEmpty(id) || id == me || inCrew.Contains(id) || !seen.Add(id))
                    {
                        continue;
                    }

                    UiKit.AddText(_listContent, 8, y + 8, 480, 40, AllianceName(pl.Name, id), 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    var b = UiKit.AddButton(_listContent, 500, y + 6, 250, 44, L("ui.crew.invite"), () => Game.Network?.SendCrewAction("invite", targetPlayerId: id));
                    b.GetComponent<Image>().color = new Color(0.2f, 0.45f, 0.7f);
                    y += 58f;
                    shown++;
                }

                if (shown == 0)
                {
                    UiKit.AddText(_listContent, 8, y, 760, 50, L("ui.crew.nobody_to_invite"), 16, UiKit.CyanDim, TextAnchor.UpperLeft);
                    y += 56f;
                }
            }

            return y;
        }

        private float DetailCrew()
        {
            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 32, L("ui.crew.title"), 24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 44f;
            UiKit.AddText(_detail, 8, y, 620, 340, L("ui.crew.about"), 17, UiKit.TextCol, TextAnchor.UpperLeft);
            y += 350f;
            return y;
        }

        private float DetailAlliances()
        {
            if (_category == "funk")
            {
                return DetailFunk();
            }

            if (_category == "crew")
            {
                return DetailCrew();
            }

            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 32, L("ui.alliance.title"), 24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 44f;
            UiKit.AddText(_detail, 8, y, 620, 260, L("ui.alliance.about"), 17, UiKit.TextCol, TextAnchor.UpperLeft);
            y += 270f;
            return y;
        }

        /// <summary>The radio pane (Alliances → Radio). #2567: comms work only with a radio WORN in a module slot (#2110) —
        /// holding one does nothing — so the pane names the worn radio and its reach, or warns and, with a radio carried,
        /// offers to put it on (the server picks a free module slot). #2569: the push-to-talk line, or that this server
        /// has voice chat off.</summary>
        private float DetailFunk()
        {
            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 32, L("ui.funk.title"), 24, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 44f;
            y = FunkLine(L("ui.funk.hint"), y, 16, UiKit.CyanDim, FontStyle.Normal);

            var voice = VoiceChat.Current;
            string worn = RadioGear.Worn(Game.Wears);
            if (worn != null)
            {
                string wornLine = L("ui.funk.worn").Replace("{radio}", ItemName(worn)).Replace("{reach}", L(RadioGear.ReachLabelKey(worn)));
                y = FunkLine(wornLine, y, 17, UiKit.Ok, FontStyle.Bold);
                if (voice != null && voice.Available)
                {
                    y = FunkLine(L("ui.voice.hint").Replace("{key}", voice.KeyLabel()), y, 17, UiKit.Cyan, FontStyle.Normal);
                }
            }
            else
            {
                y = FunkLine(L("ui.funk.not_worn"), y, 17, UiKit.Warn, FontStyle.Bold);
                if (RadioGear.Carried(Game.Personal, AboardShipNow() ? Game.Cargo : null) is { } carried)
                {
                    // Straight into a free module slot — from the backpack, or aboard from the hold (the server checks both).
                    int fromSlot = carried.Stack.Slot;
                    bool fromCargo = carried.FromCargo;
                    var wear = UiKit.AddButton(_detail, 8, y, 360, 48, L("ui.funk.equip").Replace("{radio}", ItemName(carried.Stack.Item)), () =>
                    {
                        Game.Network?.SendEquipItem(fromSlot, -1, fromCargo);
                        ClientAudio.Instance?.Cue("gear_equip");
                    });
                    wear.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
                    y += 60f;
                }
                else
                {
                    y = FunkLine(L("ui.funk.no_radio"), y, 16, UiKit.CyanDim, FontStyle.Normal);
                }
            }

            if (voice != null && Game.Rules != null && !voice.ServerVoiceOn)
            {
                y = FunkLine(L("ui.voice.unavailable"), y, 16, UiKit.CyanDim, FontStyle.Italic);
            }

            y += 6f;
            UiKit.AddInput(_detail, 8, y, 620, 46, _funkDraft, v => _funkDraft = v, L("ui.funk.placeholder"));
            y += 56f;
            var send = UiKit.AddButton(_detail, 8, y, 220, 48, L("ui.funk.send"), SendFunk);
            send.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
            y += 60f;
            return y;
        }

        private void SendFunk()
        {
            string t = (_funkDraft ?? string.Empty).Trim();
            if (t.Length == 0)
            {
                return;
            }

            Game.Network?.SendChat(t);
            _funkDraft = string.Empty;
            RebuildDetail(); // clear the input box
        }

        /// <summary>One wrapped text line of the radio pane at <paramref name="y"/>; returns the y below it.</summary>
        private float FunkLine(string text, float y, int size, Color color, FontStyle style)
        {
            var line = UiKit.AddText(_detail, 8, y, 620, 28, text, size, color, TextAnchor.UpperLeft, style);
            line.horizontalOverflow = HorizontalWrapMode.Wrap;
            return y + Mathf.Max(28f, line.preferredHeight) + 8f;
        }

        private static string AllianceName(string name, string id) => string.IsNullOrEmpty(name) ? id : name;
    }
}
