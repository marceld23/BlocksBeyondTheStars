// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The two start options a player sets without opening the game: where the persistent data lives and which port the
    /// bundled server listens on — the server that runs singleplayer and home-network games. Two sources, the command
    /// line winning over the file:
    /// <list type="bullet">
    /// <item>the portable marker next to the executable (<see cref="PortableDataDir.MarkerFileName"/>, #1285) — its
    /// directory line as before, plus an optional <c>port=&lt;n&gt;</c> line. A marker always means a portable data
    /// folder, so a file with only the port line keeps the data in <see cref="PortableDataDir.DefaultSubfolder"/> next to
    /// the executable;</item>
    /// <item>the command line — <see cref="DataDirArgument"/> <c>&lt;path&gt;</c> and <see cref="HostPortArgument"/>
    /// <c>&lt;n&gt;</c> (or <c>-name=value</c>; the names are case-insensitive and also take two dashes or the hyphenated
    /// spelling <c>--data-dir</c> / <c>--host-port</c>). That is what an installed copy's shortcut carries: its program
    /// folder is replaced by every update, so a marker there would not survive.</item>
    /// </list>
    /// A port outside <see cref="MinPort"/>..<see cref="MaxPort"/> or an unusable path is reported in
    /// <see cref="Warnings"/> and ignored — the game then starts with the default, never not at all. Unity-free so the
    /// rules are unit-tested; <c>AppPaths</c> supplies the executable folder, the marker text and the command line, and
    /// applies the result.
    /// </summary>
    public sealed class LaunchOptions
    {
        /// <summary>The command-line option naming the data folder.</summary>
        public const string DataDirArgument = "-dataDir";

        /// <summary>The command-line option naming the bundled server's port.</summary>
        public const string HostPortArgument = "-hostPort";

        /// <summary>The lowest port accepted — below it lie the ports an ordinary user may not open.</summary>
        public const int MinPort = 1024;

        /// <summary>The highest port there is.</summary>
        public const int MaxPort = 65535;

        /// <summary>Where an option came from, for the log line.</summary>
        public const string FromMarker = PortableDataDir.MarkerFileName;

        private static readonly string[] DataDirNames = { "dataDir", "data-dir" };
        private static readonly string[] HostPortNames = { "hostPort", "host-port" };

        private readonly bool _dataDirFromCommandLine;
        private readonly bool _hostPortFromCommandLine;

        private LaunchOptions(string? dataDir, bool dataDirFromCommandLine, int? hostPort, bool hostPortFromCommandLine,
            IReadOnlyList<string> warnings)
        {
            DataDir = dataDir;
            _dataDirFromCommandLine = dataDirFromCommandLine;
            HostPort = hostPort;
            _hostPortFromCommandLine = hostPortFromCommandLine;
            Warnings = warnings;
        }

        /// <summary>No option set: the platform's data folder and the default port.</summary>
        public static LaunchOptions Defaults { get; } = new LaunchOptions(null, false, null, false, Array.Empty<string>());

        /// <summary>The data folder as a full path, or null for the platform default.</summary>
        public string? DataDir { get; }

        /// <summary>Where <see cref="DataDir"/> came from: <see cref="DataDirArgument"/> or <see cref="FromMarker"/>;
        /// empty when it is not set.</summary>
        public string DataDirSource => DataDir == null ? string.Empty : _dataDirFromCommandLine ? DataDirArgument : FromMarker;

        /// <summary>The bundled server's port, or null for the default.</summary>
        public int? HostPort { get; }

        /// <summary>Where <see cref="HostPort"/> came from: <see cref="HostPortArgument"/> or <see cref="FromMarker"/>;
        /// empty when it is not set.</summary>
        public string HostPortSource => HostPort == null ? string.Empty : _hostPortFromCommandLine ? HostPortArgument : FromMarker;

        /// <summary>Options that were given but could not be used, as log lines.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// The options for an executable in <paramref name="exeDir"/>: <paramref name="markerContent"/> is the marker's
        /// text (null = no marker, <see cref="PortableDataDir.ReadMarker"/>), <paramref name="commandLine"/> the process
        /// arguments (the executable's own path at index 0 is harmless). Never throws.
        /// </summary>
        public static LaunchOptions Resolve(string exeDir, string? markerContent, IReadOnlyList<string>? commandLine)
        {
            var warnings = new List<string>();
            string? dataDir = null;
            int? hostPort = null;
            bool dataDirFromCommandLine = false, hostPortFromCommandLine = false;

            if (markerContent != null)
            {
                dataDir = TryResolveDirectory(exeDir, PortableDataDir.Resolve, markerContent, FromMarker, warnings);
                string? portText = PortableDataDir.OptionValue(markerContent, PortableDataDir.PortKey);
                if (portText != null)
                {
                    hostPort = ParsePort(portText, FromMarker, warnings);
                }
            }

            string? dirArg = ArgumentValue(commandLine, DataDirNames, DataDirArgument, warnings);
            if (dirArg != null)
            {
                string? fromArg = TryResolveDirectory(exeDir, PortableDataDir.ResolveDirectory, dirArg, DataDirArgument, warnings);
                if (fromArg != null)
                {
                    dataDir = fromArg;
                    dataDirFromCommandLine = true;
                }
            }

            string? portArg = ArgumentValue(commandLine, HostPortNames, HostPortArgument, warnings);
            if (portArg != null && ParsePort(portArg, HostPortArgument, warnings) is int port)
            {
                hostPort = port;
                hostPortFromCommandLine = true;
            }

            return new LaunchOptions(dataDir, dataDirFromCommandLine, hostPort, hostPortFromCommandLine, warnings);
        }

        /// <summary>
        /// The options that came from the command line, as arguments again — what the update restart hands on, so the
        /// restarted game keeps them. Options from the marker need no passing on: the file is still there.
        /// </summary>
        public string[] RestartArguments()
        {
            var args = new List<string>();
            if (_dataDirFromCommandLine && DataDir != null)
            {
                args.Add(DataDirArgument);
                args.Add(DataDir);
            }

            if (_hostPortFromCommandLine && HostPort is int port)
            {
                args.Add(HostPortArgument);
                args.Add(port.ToString(CultureInfo.InvariantCulture));
            }

            return args.ToArray();
        }

        private static string? TryResolveDirectory(string exeDir, Func<string, string, string> resolve, string text,
            string source, List<string> warnings)
        {
            try
            {
                return resolve(exeDir, text);
            }
            catch (Exception e)
            {
                warnings.Add($"{source} does not name a usable directory ('{text.Trim()}'): {e.Message}");
                return null;
            }
        }

        private static int? ParsePort(string text, string source, List<string> warnings)
        {
            if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port)
                && port >= MinPort && port <= MaxPort)
            {
                return port;
            }

            warnings.Add($"{source}: '{text.Trim()}' is no port between {MinPort} and {MaxPort} — the default port is used.");
            return null;
        }

        /// <summary>The value of the last occurrence of an option (<c>-name value</c> or <c>-name=value</c>), or null when
        /// it is not given. An option without a value is reported and ignored.</summary>
        private static string? ArgumentValue(IReadOnlyList<string>? args, string[] names, string display, List<string> warnings)
        {
            if (args == null)
            {
                return null;
            }

            string? value = null;
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i] ?? string.Empty;
                if (!arg.StartsWith("-", StringComparison.Ordinal))
                {
                    continue;
                }

                string name = arg.TrimStart('-');
                string? inline = null;
                int eq = name.IndexOf('=');
                if (eq >= 0)
                {
                    inline = name.Substring(eq + 1);
                    name = name.Substring(0, eq);
                }

                if (Array.FindIndex(names, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) < 0)
                {
                    continue;
                }

                // The value is the next argument — unless that is the next option (a forgotten value must not eat it).
                string? next = i + 1 < args.Count ? args[i + 1] : null;
                string? candidate = inline;
                if (candidate == null && next != null && !next.StartsWith("-", StringComparison.Ordinal))
                {
                    candidate = next;
                    i++;
                }

                if (string.IsNullOrWhiteSpace(candidate))
                {
                    warnings.Add($"{display} is given without a value — ignored.");
                    continue;
                }

                value = candidate;
            }

            return value;
        }
    }
}
