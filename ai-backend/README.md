# Blocks Beyond the Stars — AI Backend (optional)

A **separate, optional** Python service for the game server (see
[docs/AI_MISSION_BACKEND.md](../docs/AI_MISSION_BACKEND.md)). The C#/.NET game server stays
authoritative: it validates whatever this service returns, only ever shows greeting lines as
flavour, and works fine when the service is off or unreachable.

It serves four endpoints:

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
  "NpcName": "Vega-7", "Role": "vendor", "Theme": "miners", "IsRobot": false,
  "Settlement": "Karth Town", "PlayerName": "Marcel",
  "Relationship": 18, "PastInteractions": 3, "Language": "de"
}
```

→ `{ "Text": "Willkommen zurück, Marcel. Möchtest du handeln?" }`

The request also carries **L2 memory/persona fields** (`Persona`, `RecentEvents`) and — for the ship
AI VEGA — a `Situation` line; `Role` may be `"vendor"`, `"quartermaster"` or `"ship_ai"` (VEGA
banter; its no-LLM fallback is an EMPTY string, because the game has scripted VEGA lines).

## `POST /mission-text` (L3 — board-mission flavour)

Writes Title + Description around a FIXED board job (objective/reward stay server-coined):

```json
{ "GiverName": "Mira Voss", "Place": "Karth Town", "Theme": "miners",
  "NeedItem": "iron_ore", "Required": 12, "RewardItem": "iron_plate", "RewardCount": 3,
  "Language": "de" }
```

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
