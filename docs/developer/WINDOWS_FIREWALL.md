# Windows Firewall: when it asks, and how the game avoids it

Windows Firewall shows its "allow this app?" dialog the moment a program binds a UDP socket to a
**fixed port on a network address** (or listens on TCP) and no rule names that program yet. Outbound
traffic and the replies to it need no rule. Rules are stored per full executable path.

That dialog is a real cost for this game:

- On an account without administrator rights Windows writes **block** rules whatever the player
  clicks, and never asks again. Children usually play on such accounts.
- An administrator who cancels the dialog gets block rules too. A block rule beats an allow rule.
- The dialog opens behind a fullscreen game and says nothing about why the game wants it.

So the game opens only the sockets a session needs (#3707) and handles the one case that does need
a rule — hosting — itself (#3708).

## What triggers the dialog (measured)

Windows 11, a probe program started from fresh folders so no rule existed:

| Bind | Dialog |
|---|---|
| Server on `127.0.0.1`, fixed port, plus a client on `127.0.0.1` (they connect) | no |
| Client with the IPv6 socket off (IPv4 any address, dynamic port) | no |
| Raw IPv6 socket, any address, dynamic port | no |
| LiteNetLib's default client: IPv4 dynamic port, then IPv6 on that same, now fixed, port | **yes** |
| Server on every interface, fixed port | **yes** |

The fourth row is why a client that only dialed out used to raise the dialog: LiteNetLib binds its
IPv6 socket to the port the IPv4 socket was given ("one port for two sockets").

## What the game binds

| Session | Server | Client | Rule needed |
|---|---|---|---|
| Singleplayer | `--bind 127.0.0.1` (loopback only) | loopback | none |
| Join a server (IPv4) | — | IPv4 only, dynamic port | none |
| Join a server that only has an IPv6 address | — | LiteNetLib's default | Windows may ask |
| Host a world | every interface, gameplay port | loopback (to its own server) | inbound allow rule |
| Dedicated server | every interface unless `--bind` / `BBS_BIND` says otherwise | — | the operator's |

- `UdpBind` (`src/BlocksBeyondTheStars.Networking/Transport/UdpBind.cs`) holds the choice:
  `ForTarget` picks the client's sockets, `Resolve` dials IPv4 first, `TryParseServerBind` reads the
  server option.
- `LiteNetLibClientTransport.Connect` binds by target; a repeated call (the connect retry) keeps its
  sockets.
- `ServerConfig.GameplayBindAddress` (`--bind`, `BBS_BIND`): empty = every interface, `127.0.0.1` =
  this machine only, or one IPv4 address. `LocalServerLauncher` passes `--bind 127.0.0.1` for a world
  with one player and nothing for a hosted world.
- Tests that open real sockets bind loopback, so a test run raises no dialog either.

## Hosting

Other machines dial in, so Windows needs an inbound allow rule for the bundled server. Before a
hosted world starts, the host screen checks (`HostFirewall.Check`, Windows only):

1. It reads the rule strings from the registry — no elevation needed —
   (`HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules`
   plus the group-policy store) and hands them to `HostFirewallRules.Evaluate` (Unity-free,
   `src/BlocksBeyondTheStars.Client.Core/HostFirewallRules.cs`, unit-tested).
2. **Allowed** — an allow rule covers inbound UDP on the gameplay port for the server exe and is not
   blocked in every profile: the world starts, Windows does not ask.
3. **No rule** or **Blocked** — a dialog explains that Windows has to allow the game once and that
   this takes an administrator account. Choices: *Allow now*, *Host anyway*, *Back*.

*Allow now* (`HostFirewall.RequestAllow`) starts an elevated `cmd.exe` (one UAC prompt) that deletes
the server's existing rules and adds one:

```
netsh advfirewall firewall add rule name="Blocks Beyond the Stars (hosting)" dir=in action=allow
      program="<server exe>" protocol=UDP localport=<port> profile=any
```

The rule covers every network profile and remote address on purpose: home networks are often
classified "Public", and a port-forwarded host has callers from outside the subnet. It is still
narrower than the rule the Windows dialog writes (any port, TCP and UDP). Afterwards the rules are
read again; the rule list decides, not the helper's exit code.

Details worth knowing:

- A program-less rule counts only when it is a plain port rule ("open UDP 31550"). Windows keeps
  many program-less rules scoped by other fields (Store apps: `PFN=`, `LUOwn=`); reading those as
  "everything is allowed" would hide a blocked host.
- "Allow on private, block on public" (what Windows 10's dialog writes) counts as allowed.
- The check answers "allowed" when there is nothing to ask about: not Windows, the firewall
  switched off in all profiles, no rule store (Proton), or an unreadable registry.
- *Host anyway* is remembered for the session only.
- Rules are per path, so every new build folder starts without one. The installed game keeps its
  path (`…\current\`) across updates.

## Not covered

- The MSI installer does not add the rule (Velopack builds the MSI from its own template).
- Hosting without any rule would need a rendezvous server (join codes, both sides dial out).
- Other firewall products are not read.
