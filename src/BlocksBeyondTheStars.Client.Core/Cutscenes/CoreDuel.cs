// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>
    /// The Guardian core's argument duel as this client last heard it (#2478, the finale's dialogue mode): the open node, its
    /// prompt and choices, the core's answer to the last pick, the node count and the core column (the dialogue's camera
    /// anchor). Fed by every <see cref="CoreDialogueMessage"/>; the server stays the authority over the duel — the client only
    /// shows the node and sends the picked index back.
    /// </summary>
    public sealed class CoreDuelState
    {
        /// <summary>A node is open (the core waits for an answer).</summary>
        public bool Open { get; private set; }

        /// <summary>The duel was won (the core powers down).</summary>
        public bool Won { get; private set; }

        /// <summary>The open node's index.</summary>
        public int Node { get; private set; }

        /// <summary>How many nodes the duel has (0 from an older server).</summary>
        public int NodeCount { get; private set; }

        /// <summary>The core's statement (locale key).</summary>
        public string PromptKey { get; private set; } = string.Empty;

        /// <summary>The player's answers (locale keys).</summary>
        public IReadOnlyList<string> ChoiceKeys { get; private set; } = Array.Empty<string>();

        /// <summary>The core's reaction to the last pick (locale key; empty at the opening).</summary>
        public string ResponseKey { get; private set; } = string.Empty;

        /// <summary>The server sent the core column's position.</summary>
        public bool HasAnchor { get; private set; }

        public float AnchorX { get; private set; }
        public float AnchorY { get; private set; }
        public float AnchorZ { get; private set; }

        /// <summary>Counts the node messages — a new node (or a re-presented one after a wrong pick) is a new version, which
        /// redraws the dialogue and frees its buttons again.</summary>
        public int Version { get; private set; }

        /// <summary>Takes a node message in. Returns whether the duel just opened (it was not open before).</summary>
        public bool Apply(CoreDialogueMessage? message)
        {
            if (message is null)
            {
                return false;
            }

            bool opened = !Open && !message.Won;
            Version++;
            Node = message.Node;
            NodeCount = message.NodeCount;
            ResponseKey = message.ResponseKey ?? string.Empty;
            if (message.HasAnchor)
            {
                HasAnchor = true;
                AnchorX = message.AnchorX;
                AnchorY = message.AnchorY;
                AnchorZ = message.AnchorZ;
            }

            if (message.Won)
            {
                Open = false;
                Won = true;
                PromptKey = string.Empty;
                ChoiceKeys = Array.Empty<string>();
                return false;
            }

            Open = true;
            PromptKey = message.PromptKey ?? string.Empty;
            ChoiceKeys = message.ChoiceKeys ?? Array.Empty<string>();
            return opened;
        }

        /// <summary>Forgets the duel (the client left the world).</summary>
        public void Reset()
        {
            Open = false;
            Won = false;
            Node = 0;
            NodeCount = 0;
            PromptKey = string.Empty;
            ChoiceKeys = Array.Empty<string>();
            ResponseKey = string.Empty;
            HasAnchor = false;
            AnchorX = AnchorY = AnchorZ = 0f;
            Version++;
        }
    }

    /// <summary>
    /// The duel dialogue's rules (#2478, E28, synthesis §6.5 <c>fin.duel</c>), Unity-free so they are unit-tested headless:
    /// <list type="bullet">
    /// <item>the dialogue shows <b>only on the core world</b> (landed, not in the flight view, not as an observer) — the others
    /// of the finale get the duel banner from the server;</item>
    /// <item>the answers are the dialogue's <b>own buttons</b> (mouse, pad, touch) — never the hotbar digits;</item>
    /// <item>after a pick the buttons wait for the core's next node (or <see cref="PickRetrySeconds"/>, should the answer get
    /// lost), so a double click never answers twice;</item>
    /// <item>a player who stepped back faces the core again near it (<see cref="ResumeReach"/>) with the use control.</item>
    /// </list>
    /// </summary>
    public static class CoreDuelRules
    {
        /// <summary>The Guardian system's id prefix (its only body is the core world: <c>guardian_finale-core</c>).</summary>
        public const string FinaleSystemPrefix = "guardian_finale";

        /// <summary>How close to the core column a player who stepped back may face the core again (blocks).</summary>
        public const float ResumeReach = 12f;

        /// <summary>After a pick the buttons wait this long at most for the core's answer before they open again.</summary>
        public const float PickRetrySeconds = 4f;

        /// <summary>Whether the player stands on the Guardian core world (landed — a pilot flying over it is not).</summary>
        public static bool OnCoreWorld(string? activeLocationId, bool spaceView)
            => !spaceView && !string.IsNullOrEmpty(activeLocationId)
               && activeLocationId!.StartsWith(FinaleSystemPrefix, StringComparison.Ordinal);

        /// <summary>Whether this client shows the duel dialogue now: a node is open, the player is on the core world and plays
        /// (an observer only watches the world), and did not step back from it.</summary>
        public static bool ShowsDialogue(CoreDuelState? duel, string? activeLocationId, bool spaceView, bool spectating, bool steppedBack)
            => duel is not null && duel.Open && !duel.Won && !spectating && !steppedBack && OnCoreWorld(activeLocationId, spaceView);

        /// <summary>
        /// Whether the dialogue may take the screen now: never behind the loading veil, a menu, the chat input, the respawn
        /// prompt or another scene, nor while frozen controls would hurt (falling, climbing, the head under water, a vehicle,
        /// EVA, piloting, a warp, the spawn settle). In multiplayer not in danger either (a hostile close, recent damage, low
        /// vitals — the world runs on); in singleplayer the dialogue holds the world, so nothing can reach the player while it
        /// shows and it opens even with a machine in the chamber.
        /// </summary>
        public static bool MayOpen(SafeMomentInputs? s)
        {
            if (s is null || s.LoadingVeil || s.AnotherScene || s.Spectating || s.MenuOpen || s.RespawnPrompt || s.ChatTyping
                || s.Settling || s.Falling || s.Climbing || s.HeadUnderwater || s.InVehicle || s.InEva || s.PilotingInSpace || s.Warping)
            {
                return false;
            }

            if (!s.Multiplayer)
            {
                return true;
            }

            var request = new SafeMomentRequest { Tier = CutsceneTiers.SetPiece, TakesCamera = true, Requested = true };
            return SafeMomentGate.Evaluate(request, s).Verdict == SafeMomentVerdict.Play;
        }

        /// <summary>Whether the open dialogue steps aside for danger (multiplayer only — damage or space combat since it opened,
        /// a hostile closing in): the player gets the controls back and the dialogue returns once it is safe.</summary>
        public static bool StepsAsideForDanger(SafeMomentInputs? s, float secondsOpen)
            => s is not null && s.Multiplayer && SafeMomentGate.ShouldInterrupt(s, secondsOpen, worldHeld: false);

        /// <summary>Whether a player who stepped back is near enough to face the core again (the column's position from the
        /// server, measured the short way round the world's seam; without a position: anywhere on the core world).</summary>
        public static bool NearCore(CoreDuelState? duel, float px, float py, float pz, int circumference)
        {
            if (duel is null || !duel.HasAnchor)
            {
                return true;
            }

            double dx = circumference > 0 ? WorldConstants.WrapDeltaX(duel.AnchorX - (double)px, circumference) : duel.AnchorX - (double)px;
            double dy = duel.AnchorY - (double)py;
            double dz = duel.AnchorZ - (double)pz;
            return dx * dx + dy * dy + dz * dz <= ResumeReach * (double)ResumeReach;
        }

        /// <summary>Whether the answer buttons take a pick: none sent for this node yet, or the core has not answered within
        /// <see cref="PickRetrySeconds"/> (a pick lost on the way — the server ignores a second one for a node it moved on
        /// from, and re-presents a wrong one).</summary>
        public static bool ChoicesOpen(CoreDuelState? duel, int pickedVersion, float pickedAt, float now)
            => duel is not null && duel.Open && (pickedVersion != duel.Version || now - pickedAt >= PickRetrySeconds);

        /// <summary>The core's words in the dialogue: its reaction to the last pick (when there is one), then its statement —
        /// both localized. <paramref name="reactionFormat"/> wraps the reaction (the client sets it apart, e.g. in italics).</summary>
        public static string SpeakerText(CoreDuelState? duel, Func<string, string> localize, Func<string, string>? reactionFormat = null)
        {
            if (duel is null || localize is null)
            {
                return string.Empty;
            }

            string prompt = string.IsNullOrEmpty(duel.PromptKey) ? string.Empty : localize(duel.PromptKey);
            if (string.IsNullOrEmpty(duel.ResponseKey))
            {
                return prompt;
            }

            string reaction = localize(duel.ResponseKey);
            if (reactionFormat != null)
            {
                reaction = reactionFormat(reaction);
            }

            return prompt.Length == 0 ? reaction : reaction + "\n\n" + prompt;
        }

        /// <summary>"Argument 2 of 4" — the node's place (from 1) and the count; empty from an older server (no count).</summary>
        public static string ProgressText(CoreDuelState? duel, string? template)
        {
            if (duel is null || duel.NodeCount <= 0 || string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            int place = Math.Min(duel.NodeCount, Math.Max(1, duel.Node + 1));
            try
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, template!, place, duel.NodeCount);
            }
            catch (FormatException)
            {
                return template!; // a stray brace in a translation never blanks the line
            }
        }
    }

    /// <summary>
    /// The star chart's player labels (#2478, VEGA's "Many minds" in multiplayer — the <c>players</c> anchor): every other
    /// online player's name on the star of the system they are in, comma-joined, from the star map's player list.
    /// </summary>
    public static class CutsceneChart
    {
        /// <summary>The system of each other player (<paramref name="me"/> excluded, by name), names joined per system.</summary>
        public static IReadOnlyDictionary<string, string> PlayersBySystem(StarMapData? map, string? me)
        {
            var bySystem = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var p in map?.Players ?? Array.Empty<NetPlayerLocation>())
            {
                if (p is null || string.IsNullOrEmpty(p.Name) || string.Equals(p.Name, me, StringComparison.Ordinal))
                {
                    continue;
                }

                string? system = SystemOfBody(map, p.LocationId);
                if (system is null)
                {
                    continue;
                }

                if (!bySystem.TryGetValue(system, out var names))
                {
                    bySystem[system] = names = new List<string>();
                }

                if (!names.Contains(p.Name))
                {
                    names.Add(p.Name);
                }
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in bySystem)
            {
                result[pair.Key] = string.Join(", ", pair.Value);
            }

            return result;
        }

        /// <summary>Whether the star map shows at least one other player (the <c>players</c> anchor's object).</summary>
        public static bool AnyOtherPlayer(StarMapData? map, string? me) => PlayersBySystem(map, me).Count > 0;

        /// <summary>The star system a body belongs to, or null.</summary>
        public static string? SystemOfBody(StarMapData? map, string? bodyId)
        {
            if (map?.Systems == null || string.IsNullOrEmpty(bodyId))
            {
                return null;
            }

            foreach (var sys in map.Systems)
            {
                foreach (var b in sys?.Bodies ?? Array.Empty<NetBody>())
                {
                    if (b != null && b.Id == bodyId)
                    {
                        return sys!.Id;
                    }
                }
            }

            return null;
        }
    }
}
