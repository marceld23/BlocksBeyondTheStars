# Information for Parents & Teachers

*Deutsche Fassung: [PARENTS.de.md](PARENTS.de.md)*

Blocks Beyond the Stars is a family project — designed by a father and his ten-year-old son so that
children can play it, alone or together with adults. This page says plainly what is in the game, what is
not, what online play involves, and which switches you as a parent, teacher or host control.

> **No official age rating yet.** IARC ratings (PEGI / USK / ESRB in one questionnaire) are issued through
> participating storefronts, which this game is not on yet — so what follows is our own honest statement,
> not a certified label. The groundwork for the first real questionnaire is prepared
> ([docs/developer/AGE_RATING_CHECKLIST.md](../developer/AGE_RATING_CHECKLIST.md)); our own assessment is
> that the content corresponds to **PEGI 7 / USK 6 / ESRB E** territory (mild fantasy violence), with the
> usual storefront notes "Users Interact" and "User-Generated Content" for online play.

## Content statement

*(This is the statement we also publish on the website and store pages.)*

**Blocks Beyond the Stars is a block-building space game for families.** You explore procedurally
generated planets, mine resources, craft gear, build ships and bases, tame creatures and play together.

- **Mild sci-fi combat, no gore.** Players can fend off aggressive wildlife, robots and cartoonish
  "bandits" with tools and sci-fi weapons. There is no blood, no gore, no death animations of people —
  defeated creatures and robots simply break apart or flee; bandits are **chased away, never killed**.
  By default, new worlds start with **weapons in "tools only" mode** — combat is opt-in per world.
- **Player death is gentle.** Running out of air or health means waking up again in your ship's med-bay.
  Items are not lost to other players.
- **No horror.** Dark caves and a spooky ruin at most — the game's tone is curious, not frightening.
- **No purchases, no ads, no gambling.** The game is free and open source. There is nothing to buy
  in-game, no advertising, no loot boxes, and the arcade minigames award only in-game knowledge points.
- **No personal data required.** Playing needs no e-mail address, no real name and no account beyond a
  self-chosen player name. See "Data" below.
- **Online play is optional.** Singleplayer works fully offline (and in the browser). Multiplayer is
  something you or your kids choose to join.

## Online play — what it involves

When playing on a multiplayer server, your child can meet other players. That means:

- **Text chat and optional voice chat with strangers** (on public servers). Nobody transmits by accident:
  **talking is push-to-talk** (hold a key), while *listening* is on by default on a local/LAN game and can
  be switched off in Settings → Voice. Public hosted worlds ship with voice off, and any server host can
  disable it entirely.
- **User-generated content:** players type names (for themselves, bases, beacons, creatures, crews, map
  markers), build freely with blocks, and can paint designs. Anything typed or built can be seen by
  others on that server.

What protects players there — these are built into the game, not promises:

- A **chat and name filter** is on by default (server operators choose the level; "strict" is available
  for family servers, and every player-typed name goes through the same screen).
- **`/report`** lets any player — including kids on the free browser version — flag a player or message
  to the server operators with one command; on official servers reports land in a reviewed inbox.
  Nobody is auto-punished; a human looks at it.
- **`/mute <name>`** hides a player's chat and voice locally — the muted player is not told.
- Operators can **silence** a player server-wide, and hosts fully control their world's rules.
- **No join codes or friend codes** exist: groups (crews) can only be formed by inviting a player who is
  online in the same world at that moment.

**The safest setup for young children** is your own private world: host it on your own machine (LAN) or
rent nothing at all — the game ships with everything needed. See
[SELF_HOSTING](../developer/SELF_HOSTING.md), or simply play singleplayer. A private family world has
exactly the players you invited, and you hold every switch (weapons mode, voice, chat filter, visitors).

## Data

- **Accounts are pseudonymous by design.** No e-mail, no real name, no date of birth is asked for
  anywhere. A player name is all there is.
- The optional worlds portal stores that player name and technical session data needed to run hosted
  worlds; bug reports (F1/F2) contain the text the player types plus technical logs and are read by the
  maintainers.
- There is **no analytics/tracking SDK, no ad network** in the game.
- The optional AI backend (for dynamic NPC dialogue) processes the in-game conversation text to generate
  replies; it is off unless the host sets it up, and it has a non-AI fallback.

## Time, money, fairness

- No energy timers, no daily-login mechanics, no pay-to-progress — the game does not use retention
  tricks. Progress is saved locally (or on the world's server) and waits for you.
- Multiplayer is cooperative by default. Allies cannot harm each other, even on servers where combat
  between players is enabled by the host.

## Questions or concerns

Open an issue on [GitHub](https://github.com/marceld23/BlocksBeyondTheStars/issues) or use the in-game
feedback key (**F1**, browser **F2**) — both land with the maintainers (a father playing this with his
own kids). We take "my child saw something inappropriate" reports seriously and act on them.
