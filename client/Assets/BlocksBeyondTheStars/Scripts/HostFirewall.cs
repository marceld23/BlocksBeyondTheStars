// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WEBGL
using System.Runtime.InteropServices;
using System.Text;
#endif
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Windows Firewall and the hosted world (#3708). Singleplayer and joining a server need no firewall rule
    /// (#3707); hosting does, because other machines dial in. Rather than leave that to the Windows dialog — which
    /// opens behind a fullscreen game and leaves block rules behind when a child dismisses it — the host screen
    /// asks here first: <see cref="Check"/> reads the rules (no elevation needed), <see cref="RequestAllow"/> asks
    /// Windows once, with elevation, for one narrow allow rule. The verdict itself is
    /// <see cref="HostFirewallRules"/> (Unity-free, unit-tested). Everything is a no-op off Windows.
    /// </summary>
    public static class HostFirewall
    {
        /// <summary>The player chose "host anyway" in this session: do not ask again until the game restarts.</summary>
        public static bool Skipped { get; set; }

        /// <summary>What Windows will do with friends dialing the bundled server at <paramref name="serverExe"/> on
        /// UDP <paramref name="port"/>. <see cref="HostFirewallState.Allowed"/> whenever there is nothing to ask
        /// about: not Windows, the firewall switched off, no rule store (Proton), or the rules cannot be read.</summary>
        public static HostFirewallState Check(string serverExe, int port)
        {
#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WEBGL
            try
            {
                if (!FirewallSwitchedOn())
                {
                    return HostFirewallState.Allowed;
                }

                var rules = new List<string>();
                ReadRules(LocalRulesKey, rules);
                if (rules.Count == 0)
                {
                    return HostFirewallState.Allowed; // a Windows without a single rule is not a real Windows Firewall
                }

                ReadRules(PolicyRulesKey, rules);
                return HostFirewallRules.Evaluate(rules, serverExe, port);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Windows Firewall rules could not be read ({e.Message}); hosting without the check.");
                return HostFirewallState.Allowed;
            }
#else
            return HostFirewallState.Allowed;
#endif
        }

        /// <summary>Asks Windows (one elevation prompt) to allow the bundled server on UDP <paramref name="port"/>.
        /// Blocks until the prompt is answered and the rule is written, so call it off the main thread. Returns
        /// false when the request could not be started or was declined; the caller checks the rules again either
        /// way, because the rule list is the truth.</summary>
        public static bool RequestAllow(string serverExe, int port)
        {
#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WEBGL
            string arguments = HostFirewallRules.AllowCommand(serverExe, port);
            if (arguments == null)
            {
                return false;
            }

            try
            {
                // Full paths: an elevated process must not be picked from the search path.
                string system = Environment.SystemDirectory;
                var info = new ShellExecuteInfo
                {
                    Size = Marshal.SizeOf(typeof(ShellExecuteInfo)),
                    Mask = NoCloseProcess | NoAsync | NoErrorUi,
                    Verb = "runas",
                    File = System.IO.Path.Combine(system, "cmd.exe"),
                    Parameters = arguments,
                    Directory = system, // cmd.exe finds netsh.exe here first
                    Show = 0, // hidden
                };
                if (!ShellExecuteEx(ref info) || info.Process == IntPtr.Zero)
                {
                    return false; // declined at the elevation prompt, or no administrator at hand
                }

                WaitForSingleObject(info.Process, 30000);
                CloseHandle(info.Process);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not ask Windows for the firewall rule: {e.Message}");
                return false;
            }
#else
            return false;
#endif
        }

#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WEBGL
        private const string PolicyRoot = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
        private const string LocalRulesKey = PolicyRoot + @"\FirewallRules";
        private const string PolicyRulesKey = @"SOFTWARE\Policies\Microsoft\WindowsFirewall\FirewallRules";

        private static readonly IntPtr LocalMachine = new IntPtr(unchecked((int)0x80000002));
        private const int KeyRead = 0x20019;
        private const int ErrorMoreData = 234;
        private const int RegSz = 1;
        private const int RegDword = 4;
        private const int MaxNameChars = 16384;
        private const uint NoCloseProcess = 0x40;
        private const uint NoAsync = 0x100;
        private const uint NoErrorUi = 0x400;

        /// <summary>False only when all three network profiles have the firewall switched off (another firewall
        /// product is in charge then, and its rules are not ours to read).</summary>
        private static bool FirewallSwitchedOn()
        {
            bool sawSwitch = false;
            foreach (string profile in new[] { "DomainProfile", "StandardProfile", "PublicProfile" })
            {
                if (RegOpenKeyEx(LocalMachine, PolicyRoot + "\\" + profile, 0, KeyRead, out IntPtr key) != 0)
                {
                    continue;
                }

                try
                {
                    var data = new byte[4];
                    int length = data.Length;
                    if (RegQueryValueEx(key, "EnableFirewall", IntPtr.Zero, out int type, data, ref length) == 0 && type == RegDword)
                    {
                        sawSwitch = true;
                        if (BitConverter.ToInt32(data, 0) != 0)
                        {
                            return true;
                        }
                    }
                }
                finally
                {
                    RegCloseKey(key);
                }
            }

            return !sawSwitch; // no switch readable: assume the default, which is on
        }

        /// <summary>Appends every rule string under <paramref name="subKey"/> (HKLM) to <paramref name="rules"/>;
        /// a key that does not exist adds nothing.</summary>
        private static void ReadRules(string subKey, List<string> rules)
        {
            if (RegOpenKeyEx(LocalMachine, subKey, 0, KeyRead, out IntPtr key) != 0)
            {
                return;
            }

            try
            {
                var name = new byte[MaxNameChars * 2];
                var data = new byte[8192];
                for (int index = 0; ; index++)
                {
                    int nameLength = MaxNameChars;
                    int dataLength = data.Length;
                    int result = RegEnumValue(key, index, name, ref nameLength, IntPtr.Zero, out int type, data, ref dataLength);
                    if (result == ErrorMoreData && data.Length < (1 << 20))
                    {
                        data = new byte[Math.Max(dataLength, data.Length * 2)];
                        index--; // read this value again into the larger buffer
                        continue;
                    }

                    if (result != 0)
                    {
                        break; // no more values
                    }

                    if (type == RegSz && dataLength >= 2)
                    {
                        rules.Add(Encoding.Unicode.GetString(data, 0, dataLength).TrimEnd('\0'));
                    }
                }
            }
            finally
            {
                RegCloseKey(key);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellExecuteInfo
        {
            public int Size;
            public uint Mask;
            public IntPtr Window;
            [MarshalAs(UnmanagedType.LPWStr)] public string Verb;
            [MarshalAs(UnmanagedType.LPWStr)] public string File;
            [MarshalAs(UnmanagedType.LPWStr)] public string Parameters;
            [MarshalAs(UnmanagedType.LPWStr)] public string Directory;
            public int Show;
            public IntPtr InstApp;
            public IntPtr IdList;
            [MarshalAs(UnmanagedType.LPWStr)] public string Class;
            public IntPtr KeyClass;
            public uint HotKey;
            public IntPtr Icon;
            public IntPtr Process;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegOpenKeyExW")]
        private static extern int RegOpenKeyEx(IntPtr key, string subKey, int options, int access, out IntPtr result);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegEnumValueW")]
        private static extern int RegEnumValue(IntPtr key, int index, [Out] byte[] name, ref int nameLength, IntPtr reserved,
            out int type, [Out] byte[] data, ref int dataLength);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegQueryValueExW")]
        private static extern int RegQueryValueEx(IntPtr key, string name, IntPtr reserved, out int type, [Out] byte[] data, ref int dataLength);

        [DllImport("advapi32.dll")]
        private static extern int RegCloseKey(IntPtr key);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ShellExecuteExW", SetLastError = true)]
        private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

        [DllImport("kernel32.dll")]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
#endif
    }
}
