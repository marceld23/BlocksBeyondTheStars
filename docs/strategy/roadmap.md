# Roadmap — Blocks Beyond the Stars

> The strategy trio: [vision.md](vision.md) (where we want to be), [mission.md](mission.md) (what
> we do and how we decide), **roadmap.md** (the concrete path). Day-to-day status lives in
> [TODO.md](../../TODO.md); this file orders the *horizons* and gets revisited when one completes.
>
> Written 2026-07-15 against v0.8.1, based on a code-verified analysis of the crafting/content
> systems, the server's ARM64/Raspberry-Pi readiness, the glitch.fun integration, and external
> research on Steam/Xbox publishing. Horizons are ordered by the mission's rule: **fix → deepen →
> operate → widen**. Version numbers are intents, not promises.

## Where we stand (2026-07-15)

- **Live:** v0.8.1 on all channels — Windows/Linux/macOS installers, itch.io, own portal + hosted
  fleet, WebGL `/play`, and the freshly launched **glitch.fun edition** (arcade worlds, in-browser
  singleplayer, cloud saves, auto-publish on tag). Suite: 1127 tests.
- **Content:** fully data-driven — 142 blocks, 167 items, 180 recipes, 57 blueprints, 8 crafting
  stations, a real blueprint tech-tree and ore→ingot→alloy→component chains. Content validation
  rejects dangling references at startup; savegames survive content additions via the id→palette
  remap shipped in v0.7.7.
- **Server:** .NET 10, authoritative, 15 Hz tick; `linux-arm64` self-contained publish and
  multi-arch Docker already exist; a bare `GameServer` runs standalone for LAN without any portal.
- **Known soft spots:** a fresh glitch.fun launch bug (#334), a handful of half-working mechanics
  (ship console station, UFO-kill on old saves, HUD leaks), shallow demand for many late-game
  materials, and no measured performance numbers for small hardware.

---

## Horizon 0 — Stabilize the glitch.fun launch (days; v0.8.x patches)

*The arcade is our widest door and it is currently jammed. Everything here is small and urgent.*

- **Merge + deploy the #334 fix** (branch `fix/glitch-install-call-order` exists, unmerged):
  the session gateway validates installs before creating them, so **every fresh visitor gets 403**
  — the arcade is effectively closed to the public until this ships and the WorldHost is
  redeployed.
- **Live launch smoke test** on play.glitch.fun after the fix: fresh-install auto-join, pointer
  lock inside Glitch's iframe, touch controls on a tablet, cloud-save round-trip with a logged-in
  account. (No iframe `allow`/pointer-lock handling exists in code — verify it's actually fine.)
- **Fix the store-page copy inconsistency:** `tools/glitch-media/instructions.md` tells players to
  use `/report`, which arcade guests *cannot* do (needs a portal session). Reword to the real
  recourse until guest reporting exists.
- **Release-pipeline check:** confirm `release.yml` passes `linux/amd64,linux/arm64` to
  `docker.yml` (its `platforms` default is amd64-only) so tagged server images really are
  multi-arch.
- Publish the planned Baumhaus/arcade devblog post once the arcade demonstrably works.

**Done when:** a stranger on glitch.fun can click Play and be in an arcade world, on desktop and
tablet, and the fix is verified from a clean browser profile.

## Horizon 1 — Bug-free core mechanics (weeks; v0.9)

*Mission rule #1: no new depth while shipped mechanics half-work. Close the known list, then keep
it closed via playtests.*

**Fix the "does nothing / does wrong" list (all previously analyzed):**
- Ship **console station**: `E` is a no-op + missing locale key; decide and implement what engines
  and other decorative ship devices actually do (analysis: ship-device-blocks-function).
- **UFO-kill bug**: `ShipWeapons=Off` on old saves makes UFOs unkillable.
- **Spaceflight HUD leak**: compass + time panel showing during space flight.
- **Helmet lamp** stays on entering space; world-options modal transparency.
- **Dark worlds**: lava=night / jungle=rain brightness floor (5-point plan exists).
- **Loading-screen gap** (star flash) and remaining WebGL pop-in complaints as capacity allows.

**Then verify with people, not just tests:**
- Drive the standing playtest issues to closure: lighting #88, smoothness #89, audio #90, UI
  readability #91, first-session clarity #92, creatures #93, flora sprout #94; platform tests
  Linux #86, macOS #87, gamepad #201, touch #202, WebGL #203.
- Harvest the expert-feedback issues (#276 menu/settings UX, #277 crafting & ship menu UX,
  #278 URP graphics advice) into concrete follow-ups; Codex review #185.
- Keep the audit muscle: repeat the v0.7.7-style full audit once per minor version.

**Done when:** the "known broken/odd" list in TODO.md is empty, and a new-player playtest (#92)
passes without an adult explaining anything.

## Horizon 2 — Deeper crafting, more materials & items (v0.10+)

*Only after Horizon 1. The content pipeline is pure JSON — additions are cheap — but the mission
requires every addition to connect to a loop: mine → refine → craft → **use**.*

**Close the existing dead ends first (they are cheap wins):**
- **`reactor_fuel` has zero consumers** — the entire uranium/lead chain ends in an unused item.
  Give it a real sink: e.g. a ship reactor upgrade, a base power system, or factory speed-ups.
- **Thin-demand metals**: aluminium, tin, nickel, cobalt, platinum, lead, zinc, tungsten ingots are
  each used in only ~2 recipes; lithium, neodymium, light_alloy, biofuel, magnet in exactly 1.
  Target: every ore family has ≥3 meaningful uses across at least two stations.
- **Decorative blocks players can't get**: lights, strip lights, panels, machine blocks are
  worldgen-only (no drops, no recipes). Make the nice-looking blocks craftable — base-building is
  a kid-favorite loop and this is pure data work.

**Then add depth, kid-friendly by design:**
- **New tiered content** that exploits the existing tool-tier (1–3) and blueprint gates: more
  suit/gadget/vehicle-adjacent items, deeper food & farming (algae tank and seeds exist; recipes
  are shallow), more consumables with gentle effects. No durability/tool-breaking — depth through
  discovery, not maintenance chores (mission: no punishing mechanics).
- **A material rarity/tier tag** if needed for UI sorting and balance visibility (today tiering is
  implicit via `minToolTier` + chain depth; rarity exists only on planets/structures).
- **More recipes for existing stations** before any new station: detoxifier (1 recipe) and
  algae tank (1 recipe) are nearly empty; factories offer only 10 recipes.

**Engineering guardrails for content growth (verify before the big batch):**
- Block atlas: 16×16 = 256 slots, effective ceiling ≈ 239 blocks before procedural variants are
  dropped — plan a larger atlas (or per-block texture fields) before crossing ~200 blocks.
- Plain blocks/items are data-only; blocks with special rendering (transparency, cutout, scatter)
  need `ChunkMesher`/`BlockTextureAtlas` code — batch those deliberately.
- Every item/block ships DE+EN locale keys (parity is test-enforced) and, where relevant, a Codex
  entry; content batches end with a save/load round-trip test on an existing world.

**Done when:** zero defined-but-useless items in the content data, every station has a reason to
be visited, and a returning player always has an affordable *next* blueprint.

## Horizon 3 — Runs on a Raspberry Pi (parallel track, promote after H1)

*The server is closer than expected: `linux-arm64` self-contained builds and ARM64-ready deps
already ship. What's missing is measurement, tuning and a documented family-hosting story.*

- **Measure before tuning:** capture real per-world RAM/CPU on ARM64 (or the VPS as proxy) — the
  only number today is the 768 MB/2-CPU *policy fence*, not a measured working set. Add a tiny
  perf log (tick overrun counter already implied by `ChunkStreamPerTick` docs).
- **A "small hardware" server profile:** documented preset lowering `ChunkStreamPerTick` (16→~4),
  `TickRate` (15→10), `ViewDistanceChunks`, `MaxPlayers` (target: 4–6 players, 1–2 worlds on a Pi
  4/5 with 4–8 GB). Consider `DOTNET_GCHeapHardLimit` guidance for fenceless bare-metal runs.
- **Take worldgen off the tick thread** (the one structural risk for weak hardware): fresh chunks
  are generated synchronously in-tick, up to 16/tick during exploration — the main stutter/overrun
  source on a Pi. This also helps the VPS fleet and browser singleplayer.
- **Storage guidance:** keep and sharpen the existing "SSD over microSD" warning (WAL autosave
  write amplification); document autosave/backup interval tuning for SD-card setups.
- **Ship it as a story, not just a zip:** a SELF_HOSTING.md "Raspberry Pi family server" section
  with a tested step-by-step (bare `dotnet` + systemd unit — no Docker/portal needed for LAN),
  verified on real hardware once. This is also a lovely devblog.

**Done when:** a stock Pi hosts a 4-player family world through an evening session with no tick
overruns, following only the docs.

## Horizon 4 — Steam & Xbox (the heart goals; sequenced, researched 2026-07)

*Not for revenue — because our own game on the shelf is the point. Order follows realism.*

**4a. Gamepad-first UX (prerequisite for everything console, start during H1):**
Full controller support is the single investment that serves Steam, Steam Deck *and* Xbox — and
issue #201 (Xbox pad on Windows) already tracks its verification. Target: complete a session —
menus, crafting, building, flying, chat via on-screen keyboard — without touching a mouse.

**4b. Steam (realistic first store; weeks of calendar time, ~$100):**
- Individuals in Germany can onboard as sole proprietor — no company needed; free games are fine;
  connecting to our own dedicated servers is explicitly supported; open source is fine (AGPL note:
  skip linking the Steamworks SDK initially — upload via SteamPipe works without it).
- Calendar mechanics: tax interview (days), $100 app fee → 30-day minimum to release, "coming
  soon" page ≥2 weeks. Work items: store assets, Steam build depots (Windows + native Linux —
  the Linux build doubles as the Steam Deck path), controller polish for a Deck-friendly review.
- Honest constraint: Steam accounts are 13+ (COPPA) — Steam is the "families and older kids"
  channel; the browser/glitch.fun channel remains the youngest players' door.
- **Decision point for the user:** German side (hobby vs. Gewerbe for a zero-revenue release) —
  clarify once before paying the fee.

**4c. Xbox Dev Mode (~$19, days — the family win):**
Retail-Xbox **Dev Mode** still lets us deploy our own UWP build to our own console. Unity 6 still
has the UWP target (IL2CPP-only). This puts Blocks Beyond the Stars on the living-room Xbox for
us — no store, no certification, this year. Known ports of work: IL2CPP AOT quirks (the
MessagePack→JSON lesson from WebGL will recur) and running the singleplayer server in-process
(the glitch.fun loopback architecture is exactly the reusable piece).

**4d. ID@Xbox (the real store shelf; months, only after Steam):**
- The Xbox Creators Program is effectively closed for new console games (and never allowed online
  multiplayer anyway) — **ID@Xbox is the only retail path.** It is free (application, cert, two
  dev kits) but curated: concept approval, NDA, likely a registered business (a simple Gewerbe
  should suffice; unverified for pure private persons), full certification.
- Technical scope beyond 4a/4c: Xbox network sign-in + parental-control/privilege checks, MPSD
  session state, chat permission handling, UGC reporting, cross-network-play approval (Xbox
  players in the same worlds as PC/web is exactly our model and is approvable), cert-grade
  network-failure handling. NDA means Xbox-specific integration code lives outside the public
  repo.
- Treat as its own project with a go/no-go **after** Steam ships and Dev Mode proves the port.

## Ongoing tracks (never "done", grow with reach)

- **Kid safety & moderation:** guest reporting for arcade/browser players (today `/report` needs a
  portal session); name/chat moderation stages from the existing analysis; PEGI/IARC self-rating
  (needed for Xbox anyway — do it once, reuse everywhere); moderation capacity gates any arcade
  pool growth.
- **Community & localization:** keep the invite issues alive (#95–#100 designs/languages/docs),
  credit contributors, honest devblogs per release.
- **Operations:** VPS fleet health (Netdata alerts live), rate-limit follow-ups, CI cache fix,
  UptimeRobot; one tagged pipeline feeds all channels — keep it that way as channels grow.

## Explicitly not on this roadmap

- Monetization in any form; paid platform features.
- New big mechanics (weather systems, new vehicle classes, story arcs) while Horizon 1 is open.
- Public/open registration or unmoderated world listings — the Baumhaus rule stands.
- PlayStation/Switch — one console dream at a time.

## Review cadence

Revisit this file when a horizon completes or at least every two months; keep per-release status
in TODO.md and per-decision records in `docs/developer/adr/`.
