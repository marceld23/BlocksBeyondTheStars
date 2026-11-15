// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What Windows Firewall will do with other machines dialing the hosted world.</summary>
    public enum HostFirewallState
    {
        /// <summary>An allow rule covers the bundled server: friends can join, and Windows will not ask.</summary>
        Allowed,

        /// <summary>No rule names the server: Windows blocks other machines and shows its own dialog.</summary>
        NoRule,

        /// <summary>Only block rules name the server (a dismissed dialog leaves them behind): nobody can join,
        /// and Windows will not ask again.</summary>
        Blocked,
    }

    /// <summary>
    /// Reads Windows Firewall rules for the hosted world (#3708). Hosting is the one case that needs an inbound
    /// allow rule — other machines dial in — so the host screen checks the rules before the world starts rather
    /// than leaving the player with a join address nobody can reach.
    /// <para>
    /// The rules come as the strings Windows keeps under
    /// <c>HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules</c>, e.g.
    /// <c>v2.33|Action=Allow|Active=TRUE|Dir=In|Protocol=17|Profile=Private|LPort=31550|App=C:\…\Server.exe|Name=…|</c>.
    /// The Unity client reads them (no elevation needed); this class is pure, so the evaluation is covered by the
    /// headless tests.
    /// </para>
    /// </summary>
    public static class HostFirewallRules
    {
        /// <summary>The display name of the allow rule the game asks Windows for.</summary>
        public const string RuleName = "Blocks Beyond the Stars (hosting)";

        private const int UdpProtocol = 17;
        private const int Domain = 1;
        private const int Private = 2;
        private const int Public = 4;
        private const int EveryProfile = Domain | Private | Public;

        /// <summary>
        /// The verdict for inbound UDP on <paramref name="udpPort"/> to the program at
        /// <paramref name="serverExePath"/>. A block rule beats an allow rule in the same network profile, as in
        /// Windows; the world counts as allowed when at least one profile is left allowed (Windows 10's dialog
        /// writes "allow on private, block on public", and that host is fine at home).
        /// </summary>
        public static HostFirewallState Evaluate(IEnumerable<string> rules, string serverExePath, int udpPort)
        {
            string exe = NormalizePath(serverExePath);
            int allowed = 0;
            int blocked = 0;
            foreach (string rule in rules)
            {
                if (!TryMatch(rule, exe, udpPort, out bool allow, out int profiles))
                {
                    continue;
                }

                if (allow)
                {
                    allowed |= profiles;
                }
                else
                {
                    blocked |= profiles;
                }
            }

            if ((allowed & ~blocked) != 0)
            {
                return HostFirewallState.Allowed;
            }

            return blocked != 0 ? HostFirewallState.Blocked : HostFirewallState.NoRule;
        }

        /// <summary>
        /// The arguments for an elevated <c>cmd.exe</c> that replaces the server's rules by one narrow allow rule:
        /// inbound, UDP, the gameplay port, this program. Every network profile and remote address, because home
        /// networks are often classified "Public" and a port-forwarded host has callers from outside the subnet.
        /// Returns <c>null</c> when the path cannot be quoted safely for the command line.
        /// </summary>
        public static string? AllowCommand(string serverExePath, int udpPort)
        {
            string exe = (serverExePath ?? string.Empty).Trim().Replace('/', '\\');
            if (exe.Length == 0 || udpPort <= 0 || udpPort > 65535)
            {
                return null;
            }

            foreach (char c in exe)
            {
                // A quote would end the quoted path; a percent sign would be expanded by cmd.exe.
                if (c == '"' || c == '%' || char.IsControl(c))
                {
                    return null;
                }
            }

            string port = udpPort.ToString(CultureInfo.InvariantCulture);
            return "/c \"netsh advfirewall firewall delete rule name=all program=\"" + exe + "\""
                + " & netsh advfirewall firewall add rule name=\"" + RuleName + "\" dir=in action=allow program=\"" + exe + "\""
                + " protocol=UDP localport=" + port + " profile=any\"";
        }

        /// <summary>True when <paramref name="rule"/> is an active inbound rule that applies to the program and
        /// port; then <paramref name="allow"/> is its action and <paramref name="profiles"/> the profiles it covers.</summary>
        private static bool TryMatch(string rule, string exe, int udpPort, out bool allow, out int profiles)
        {
            allow = false;
            profiles = 0;
            if (string.IsNullOrEmpty(rule))
            {
                return false;
            }

            var fields = default(RuleFields);
            foreach (string field in rule.Split('|'))
            {
                int eq = field.IndexOf('=');
                if (eq > 0 && !fields.Read(field.Substring(0, eq), field.Substring(eq + 1), exe, udpPort))
                {
                    return false;
                }
            }

            // A rule that names a program has to name this one. A rule without one counts only as a plain port rule
            // ("open UDP 31550"): Windows keeps many program-less rules that are scoped by fields this reader does not
            // model, and reading those as "everything is allowed" would hide a blocked host.
            bool applies = fields.HasApp ? fields.AppMatches && (!fields.HasPort || fields.PortMatches) : fields.PortMatches;
            if (!fields.HasAction || !fields.Active || !fields.Inbound || !applies)
            {
                return false;
            }

            allow = fields.Allow;
            profiles = fields.Profiles == 0 ? EveryProfile : fields.Profiles; // no Profile= field: the rule applies everywhere
            return true;
        }

        /// <summary>What one rule string says, as far as the verdict needs it.</summary>
        private struct RuleFields
        {
            public bool HasAction;
            public bool Allow;
            public bool Active;
            public bool Inbound;
            public bool HasApp;
            public bool AppMatches;
            public bool HasPort;
            public bool PortMatches;
            public int Profiles;

            /// <summary>Takes one <c>Key=Value</c> field in. Returns false when the field alone rules the rule out:
            /// another protocol, an action that is neither allow nor block, a service's or a Store app's rule.</summary>
            public bool Read(string key, string value, string exe, int udpPort)
            {
                switch (key)
                {
                    case "Action":
                        HasAction = true;
                        Allow = string.Equals(value, "Allow", StringComparison.OrdinalIgnoreCase);
                        return Allow || string.Equals(value, "Block", StringComparison.OrdinalIgnoreCase);
                    case "Active":
                        Active = string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase);
                        return true;
                    case "Dir":
                        Inbound = string.Equals(value, "In", StringComparison.OrdinalIgnoreCase);
                        return true;
                    case "Protocol":
                        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int protocol) && protocol == UdpProtocol;
                    case "Profile":
                        Profiles |= ProfileBit(value);
                        return true;
                    case "App":
                        HasApp = true;
                        AppMatches = string.Equals(NormalizePath(value), exe, StringComparison.OrdinalIgnoreCase);
                        return true;
                    case "Svc":
                    case "PFN":
                    case "AppPkgId":
                    case "LUOwn":
                    case "LUAuth":
                        return false; // a service's or a packaged (Store) app's rule — not this program
                    default:
                        if (key.StartsWith("LPort", StringComparison.Ordinal))
                        {
                            HasPort = true;
                            PortMatches |= PortCovers(value, udpPort);
                        }

                        return true;
                }
            }
        }

        private static int ProfileBit(string value)
        {
            if (string.Equals(value, "Domain", StringComparison.OrdinalIgnoreCase))
            {
                return Domain;
            }

            if (string.Equals(value, "Private", StringComparison.OrdinalIgnoreCase))
            {
                return Private;
            }

            return string.Equals(value, "Public", StringComparison.OrdinalIgnoreCase) ? Public : 0;
        }

        /// <summary>A port field: one number (<c>31550</c>) or a range (<c>31000-32000</c>); keywords such as
        /// <c>RPC</c> never cover a game port.</summary>
        private static bool PortCovers(string value, int port)
        {
            int dash = value.IndexOf('-');
            if (dash < 0)
            {
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int single) && single == port;
            }

            return int.TryParse(value.Substring(0, dash), NumberStyles.Integer, CultureInfo.InvariantCulture, out int low)
                && int.TryParse(value.Substring(dash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int high)
                && port >= low && port <= high;
        }

        /// <summary>A program path as rules spell it: backslashes, environment variables expanded, no quotes.</summary>
        private static string NormalizePath(string path)
        {
            string value = (path ?? string.Empty).Trim().Trim('"').Replace('/', '\\');
            return value.IndexOf('%') >= 0 ? Environment.ExpandEnvironmentVariables(value) : value;
        }
    }
}
