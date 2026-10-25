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
    /// The Character tab of <see cref="CraftingTechShipUI"/> (#2505): the character page with its live avatar
    /// preview and in-world options, the people the player knows (#1118) and the "change the main story?" dialog.
    /// </summary>
    public sealed partial class CraftingTechShipUI
    {
        private AvatarPreviewRig _avatarPreview; // live faced-avatar preview for the colour menu (B25)

        /// <summary>"People you know" (#1118): everyone with a standing, friendliest first, straight from
        /// the server's persisted NPC memory. Requested lazily (rate-limited) — the refresh hash rebuilds
        /// the list when the roster lands.</summary>
        private float BuildPeopleList()
        {
            if (Game?.Network != null && Time.unscaledTime - _peopleRequestedAt > 5f)
            {
                _peopleRequestedAt = Time.unscaledTime;
                Game.Network.SendRequestKnownNpcs();
            }

            float y = 8f;
            var people = Game?.KnownNpcs;
            if (people == null || people.Length == 0)
            {
                var empty = UiKit.AddText(_listContent, 16, y, 740, 60, L("ui.character.people_empty"), 17, UiKit.CyanDim, TextAnchor.UpperLeft);
                empty.horizontalOverflow = HorizontalWrapMode.Wrap;
                return y + 70f;
            }

            foreach (var person in people)
            {
                UiKit.AddText(_listContent, 8, y + 4, 700, 30, person.Name, 22, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                string place = string.IsNullOrEmpty(person.Place) ? string.Empty : $"  ·  {person.Place}";
                UiKit.AddText(_listContent, 28, y + 34, 720, 24, $"{L(person.RoleKey)}  ·  {L(person.StageKey)}{place}", 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 68f;
            }

            return y + 8f;
        }

        private float _peopleRequestedAt = -999f;

        private float BuildCharacterList()
        {
            float y = 0f;
            Color[] cols = Menu != null && Menu.Settings != null
                ? new[] { Menu.Settings.SkinColor, Menu.Settings.TorsoColor, Menu.Settings.ArmColor, Menu.Settings.LegColor }
                : new[] { Color.gray, Color.gray, Color.gray, Color.gray };

            // Recolour the live preview as the player cycles a part (this list rebuilds on every cycle).
            if (_avatarPreview != null && Menu?.Settings != null)
            {
                _avatarPreview.SetColors(cols[0], cols[1], cols[2], cols[3]);
                _avatarPreview.SetFace(Menu.Settings.FacePixels);
                for (int part = 0; part < BodyPaintKit.PartCount; part++)
                {
                    _avatarPreview.SetBodyPaint(part, Menu.Settings.GetBodyPaint(part)); // body paintings (#874)
                }
            }

            // ONE appearance card (#899). This list used to carry nine: four "cycle this colour" rows and five
            // "open that editor" rows — the colour of a part and the painting on it edited two menu levels
            // apart, though they are the same decision (unpainted pixels ARE the base colour). The editor now
            // holds both, with the parts as tabs and a live figure beside the canvas.
            var card = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () => Menu?.OpenAppearanceEditor());
            UiKit.AddText(card.transform, 16, 0, 380, 78, L("ui.appearance.title"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            for (int i = 0; i < 4; i++)
            {
                UiKit.AddImage(card.transform, 400 + i * 44, 24, 38, 30, UiKit.SolidSprite, cols[i]); // the four base colours at a glance
            }

            UiKit.AddText(card.transform, 600, 0, 170, 78, L("ui.face.open"), 18, UiKit.Cyan, TextAnchor.MiddleLeft);
            y += 96f;

            // Master volume — − / + adjust the audio bus live (and persist).
            int pct = Mathf.RoundToInt((Menu?.Settings?.MasterVolume ?? 0.8f) * 100f);
            UiKit.AddText(_listContent, 16, y, 380, 78, $"{L("ui.settings.volume")}: {pct}%", 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddButton(_listContent, 420, y + 11, 80, 56, "−", () => { AdjustVolume(-0.1f); RebuildList(); });
            UiKit.AddButton(_listContent, 510, y + 11, 80, 56, "+", () => { AdjustVolume(0.1f); RebuildList(); });
            y += 96f;

            // Visor HUD effect on/off — toggles the holographic styling live (better readability when off).
            bool visorOn = Menu?.Settings?.VisorEffects ?? true;
            var visorBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                if (Menu?.Settings != null)
                {
                    Menu.Settings.VisorEffects = !Menu.Settings.VisorEffects;
                    Menu.Settings.Save();
                    RebuildList();
                }
            });
            UiKit.AddText(visorBtn.transform, 16, 0, 520, 78, L("ui.settings.visor"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(visorBtn.transform, 560, 0, 200, 78, visorOn ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                visorOn ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;

            // Textures (#1959): the editor, and the two per-player switches. "Show this world's textures" is the
            // safety valve — whatever an admin published, a player (or a parent) can turn it off for themselves.
            UiKit.AddText(_listContent, 16, y, 760, 30, L("ui.settings.textures_title"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 40f;
            var texEditorBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () => Menu?.OpenTextureEditor());
            UiKit.AddText(texEditorBtn.transform, 16, 0, 520, 78, L("ui.menu.texture_editor"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(texEditorBtn.transform, 600, 0, 170, 78, L("ui.face.open"), 18, UiKit.Cyan, TextAnchor.MiddleLeft);
            y += 96f;

            void TextureToggle(string label, bool on, System.Action<bool> apply)
            {
                var btn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
                {
                    if (Menu?.Settings != null)
                    {
                        apply(!on);
                        Menu.Settings.Save();
                        RebuildList();
                    }
                });
                UiKit.AddText(btn.transform, 16, 0, 520, 78, label, 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddText(btn.transform, 560, 0, 200, 78, on ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                    on ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 96f;
            }

            TextureToggle(string.Format(L("ui.settings.use_texture_pack"), GameTextures.LocalCount), Menu?.Settings?.UseTexturePack ?? true, v =>
            {
                Menu.Settings.UseTexturePack = v;
                GameTextures.UseLocalPack = v;
            });
            TextureToggle(string.Format(L("ui.settings.show_world_textures"), GameTextures.WorldCount), Menu?.Settings?.ShowWorldTextures ?? true, v =>
            {
                Menu.Settings.ShowWorldTextures = v;
                GameTextures.ShowWorldTextures = v;
            });

            // World rules (world options, live edit): creatures + the three enemy activities. The server
            // enforces the admin gate (non-admins get a reject toast); the rows re-render when the
            // re-broadcast ServerRules lands.
            UiKit.AddText(_listContent, 16, y, 760, 30, L("ui.worldopt.live_title"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 40f;
            void StepRow(string label, string[] steps, string labelPrefix, string current, System.Action<string> send)
            {
                int idx = System.Array.IndexOf(steps, current);
                if (idx < 0) idx = 2;
                string stepName = L(labelPrefix + idx);
                UiKit.AddText(_listContent, 16, y, 360, 56, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddText(_listContent, 380, y, 180, 56, stepName, 20, UiKit.Cyan, TextAnchor.MiddleCenter);
                UiKit.AddButton(_listContent, 570, y + 6, 80, 44, "−", () =>
                {
                    if (idx > 0) { send(steps[idx - 1]); Invoke(nameof(RebuildList), 0.35f); }
                });
                UiKit.AddButton(_listContent, 660, y + 6, 80, 44, "+", () =>
                {
                    if (idx < steps.Length - 1) { send(steps[idx + 1]); Invoke(nameof(RebuildList), 0.35f); }
                });
                y += 62f;
            }

            void RuleRow(string label, string current, System.Action<string> send)
                => StepRow(label, WorldCreationOptions.Activity, "ui.worldopt.aa.", current, send);

            var rules = Game?.Rules;
            RuleRow(L("ui.worldopt.creatures"), rules?.CreatureAbundance ?? "Normal", v => Game?.Network?.SendSetWorldRules(creatures: v));
            RuleRow(L("ui.worldopt.planet_enemies"), rules?.PlanetEnemies ?? "Normal", v => Game?.Network?.SendSetWorldRules(planetEnemies: v));
            RuleRow(L("ui.worldopt.space_npcs"), rules?.SpaceNpcEnemies ?? "Normal", v => Game?.Network?.SendSetWorldRules(spaceNpcs: v));
            RuleRow(L("ui.worldopt.ufos"), rules?.AlienUfos ?? "Off", v => Game?.Network?.SendSetWorldRules(ufos: v));
            RuleRow(L("ui.worldopt.bandits"), rules?.Bandits ?? "Normal", v => Game?.Network?.SendSetWorldRules(bandits: v));
            // #2972: on-foot PvP and friendly fire, live. An older server sends no friendly-fire value — then neither row shows.
            if (!string.IsNullOrEmpty(rules?.FriendlyFire))
            {
                StepRow(L("ui.worldopt.pvp"), new[] { "Off", "DuelsOnly", "GroupBased", "On" }, "ui.worldopt.pvp.", rules.Pvp,
                    v => Game?.Network?.SendSetPvpRules(pvp: v));
                StepRow(L("ui.worldopt.friendly_fire"), new[] { "Off", "On" }, "ui.worldopt.ff.", rules.FriendlyFire,
                    v => Game?.Network?.SendSetPvpRules(friendlyFire: v));
            }
            // Environmental hazards (#670): the live switch for the temperature survival hazard — Off
            // disables it on a running world, Light/Hard soften/sharpen the drain and exposure damage.
            StepRow(L("ui.worldopt.hazards"), WorldCreationOptions.HazardSteps, "ui.worldopt.hz.",
                rules?.EnvironmentalHazards ?? "Normal", v => Game?.Network?.SendSetWorldRules(hazards: v));

            // #2269: how long the machines that were running catch up when someone comes back (Off = they wait).
            {
                var steps = BlocksBeyondTheStars.Shared.Definitions.CrystalNetRules.CatchUpChoicesMinutes;
                int minutes = rules != null && rules.MachineCatchUpMinutes >= 0
                    ? rules.MachineCatchUpMinutes
                    : BlocksBeyondTheStars.Shared.Definitions.CrystalNetRules.CatchUpDefaultMinutes;
                int idx = System.Array.IndexOf(steps, minutes);
                if (idx < 0) idx = System.Array.IndexOf(steps, BlocksBeyondTheStars.Shared.Definitions.CrystalNetRules.CatchUpDefaultMinutes);
                UiKit.AddText(_listContent, 16, y, 360, 56, L("ui.rules.machine_catchup"), 20, UiKit.TextCol, TextAnchor.MiddleLeft);
                UiKit.AddText(_listContent, 380, y, 180, 56, L("ui.rules.machine_catchup." + steps[idx]), 20, UiKit.Cyan, TextAnchor.MiddleCenter);
                UiKit.AddButton(_listContent, 570, y + 6, 80, 44, "−", () =>
                {
                    if (idx > 0) { Game?.Network?.SendSetWorldRules(machineCatchUpMinutes: steps[idx - 1]); Invoke(nameof(RebuildList), 0.35f); }
                });
                UiKit.AddButton(_listContent, 660, y + 6, 80, 44, "+", () =>
                {
                    if (idx < steps.Length - 1) { Game?.Network?.SendSetWorldRules(machineCatchUpMinutes: steps[idx + 1]); Invoke(nameof(RebuildList), 0.35f); }
                });
                y += 50f;
                UiKit.AddText(_listContent, 16, y, 760, 30, L("ui.rules.machine_catchup.hint"), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
                y += 40f;
            }

            // #2472: the stories' world rules, live — the tutorial (Auto / Short courses only / On request / Off, #2498, with
            // the chosen rule's one-line explanation) and the story pace. The same words and order as the world-creation
            // Stories page (WorldStoryOptions); the server checks the admin role. An older server sends no value — then the
            // row is not there.
            void StoryRuleRow(string label, string[] steps, string[] nameKeys, int idx, System.Action<string> send)
            {
                UiKit.AddText(_listContent, 16, y, 360, 56, label, 20, UiKit.TextCol, TextAnchor.MiddleLeft);
                var ruleName = UiKit.AddText(_listContent, 380, y, 180, 56, L(nameKeys[idx]), 20, UiKit.Cyan, TextAnchor.MiddleCenter);
                UiKit.FitLabel(ruleName, 12, 20); // "Short courses only" is the longest step name
                UiKit.AddButton(_listContent, 570, y + 6, 80, 44, "−", () =>
                {
                    if (idx > 0) { send(steps[idx - 1]); Invoke(nameof(RebuildList), 0.35f); }
                });
                UiKit.AddButton(_listContent, 660, y + 6, 80, 44, "+", () =>
                {
                    if (idx < steps.Length - 1) { send(steps[idx + 1]); Invoke(nameof(RebuildList), 0.35f); }
                });
            }

            // #2506: the main story, live — the world stories this client knows and None (today VEGA / None). Only a server
            // whose switch keeps progress names the running story (ServerRules.MainStory, sent to world admins only); an older
            // server would start the story over, so without the field the row is not there. Leaving a running story asks first
            // (ShowStorySwitchDialog); switching one on does not. The row follows the re-sent rules.
            string mainStory = rules?.MainStory;
            if (WorldStoryOptions.ShowsMainStoryRow(mainStory))
            {
                var storyChoices = WorldStoryOptions.SwitchChoices(Game?.Content?.Stories?.Values, mainStory);
                int storyIdx = WorldStoryOptions.ChoiceIndex(storyChoices, mainStory);
                UiKit.AddText(_listContent, 16, y, 360, 56, L("ui.worldopt.main_story"), 20, UiKit.TextCol, TextAnchor.MiddleLeft);
                var storyName = UiKit.AddText(_listContent, 380, y, 180, 56,
                    WorldStoryOptions.StoryShortName(mainStory, Game?.Content?.Stories, L, HasLoc), 20, UiKit.Cyan, TextAnchor.MiddleCenter);
                UiKit.FitLabel(storyName, 12, 20);
                UiKit.AddButton(_listContent, 570, y + 6, 80, 44, "−", () =>
                {
                    if (storyIdx > 0) { RequestStorySwitch(mainStory, storyChoices[storyIdx - 1]); }
                });
                UiKit.AddButton(_listContent, 660, y + 6, 80, 44, "+", () =>
                {
                    if (storyIdx < storyChoices.Count - 1) { RequestStorySwitch(mainStory, storyChoices[storyIdx + 1]); }
                });
                y += 62f;
            }

            int tutorialIdx = WorldStoryOptions.TutorialIndex(rules?.TutorialPolicy);
            if (tutorialIdx >= 0)
            {
                StoryRuleRow(L("ui.worldopt.tutorial"), WorldStoryOptions.TutorialSteps, WorldStoryOptions.TutorialNameKeys, tutorialIdx,
                    v => Game?.Network?.SendSetWorldRules(tutorialPolicy: v));
                y += 50f;
                var tutorialHint = UiKit.AddText(_listContent, 16, y, 760, 30, L(WorldStoryOptions.TutorialHintKeys[tutorialIdx]), 15,
                    UiKit.CyanDim, TextAnchor.MiddleLeft);
                UiKit.FitLabel(tutorialHint, 10, 15);
                y += 40f;
            }

            int paceIdx = WorldStoryOptions.DensityIndex(rules?.StoryDensity);
            if (paceIdx >= 0)
            {
                StoryRuleRow(L("ui.worldopt.story_density"), WorldStoryOptions.DensitySteps, WorldStoryOptions.DensityNameKeys, paceIdx,
                    v => Game?.Network?.SendSetWorldRules(storyDensity: v));
                y += 62f;
            }

            // Instant Travel (world option): when on, the travel screen may quick-travel anywhere; when off
            // (default) it is limited to worlds you've already landed on. The server enforces the admin gate.
            bool instant = rules?.InstantTravel ?? false;
            var instantBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                Game?.Network?.SendSetWorldRules(instantTravel: instant ? "Off" : "On");
                Invoke(nameof(RebuildList), 0.35f);
            });
            UiKit.AddText(instantBtn.transform, 16, 0, 520, 78, L("ui.worldopt.instant_travel"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(instantBtn.transform, 560, 0, 200, 78, instant ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                instant ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;

            // Keep ship on destruction (world option): when on (default) a ship lost in space combat is recovered
            // intact to base; when off it is left a wreck the owner must repair before flying again.
            bool keepShip = rules?.KeepShipOnDeath ?? true;
            var keepShipBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                Game?.Network?.SendSetWorldRules(keepShip: keepShip ? "Off" : "On");
                Invoke(nameof(RebuildList), 0.35f);
            });
            UiKit.AddText(keepShipBtn.transform, 16, 0, 520, 78, L("ui.worldopt.keep_ship"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(keepShipBtn.transform, 560, 0, 200, 78, keepShip ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                keepShip ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;

            // Auto-aim (world option, #693): when on (default) weapons acquire targets in a forward cone by
            // themselves; when off only what is actually under the crosshair can be hit — for everyone in
            // this world. The server enforces the admin gate and validates shots accordingly.
            bool autoAim = rules?.AutoAim ?? true;
            var autoAimBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                Game?.Network?.SendSetWorldRules(autoAim: autoAim ? "Off" : "On");
                Invoke(nameof(RebuildList), 0.35f);
            });
            UiKit.AddText(autoAimBtn.transform, 16, 0, 520, 78, L("ui.worldopt.auto_aim"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(autoAimBtn.transform, 560, 0, 200, 78, autoAim ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                autoAim ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;

            // Starter teleporter (world option, #1056): when on, every player who joins without a suit teleporter
            // is handed one (and everyone online gets one the moment it is switched on) — a multiplayer crew can
            // beam to allies on the same body and back to their ships without grinding the blueprint first.
            bool starterTp = rules?.StarterTeleporter ?? false;
            var starterTpBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                Game?.Network?.SendSetWorldRules(starterTeleporter: starterTp ? "Off" : "On");
                Invoke(nameof(RebuildList), 0.35f);
            });
            UiKit.AddText(starterTpBtn.transform, 16, 0, 520, 78, L("ui.worldopt.starter_teleporter"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(starterTpBtn.transform, 560, 0, 200, 78, starterTp ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                starterTp ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;

            // "Back to my ship" (world option, #2286): the pause menu's rescue teleport for a stuck player on foot —
            // free, three minutes apart, never in a fight or while falling, only to the own ship landed on this body.
            // On by default; a hard world switches it off. An older server sends no value — then there is nothing to switch.
            if (!string.IsNullOrEmpty(rules?.ReturnToShip))
            {
                bool returnShip = string.Equals(rules.ReturnToShip, "On", System.StringComparison.Ordinal);
                var returnShipBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
                {
                    Game?.Network?.SendSetWorldRules(returnToShip: returnShip ? "Off" : "On");
                    Invoke(nameof(RebuildList), 0.35f);
                });
                UiKit.AddText(returnShipBtn.transform, 16, 0, 520, 78, L("ui.worldopt.return_ship"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddText(returnShipBtn.transform, 560, 0, 200, 78, returnShip ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                    returnShip ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 96f;
            }

            // World textures (#1959): may the admins of this world publish textures for everyone? Off also takes
            // the published ones away from every client (they stay stored and return when it is switched on).
            // An older server sends no value — then there is nothing to switch.
            if (!string.IsNullOrEmpty(rules?.WorldTextures))
            {
                bool worldTex = string.Equals(rules.WorldTextures, "Admins", System.StringComparison.Ordinal);
                var worldTexBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
                {
                    Game?.Network?.SendSetWorldRules(worldTextures: worldTex ? "Off" : "On");
                    Invoke(nameof(RebuildList), 0.35f);
                });
                UiKit.AddText(worldTexBtn.transform, 16, 0, 520, 78, L("ui.worldopt.world_textures"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddText(worldTexBtn.transform, 560, 0, 200, 78, worldTex ? L("ui.worldopt.world_textures_admins") : L("ui.toggle.off"), 22,
                    worldTex ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 96f;
            }

            // Frontier danger (#1122, opt-in): tougher machines out in the frontier tier. Only offered
            // when planet enemies exist at all — on peaceful/family worlds (PlanetEnemies Off) the row is
            // hidden, matching "richness only, never danger" for those setups.
            if (!string.Equals(rules?.PlanetEnemies, "Off", System.StringComparison.OrdinalIgnoreCase))
            {
                bool frontier = rules?.FrontierDanger ?? false;
                var frontierBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
                {
                    Game?.Network?.SendSetWorldRules(frontierDanger: frontier ? "Off" : "On");
                    Invoke(nameof(RebuildList), 0.35f);
                });
                UiKit.AddText(frontierBtn.transform, 16, 0, 520, 78, L("ui.worldopt.frontier_danger"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddText(frontierBtn.transform, 560, 0, 200, 78, frontier ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                    frontier ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 96f;
            }

            // Visitors at the base (#1224, opt-in): bandit scouts look at a founded base from the zone edge.
            // Gated on BOTH the bandits slider (scouts are bandits — with robbers off the switch would do
            // nothing) AND planet enemies (#1297): on peaceful/family worlds the sentry post stays silent,
            // so scouts must not arrive there either — same rule as the frontier-danger row above.
            if (!string.Equals(rules?.Bandits, "Off", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(rules?.PlanetEnemies, "Off", System.StringComparison.OrdinalIgnoreCase))
            {
                bool visitors = rules?.BaseVisitors ?? false;
                var visitorsBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
                {
                    Game?.Network?.SendSetWorldRules(baseVisitors: visitors ? "Off" : "On");
                    Invoke(nameof(RebuildList), 0.35f);
                });
                UiKit.AddText(visitorsBtn.transform, 16, 0, 520, 78, L("ui.worldopt.base_visitors"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddText(visitorsBtn.transform, 560, 0, 200, 78, visitors ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                    visitors ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 96f;
            }

            // Friendly visitors (#2602, default on): now and then a neighbour from the village, a travelling merchant or a
            // lost traveller walks up to a base whose owner is home. Not tied to any enemy setting, so the row is always
            // there — only an older server sends no value, and then there is nothing to switch.
            if (!string.IsNullOrEmpty(rules?.FriendlyVisitors))
            {
                bool friendly = string.Equals(rules.FriendlyVisitors, "On", System.StringComparison.Ordinal);
                var friendlyBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
                {
                    Game?.Network?.SendSetWorldRules(friendlyVisitors: friendly ? "Off" : "On");
                    Invoke(nameof(RebuildList), 0.35f);
                });
                UiKit.AddText(friendlyBtn.transform, 16, 0, 520, 78, L("ui.worldopt.friendly_visitors"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                UiKit.AddText(friendlyBtn.transform, 560, 0, 200, 78, friendly ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                    friendly ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 96f;
            }

            // Per-player mode overrides (#1121): one row per online player, cycling World / Survival /
            // Creative. The server fills the roster ONLY for world admins, so the section simply is not
            // there for everyone else — same admin gating as the rule rows above, but without the noise.
            var modeNames = rules?.PlayerModeNames;
            if (modeNames != null && modeNames.Length > 0)
            {
                UiKit.AddText(_listContent, 16, y, 760, 30, L("ui.worldopt.player_modes"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold);
                y += 40f;
                string[] modeSteps = { "None", "Survival", "Creative" };
                for (int i = 0; i < modeNames.Length; i++)
                {
                    string playerName = modeNames[i];
                    string current = rules.PlayerModeValues != null && i < rules.PlayerModeValues.Length
                        ? rules.PlayerModeValues[i] : "None";
                    StepRow(playerName, modeSteps, "ui.worldopt.pmode.", current,
                        v => Game?.Network?.SendAdminCommand("set_mode",
                            stringArg: v == "None" ? "world" : v.ToLowerInvariant(), targetPlayer: playerName));
                }
            }

            y += 16f;

            // VEGA advisor hints on/off — mutes the ship AI's optional coaching (onboarding chip stays).
            bool vegaOn = Menu?.Settings?.VegaHints ?? true;
            var vegaBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                if (Menu?.Settings != null)
                {
                    Menu.Settings.VegaHints = !Menu.Settings.VegaHints;
                    Menu.Settings.Save();
                    // The server speaks the hints — it holds them back while muted instead of burning them unseen (#2462, B31).
                    Game?.Network?.SendSetNpcCalls(Menu.Settings.NpcCalls, !Menu.Settings.VegaHints);
                    RebuildList();
                }
            });
            UiKit.AddText(vegaBtn.transform, 16, 0, 520, 78, L("ui.settings.vega_hints"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(vegaBtn.transform, 560, 0, 200, 78, vegaOn ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                vegaOn ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;

            // #2471: "Show all objectives" — up to three goals under the objective chip (from the objective list), never while
            // the chat is open. A per-installation preference like the hints switch above.
            bool showAll = Menu?.Settings?.ShowAllObjectives ?? false;
            var showAllBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
            {
                if (Menu?.Settings != null)
                {
                    Menu.Settings.ShowAllObjectives = !Menu.Settings.ShowAllObjectives;
                    Menu.Settings.Save();
                    RebuildList();
                }
            });
            UiKit.AddText(showAllBtn.transform, 16, 0, 520, 78, L("ui.settings.show_all_objectives"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(showAllBtn.transform, 560, 0, 200, 78, showAll ? L("ui.toggle.on") : L("ui.toggle.off"), 22,
                showAll ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            y += 96f;

            // The tutorial (#2471): "Tutorial & courses: on / off" (#2498 named the courses — the player's own switch for the
            // whole pack, optOut / optIn; an opt-in world's tutorial starts with it) plus "Open the Journal", where its parts are skipped, restarted and replayed. Lives HERE (not on
            // the HUD chip) because gameplay captures the mouse — the menu is where the cursor is free to click. An older
            // server sends no story list: the old skip / restart button stays for it.
            var tutorialState = TutorialSwitch.Of(Game?.StoryPackStates);
            if (tutorialState == TutorialSwitch.State.Unknown)
            {
                bool onboarding = Game?.OnboardingActive ?? false;
                var tut = UiKit.AddButton(_listContent, 0, y, 780, 78, onboarding ? L("ui.vega.skip") : L("ui.vega.restart"), () =>
                {
                    Game?.Network?.SendSkipOnboarding(restart: !onboarding);
                    Invoke(nameof(RebuildList), 0.35f);
                });
                tut.GetComponent<Image>().color = new Color(0.16f, 0.28f, 0.40f);
                y += 96f;
            }
            else
            {
                if (tutorialState != TutorialSwitch.State.None)
                {
                    var tutorial = TutorialSwitch.TutorialOf(Game.StoryPackStates);
                    string action = TutorialSwitch.ActionFor(tutorialState);
                    var tutBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, string.Empty, () =>
                    {
                        if (action.Length > 0 && tutorial != null)
                        {
                            Game?.Network?.SendStoryPack(tutorial.Id, action);
                        }
                    });
                    tutBtn.GetComponent<Image>().color = new Color(0.16f, 0.28f, 0.40f);
                    UiKit.AddText(tutBtn.transform, 16, 0, 520, 78, L("ui.settings.tutorial"), 24, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
                    string value = tutorialState switch
                    {
                        TutorialSwitch.State.On => L("ui.toggle.on"),
                        TutorialSwitch.State.Off => L("ui.toggle.off"),
                        _ => L("ui.settings.tutorial_blocked"),
                    };
                    UiKit.AddText(tutBtn.transform, 560, 0, 210, 78, value, 22,
                        tutorialState == TutorialSwitch.State.On ? UiKit.Ok : UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                    y += 96f;
                }

                var journalBtn = UiKit.AddButton(_listContent, 0, y, 780, 78, L("ui.settings.open_journal"), () => OnTab((int)Mode.Story));
                journalBtn.GetComponent<Image>().color = new Color(0.16f, 0.28f, 0.40f);
                y += 96f;
            }

            // Explicit save (on top of the periodic autosave).
            var save = UiKit.AddButton(_listContent, 0, y, 780, 78, L("ui.settings.save_game"), () =>
            {
                Game.Network?.SendSaveGame();
                if (_feedback != null) _feedback.text = L("ui.settings.saved");
            });
            save.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.36f);
            y += 96f;

            return y;
        }

        /// <summary>Builds (once) the live faced-avatar preview rig, coloured from the player's current settings.</summary>
        private void EnsureAvatarPreview()
        {
            if (_avatarPreview != null)
            {
                return;
            }

            var go = new GameObject("AvatarPreviewRig");
            go.transform.SetParent(transform, false);
            _avatarPreview = go.AddComponent<AvatarPreviewRig>();
            var s = Menu?.Settings;
            _avatarPreview.EnsureBuilt(
                s?.SkinColor ?? Color.gray, s?.TorsoColor ?? Color.gray, s?.ArmColor ?? Color.gray, s?.LegColor ?? Color.gray);
        }

        /// <summary>Shows the rotating faced-avatar preview (rendered to a texture) in the colour tab's detail
        /// pane, so the player sees their colour choices on the actual figure — with a face (B25).</summary>
        private float BuildCharacterPreview()
        {
            EnsureAvatarPreview();
            _avatarPreview.SetActive(true);
            var s = Menu?.Settings;
            if (s != null)
            {
                _avatarPreview.SetColors(s.SkinColor, s.TorsoColor, s.ArmColor, s.LegColor);
                _avatarPreview.SetFace(s.FacePixels);
            }

            UiKit.AddPanel(_detail, 64, 16, 500, 716, new Color(0.03f, 0.06f, 0.11f, 0.92f));
            UiKit.AddText(_detail, 64, 24, 500, 26, L("ui.settings.preview"), 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            var go = new GameObject("AvatarPreview", typeof(RectTransform));
            go.transform.SetParent(_detail, false);
            UiKit.Place(go, 94, 58, 440, 660);
            var img = go.AddComponent<RawImage>();
            img.texture = _avatarPreview.Texture;
            return 744f;
        }

        /// <summary>Nudges master volume and applies + persists it immediately.</summary>
        private void AdjustVolume(float delta)
        {
            var s = Menu?.Settings;
            if (s == null)
            {
                return;
            }

            s.MasterVolume = Mathf.Clamp01(s.MasterVolume + delta);
            s.Apply(); // pushes AudioListener.volume
            s.Save();
        }

        private GameObject _storySwitchDialog; // #2506: the "change the main story?" confirm, one at a time

        /// <summary>#2506: the "Main story" row's −/+ — switching a story on goes straight to the server; leaving a running
        /// story asks first (<see cref="ShowStorySwitchDialog"/>). The server checks the admin role and keeps every story's
        /// progress; the row re-renders from the re-sent rules.</summary>
        private void RequestStorySwitch(string current, string target)
        {
            if (WorldStoryOptions.SwitchNeedsConfirm(current, target))
            {
                ShowStorySwitchDialog(current, target);
                return;
            }

            SendStorySwitch(target);
        }

        private void SendStorySwitch(string target)
        {
            Game?.Network?.SendStorySelect(target);
            Invoke(nameof(RebuildList), 0.35f);
        }

        /// <summary>#2506: the confirm before leaving a running story — it takes a break for everyone, nothing is lost, its
        /// progress comes back when it is switched on again. Keep it running / Change; closing the menu drops it.</summary>
        private void ShowStorySwitchDialog(string current, string target)
        {
            CloseStorySwitchDialog();
            var canvas = UiKit.CreateCanvas("Story Switch");
            canvas.sortingOrder = 61; // above the Tab menu — the discard confirm's tier
            _storySwitchDialog = canvas.gameObject;
            UiNav.Enable(canvas.gameObject); // pad: stick walks Keep / Change, A picks

            string storyTitle = WorldStoryOptions.StoryName(current, Game?.Content?.Stories, L, HasLoc);
            var (_, panel) = UiKit.AddModalOverlay(canvas.transform, 660f, 360f, 600f, 340f);
            var title = UiKit.AddText(panel.transform, 24f, 24f, 552f, 44f,
                L("ui.worldopt.story_switch_title"), 26, UiKit.TextCol, TextAnchor.MiddleCenter);
            UiKit.FitLabel(title, 16, 26);
            var body = UiKit.AddText(panel.transform, 24f, 76f, 552f, 140f,
                L("ui.worldopt.story_switch_text").Replace("{story}", storyTitle), 18, UiKit.CyanDim, TextAnchor.UpperCenter);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.FitLabel(body, 12, 18);
            var keep = UiKit.AddButton(panel.transform, 40f, 240f, 240f, 56f, L("ui.worldopt.story_switch_keep"), CloseStorySwitchDialog);
            UiKit.FitLabel(keep.GetComponentInChildren<Text>(), 12, 20);
            var change = UiKit.AddButton(panel.transform, 320f, 240f, 240f, 56f, L("ui.worldopt.story_switch_yes"), () =>
            {
                CloseStorySwitchDialog();
                SendStorySwitch(target);
            });
            UiKit.FitLabel(change.GetComponentInChildren<Text>(), 12, 20);
        }

        private void CloseStorySwitchDialog()
        {
            if (_storySwitchDialog != null)
            {
                Destroy(_storySwitchDialog);
                _storySwitchDialog = null;
            }
        }
    }
}
