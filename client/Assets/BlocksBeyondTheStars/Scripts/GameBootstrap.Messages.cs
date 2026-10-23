// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's player messages (#2912): the recent chat and NPC calls, toasts, the reader, server text and
    /// rejection resolution, and the loot notice.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>Recent radio (Funk) chat lines mirrored for the Alliances-tab chat panel (oldest first, capped).
        /// The standalone <see cref="ChatUi"/> overlay keeps its own buffer; this is the shared feed for the tab.</summary>
        public System.Collections.Generic.IReadOnlyList<ChatMessage> RecentChat => _recentChat;
        private readonly System.Collections.Generic.List<ChatMessage> _recentChat = new();
        private const int RecentChatMax = 60;

        /// <summary>NPC radio calls (#1158, newest last, capped) — a call is easy to lose in chat scrollback,
        /// so the Missions tab shows the recent ones in its own ☎ block.</summary>
        public System.Collections.Generic.IReadOnlyList<ChatMessage> RecentNpcCalls => _recentNpcCalls;
        private readonly System.Collections.Generic.List<ChatMessage> _recentNpcCalls = new();
        private const int RecentNpcCallsMax = 6;

        /// <summary>Last server feedback line (craft result / rejection / message) for a HUD toast. Every
        /// assignment bumps <see cref="LastMessageSeq"/>, so the HUD can tell a RE-SENT identical line from the
        /// one it already showed (#1860); <see cref="ExpireMessage"/> blanks it once the toast's lifetime is up.</summary>
        public string LastMessage
        {
            get => _lastMessage;
            private set
            {
                _lastMessage = value ?? string.Empty;
                LastMessageSeq++;
            }
        }

        private string _lastMessage = string.Empty;

        /// <summary>Bumped on every <see cref="LastMessage"/> assignment (#1860) — the HUD toast keys on it.</summary>
        public int LastMessageSeq { get; private set; }

        /// <summary>The HUD toast for <paramref name="seq"/> ran its lifetime out (#1860): blank the message —
        /// unless a newer one has already replaced it. Blanking bumps the sequence, so the next identical
        /// server line shows again instead of comparing equal to a toast that already faded.</summary>
        public void ExpireMessage(int seq)
        {
            if (seq == LastMessageSeq && _lastMessage.Length > 0)
            {
                LastMessage = string.Empty;
            }
        }

        /// <summary>The resolved station hull-open warning while it is the toast (#1836) — cleared once the station
        /// reports the pocket sealed again, so the banner does not outlive the leak.</summary>
        private string _stationAirWarning = string.Empty;

        /// <summary>Shows a transient HUD message from a client-side system (e.g. the VEGA autopilot).</summary>
        public void ShowMessage(string text) => LastMessage = text ?? string.Empty;

        /// <summary>Bumped on every refused sit-down (#2122, <c>ActionRejected</c> with action "seat"): the player
        /// controller, seated since before the bump, stands back up — the toast already says why.</summary>
        public int SeatRejections { get; private set; }

        /// <summary>Bumped on every refused glide (#2296, <c>ActionRejected</c> with action "glider" — no glider worn as far
        /// as the server knows): the player controller folds the wing it opened.</summary>
        public int GliderRejections { get; private set; }

        /// <summary>#2321: the rope gun is held and a block the rope can stick to is under the crosshair within range — the
        /// HUD tints the reticle. Set by the player controller every frame it holds the gun.</summary>
        public bool RopeAimValid { get; set; }

        /// <summary>Opens the story reader panel (#1110) with a localized title/label + text key — or falls
        /// back to the message toast when no reader exists (headless/degraded rigs stay functional).</summary>
        private void OpenReader(string title, string label, string textKey)
        {
            if (StoryReaderUi.Instance != null)
            {
                StoryReaderUi.Instance.Open(title, label, Localizer?.Get(textKey) ?? textKey);
            }
            else
            {
                ShowMessage(Localizer?.Get(textKey) ?? textKey);
            }
        }

        /// <summary>Localizes a server-authored token for a screen outside the in-game HUD (the menu's join
        /// rejection, #964). Returns the text unchanged when it is not an "@srv.*" token — operator-authored
        /// reasons must stay verbatim.</summary>
        public string ServerTokenText(string text) => ResolveServerToken(text) ?? text;

        /// <summary>Generic resolution for "@srv.*" tokens: the token (minus '@') IS the locale key, an
        /// optional ":arg" tail fills the template's {name} placeholder, and <c>{key:Action}</c> tokens name the
        /// player's own control (#2463 — the ship stations' "Cockpit — {key:Interact}: launch"). New server messages use
        /// this instead of growing the hand-written token maps below; returns null for anything else.</summary>
        private string ResolveServerToken(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith("@srv.", System.StringComparison.Ordinal))
            {
                return null;
            }

            string key = text.Substring(1);
            string arg = null;
            int colon = key.IndexOf(':');
            if (colon >= 0)
            {
                arg = key.Substring(colon + 1);
                key = key.Substring(0, colon);
            }

            // #2559: the fight refusal with its seconds reads its own key; the plain key keeps an older client's sentence.
            key = BlocksBeyondTheStars.Shared.Definitions.ReturnToShipRules.ReasonKey(key, arg);
            string template = Localizer?.Get(key);
            if (string.IsNullOrEmpty(template))
            {
                return text;
            }

            template = HudUi.ExpandKeyTokens(Localizer, template); // before the arg: a player-chosen name stays verbatim
            return arg != null ? template.Replace("{name}", arg) : template;
        }

        /// <summary>Machine-readable server-message tokens → localized player text (same idea as
        /// <see cref="CraftFailMessage"/>; plain text passes through unchanged).</summary>
        private string ServerMessageText(string text)
        {
            string generic = ResolveServerToken(text);
            if (generic != null)
            {
                return generic;
            }

            if (text == "@spawn_set")
            {
                return Localizer?.Get("ui.spawn.set") ?? "Spawn point set — you'll respawn here from now on.";
            }

            // #600: mined drops / craft outputs that fit nowhere used to vanish without a word.
            if (text == "@inventory_full")
            {
                return Localizer?.Get("ui.hint.inventory_full")
                    ?? "Backpack and cargo hold are full — anything else you pick up is lost.";
            }

            // #853: mining with a full pack no longer stops — the overflow is lying on the ground.
            if (text == "@dropped_on_ground")
            {
                return Localizer?.Get("ui.hint.dropped_on_ground")
                    ?? "Backpack full — the rest is lying on the ground. Walk over it once you have room.";
            }

            // #794: a base's sealed rooms just lost their air seal (a wall was mined, a door pulled …).
            if (text == "@base_air_lost")
            {
                return Localizer?.Get("ui.base.air_lost")
                    ?? "Warning: the base is no longer airtight — air only reaches the core zone!";
            }

            // #1473: a player-built station's hull is open — no air outside a sealed pocket until it is patched.
            if (text == "@station_air_lost")
            {
                _stationAirWarning = Localizer?.Get("ui.station.air_lost")
                    ?? "Warning: the station is no longer airtight — helmet on until the hull is patched!";
                return _stationAirWarning;
            }

            // (#817/#821 block painting uses the generic "@srv.paint.*" tokens resolved above.)
            return text;
        }

        /// <summary>Turns a craft-failure reason into a player-facing line. Station-availability failures arrive
        /// as a machine-readable "@need_station:&lt;station&gt;" token so the client can localize them and name the
        /// exact station the recipe needs (Severin playtest #2); "@inventory_full" is the same idea for "the
        /// result wouldn't fit". #2585: every "@srv.*" reason reads its own sentence through
        /// <see cref="ResolveServerToken"/> — the module aboard ("@srv.craft.need_module:Refinery"), the missing items with
        /// counts ("@srv.craft.missing_named:Titanium Ore (0/2)"), a locked blueprint, the wrong vendor, not today …; the
        /// generic line used to swallow them all. Anything else falls back to a generic localized line — the raw English
        /// server text used to be shown to the player, which read as broken in a German session.</summary>
        private string CraftFailMessage(string reason)
        {
            const string stationPrefix = "@need_station:";
            if (!string.IsNullOrEmpty(reason) && reason.StartsWith(stationPrefix))
            {
                string stationKey = reason.Substring(stationPrefix.Length);
                string stationName = Localizer?.Get("ui.craft.station_" + stationKey) ?? stationKey;
                string template = Localizer?.Get("ui.craft.need_station") ?? "You need a {station} nearby to craft this.";
                return template.Replace("{station}", stationName);
            }

            if (reason == "@inventory_full")
            {
                return Localizer?.Get("ui.craft.inventory_full")
                    ?? "No room for the result — make space in your inventory first.";
            }

            // The argument is everything after the FIRST ':' and goes in after the template's {key:…} tokens are expanded,
            // so an item name with a ':' or a ',' in it reaches the sentence verbatim.
            string named = ResolveServerToken(reason);
            if (named != null)
            {
                return named;
            }

            Debug.Log($"Craft failed: {reason}"); // keep the exact server reason for diagnosis, not for the player
            return Localizer?.Get("ui.craft.failed") ?? "That didn't work.";
        }

        /// <summary>Turns a rejected action into a player-facing line. Reasons the player is expected to act on
        /// arrive as machine-readable "@token"s so they can be localized; everything else is a developer-facing
        /// string that stays in the log rather than being pushed at the player as raw English.</summary>
        private string RejectionMessage(string action, string reason)
        {
            string generic = ResolveServerToken(reason);
            if (generic != null)
            {
                return generic;
            }

            if (reason == "@inventory_full")
            {
                // Mining says "make space"; picking a door back up is the same situation.
                return Localizer?.Get(action == "mine" ? "ui.mine.inventory_full" : "ui.craft.inventory_full")
                    ?? "Inventory full — make space first.";
            }

            if (reason == "@no_air")
            {
                // A torch on an airless body: there is nothing for the flame to burn.
                return Localizer?.Get("ui.place.no_air")
                    ?? "There is no air here — a torch only burns on worlds with an atmosphere.";
            }

            if (reason == "@out_of_reach")
            {
                return Localizer?.Get("ui.loot.out_of_reach") ?? "The cache is out of reach — move closer.";
            }

            if (reason == "@no_energy")
            {
                // A powered drill with an empty suit: the swing costs energy the player does not have.
                return Localizer?.Get("ui.mine.no_energy")
                    ?? "Not enough suit energy — recharge, or switch to a drill that needs none.";
            }

            if (reason == "@fire_protected")
            {
                // A torch against a settlement / station / ship / claimed base: those never catch fire.
                return Localizer?.Get("ui.fire.protected")
                    ?? "This is protected — it will not catch fire.";
            }

            return $"{action}: {reason}";
        }

        private string _pendingLootId;
        private int _pendingLootCount;
        private float _pendingLootDeadline;

        /// <summary>Remembers a just-sent loot intent (#751) so the success cue plays only once the server
        /// actually moved items — the cue used to play unconditionally before the request even left, which
        /// made a rejected/no-op loot indistinguishable from a dead key.</summary>
        public void NoteLootRequested(string containerId)
        {
            _pendingLootId = containerId;
            _pendingLootCount = 0;
            if (Containers != null)
            {
                foreach (var c in Containers)
                {
                    if (c.Id == containerId)
                    {
                        _pendingLootCount = c.ItemCount;
                        break;
                    }
                }
            }

            _pendingLootDeadline = Time.time + 2f;
        }

        /// <summary>Applies a container broadcast and confirms a pending loot (#751): the looted container
        /// vanishing (emptied) or shrinking (partial take) is the server's implicit success signal.</summary>
        private void OnContainers(BlocksBeyondTheStars.Networking.Messages.ContainerList m)
        {
            if (_pendingLootId != null && Time.time <= _pendingLootDeadline)
            {
                int count = -1;
                if (m.Containers != null)
                {
                    foreach (var c in m.Containers)
                    {
                        if (c.Id == _pendingLootId)
                        {
                            count = c.ItemCount;
                            break;
                        }
                    }
                }

                if (count < _pendingLootCount)
                {
                    ClientAudio.Instance?.Cue("loot");
                    _pendingLootId = null;
                }
            }

            Containers = m.Containers;
        }

        /// <summary>The "you made something" toast. Names the crafted ITEM in the player's language — this used to
        /// read "Crafted &lt;recipe_key&gt;" in English regardless of language (a German player saw
        /// "Crafted glass"), which is also how we learned the craft reported success while the item was being
        /// destroyed by a full inventory.</summary>
        private string CraftedMessage(string recipeKey)
        {
            // "tint" and "shape" are pseudo-recipes for the dye/re-form actions, not entries in recipes.json.
            var outputs = Content?.GetRecipe(recipeKey)?.Outputs;
            if (outputs == null || outputs.Count == 0)
            {
                return Localizer?.Get("ui.craft.crafted_generic") ?? "Crafted!";
            }

            string baseKey = BlocksBeyondTheStars.Shared.State.ItemKey.Parse(outputs[0].Item).Base;
            string itemName = Localizer?.Get($"item.{baseKey}.name") ?? baseKey;
            string template = Localizer?.Get("ui.craft.crafted") ?? "Crafted {item}";
            return template.Replace("{item}", itemName);
        }

        /// <summary>Start's subscriptions for the message feed: chat, containers, craft results, rejections and server
        /// messages. Called once from <see cref="Start"/>, before the transport connects.</summary>
        private void WireMessages()
        {
            Network.ChatReceived += m =>
            {
                // A muted sender is dropped from the Funk mirror too (#1209) — hiding a line in the chat
                // overlay but leaving it in the Alliances tab would only move the problem one tab across.
                if (!m.IsNpcCall && Settings != null && Settings.IsMuted(m.SenderId, m.Sender))
                {
                    return;
                }

                _recentChat.Add(m);
                if (_recentChat.Count > RecentChatMax) _recentChat.RemoveAt(0);
                if (m.IsNpcCall)
                {
                    _recentNpcCalls.Add(m); // mirrored into the Missions tab's ☎ block (#1158)
                    if (_recentNpcCalls.Count > RecentNpcCallsMax) _recentNpcCalls.RemoveAt(0);
                }
            };
            Network.ContainersReceived += m => OnContainers(m);
            Network.CraftCompleted += m => LastMessage = m.Success ? CraftedMessage(m.RecipeKey) : CraftFailMessage(m.Reason);
            Network.ActionRejected += m =>
            {
                Debug.Log($"Action '{m.Action}' rejected: {m.Reason}");
                LastMessage = RejectionMessage(m.Action, m.Reason);
                if (m.Action == "seat")
                {
                    SeatRejections++; // #2122: somebody sits there — PlayerController stands us back up
                }
                else if (m.Action == "glider")
                {
                    GliderRejections++; // #2296: no glider worn as the server sees it — PlayerController folds the wing
                }
                else if (m.Action == "visit")
                {
                    VisitRefusals++; // #2724: the flight view drops the veil it raised for the visit
                }
                else if (m.Action == "return_ship")
                {
                    // #2559: the open pause menu shows why instead of closing — and a fight refusal carries the seconds
                    // left, so the row greys out at once even if no state update said so yet.
                    ReturnToShipRefusalText = LastMessage;
                    ReturnToShipRefusals++;
                    float fight = (float)BlocksBeyondTheStars.Shared.Definitions.ReturnToShipRules.FightSecondsOf(m.Reason);
                    if (fight > 0f)
                    {
                        _returnToShipFightUntil = Mathf.Max(_returnToShipFightUntil, Time.realtimeSinceStartup + fight);
                    }
                }

                // The server is authoritative: if a dig is rejected because the cell is "already empty", the
                // client's view of it is stale — a ghost block (a cell some server path cleared to air without a
                // broadcast the client applied). Clear it directly so the phantom vanishes at once and the next
                // dig hits whatever is actually behind it, instead of "mining" a block that isn't there (B32).
                if (m.Action == "mine" && !string.IsNullOrEmpty(m.Reason) && m.Reason.Contains("empty")
                    && World != null)
                {
                    var c = LastMineCell;
                    if (World.ApplyBlockChange(c.x, c.y, c.z, 0, out var ghostCoord))
                    {
                        // Neighbours too: a ghost on a chunk border leaves the adjacent chunk's
                        // now-exposed wall faces missing otherwise (see-through hole when mining fast).
                        MarkChunkAndNeighborsDirty(ghostCoord);
                    }
                }
            };
            Network.ServerMessageReceived += m => { Debug.Log(m.Text); LastMessage = ServerMessageText(m.Text); };
        }
    }
}
