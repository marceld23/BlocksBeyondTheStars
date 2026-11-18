# Hard Survival and the fallen (#3765)

A young player's wish: *"an apocalypse mode with extremely many machines … oxygen and hunger run down slowly, and a
death means you can look at the world but nothing else; in multiplayer the others can bring you back."* Creative mode
stays; Hard Survival is a **fourth mode** of the new-world screen. This note says what the mode is made of and how the
death rule works.

## The mode is six existing rules plus one death rule

The fourth card (`UiSaveSelect.Create.cs`, mode 3) launches the bundled server with the player's world options and
`WorldCreationOptions.HardSurvival` on top:

| Rule | Value | Server switch |
|---|---|---|
| Alien enemies (planets) | Extreme | `--planet-enemies Extreme` |
| Bandits | Extreme | `--bandits Extreme` |
| Environmental hazards | Hard | `--hazards Hard` |
| Oxygen consumption | Slow | `--oxygen Slow` |
| Hunger | Slow | `--hunger Slow` |
| Death | **Revive** | `--death-penalty Revive` |

`/gamemode hard` (also `überlebenskampf`) sets the same six rules on a running world (`GameServerWorldMode.cs`);
leaving the mode puts the death rule back to Light and leaves the levels where they are.

There is **no stored mode**. As with Creative and Sandbox the mode is read back from the rules: a world whose rules
carry `DeathPenalty.Revive` is Hard Survival — in the server (`CurrentWorldMode`), in the save's sidecar
(`WorldSaveSummary.ModeOf` → `"hard"`) and in the world list (`SavedWorld.ModeKeyOf` → `ui.save.mode_hard`). Sandbox
wins over it (nothing dies in a sandbox).

`DeathPenalty.Revive` is **appended** to the enum, so stored values keep their meaning, and it needs no new row in the
server's option table (`--death-penalty` takes every enum name). The world-options page keeps its four steps
None … Hard; only the mode sets Revive (`WorldCreationOptions.ReviveOnDeath` replaces the step when it is set).

Not part of the mode, although the issue's first table listed them: *Aggressive creatures* (the rule has no reader
anywhere in the game) and *Frontier danger* (it has no launch switch; an admin can switch it on in the world rules).

## The fallen

Rules: `Shared/Definitions/FallenRules.cs`. Server: `GameServerFallen.cs`. Client: `FallenView.cs`, the banner's rule
in `Client.Core/FallenBanner.cs`.

**Falling.** `RespawnPlayer` first fills the salvage capsule as for Normal and Hard — the things stay at the place of
death — and then asks `FallInsteadOfRespawn`. It answers yes for a death **on foot on a world** under the Revive rule.
A death in flight, on a spacewalk, inside a ship or on a station, and a defeat by another player (#2972), respawn as
everywhere: there is no place a team-mate could walk to, or nothing to punish.

**Watching.** The fallen player

- cannot be hurt and is ignored by every hostile (`GodMode` — a session flag, never saved),
- flies (`Fly`), and the client flies the admins' observer camera, because the server reports
  `PlayerStateUpdate.Spectating` for them,
- is invisible to the other players (the presence's `Stealthed`),
- may send only what `FallenMayHandle` lets through: moving the camera, chat, the far view and the star map, a call
  for an admin — and the wish to start again.

It is deliberately **not** the admins' observer mode (`ObserverSystem`): that one gives up the parked ship and the
landing pad, hides the player from every list, logs as staff and may remove blocks. `session.Spectating` stays false
for the fallen; only the client is told the same thing.

A light pulses every two seconds at the place somebody fell (`WorldFx` kind `signal_ping`, sent to the players on that
body), and everybody is told in the chat line that `{player}` has fallen.

**The ways back.**

| Situation | What happens |
|---|---|
| Another player uses a **medkit** within its radius (6 blocks) of the place of death | The fallen player stands up there with the medkit's health (45), full oxygen and at least 30 hunger. `MedkitWouldHeal` counts a fallen player in reach, so a medkit used only to revive is not refused as "everybody is healthy". |
| Nobody else is standing (singleplayer, or the others have left) | After `FallenRules.AloneSeconds` (60) the player may start again at the ship. |
| **Everybody** has fallen (two or more) | All of them may start again at once; the permission stays once given, so the first one standing again does not lock the others out. |

A restart is always the player's **own intent** — the death screen's `RespawnChoiceIntent`, sent by the banner when
the Interact control is pressed — and `RestartFallenAtShip` checks it again. The server tick never moves a player; it
only works out who may restart and tells the client when the answer changes kind (`RespawnOptions.Fallen`,
`RestartInSeconds`: -1 wait for the team, a positive count, 0 now). The client counts a positive number down itself.
The wake-up at the ship after watching is no second death: `RespawnNotice.Died` is false, so there is no second death
flash and no "you have died" gate.

**Leaving and crashing.** Nothing of the state is saved. A fallen player who leaves is put back at the ship, alive,
before the save (`FallenOnLeave`, a leave step) — leaving is no way to stand up where one fell, and nobody may come
back hanging in the air. For a save written while its player watched and never saw the leave (a crash), the player
carries the milestone `fallen:pending`; `LoadJoiningPlayer` undoes it the same way.

## Wire

No new message. `RespawnOptions` gained three additive fields (`Fallen`, `FallenAgain`, `RestartInSeconds`); an older
client shows its ordinary choice screen, whose answer the server refuses until a restart is allowed.

## Tests

`FallenTests` (server: falling, the capsule, the gate, the medkit there and only there, alone after a minute,
everybody fallen, leaving, the crash marker, the mode in the sidecar), `HardSurvivalWiringTests` (client: the banner's
rule, the world list, the fourth card's launch options, the wiring).

## Open

Never seen in the running game: the banner's place on the screen, the four narrower mode cards in every language,
whether Extreme machines plus Extreme bandits is fun or only loud. The fallen do not show in the player list as
fallen, and the place of death has no marker on the map — the pulsing light and the chat line carry it.
