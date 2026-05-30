# Spacecraft AI Mission Backend (optional)

A **separate, optional** Python service that proposes missions for the game server
(`plans/anf_mission_editor.md`). The C#/.NET game server stays authoritative: it validates,
clamps and publishes whatever this service returns, and works fine when this service is off
or unreachable.

## Contract

`POST /mission-plan` with `{ "context": "<free text about the world/player/needs>" }`
returns a `MissionPlan` (PascalCase keys, matching the server model):

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

`GET /health` → `{ "status": "ok" }`.

This MVP uses a deterministic template generator as a stand-in for a real LLM — the HTTP
contract is the point; an LLM can be slotted in behind the same endpoint later.

## Run

```bash
uv run uvicorn app.main:app --host 127.0.0.1 --port 8077
```

Then on the game server set `AiLevel` (Suggest or Auto) and `AiBackendUrl`
(`http://127.0.0.1:8077`) in `config/server.json`. With `AiLevel = Off` the server never
calls this service.

## Safety

- The AI only **proposes**; the server validates objectives/rewards against its content and
  **clamps reward counts** (max 25). Unknown items/blocks are rejected.
- `Suggest` stores AI missions as inactive drafts for admin review; `Auto` publishes valid
  ones immediately.
- Any backend error → the server logs a warning and continues without AI (no crash).
