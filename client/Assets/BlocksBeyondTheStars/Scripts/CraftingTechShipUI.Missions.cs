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
    /// The Missions tab of <see cref="CraftingTechShipUI"/>: the mission board, the player-created mission form
    /// and the mission detail with its objective labels.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        // Player-created mission form state (item 31, Missions tab "create" category).
        private static readonly string[] PmTypes = { "Mine", "Collect", "Deliver" };
        private static readonly string[] PmTargets = { "iron_ore", "copper_ore", "titanium_ore", "crystal", "carbon", "silicate", "stone", "ice" };
        private static readonly string[] PmRewards = { "iron_ore", "copper_ore", "titanium_ore", "crystal", "carbon", "plant_fiber", "berries", "iron_plate" };
        private string _pmTitle = string.Empty, _pmDesc = string.Empty;
        private int _pmType, _pmTarget, _pmCount = 5, _pmRewardItem = 3, _pmRewardCount = 1;
        private readonly System.Collections.Generic.List<NetMissionObjective> _pmObjectives = new();

        private float BuildMissionsList()
        {
            if (_category == "create")
            {
                return BuildMissionForm();
            }

            var list = Game.Missions;
            if (list == null)
            {
                UiKit.AddText(_listContent, 8, 8, 700, 30, L("ui.map.loading"), 22, UiKit.CyanDim, TextAnchor.UpperLeft);
                return 40f;
            }

            var missions = _category == "active" ? list.Active : list.Available;
            float y = 0f;

            // Recent NPC radio calls (#1158): a call is a fleeting chat line — give it a place to be found.
            var calls = Game.RecentNpcCalls;
            if (calls != null && calls.Count > 0)
            {
                UiKit.AddText(_listContent, 0, y, 780, 24, L("ui.missions.radio"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 28f;
                for (int i = calls.Count - 1; i >= 0; i--)
                {
                    UiKit.AddText(_listContent, 8, y, 772, 22, $"{calls[i].Sender}: {calls[i].Text}", 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                    y += 24f;
                }

                y += 10f;
            }

            foreach (var m in missions)
            {
                string status = m.Objectives.Length > 0 ? $"{m.Objectives[0].Progress}/{m.Objectives[0].Required}" : string.Empty;
                if (m.ChainStep > 0)
                {
                    status = ChainStepLabel(m) + "  ·  " + status; // #1212: "Part 2 of 4 · 0/3"
                }

                AddCard(y, MissionText(m), "cat_mission", status, UiKit.CyanDim, "mis:" + m.Id, () => { _selected = "mis:" + m.Id; RebuildDetail(); });
                y += 88f;
            }

            return y;
        }

        /// <summary>The "post a mission" form (item 31): title + description, an objectives builder (type / target /
        /// count, add multiple), and a staked reward. Posting sends a <c>CreateMissionIntent</c>; the server
        /// escrows the stake, and when someone else completes it the poster gets a multiple of the stake back.</summary>
        private float BuildMissionForm()
        {
            var c = _listContent;
            const float W = 780f;
            float y = 0f;

            UiKit.AddText(c, 0, y, W, 24, L("ui.missions.create_hint"), 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 30f;
            UiKit.AddInput(c, 0, y, W, 42, _pmTitle, v => _pmTitle = v, L("ui.missions.title_ph"));
            y += 50f;
            UiKit.AddInput(c, 0, y, W, 42, _pmDesc, v => _pmDesc = v, L("ui.missions.desc_ph"));
            y += 58f;

            UiKit.AddText(c, 0, y, W, 22, L("ui.missions.objectives"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 28f;
            for (int i = 0; i < _pmObjectives.Count; i++)
            {
                int idx = i;
                var o = _pmObjectives[i];
                UiKit.AddText(c, 12, y, 600, 32, $"{IdLabel("ui.missions.objtype_", o.Type)}  {o.Required}× {ItemName(o.Target)}", 18, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddButton(c, 700, y, 60, 32, "✕", () => { _pmObjectives.RemoveAt(idx); RebuildList(); });
                y += 38f;
            }

            // Builder row: type / target / count / add.
            // The cycler shows the localized label; PmTypes stays the wire value (NetMissionObjective.Type).
            UiKit.AddButton(c, 0, y, 150, 38, IdLabel("ui.missions.objtype_", PmTypes[_pmType]), () => { _pmType = (_pmType + 1) % PmTypes.Length; RebuildList(); });
            UiKit.AddButton(c, 158, y, 210, 38, ItemName(PmTargets[_pmTarget]), () => { _pmTarget = (_pmTarget + 1) % PmTargets.Length; RebuildList(); });
            UiKit.AddButton(c, 376, y, 44, 38, "−", () => { _pmCount = Mathf.Max(1, _pmCount - 1); RebuildList(); });
            UiKit.AddText(c, 422, y, 54, 38, _pmCount.ToString(), 20, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(c, 478, y, 44, 38, "+", () => { _pmCount++; RebuildList(); });
            UiKit.AddButton(c, 560, y, 200, 38, L("ui.missions.add_objective"), () =>
            {
                _pmObjectives.Add(new NetMissionObjective { Type = PmTypes[_pmType], Target = PmTargets[_pmTarget], Required = _pmCount });
                RebuildList();
            });
            y += 56f;

            UiKit.AddText(c, 0, y, W, 22, L("ui.missions.stake"), 16, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 28f;
            UiKit.AddButton(c, 0, y, 210, 38, ItemName(PmRewards[_pmRewardItem]), () => { _pmRewardItem = (_pmRewardItem + 1) % PmRewards.Length; RebuildList(); });
            UiKit.AddButton(c, 218, y, 44, 38, "−", () => { _pmRewardCount = Mathf.Max(1, _pmRewardCount - 1); RebuildList(); });
            UiKit.AddText(c, 264, y, 54, 38, _pmRewardCount.ToString(), 20, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.AddButton(c, 320, y, 44, 38, "+", () => { _pmRewardCount++; RebuildList(); });
            UiKit.AddText(c, 380, y, 380, 38, $"(×{Owned(PmRewards[_pmRewardItem])})", 16, UiKit.CyanDim, TextAnchor.MiddleLeft);
            y += 58f;

            var post = UiKit.AddButton(c, 0, y, 320, 50, L("ui.missions.post"), PostMission);
            post.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
            y += 60f;
            return y;
        }

        private void PostMission()
        {
            if (string.IsNullOrWhiteSpace(_pmTitle) || _pmObjectives.Count == 0)
            {
                if (_feedback != null) _feedback.text = L("ui.missions.need_fields");
                return;
            }

            var rewards = new[] { new NetReward { Item = PmRewards[_pmRewardItem], Count = _pmRewardCount } };
            Game.Network?.SendCreateMission(_pmTitle, _pmDesc, _pmObjectives.ToArray(), rewards);
            _pmObjectives.Clear();
            _pmTitle = string.Empty;
            _pmDesc = string.Empty;
            _category = "available"; // jump to the board so the poster sees it appear
            Game.Network?.SendRequestMissions();
            RebuildList();
            RebuildSidebar();
        }

        private float DetailMissions()
        {
            var list = Game.Missions;
            if (list == null)
            {
                return 0f;
            }

            string id = _selected.Substring(4);
            var avail = list.Available.FirstOrDefault(m => m.Id == id);
            var active = list.Active.FirstOrDefault(m => m.Id == id);
            var m2 = avail ?? active;
            if (m2 == null)
            {
                return 0f;
            }

            float y = 0f;
            UiKit.AddText(_detail, 8, y, 620, 40, MissionText(m2), 28, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Bold);
            y += 46f;
            // Mission-giver NPC (item 13): "Mission from {Name}".
            if (!string.IsNullOrEmpty(m2.GiverName))
            {
                UiKit.AddText(_detail, 8, y, 620, 24, $"{L("ui.missions.giver")} {m2.GiverName}", 16, UiKit.Cyan, TextAnchor.UpperLeft);
                y += 28f;
            }

            // #2714: a villager's wish — the village it is handed over in; on that body with the distance and "Navigate" (the
            // village's map marker is in the marker list then).
            if (!string.IsNullOrEmpty(m2.PlaceName))
            {
                var place = WishPlaceText.MarkerFor(m2, Game.Markers);
                int metres = -1;
                if (place != null)
                {
                    float dx = Game.SceneX(place.X) - Game.PlayerPosition.x, dz = Game.SceneZ(place.Z) - Game.PlayerPosition.z;
                    metres = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz));
                }

                UiKit.AddText(_detail, 8, y, place != null ? 460 : 620, 24, WishPlaceText.MissionLine(m2, metres, L), 16, UiKit.Cyan, TextAnchor.UpperLeft);
                if (place != null)
                {
                    float px = place.X, pz = place.Z;
                    UiKit.AddButton(_detail, 476, y - 4, 140, 32, L("ui.marker.nav"), () => Game.Waypoint = new Vector3(px, 0f, pz));
                }

                y += 34f;
            }

            // Mission chains (#1212): which part of the chain this is.
            if (m2.ChainStep > 0)
            {
                UiKit.AddText(_detail, 8, y, 620, 24, ChainStepLabel(m2), 16, UiKit.Cyan, TextAnchor.UpperLeft, FontStyle.Bold);
                y += 28f;
            }

            // The mission's flavour/instructions. System missions send a locale key (resolved via L);
            // player-posted missions and L3 LLM board texts send display text (FreeText) shown verbatim.
            if (!string.IsNullOrEmpty(m2.Description))
            {
                // Wrap the flavour/instructions so long objectives (e.g. the first-iron hint
                // "…dig straight down to find it.") are not clipped by the RectMask2D viewport,
                // and advance y by the actual wrapped height so it never overlaps the objectives.
                var desc = UiKit.AddText(_detail, 8, y, 620, 60, m2.FreeText ? m2.Description : L(m2.Description), 17, UiKit.CyanDim, TextAnchor.UpperLeft);
                desc.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += Mathf.Max(64f, desc.preferredHeight + 8f);
            }

            // #1205: an objective row names WHAT to do and with what ("Scan · any creature  2/3"), not just a count —
            // with five objective kinds on the boards a bare "0/3" no longer told the player anything.
            foreach (var o in m2.Objectives)
            {
                var row = UiKit.AddText(_detail, 8, y, 620, 28,
                    $"{IdLabel("ui.missions.objtype_", o.Type)} · {ObjectiveTargetLabel(o.Type, o.Target)}  {o.Progress}/{o.Required}",
                    20, UiKit.CyanDim, TextAnchor.UpperLeft);
                row.horizontalOverflow = HorizontalWrapMode.Wrap;
                y += Mathf.Max(30f, row.preferredHeight + 6f);
            }

            if (m2.KnowledgeReward > 0)
            {
                UiKit.AddText(_detail, 8, y, 620, 26, $"+{m2.KnowledgeReward} {L("ui.missions.knowledge_reward")}", 17, UiKit.Cyan, TextAnchor.UpperLeft);
                y += 28f;
            }

            y += 10f;
            if (avail != null)
            {
                UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.action.accept"), () => Game.Network.SendAcceptMission(m2.Id));
            }
            else
            {
                UiKit.AddButton(_detail, 8, y, 280, 56, L("ui.action.turn_in"), () => Game.Network.SendTurnInMission(m2.Id));
            }

            return y + 70f;
        }

        /// <summary>Human-readable objective target (#1205): items by name, blocks by name, the Build groups and
        /// the Scan grammar (<c>creature:any</c>, <c>block:&lt;key&gt;</c>, <c>asteroid</c>, …) through
        /// <c>ui.missions.scantarget.*</c> / <c>ui.missions.buildtarget_*</c>; unknown shapes fall back to the raw key.</summary>
        private string ObjectiveTargetLabel(string type, string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                return string.Empty;
            }

            switch ((type ?? string.Empty).ToLowerInvariant())
            {
                case "collect":
                case "deliver":
                    return ItemName(target);
                case "mine":
                    return BlockLabel(target);
                case "build":
                    return Game?.Localizer?.Has("ui.missions.buildtarget_" + target) == true
                        ? L("ui.missions.buildtarget_" + target)
                        : BlockLabel(target);
                case "travel":
                    return Game?.Localizer?.Has("ui.missions.traveltarget_" + target) == true
                        ? L("ui.missions.traveltarget_" + target) // #1212: the relative "other_body" target
                        : target;
                case "scan":
                {
                    string key = "ui.missions.scantarget." + target.Replace(':', '_');
                    if (Game?.Localizer?.Has(key) == true)
                    {
                        return L(key);
                    }

                    if (target.StartsWith("block:", System.StringComparison.Ordinal))
                    {
                        return BlockLabel(target.Substring("block:".Length));
                    }

                    return target.StartsWith("creature:", System.StringComparison.Ordinal) ? target.Substring("creature:".Length) : target;
                }
                case "circuit":
                {
                    // #2462 (B25): what the Crystal Net circuit must do ("a switch lights a lamp"), not "lamp_by_switch".
                    string key = "ui.missions.circuittarget." + target;
                    return Game?.Localizer?.Has(key) == true ? L(key) : L("ui.missions.circuittarget.any");
                }
                default:
                    return target;
            }
        }

        private string BlockLabel(string blockKey)
            => Game?.Localizer?.Has("block." + blockKey + ".name") == true ? L("block." + blockKey + ".name") : blockKey;

        /// <summary>"Part 2 of 4" for a chain mission (#1212); the length falls back to the step when unknown.</summary>
        private string ChainStepLabel(NetMission m)
            => L("ui.missions.chain_step").Replace("{0}", m.ChainStep.ToString()).Replace("{1}", Mathf.Max(m.ChainStep, m.ChainLength).ToString());

        /// <summary>A mission's display title: FreeText (player missions, L3 LLM board texts) verbatim,
        /// otherwise the locale key resolved.</summary>
        private string MissionText(NetMission m) => m.FreeText ? m.Title : L(m.Title);
    }
}
