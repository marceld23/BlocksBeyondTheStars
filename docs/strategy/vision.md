# Vision — Blocks Beyond the Stars

> The strategy trio: **vision.md** (where we want to be), [mission.md](mission.md) (what we do and
> how we decide), [roadmap.md](roadmap.md) (the concrete path). Status tracking stays in
> [TODO.md](../../TODO.md); this document changes rarely.

## The one-sentence vision

**A voxel space adventure that any kid can safely play anywhere — in a browser tab, on the family
PC, on a Raspberry Pi under the desk, and one day on the living-room console — built in the open by
a father and son who love making it.**

## What the world looks like when we get there

### A finished-feeling game, not a tech demo

The core loop — *explore planets, mine materials, craft better gear, build bases and ships, fly to
the next star* — works without rough edges. Every station, device and item in the game **does
something**; there are no decorative dead ends, no recipes into nowhere, no "this button does
nothing yet". A new player always knows what to do next, and a returning player always has a next
goal on the tech tree.

### Deep enough to stay, gentle enough for kids

Crafting and progression have real depth — tiered tools, metallurgy chains, factories, blueprints —
but the game never punishes: no fear-driven mechanics, no harsh loss on death, no reading-age walls
(bilingual DE/EN, symbols over text). Depth comes from *more to discover and combine*, not from
grind or pressure. "Kid-friendly" is a design pillar, not a marketing label: content, chat, names
and community stay safe by design.

### Playable anywhere, hostable by anyone

- **One click in the browser** — the glitch.fun arcade and our own portal make the first minute
  free of installers, accounts and passwords.
- **At home on anything** — the dedicated server runs on small hardware, explicitly including a
  Raspberry Pi for a family or classroom LAN world. Self-hosting is a documented first-class path,
  not an afterthought.
- **On the platforms we grew up with** — the game on **Xbox** (and Steam) is a heart goal: not for
  revenue, but because seeing our own game on the console shelf is the point of making it.

### Carried by a community, not just by us

The game grows a community that supports it from every side — and there is a real seat at the
table for each of them:

- **People who play and make us better.** Players — kids, parents, friends — who report what's
  confusing, what's too dark, what's boring, and what they'd love next. Playtesting is a
  first-class contribution: the standing playtest issues, the in-game report flow and the devblog
  comment threads are how the game actually improves.
- **People who build on GitHub.** From the one-line locale fix to a whole platform port (the
  Linux client was our first community contribution) — the source stays open, the content stays
  data-driven (JSON in `data/`), and adding an item, recipe, language or ship design is something
  a motivated teenager can do. Hobby programmers are as welcome as professionals.
- **Experts from every craft.** Graphic artists, musicians, sound designers, game designers,
  UX reviewers, translators, engine specialists — the "expertise wanted" issues invite exactly
  this: feedback-only contributions count fully, no code required.

Every contribution is credited (in-game and in the repo), invitations stay concrete and honest
("design a village", "review our crafting UX", "is the audio balanced?"), and the devblog tells
the story openly — including the failures — so people know what they are joining.

## What we are deliberately NOT

- **Not a commercial product.** Free, no monetization, no ads, no dark patterns. Platform releases
  (glitch.fun, itch.io, Steam, Xbox) are distribution and pride, not revenue.
- **Not a Minecraft clone race.** We don't chase feature parity with the giants; we keep the scope
  a two-person hobby team can polish to a high standard.
- **Not a hardware-hungry showcase.** Every feature must survive the question: *does it still run
  in a browser tab and on a Pi?* Performance is a feature, not an optimization phase.
- **Not an unmoderated public playground.** Multiplayer stays password/word-of-mouth on our portal
  and platform-account-scoped on glitch.fun; we grow moderation before we grow reach.

## How we know we are getting closer

- Playtesters (especially kids) finish their first session without help and want to come back.
- Contributions arrive from beyond the two of us — code and non-code alike (playtest reports,
  art, music, translations, design feedback) — and repeat contributors stick around.
- Zero known "does nothing" mechanics in a release; the open-bug list trends short and young.
- A stock Raspberry Pi hosts a family world for 4+ players at stable tick rate.
- The game is live and healthy on glitch.fun, itch.io and our portal from one release pipeline —
  and eventually installable on Steam and Xbox.
