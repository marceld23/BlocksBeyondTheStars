# ADR 0020 — Seasonal festivals: the calendar sets the default, nothing can be missed

- **Status:** Accepted
- **Date:** 2026-10-30
- **Context source:** epic [#3000](https://github.com/marceld23/BlocksBeyondTheStars/issues/3000) (maintainer
  decisions 2026-10-29); [mission.md](../../strategy/mission.md) "What we say no to" and "Kid-friendly is a hard
  constraint"; ADR 0001 (one authoritative server).

## Context

The game wants festivals of the real-world calendar: a cosy-spooky Halloween week with pumpkins, lantern-lit December
nights, petals at Hanami, shooting stars at Tanabata — with a mood, a few extra things in the world and a small challenge
each. Three facts constrained the design:

1. The mission rules out "features whose main value is monetization-shaped (battle passes, timers, FOMO)". A reward that
   can only be earned in a window is exactly that shape, and the game's own festival and wish systems already wait
   without deadlines for that reason.
2. The mission forbids horror and fear-driven mechanics; Halloween must be cosy, never frightening.
3. The game speaks fourteen languages and is played by kids around the world; a Western-only or religion-framed festival
   list would leave most of them out.

World generation is a pure function of seed, planet and generation number, and existing worlds must keep their natural
blocks (goldens, the no-row optimisation) — so the content a festival puts into a world must be a runtime layer.

## Decision

1. **The calendar sets the default only.** Inside a festival's window the server switches it on; outside, a world admin
   (in singleplayer: the player) can switch any festival on by hand (`/festival <key>`) and it stays on until
   `/festival calendar`. Rewards are decoration only and progress persists. Nothing in a festival can be missed.
2. **Individual festivals, each with a kid-friendly two-sentence explanation**, framed culturally (what people do and
   where), never through religious iconography; fifty-two festivals from all fourteen language communities and beyond,
   including the lunar, Hebrew, Islamic and Easter-bound calendars with explicit dates maintained a year ahead.
3. **Server is the truth, the bundle is the schedule.** The server evaluates its bundled `data/festivals.json` against
   its UTC clock; the client runs the same code on the same file for the main menu. No online fetch of the schedule —
   the release cadence is short enough, and a self-hosted LAN server must work offline.
4. **Order of authority:** operator override (never saved; the bundled singleplayer host passes the client's global
   setting this way) → world rule (default on, so every existing save gets the festivals) → manual pick → calendar.
5. **Existing saves get festivals through runtime layers only** (the weather-snow deposit pattern for festival spawns,
   client-side cosmetics for the mood); world generation changes only for new worlds (the wild pumpkin species).
6. **Cosmetics stay on the client, gameplay on the server.** Motes, grade, fog, snow caps and music are client-side
   parameters on existing systems, switchable per client; spawns, challenges and the active list are the server's.

## Consequences

- A festival window needs no release to be *played*: any festival is reachable any day by the admin's choice. Changing
  dates or adding a festival is a data-and-locale change in a normal release.
- The dated festivals need their next year added before 1 January of `datesThrough`; a content test reminds us.
- Overlapping festivals are normal (Christmas and Kwanzaa); the primary one (started last) drives chip and mood.
- Southern-hemisphere players see "Midsummer" in their winter; festivals are therefore named after what they are, not
  after a season.
- Native speakers are invited to review the explanations ([#3008](https://github.com/marceld23/BlocksBeyondTheStars/issues/3008)).
