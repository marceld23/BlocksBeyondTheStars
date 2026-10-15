# Blocks Beyond the Stars — AI Backend (optional)

A **separate, optional** Python service for the game server (see
[docs/developer/AI_MISSION_BACKEND.md](../docs/developer/AI_MISSION_BACKEND.md)). The C#/.NET game server stays
authoritative: it validates whatever this service returns, only ever shows greeting lines as
flavour, and works fine when the service is off or unreachable.

It serves five endpoints (version 0.4.0):

## `POST /mission-plan`

`{ "context": "<free text about the world/player/needs>" }` → a `MissionPlan` (PascalCase keys,
matching the server model):

```json
{
  "Title": "Field Order: Iron Ore",
  "Description": "Command needs 10 iron ore. ...",
  "GiverName": "Outpost Control",
  "StartDialog": "...", "CompleteDialog": "...", "Difficulty": "normal",
  "Objectives": [ { "Type": "Mine", "Target": "iron_ore", "Required": 10 } ],
  "SuggestedRewards": [ { "Item": "iron_plate", "Count": 5 } ]
}
```

**L0: LLM-authored when a model is configured** (strict-JSON prompt; the allowed target/reward keys
arrive inside the context string from the server), else the deterministic template stand-in. The
C# server validates + clamps either way, so a hallucinated key just means “rejected”.

## `POST /npc-line` (item 15 — NPC greetings)

A short, in-character greeting line generated **by an LLM** (when configured) in the player's
language. Request (PascalCase from the server; snake_case also accepted):

```json
{
  "NpcName": "Vega-7", "Role": "vendor", "Job": "vendor", "Theme": "miners", "IsRobot": false,
  "Settlement": "Karth Town", "PlayerName": "{player}",
  "Relationship": 25, "PastInteractions": 3, "Language": "de"
}
```

→ `{ "Text": "Willkommen zurück, {player}. Möchtest du handeln?" }`

**`PlayerName` is the placeholder `{player}`** (#2590): the server caches a line per NPC, standing and
language — not per player — and fills in each receiving player's own name when it sends the line, so
the model must copy the token literally. For the same reason the request is a pure function of that
cache key (#2618): `Relationship`/`PastInteractions` are the standing tier's representative numbers
(hostile −30/0, stranger 0/0, known 25/3, trusted 60/10 — each inside its `_tier` bucket) and
`RecentEvents` arrives empty, never one player's history. The request also carries **L2 memory/persona
fields** (`Persona`, `RecentEvents`), the NPC's `Job` (`gardener`, `guard`, a profession such as `doctor` …, which
describes a settler by what they do) and — for the ship AI VEGA — a `Situation` line. `Role` may be
`"vendor"`, `"quartermaster"`, `"settler"`, `"guardian"` or `"ship_ai"` (VEGA banter). The prompt is
kid-friendly (families and kids; bandits are chased off, never hurt; no fear, romance or violence). The
no-LLM template only exists for the vendor and the quartermaster; **every other role gets an EMPTY
string**, so the game shows its own localized line instead of a merchant's "Care to trade?".

## `POST /npc-lines` (#2591 — the one path for every new NPC/VEGA line)

Re-words the game's **authored, localized lines** in a speaker's voice. Exact facts never reach the
model as values: names, numbers, items and places are **placeholder tokens** that the server fills in
per receiving player. Request (PascalCase; snake_case also accepted; unknown fields ignored):

```json
{
  "Purpose": "chatter", "Language": "de", "Place": "a farming village", "Theme": "settlers",
  "Stage": "", "AwayDays": 0, "Situation": "evening, light rain",
  "Speakers": [
    { "Token": "{a}", "Role": "settler", "Job": "gardener", "Persona": "sunny optimist ...", "IsRobot": false },
    { "Token": "{b}", "Role": "settler", "Job": "", "Persona": "dry humorist ...", "IsRobot": false }
  ],
  "TopicHint": "rain on the gardens", "Facts": ["it is raining in {place}"],
  "Tokens": ["{a}", "{b}", "{place}"],
  "Authored": ["Schon wieder Regen! Nasse Stiefel.", "Gut für die Beete, {a}!"],
  "LineCount": 2, "MaxChars": 56
}
```

→ `{ "Lines": ["...", "..."] }` — exactly `LineCount` strings, or **`[]`** when no LLM is configured,
the call fails or the answer is malformed (the server keeps its authored lines then; an older backend
without this endpoint answers 404, which the server treats the same way).

`Purpose` is one of `passing_hello, welcome_back, resident_hello, dialog_open, friend_hello,
friend_gift, chatter, cheer, festival_thanks, news_deed, wish_ask, wish_thanks, guest_greet,
guest_offer, trader_hail, vega_welcome, vega_news, vega_praise` (one sentence of guidance each in
`_PURPOSE_DESC`; an unknown purpose gets generic guidance). Several speakers alternate in the given
order. The prompt is kid-friendly and tells the model to use **only the listed tokens, literally**, to
never invent names, numbers, items or places, to never write digits, and to keep each line within
`MaxChars`. `max_tokens` scales with the line count (≈ 70 per line, at most 600). The game server
checks every answer again (line count, length, allowed/required tokens, no digits outside tokens,
content screen) and drops the whole answer on any failure.

## `POST /mission-text` (L3 — board-mission flavour)

Writes Title + Description around a FIXED board job (objective/reward stay server-coined):

```json
{ "GiverName": "Mira Voss", "Place": "Karth Town", "Theme": "miners",
  "NeedItem": "iron_ore", "Required": 12, "RewardItem": "iron_plate", "RewardCount": 3,
  "Language": "de", "Kind": "gather" }
```

`Kind` (optional, default `"gather"`) selects the job shape: `"gather"` (deliver goods),
`"bounty_camp"` (drive bandits from their camp) or `"bounty_ship"` (drive off a raider ship) —
bounty postings get a kid-friendly drive-off framing instead of reading like a delivery.

→ `{ "Title": "...", "Description": "..." }` — empty fields when no LLM is configured (the server
keeps its localized static board text then).
`GET /health` → `{ "status": "ok", "llm": true|false }` (`llm` = whether a model is configured).

## LLM provider (LangChain + LangGraph, OpenAI-compatible)

The greeting generator is **provider-agnostic**: it speaks the OpenAI-compatible chat API, so the
same code works with **LM Studio** (self-hosted), **OpenAI**, or **Claude** (Anthropic's
OpenAI-compatible endpoint) — selected purely by env. LangChain builds the prompt→model chain and
LangGraph wraps it in a one-node graph (easy to extend with moderation/retry nodes later).

Copy `.env.example` → `.env` and set one provider:

```bash
BBTS_AI_BASE_URL=http://localhost:1234/v1   # LM Studio
BBTS_AI_MODEL=local-model
BBTS_AI_API_KEY=lm-studio                   # ignored by LM Studio
```

With **no** model configured the endpoint returns a deterministic **bilingual template** line, so
it works with zero setup. Any error → the same template. The game server *also* falls back to a
static localized line, so greetings appear with or without this service.

## Run

```bash
uv run uvicorn app.main:app --host 127.0.0.1 --port 8077
```

Then on the game server set `AiLevel` (Suggest/Auto for missions; any non-`Off` value lets the
server fetch greetings) and `AiBackendUrl` (`http://127.0.0.1:8077`) in `config/server.json`.
With `AiLevel = Off` the server never calls this service (greetings use the static fallback).

## Safety

- The AI only **proposes**; the server validates mission objectives/rewards against its content and
  **clamps reward counts** (max 25). Unknown items/blocks are rejected. Greetings are flavour only.
- `Suggest` stores AI missions as inactive drafts for admin review; `Auto` publishes valid ones.
- Any backend error → the server logs a warning and continues without AI (no crash); greetings
  fall back to the static localized line.
