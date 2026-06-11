"""LLM layer for the Spacecraft AI backend (items 15 + L0/L2/L3).

Generates NPC greeting lines, ship-AI (VEGA) banter, full mission plans and board-mission flavour
text. It is **provider-agnostic**: everything goes through the OpenAI-compatible chat API, so the
very same code talks to

  * **LM Studio** (self-hosted)            base_url = http://localhost:1234/v1
  * **OpenAI**                              base_url = https://api.openai.com/v1
  * **Claude** (Anthropic OpenAI-compatible) base_url = https://api.anthropic.com/v1/

selected purely by environment variables (see `.env.example`). LangChain builds the prompt → model
chains and **LangGraph** wraps each flow in a small graph, so the flows are easy to extend later
(e.g. add a moderation or retry node) without touching the HTTP layer.

The whole module degrades gracefully: if the LLM libraries aren't installed, no model is configured,
or a call fails, every generator returns its deterministic fallback (template line / None) instead
of raising — the game server then uses its own static/localized fallback.
"""
from __future__ import annotations

import json
import os
import re
from typing import TypedDict

try:
    from dotenv import load_dotenv

    load_dotenv()  # read ai-backend/.env if present
except Exception:  # python-dotenv not installed yet — env vars still work, just no .env file
    pass

# --- Configuration (env) ---------------------------------------------------------------------
# A model + base URL together enable the LLM; otherwise the deterministic fallbacks are used.
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


def _make_llm(max_tokens: int):
    """One ChatOpenAI handle, or None when the stack is unavailable / unconfigured."""
    if not (_BASE_URL and _MODEL):
        return None
    try:
        from langchain_openai import ChatOpenAI

        return ChatOpenAI(
            model=_MODEL,
            base_url=_BASE_URL,
            api_key=_API_KEY or "not-needed",  # LM Studio ignores the key
            temperature=_TEMPERATURE,
            max_tokens=max_tokens,
            timeout=20,
        )
    except Exception:
        return None


# ==============================================================================================
# NPC line (item 15 L1 + L2 memory/persona + VEGA ship-AI banter)
# ==============================================================================================
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
    persona: str         # L2: a short persona descriptor the server derives per NPC
    recent_events: str   # L2: compact recent interaction log ("trade, trade, mission accepted")
    situation: str       # VEGA: current game situation ("on ocean world 'Velda', night, 3 fragments")


# --- Deterministic fallback (no LLM / error) -------------------------------------------------
def _template_line(req: "NpcLineInput") -> str:
    de = (req.get("language") or "en").lower().startswith("de")
    name = req.get("player_name") or ("Reisender" if de else "traveler")
    known = (req.get("past_interactions") or 0) > 0
    if req.get("role") == "ship_ai":
        return ""  # VEGA banter is LLM-only flavour — the scripted lines cover the rest
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


_SYSTEM = (
    "You write a single short in-character line for a character in a sci-fi voxel space "
    "exploration game. Keep it to ONE or TWO sentences, natural and grounded, with a hint of the "
    "character's role and personality. Do NOT use quotation marks, emojis, stage directions or the "
    "player's stats verbatim — just the spoken line. Write it in {language}."
)

_HUMAN = (
    "Character name: {npc_name}\n"
    "Character role: {role} (a {role_desc})\n"
    "Persona: {persona}\n"
    "Place: {settlement}\n"
    "Character kind: {kind}\n"
    "Player name: {player_name}\n"
    "Relationship with this player: {tier} (score {relationship}, {past_interactions} past visits)\n"
    "What they remember of this player recently: {recent_events}\n"
    "Current situation: {situation}\n\n"
    "Write the line now."
)

_ROLE_DESC = {
    "vendor": "merchant who barters goods themed around the settlement",
    "quartermaster": "mission-giver who hands out field jobs from a board",
    "ship_ai": "the player's ship AI: dry, laconic, deadpan-witty but loyal; it comments on the "
               "current situation in passing, like a co-pilot making conversation",
}


def _build_line_graph():
    """Compile a LangGraph that runs the prompt→model→text chain in a single node. Returns the
    compiled graph, or None if the LLM stack is unavailable / unconfigured."""
    llm = _make_llm(max_tokens=90)
    if llm is None:
        return None
    try:
        from langchain_core.output_parsers import StrOutputParser
        from langchain_core.prompts import ChatPromptTemplate
        from langgraph.graph import END, START, StateGraph
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
                "persona": r.get("persona") or "(no specific persona)",
                "settlement": r.get("settlement") or "a frontier settlement",
                "kind": "an android" if r.get("is_robot") else "a person",
                "player_name": r.get("player_name") or "the pilot",
                "tier": _tier(r.get("relationship", 0), r.get("past_interactions", 0)),
                "relationship": r.get("relationship", 0),
                "past_interactions": r.get("past_interactions", 0),
                "recent_events": r.get("recent_events") or "(nothing notable)",
                "situation": r.get("situation") or "(an ordinary day)",
            }
        )
        return {"req": r, "text": (text or "").strip().strip('"')}

    g = StateGraph(GraphState)
    g.add_node("generate", generate)
    g.add_edge(START, "generate")
    g.add_edge("generate", END)
    return g.compile()


# Build once at import; None means "no LLM configured" → template fallback.
_LINE_GRAPH = _build_line_graph()


def llm_enabled() -> bool:
    return _LINE_GRAPH is not None


def generate_npc_line(req: NpcLineInput) -> str:
    """Returns a line, always. Tries the LLM graph; on any failure falls back to a deterministic
    bilingual template so the endpoint never errors out. (For role "ship_ai" the fallback is empty
    — banter is pure LLM flavour and the game has scripted VEGA lines for everything else.)"""
    if _LINE_GRAPH is not None:
        try:
            out = _LINE_GRAPH.invoke({"req": req, "text": ""})
            text = (out.get("text") or "").strip()
            if text:
                return text
        except Exception:
            pass
    return _template_line(req)


# ==============================================================================================
# L0 — full mission plan. The LLM proposes a strict-JSON MissionPlan; the C# server validates and
# clamps it, so a hallucinated item key just means "rejected → no mission" (never breakage).
# ==============================================================================================
_MISSION_SYSTEM = (
    "You design ONE side mission for a sci-fi voxel space exploration game. Respond with ONLY a "
    "JSON object — no markdown fences, no commentary. Schema:\n"
    '{{"Title": str (<= 60 chars), "Description": str (1-3 sentences), "GiverName": str, '
    '"StartDialog": str (1-2 spoken sentences), "CompleteDialog": str (1-2 spoken sentences), '
    '"Difficulty": "easy"|"normal"|"hard", '
    '"Objectives": [{{"Type": "Mine"|"Collect"|"Deliver", "Target": str, "Required": int}}], '
    '"SuggestedRewards": [{{"Item": str, "Count": int}}]}}\n'
    "Use 1-2 objectives, Required between 3 and 24, 1-2 rewards, Count between 1 and 6. "
    "Objective Target and reward Item MUST be chosen from the allowed lists in the request — "
    "never invent keys. Keep the fiction grounded in the given context."
)

_MISSION_HUMAN = "Mission request context:\n{context}\n\nWrite the JSON now."


def _build_mission_graph():
    llm = _make_llm(max_tokens=420)
    if llm is None:
        return None
    try:
        from langchain_core.output_parsers import StrOutputParser
        from langchain_core.prompts import ChatPromptTemplate
        from langgraph.graph import END, START, StateGraph
    except Exception:
        return None

    prompt = ChatPromptTemplate.from_messages([("system", _MISSION_SYSTEM), ("human", _MISSION_HUMAN)])
    chain = prompt | llm | StrOutputParser()

    class GraphState(TypedDict):
        context: str
        plan: dict | None

    def generate(state: GraphState) -> GraphState:
        raw = chain.invoke({"context": state["context"] or "(no specific context)"})
        return {"context": state["context"], "plan": _parse_json_object(raw)}

    g = StateGraph(GraphState)
    g.add_node("generate", generate)
    g.add_edge(START, "generate")
    g.add_edge("generate", END)
    return g.compile()


_MISSION_GRAPH = _build_mission_graph()


def _parse_json_object(raw: str) -> dict | None:
    """Extracts the first JSON object from an LLM reply (tolerates ```json fences and prose)."""
    if not raw:
        return None
    text = raw.strip()
    text = re.sub(r"^```(?:json)?\s*|\s*```$", "", text, flags=re.IGNORECASE)
    start = text.find("{")
    end = text.rfind("}")
    if start < 0 or end <= start:
        return None
    try:
        obj = json.loads(text[start:end + 1])
        return obj if isinstance(obj, dict) else None
    except Exception:
        return None


def generate_mission_plan(context: str) -> dict | None:
    """Returns an LLM-authored MissionPlan dict, or None (caller falls back to the template).
    Light shape check only — the authoritative validation/clamping happens in the C# server."""
    if _MISSION_GRAPH is None:
        return None
    try:
        out = _MISSION_GRAPH.invoke({"context": context, "plan": None})
        plan = out.get("plan")
        if not plan or not isinstance(plan.get("Objectives"), list) or not plan["Objectives"]:
            return None
        if not str(plan.get("Title") or "").strip() or not str(plan.get("Description") or "").strip():
            return None
        return plan
    except Exception:
        return None


# ==============================================================================================
# L3 — board-mission flavour text. The objective/reward are FIXED by the server; the LLM only
# writes Title + Description around them, in the player's language.
# ==============================================================================================
class MissionTextInput(TypedDict, total=False):
    giver_name: str
    place: str
    theme: str
    need_item: str
    required: int
    reward_item: str
    reward_count: int
    language: str


_TEXT_SYSTEM = (
    "You write the posting text for ONE delivery job on a settlement mission board in a sci-fi "
    "voxel space exploration game. Respond with ONLY a JSON object — no markdown fences:\n"
    '{{"Title": str (<= 48 chars, punchy, no quotes), "Description": str (1-2 sentences, in the '
    "giver's voice, says WHY they need the goods)}}\n"
    "Do not change the quantities or items — they are fixed. Write both fields in {language}."
)

_TEXT_HUMAN = (
    "Job giver: {giver_name} at {place} (community of {theme})\n"
    "They need: {required} x {need_item}\n"
    "They pay: {reward_count} x {reward_item}\n\n"
    "Write the JSON now."
)


def _build_text_graph():
    llm = _make_llm(max_tokens=160)
    if llm is None:
        return None
    try:
        from langchain_core.output_parsers import StrOutputParser
        from langchain_core.prompts import ChatPromptTemplate
        from langgraph.graph import END, START, StateGraph
    except Exception:
        return None

    prompt = ChatPromptTemplate.from_messages([("system", _TEXT_SYSTEM), ("human", _TEXT_HUMAN)])
    chain = prompt | llm | StrOutputParser()

    class GraphState(TypedDict):
        req: MissionTextInput
        text: dict | None

    def generate(state: GraphState) -> GraphState:
        r = state["req"]
        raw = chain.invoke(
            {
                "language": _language_name(r.get("language", "en")),
                "giver_name": r.get("giver_name") or "the quartermaster",
                "place": r.get("place") or "a frontier settlement",
                "theme": r.get("theme") or "settlers",
                "need_item": (r.get("need_item") or "supplies").replace("_", " "),
                "required": r.get("required") or 10,
                "reward_item": (r.get("reward_item") or "supplies").replace("_", " "),
                "reward_count": r.get("reward_count") or 1,
            }
        )
        return {"req": r, "text": _parse_json_object(raw)}

    g = StateGraph(GraphState)
    g.add_node("generate", generate)
    g.add_edge(START, "generate")
    g.add_edge("generate", END)
    return g.compile()


_TEXT_GRAPH = _build_text_graph()


def generate_mission_text(req: MissionTextInput) -> dict | None:
    """Returns {"Title", "Description"} in the requested language, or None (caller keeps the
    localized static board text)."""
    if _TEXT_GRAPH is None:
        return None
    try:
        out = _TEXT_GRAPH.invoke({"req": req, "text": None})
        text = out.get("text")
        if not text:
            return None
        title = str(text.get("Title") or "").strip()
        desc = str(text.get("Description") or "").strip()
        if not title or not desc:
            return None
        return {"Title": title[:64], "Description": desc[:280]}
    except Exception:
        return None
