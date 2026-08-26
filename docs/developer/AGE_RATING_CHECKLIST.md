# Age-Rating Readiness Checklist (IARC / PEGI / USK / ESRB)

Groundwork for #1226 so the first real questionnaire — which only becomes possible through a
participating storefront (Google Play, Microsoft Store/Xbox, Nintendo eShop, PlayStation Store, Meta
Quest, Epic; Steam runs its own content survey) — can be filed in an afternoon when the storefront step
(roadmap H4) arrives. Until then the game publishes its **own statement**: see
[docs/user/PARENTS.md](../user/PARENTS.md); no PEGI/USK logo may be shown before a real certificate exists.

## Expected outcome (our honest self-assessment, not a claim)

| System | Expected | Driver |
|---|---|---|
| PEGI | **7** | Non-realistic violence against fantasy/sci-fi characters; mild fear (dark caves) |
| USK | **6** | Abstrahierte Kampfhandlungen ohne Blut, freundliche Präsentation |
| ESRB | **E10+** (possibly E) | Fantasy violence, mild |
| IARC interactive elements | **Users Interact · User-Generated Content** | Chat/voice + names/builds online |

## Questionnaire answers, prepared

**Violence**
- Combat targets: wildlife creatures, robots/drones, cartoon "bandits" (humanoid). Bandits flee below a
  health floor — they are chased away, never killed (`GameServerBandits`); creatures break apart, no
  corpses persist, no blood or gore anywhere, no violence against realistic humans.
- Player combat: `GameRules.WeaponMode` **default ToolsOnly** (weapons opt-in per world);
  `ShipWeapons` default **Off**. Allies can never harm each other even with PvP-style rules on.
- Player death: respawn in the med-bay; no loot stealing by other players.

**Fear / horror** — dark caves, one spooky ruin archetype, hostile robot "Guardian" boss. Tone stays
family-adventure; no jump-scare design, no horror imagery.

**Language** — no profanity in shipped content (EN/DE + machine locales); chat is user content, see below.

**Sex / nudity / drugs / alcohol / tobacco / gambling** — none. Arcade minigames award in-game knowledge
only (no wagering, no chance-based rewards; VoidSolitaire is a card layout, not gambling).

**Digital purchases** — none. No in-game purchases, no DLC, no ads, no loot boxes. (Answer "no purchases"
everywhere; the game itself is free.)

**Online interaction (the "Users Interact" block)**
- Free-text chat: yes (text; voice is push-to-talk, listening on by default for LAN games and off on
  hosted worlds, host can disable server-wide).
- User-generated content shared with others: names (player/base/beacon/creature/crew/marker), block
  builds, paint designs, photo notes.
- Controls that exist in-game (list these in the questionnaire's "safety measures" field):
  chat/name content filter with operator levels incl. Strict (#1207, names + AI text #1221), anti-spam
  auto-mute (#1208), `/report` for every player incl. browser guests with operator inbox (#1222), local
  `/mute` (#1209), operator `/silence` (#1223), voice chat push-to-talk + host kill-switch, no join codes
  (crews invite online players only, #1216),
  official hosted worlds carry a moderated report inbox.
- Location sharing: **no**. Personal information: **no** (pseudonymous player name only, no e-mail).

**Data collection (store data-safety forms)**
- Collected: self-chosen player name; hosted-world session data; bug-report text + technical logs
  (user-initiated). No analytics SDK, no advertising SDK, no third-party tracking.
- Optional AI NPC backend processes in-game dialogue text when a host enables it; template fallback
  without it.

## Still open before filing

- [ ] Pick the storefront (decides which questionnaire actually runs) — roadmap H4.
- [ ] Screenshot/video set of combat "at its worst" for the reviewer (bandit fight, Guardian).
- [ ] Verify the browser (glitch.fun) build's chat path matches the answers (guest `/report` exists, #1222).
- [ ] Legal entity / contact for the certificate (JuMaVe Games contact address).
- [ ] Re-read PARENTS.md against the shipped feature set at filing time (rules drift).
