# AI Mission Backend — Decision

Covers the optional AI mission system (`plans/anf_mission_editor.md`).

## Decision

- The AI is an **optional, separate Python service** (`ai-backend/`, FastAPI). The game
  server is C#/.NET and **stays authoritative**.
- The first mission MVP works **entirely without AI** (already implemented in M13). AI is
  additive and **off by default** (`AiLevel = Off`).
- The AI **proposes** a structured `MissionPlan` (creative layer + rule layer). The server
  **validates, clamps and publishes** it. The AI never grants items, finalizes rewards or
  bypasses rules.

## Levels (`AiLevel` in server config)

| Level | Behaviour |
|---|---|
| `Off` | Server never contacts the backend (default). |
| `TextOnly` | (Reserved) AI only writes text for existing missions; no full generation. |
| `Suggest` | Valid AI missions are stored as **inactive drafts** for admin review. |
| `Auto` | Valid AI missions are **published** immediately. |

## Flow

1. An admin triggers generation (admin command `ai_mission` with a context string).
2. Server asks the backend (`POST /mission-plan`); on any error it logs and falls back to
   "no mission" — never crashes (anf_mission_editor.md §14).
3. Server converts `MissionPlan` → `MissionDefinition`, **clamping reward counts (≤25)**.
4. `MissionValidator` checks objectives/rewards against loaded content; invalid → rejected.
5. Per level: `Auto` publishes, `Suggest` drafts (inactive).

## Why this shape

- Keeps the heavy/optional generative dependency out of the lightweight, Pi-friendly game
  server (separate process, separate language).
- Server-side validation + reward clamping make AI output safe by construction.
- A real LLM can replace the template generator behind the same HTTP contract with no
  server change.
