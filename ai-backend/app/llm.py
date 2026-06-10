"""LLM layer for the Spacecraft AI backend (item 15).

Generates short, in-character NPC greeting lines. It is **provider-agnostic**: everything goes
through the OpenAI-compatible chat API, so the very same code talks to

  * **LM Studio** (self-hosted)            base_url = http://localhost:1234/v1
  * **OpenAI**                              base_url = https://api.openai.com/v1
  * **Claude** (Anthropic OpenAI-compatible) base_url = https://api.anthropic.com/v1/

selected purely by environment variables (see `.env.example`). LangChain builds the prompt → model
chain and **LangGraph** wraps it in a one-node graph, so the flow is easy to extend later (e.g. add
a moderation or retry node) without touching the HTTP layer.

The whole module degrades gracefully: if the LLM libraries aren't installed, no model is configured,
or a call fails, `generate_npc_line` returns a deterministic bilingual template line instead of
raising — the game server then shows that, or its own localized fallback.
"""
from __future__ import annotations

import os
from typing import TypedDict

try:
    from dotenv import load_dotenv

    load_dotenv()  # read ai-backend/.env if present
except Exception:  # python-dotenv not installed yet — env vars still work, just no .env file
    pass

# --- Configuration (env) ---------------------------------------------------------------------
# A model + base URL together enable the LLM; otherwise the deterministic template is used.
_BASE_URL = os.getenv("SPACECRAFT_AI_BASE_URL", "").strip()
_API_KEY = os.getenv("SPACECRAFT_AI_API_KEY", "").strip()
_MODEL = os.getenv("SPACECRAFT_AI_MODEL", "").strip()
_TEMPERATURE = float(os.getenv("SPACECRAFT_AI_TEMPERATURE", "0.8"))


def _language_name(code: str) -> str:
    return "German" if (code or "en").lower().startswith("de") else "English"


def _tier(relationship: int, past: int) -> str:
    """A coarse relationship bucket, mirrored from the C# server, used to flavour tone."""
    if relationship <= -20:
        return "wary/cold"
    if past <= 0 or relationship < 10:
        return "a first meeting / stranger"
    if relationship < 40:
        return "a familiar regular"
    return "a trusted friend"


# --- Deterministic fallback (no LLM / error) -------------------------------------------------
def _template_line(req: "NpcLineInput") -> str:
    de = (req.get("language") or "en").lower().startswith("de")
    name = req.get("player_name") or ("Reisender" if de else "traveler")
    known = (req.get("past_interactions") or 0) > 0
    if req.get("role") == "quartermaster":
        if de:
            return f"Schön dich wiederzusehen, {name}. Am Brett gibt es Arbeit." if known \
                else f"Suchst du Arbeit, {name}? Sieh dich am Brett um."
        return f"Good to see you again, {name}. There's work on the board." if known \
            else f"Looking for work, {name}? Check the board."
    # vendor
    if de:
        return f"Willkommen zurück, {name}. Möchtest du handeln?" if known \
            else f"Willkommen an meinem Stand, {name}. Möchtest du handeln?"
    return f"Welcome back, {name}. Care to trade?" if known \
        else f"Welcome to my stall, {name}. Care to trade?"


# --- LLM chain via LangChain + LangGraph -----------------------------------------------------
class NpcLineInput(TypedDict, total=False):
    npc_name: str
    role: str
    theme: str
    is_robot: bool
    settlement: str
    player_name: str
    relationship: int
    past_interactions: int
    language: str


_SYSTEM = (
    "You write a single short in-character greeting line for an NPC in a sci-fi voxel space "
    "exploration game. Keep it to ONE or TWO sentences, natural and grounded, with a hint of the "
    "NPC's role and personality. Do NOT use quotation marks, emojis, stage directions or the "
    "player's stats verbatim — just the spoken line. Write it in {language}."
)

_HUMAN = (
    "NPC name: {npc_name}\n"
    "NPC role: {role} (a {role_desc})\n"
    "Settlement: {settlement}\n"
    "NPC kind: {kind}\n"
    "Player name: {player_name}\n"
    "Relationship with this player: {tier} (score {relationship}, {past_interactions} past visits)\n\n"
    "Write the greeting now."
)

_ROLE_DESC = {
    "vendor": "merchant who barters goods themed around the settlement",
    "quartermaster": "mission-giver who hands out field jobs from a board",
}


def _build_graph():
    """Compile a LangGraph that runs the prompt→model→text chain in a single node. Returns the
    compiled graph, or None if the LLM stack is unavailable / unconfigured."""
    if not (_BASE_URL and _MODEL):
        return None
    try:
        from langchain_core.output_parsers import StrOutputParser
        from langchain_core.prompts import ChatPromptTemplate
        from langchain_openai import ChatOpenAI
        from langgraph.graph import END, START, StateGraph
    except Exception:
        return None

    try:
        llm = ChatOpenAI(
            model=_MODEL,
            base_url=_BASE_URL,
            api_key=_API_KEY or "not-needed",  # LM Studio ignores the key
            temperature=_TEMPERATURE,
            max_tokens=90,
            timeout=20,
        )
    except Exception:
        return None

    prompt = ChatPromptTemplate.from_messages([("system", _SYSTEM), ("human", _HUMAN)])
    chain = prompt | llm | StrOutputParser()

    class GraphState(TypedDict):
        req: NpcLineInput
        text: str

    def generate(state: GraphState) -> GraphState:
        r = state["req"]
        text = chain.invoke(
            {
                "language": _language_name(r.get("language", "en")),
                "npc_name": r.get("npc_name") or "(unnamed)",
                "role": r.get("role") or "vendor",
                "role_desc": _ROLE_DESC.get(r.get("role", ""), "settlement inhabitant"),
                "settlement": r.get("settlement") or "a frontier settlement",
                "kind": "an android" if r.get("is_robot") else "a person",
                "player_name": r.get("player_name") or "the pilot",
                "tier": _tier(r.get("relationship", 0), r.get("past_interactions", 0)),
                "relationship": r.get("relationship", 0),
                "past_interactions": r.get("past_interactions", 0),
            }
        )
        return {"req": r, "text": (text or "").strip().strip('"')}

    g = StateGraph(GraphState)
    g.add_node("generate", generate)
    g.add_edge(START, "generate")
    g.add_edge("generate", END)
    return g.compile()


# Build once at import; None means "no LLM configured" → template fallback.
_GRAPH = _build_graph()


def llm_enabled() -> bool:
    return _GRAPH is not None


def generate_npc_line(req: NpcLineInput) -> str:
    """Returns a greeting line, always. Tries the LLM graph; on any failure falls back to a
    deterministic bilingual template so the endpoint never errors out."""
    if _GRAPH is not None:
        try:
            out = _GRAPH.invoke({"req": req, "text": ""})
            text = (out.get("text") or "").strip()
            if text:
                return text
        except Exception:
            pass
    return _template_line(req)
