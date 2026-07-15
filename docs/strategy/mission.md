# Mission — Blocks Beyond the Stars

> The strategy trio: [vision.md](vision.md) (where we want to be), **mission.md** (what we do and
> how we decide), [roadmap.md](roadmap.md) (the concrete path).

## Mission statement

**We build and operate a free, open-source, kid-friendly voxel space game — and we make its
existing mechanics bug-free and satisfying before we make them bigger.** We keep it fast enough to
run in a browser tab and on a Raspberry Pi, we keep it safe enough for children to play online, and
we carry it to the places players actually are: glitch.fun, itch.io, our own portal — and, as a
long-held wish rather than a business plan, **Xbox and Steam**.

## What we do (in priority order)

1. **Fix before feature.** A shipped mechanic that half-works (a station that ignores `E`, a
   recipe chain that ends in an unused item, a HUD that leaks into space flight) outranks any new
   content. Every release should shrink the "known broken/odd" list.
2. **Deepen what exists.** More items, materials and crafting recipes are welcome **when they
   connect to existing loops** — a new ore needs a chain (mine → refine → craft → use), not just an
   atlas tile. Content is data-driven by design; we exploit that for cheap, well-connected additions.
3. **Operate the live game.** glitch.fun arcade, the hosted fleet and the portal are production:
   launch bugs get patched in days, moderation levers exist before problems do, and every release
   reaches all channels from one tagged pipeline.
4. **Widen where it runs.** Browser and desktop today; documented Raspberry-Pi self-hosting next;
   Steam and Xbox as deliberate, researched milestones — because having our game there would be
   wonderful, not profitable.

## Operating principles

### Kid-friendly is a hard constraint
- No horror, no gore, no gambling patterns, no punishing loss mechanics; bilingual DE+EN.
- Online safety scales with reach: password-gated private worlds on our portal (the "Baumhaus
  rule"), install-scoped guests on glitch.fun, moderation tooling grows **before** audience does.
- Ratings and store policies for kids (IARC/PEGI, console cert) are treated as design inputs, not
  paperwork at the end.

### Performance is a budget, not a wish
- Reference targets: a WebGL tab on a school laptop, and a **Raspberry Pi (ARM64, 4–8 GB) hosting a
  family world** — the server already publishes `linux-arm64`; we keep it that way and measure,
  not guess (per-world RAM, tick headroom, SD-card write load).
- New server features must state their tick/memory cost; new client features must survive the
  WebGL "Lite" profile.

### Quality has a routine
- Authoritative server + full test suite (1100+ tests) stay green; regressions get a test.
- Changes to the client get a local Unity build check; releases go through the tagged CI pipeline
  (installers, Docker multi-arch, itch.io, WebGL `/play`, glitch.fun) — never hand-rolled.
- Real playtests (the standing playtest issues, and our own kids) are part of "done".

### Open by default
- Source, content data, docs and honest devblogs stay public; community contributions (languages,
  designs, playtests, expert feedback) are actively invited and credited.
- English for docs and code; the game itself speaks German and English.

## Who this is for

- **Kids and families** who want a safe, curious space sandbox — solo in a browser or together on
  a home LAN.
- **The two of us** — this is a father-son project; joy of building it is a first-class goal,
  which is exactly why *our game on the Xbox* matters despite earning nothing.
- **Tinkerers and contributors** who want a readable, open, data-driven game they can run on their
  own hardware and extend with a JSON file.

## What we say no to

- Monetization of any kind, and features whose main value is monetization-shaped (battle passes,
  timers, FOMO).
- Big-bang rewrites and speculative engines; we evolve the shipped architecture.
- New mechanics while their neighbors are broken — depth follows solidity.
- Reach without moderation: we do not open doors (public listings, open registration, bigger
  arcade pools) faster than our safety tooling grows.
