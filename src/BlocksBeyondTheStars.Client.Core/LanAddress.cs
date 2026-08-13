// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One IPv4 address of one network interface, reduced to what the choice depends on.
    /// The Unity client fills these from <c>NetworkInterface.GetAllNetworkInterfaces()</c>; the tests
    /// fill them by hand.</summary>
    public readonly struct LanCandidate
    {
        public LanCandidate(string address, bool hasGateway, bool physical, string name)
        {
            Address = address ?? string.Empty;
            HasGateway = hasGateway;
            Physical = physical;
            Name = name ?? string.Empty;
        }

        /// <summary>The IPv4 address in dotted form.</summary>
        public string Address { get; }

        /// <summary>The interface has a default gateway — i.e. it is on a network that routes somewhere,
        /// which is the single strongest hint that this is the LAN the household actually uses.</summary>
        public bool HasGateway { get; }

        /// <summary>The interface reports itself as Ethernet or Wi-Fi (as opposed to tunnels, PPP and the
        /// assorted "unknown" types the virtual switches use).</summary>
        public bool Physical { get; }

        /// <summary>Interface name + description, used to spot virtual adapters by their vendor wording.</summary>
        public string Name { get; }
    }

    /// <summary>
    /// Picks the IPv4 address a friend on the same network should be told to join (#984).
    /// <para>
    /// "The first interface that is up" is not good enough: a developer or gamer box routinely carries a
    /// Hyper-V/WSL <c>vEthernet</c> switch, a VirtualBox host-only net (192.168.56.x), VMware, Docker, a
    /// VPN or a Tailscale/ZeroTier tunnel, and any of them can enumerate before the real Wi-Fi. Handing
    /// out one of those addresses looks perfectly plausible and is simply unreachable from the sofa.
    /// </para>
    /// <para>
    /// So the candidates are scored instead: a default gateway outweighs everything (no gateway = no
    /// network anyone else is on), a real Ethernet/Wi-Fi type beats an "unknown" one, adapter names that
    /// give a virtual switch away are penalised, and a home LAN range beats CGNAT. Ties keep enumeration
    /// order, so the answer is stable between two runs on the same machine. Pure and Unity-free, so the
    /// ranking is covered by the headless test suite rather than by one developer's NIC list.
    /// </para>
    /// </summary>
    public static class LanAddress
    {
        /// <summary>What callers get when the machine has no usable address (no network at all): the host
        /// can still play alone, and the port beside it stays meaningful.</summary>
        public const string Loopback = "127.0.0.1";

        /// <summary>Wording that gives away a virtual/tunnel adapter, matched case-insensitively against
        /// the interface name and description. Deliberately about *vendors and roles*, never about
        /// address ranges — a virtual switch can hand out a perfectly ordinary-looking 192.168 address.</summary>
        private static readonly string[] VirtualMarkers =
        {
            "vethernet", "hyper-v", "virtualbox", "vmware", "virtual", "wsl", "docker", "loopback",
            "tap-", "tap adapter", "tunnel", "teredo", "vpn", "wireguard", "openvpn", "nordlynx",
            "tailscale", "zerotier", "hamachi", "radmin", "bluetooth", "pseudo-interface",
        };

        /// <summary>The best address to advertise, or <see cref="Loopback"/> if nothing qualifies.</summary>
        public static string Pick(IEnumerable<LanCandidate> candidates)
        {
            if (candidates is null)
            {
                return Loopback;
            }

            string best = Loopback;
            int bestScore = int.MinValue;
            foreach (var c in candidates)
            {
                if (!IsUsable(c.Address))
                {
                    continue;
                }

                int score = Score(c);
                if (score > bestScore) // strictly greater ⇒ the first of equally good candidates wins
                {
                    bestScore = score;
                    best = c.Address;
                }
            }

            return best;
        }

        /// <summary>How good a candidate is; higher wins. Public so a diagnostic can print the ranking.</summary>
        public static int Score(LanCandidate candidate)
        {
            int score = 0;
            if (candidate.HasGateway)
            {
                score += 40;
            }

            if (candidate.Physical)
            {
                score += 20;
            }

            if (LooksVirtual(candidate.Name))
            {
                // Heavier than the "physical type" bonus so a named virtual switch never beats a real
                // adapter, yet lighter than a gateway: a virtual-looking name is a hint, not proof.
                score -= 30;
            }

            return score + RangeScore(candidate.Address);
        }

        /// <summary>True for an address worth showing at all: a dotted IPv4 that is not loopback, not
        /// link-local (169.254.x, "the cable is unplugged"), not a broadcast/unspecified address.</summary>
        public static bool IsUsable(string address)
        {
            if (!TryParse(address, out int a, out int b, out _, out _))
            {
                return false;
            }

            return a != 0            // 0.0.0.0/8 — unspecified
                && a != 127          // loopback
                && a < 224           // multicast + reserved + 255.255.255.255
                && !(a == 169 && b == 254); // link-local: no DHCP answered
        }

        /// <summary>Prefers the ranges a home network actually uses. Public addresses stay ahead of CGNAT
        /// (100.64/10), which is what a carrier or a mesh VPN hands out and what nobody can join over.</summary>
        private static int RangeScore(string address)
        {
            if (!TryParse(address, out int a, out int b, out _, out _))
            {
                return 0;
            }

            if (a == 192 && b == 168)
            {
                return 12; // the classic home LAN
            }

            if (a == 10)
            {
                return 10;
            }

            if (a == 172 && b >= 16 && b <= 31)
            {
                return 8;
            }

            if (a == 100 && b >= 64 && b <= 127)
            {
                return 1; // CGNAT / mesh-VPN space
            }

            return 5; // a routable public address — unusual at home, but reachable if it is real
        }

        private static bool LooksVirtual(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            string lower = name.ToLowerInvariant();
            foreach (string marker in VirtualMarkers)
            {
                if (lower.Contains(marker))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParse(string address, out int a, out int b, out int c, out int d)
        {
            a = b = c = d = -1;
            if (string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            string[] parts = address.Split('.');
            if (parts.Length != 4)
            {
                return false;
            }

            return Octet(parts[0], out a) && Octet(parts[1], out b) && Octet(parts[2], out c) && Octet(parts[3], out d);
        }

        private static bool Octet(string text, out int value)
        {
            value = -1;
            if (text.Length == 0 || text.Length > 3)
            {
                return false;
            }

            int n = 0;
            foreach (char ch in text)
            {
                if (ch < '0' || ch > '9')
                {
                    return false;
                }

                n = (n * 10) + (ch - '0');
            }

            value = n;
            return n <= 255;
        }
    }
}
