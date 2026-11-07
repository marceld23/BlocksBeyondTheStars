// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the loading screen's status row says while a world starts (#3260). The bundled server announces every boot
    /// pass BEFORE it runs it — <c>[boot] &gt; 7/12 landing_pads</c> for a pass, <c>[boot]   &gt; settlements</c> for a
    /// step inside one — with a stable id (the pass name lower-cased, spaces as <c>_</c>). The finish lines with their
    /// durations (<c>[boot] 7/12 landing pads (4120 ms)</c>) still drive the bar; these start lines name what the player
    /// is waiting for right now, so an 11-second landing-pad search no longer sits under a fixed flavour line.
    /// <para>The ids map onto a dozen kid-friendly, grouped messages (<c>ui.loading.stage.&lt;group&gt;</c>): the
    /// player needs "Building settlements…", not the name of every stamper. An id this client does not know (a newer
    /// server's pass) reads as "Preparing the world…"; a server that prints no start lines at all (an older one, or a
    /// remote host) keeps the old flavour line.</para>
    /// Pure, so the headless suite pins the parser, the mapping and that every server pass is mapped.
    /// </summary>
    public static class BootStageText
    {
        /// <summary>Key prefix of the grouped stage messages.</summary>
        public const string KeyPrefix = "ui.loading.stage.";

        /// <summary>The group an unknown id falls back to: "Preparing the world…".</summary>
        public const string FallbackGroup = "world";

        /// <summary>The server's final pass: it opens its port — the client is about to connect.</summary>
        public const string ReadyId = "ready";

        /// <summary>The client's own preparation once the server is up (rig, materials): "Building the world…".</summary>
        public const string ClientPrepKey = KeyPrefix + "client";

        // Every boot pass and step the server announces (GameServer.Start / LoadWorld / the structures pass), by id.
        // BootStageTextTests reads the server's sources and fails on a pass that is missing here.
        private static readonly Dictionary<string, string> Groups = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["galaxy"] = "galaxy",
            ["weather"] = "nature",
            ["flora"] = "nature",
            ["landing_pads"] = "landing_pads",
            ["creatures"] = "creatures",
            ["settlements"] = "settlements",
            ["intercity_rail"] = "settlements",
            ["lodges"] = "settlements",
            ["pioneer_outposts"] = "settlements",
            ["npcs"] = "residents",
            ["doors"] = "residents",
            ["structures"] = "ruins",
            ["ruins"] = "ruins",
            ["rail_ruins"] = "ruins",
            ["bandit_camps"] = "ruins",
            ["sps_labs"] = "ruins",
            ["monuments"] = "ruins",
            ["text_monuments"] = "ruins", // #3310: KuBa-W's stone tablets
            ["factories"] = "ruins",
            ["wreck"] = "ruins",
            ["vaults"] = "ruins",
            ["data_cubes"] = "treasure",
            ["net_fragments"] = "treasure",
            ["chests"] = "treasure",
            ["unique_sites"] = "treasure",
            ["persistence"] = FallbackGroup,
            ["registries"] = FallbackGroup,
            ["containers"] = FallbackGroup,
            ["fluids"] = FallbackGroup,
            ["fire"] = FallbackGroup,
            [ReadyId] = "ready",
        };

        /// <summary>Every key this table can answer, the client step included — for the locale and fit tests.</summary>
        public static IReadOnlyList<string> AllKeys
        {
            get
            {
                var keys = new SortedSet<string>(StringComparer.Ordinal) { ClientPrepKey, KeyPrefix + FallbackGroup };
                foreach (var group in Groups.Values)
                {
                    keys.Add(KeyPrefix + group);
                }

                return new List<string>(keys);
            }
        }

        /// <summary>True when <paramref name="id"/> has its own message (not the fallback).</summary>
        public static bool IsKnown(string? id) => id != null && Groups.ContainsKey(id);

        /// <summary>The message key for a stage id; an unknown or empty id reads "Preparing the world…".</summary>
        public static string KeyFor(string? id)
            => KeyPrefix + (id != null && Groups.TryGetValue(id, out var group) ? group : FallbackGroup);

        /// <summary>
        /// The status row's key for this moment of a world start.
        /// </summary>
        /// <param name="clientPrep">The loading screen is handing off to the in-game rig (the client's own setup).</param>
        /// <param name="serverBooting">A bundled server was spawned for this world and has not reported ready.</param>
        /// <param name="serverReady">That server reported ready; the hand-off has not happened yet.</param>
        /// <param name="stageId">The server's current stage id, or null when it announced none (yet).</param>
        /// <param name="flavourKey">The old fixed line — shown when no stage is known.</param>
        public static string StatusKey(bool clientPrep, bool serverBooting, bool serverReady, string? stageId, string flavourKey)
        {
            if (clientPrep)
            {
                return ClientPrepKey;
            }

            if (serverReady)
            {
                return KeyFor(ReadyId);
            }

            return serverBooting && !string.IsNullOrEmpty(stageId) ? KeyFor(stageId) : flavourKey;
        }

        /// <summary>
        /// Reads a server START line: <c>[boot] &gt; 7/12 landing_pads</c> → stage 7 of 12, id <c>landing_pads</c>;
        /// <c>[boot]   &gt; settlements</c> (a step inside a pass) → stage 0 of 0, id <c>settlements</c>. Text before
        /// the marker (a timestamp, the launcher's <c>[server]</c> prefix) is skipped. A finish line, any other line and
        /// a malformed one answer false.
        /// </summary>
        public static bool TryParseStart(string? line, out int stage, out int stages, out string id)
        {
            stage = 0;
            stages = 0;
            id = string.Empty;
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            const string marker = "[boot] ";
            int at = line.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            int i = SkipSpaces(line, at + marker.Length);
            if (i >= line.Length || line[i] != '>')
            {
                return false;
            }

            i = SkipSpaces(line, i + 1);
            string first = Token(line, i);
            int slash = first.IndexOf('/');
            if (slash > 0)
            {
                if (!int.TryParse(first.Substring(0, slash), out stage) || !int.TryParse(first.Substring(slash + 1), out stages)
                    || stage < 0 || stages <= 0)
                {
                    stage = stages = 0;
                    return false;
                }

                i = SkipSpaces(line, i + first.Length);
                first = Token(line, i);
            }

            id = first;
            return id.Length > 0;
        }

        private static int SkipSpaces(string line, int i)
        {
            while (i < line.Length && line[i] == ' ')
            {
                i++;
            }

            return i;
        }

        private static string Token(string line, int start)
        {
            int end = start;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
            {
                end++;
            }

            return start < line.Length ? line.Substring(start, end - start) : string.Empty;
        }
    }
}
