# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""LLM layer for the BlocksBeyondTheStars AI backend (items 15 + L0/L2/L3).

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
_BASE_URL = os.getenv("BBTS_AI_BASE_URL", "").strip()
_API_KEY = os.getenv("BBTS_AI_API_KEY", "").strip()
_MODEL = os.getenv("BBTS_AI_MODEL", "").strip()
_TEMPERATURE = float(os.getenv("BBTS_AI_TEMPERATURE", "0.8"))
# Per-call LLM timeout in seconds. Generous on purpose: the game server never blocks on us (players get
# an instant static line; the LLM line upgrades it asynchronously), so a slow provider still beats no
# line. Keep this BELOW the game server's own HTTP timeout (BBS_AI_TIMEOUT_SECONDS, default 35) so our
# template fallback always reaches it in time.
_TIMEOUT = float(os.getenv("BBTS_AI_TIMEOUT", "30"))


_LANGUAGE_NAMES = {
    "de": "German",
    "fr": "French",
    "es": "Spanish",
    "it": "Italian",
    "pt": "Brazilian Portuguese",
    "pl": "Polish",
    "tr": "Turkish",
    "nl": "Dutch",
    "ru": "Russian",
    "uk": "Ukrainian",
    "zh": "Simplified Chinese",
    "ja": "Japanese",
    "ko": "Korean",
}


def _language_name(code: str) -> str:
    """Maps a client locale code to the language name the LLM is instructed with. Unknown codes
    fall back to English — mirroring the game's per-key English fallback. The deterministic
    offline templates stay EN/DE only; other languages get the English template."""
    return _LANGUAGE_NAMES.get((code or "en").lower()[:2], "English")


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
            timeout=_TIMEOUT,
        )
    except Exception:
        return None


# ==============================================================================================
# NPC line (item 15 L1 + L2 memory/persona + VEGA ship-AI banter)
# ==============================================================================================
class NpcLineInput(TypedDict, total=False):
    npc_name: str
    role: str
    job: str             # #2590: "guard" / "gardener" / a profession such as "doctor" — "" when none
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
    """The no-model line. Only the vendor and the quartermaster have one (it is about trading / the job
    board); every other role answers "" so the game shows its own localized line for that NPC instead of
    a merchant's "Care to trade?" (#2590). Current servers send the player name as the placeholder
    {player} and fill in the receiving player's name themselves — the template simply copies it."""
    role = req.get("role") or ""
    if role not in ("vendor", "quartermaster"):
        return ""  # settlers, guards, professions, VEGA banter … — the game's scripted lines cover them
    de = (req.get("language") or "en").lower().startswith("de")
    name = req.get("player_name") or ("Reisender" if de else "traveler")
    known = (req.get("past_interactions") or 0) > 0
    if role == "quartermaster":
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
    "exploration game played by families and kids. Keep it to ONE or TWO sentences, natural, "
    "friendly and grounded, with a hint of the character's role and personality. Keep it "
    "kid-friendly: bandits are chased off, never hurt; no fear, no romance, no violence, no insults. "
    "Do NOT use quotation marks, emojis, stage directions or the player's stats verbatim — just the "
    "spoken line. Write it in {language}."
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
    "If you use the player's name, write it EXACTLY as given above, character for character — it may "
    "be the placeholder {{player}}, which the game replaces with the real name. Copy it with its "
    "braces and never invent, translate or change a name.\n"
    "Write the line now."
)

# Role and job descriptions (#2590): a settler is described by what they do, so a gardener doesn't
# sound like a merchant. The server sends the NPC's job next to its role; a known job wins.
_ROLE_DESC = {
    "vendor": "merchant who barters goods themed around the settlement",
    "quartermaster": "mission-giver who hands out field jobs from a board",
    "ship_ai": "the player's ship AI: dry, laconic, deadpan-witty but loyal; it comments on the "
               "current situation in passing, like a co-pilot making conversation",
    "settler": "villager who lives here and likes a friendly chat with visitors",
    "guardian": "guardian machine that watches over an ancient city",
    "guard": "guard who keeps watch at the walls and sends bandit scouts on their way",
    "gardener": "gardener who grows berries, fruit and seedlings for everyone",
    "craftsman": "craftsman who works at the workbench and the forge",
    "innkeeper": "innkeeper who runs the tavern and always knows the latest news",
    "doctor": "doctor who looks after everyone's health and sells medkits",
    "grocer": "grocer who sells food and fresh produce in a little shop",
    "arms_dealer": "trader of tools and gear for explorers",
    "sage": "wise sage who reads old books and gladly shares what they know",
    "tamer": "animal tamer who cares for tame creatures",
    "blockfarmer": "blockfarmer who quarries building blocks outside the village",
    "rail_dealer": "rail dealer who sells monorail parts at the depot",
    "streamer": "streamer who loves taking photos and sharing adventures",
    "reporter": "reporter who interviews people and writes the local news",
    # #2639 the wilderness folk: people who live out in the open land — at a lodge or on the road.
    "gatherer": "gatherer who lives out in the wilds and picks berries, fruit and herbs",
    "miner": "miner who lives out in the wilds and chips ore from the rock",
    "woodcutter": "woodcutter who lives out in the wilds, fells trees and always plants a new one",
    "hunter": "hunter who lives out in the wilds, follows the herds and trades meat",
    # #2682: a pioneer — an explorer who built a small base of their own out in the wilds.
    "pioneer": "pioneer and explorer who built a small outpost of their own out in the wilds, hunts and gathers "
               "nearby and is sometimes away exploring for a day",
}


def _role_desc(role: str, job: str = "") -> str:
    """How a speaker is described to the model: by its job when known, else by its role."""
    return _ROLE_DESC.get(job or "") or _ROLE_DESC.get(role or "", "settlement inhabitant")


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
                "role_desc": _role_desc(r.get("role", ""), r.get("job", "")),
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
    bilingual template so the endpoint never errors out. (For every role but vendor/quartermaster the
    fallback is empty — the game has its own localized lines for them, and VEGA banter is pure LLM
    flavour.)"""
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
# /npc-lines (#2591) — ONE path for every new NPC/VEGA line of the village-life package. The game
# always has an authored, localized line; the model only re-words it in the speaker's voice. Exact
# facts (names, numbers, items, places) travel as placeholder tokens such as {player} that the
# server fills in per receiving player — so the model must copy tokens literally and invent nothing.
# The C# server validates the answer again (count, length, tokens, digits) and drops it on any doubt.
# ==============================================================================================
class NpcLinesSpeaker(TypedDict, total=False):
    token: str
    role: str
    job: str
    persona: str
    is_robot: bool


class NpcLinesInput(TypedDict, total=False):
    purpose: str
    language: str
    place: str
    theme: str
    stage: str
    away_days: int
    situation: str
    speakers: list[NpcLinesSpeaker]
    topic_hint: str
    facts: list[str]
    tokens: list[str]
    authored: list[str]
    line_count: int
    max_chars: int


# Hard sanity bounds — the server never asks for more (a festival cheer is 6 lines).
_MAX_LINE_COUNT = 8
_MAX_LINE_CHARS = 400

# One sentence of guidance per purpose. An unknown purpose still works (generic guidance).
_PURPOSE_DESC = {
    "passing_hello": "A quick, friendly hello as the player walks past — one short sentence.",
    "welcome_back": "The character is glad the player is back after being away for a while.",
    "resident_hello": "A resident of the player's own home base says hello and shares that all is well at home.",
    "dialog_open": "Re-word the authored opening line of a conversation in the speaker's voice; keep its meaning "
                   "exactly, because the player's answer choices follow it.",
    "friend_hello": "A good friend is happy to see the player and greets them warmly.",
    "friend_gift": "A friend hands the player a small present and says something kind about it.",
    "chatter": "Two villagers chat with each other about the topic; the player only overhears them.",
    "cheer": "Villagers cheer happily at a village festival held in the player's honour.",
    "festival_thanks": "The quartermaster thanks the player warmly for all their help, at the village festival.",
    "news_deed": "A short, cheerful local news headline about something good the player did.",
    "wish_ask": "A villager politely asks the player for a small favour; there is no hurry and no deadline.",
    "wish_thanks": "A villager happily thanks the player for bringing what they wished for.",
    "guest_greet": "A friendly visitor arrives at the player's base and says hello.",
    "guest_offer": "A visitor at the player's base offers a small gift, their goods or asks for a little help.",
    "trader_hail": "A trader who just landed calls out to the player to come and trade.",
    "vega_welcome": "VEGA, the player's ship AI, welcomes the player back — dry, warm and loyal.",
    "vega_news": "VEGA, the player's ship AI, passes on news from the player's base — calm and short.",
    "vega_praise": "VEGA, the player's ship AI, praises the player for an achievement — dry but proud.",
}

_LINES_SYSTEM = (
    "You write short spoken lines for characters in a friendly sci-fi voxel space exploration game "
    "played by families and kids. Every line is friendly, warm and simple. Bandits are only ever "
    "chased away or sent off, never hurt. No romance, no fear, no violence, no insults, nothing scary.\n"
    "Rules:\n"
    "- Each line has at most {max_chars} characters and is just what the character says: no quotation "
    "marks, no emojis, no stage directions, no speaker names or labels in front.\n"
    "- Placeholders: the request lists the allowed tokens, such as {{player}}. They stand for names, "
    "numbers, items and places that the game fills in. Write a token EXACTLY as given, braces included, "
    "and only the listed ones. Never replace a token with a real value.\n"
    "- Never invent names, numbers, items or places, and never write digits. Only mention facts from "
    "the request.\n"
    "- If the written lines are given, keep their meaning (and every token they use) — just say it in "
    "the speaker's own voice.\n"
    "- When there are several speakers, the lines alternate between them in the order given: line 1 "
    "is the first speaker, line 2 the second, and so on, starting again from the first.\n"
    "- Write every line in {language}.\n"
    "Return ONLY a JSON array of exactly {line_count} strings — no markdown fences, no commentary."
)

_LINES_HUMAN = (
    "Purpose: {purpose} — {purpose_desc}\n"
    "Place: {place}\n"
    "Community: {theme}\n"
    "Relationship with the player: {stage}\n"
    "Days the player was away: {away_days}\n"
    "Situation: {situation}\n"
    "Speakers, in speaking order:\n{speakers}\n"
    "Topic: {topic_hint}\n"
    "Facts you may mention:\n{facts}\n"
    "Allowed tokens: {tokens}\n"
    "The written lines (keep their meaning):\n{authored}\n\n"
    "Write the JSON array of {line_count} lines now."
)


def _bullets(items: list[str] | None, empty: str) -> str:
    rows = [f"- {str(i).strip()}" for i in (items or []) if str(i).strip()]
    return "\n".join(rows) if rows else empty


def _speakers_text(speakers: list[NpcLinesSpeaker] | None) -> str:
    rows = []
    for n, s in enumerate(speakers or [], start=1):
        kind = "an android" if s.get("is_robot") else "a person"
        token = s.get("token") or "(not named in the lines)"
        persona = s.get("persona") or "(no specific persona)"
        rows.append(f"{n}. {token}: {kind}, a {_role_desc(s.get('role', ''), s.get('job', ''))}; "
                    f"persona: {persona}")
    return "\n".join(rows) if rows else "1. a friendly villager"


def _parse_json_array(raw: str) -> list | None:
    """Extracts the first JSON array from an LLM reply (tolerates ```json fences and prose)."""
    if not raw:
        return None
    text = raw.strip()
    text = re.sub(r"^```(?:json)?\s*|\s*```$", "", text, flags=re.IGNORECASE)
    start = text.find("[")
    end = text.rfind("]")
    if start < 0 or end <= start:
        return None
    try:
        arr = json.loads(text[start:end + 1])
        return arr if isinstance(arr, list) else None
    except Exception:
        return None


_QUOTES = "\"'`“”„‚‘’«»‹›「」『』"


def _clean_lines(arr: list | None, line_count: int) -> list[str]:
    """Exactly `line_count` non-empty strings (whitespace collapsed, wrapping quotes stripped), else
    []. The server repeats every check — this only keeps obviously broken answers off the wire."""
    if not isinstance(arr, list) or len(arr) != line_count:
        return []
    lines = []
    for item in arr:
        if not isinstance(item, str):
            return []
        text = " ".join(item.split()).strip().strip(_QUOTES).strip()
        if not text:
            return []
        lines.append(text)
    return lines


def _lines_max_tokens(line_count: int) -> int:
    """~70 tokens per line, capped — enough for a short line in any language plus JSON overhead."""
    return min(600, 70 * max(1, line_count))


_LINES_CHAINS: dict = {}


def _lines_chain(max_tokens: int):
    """A prompt→model→text chain for one max_tokens budget, built on first use and kept (the line count
    only takes a handful of values, so this stays tiny). None when the stack is unavailable."""
    if max_tokens in _LINES_CHAINS:
        return _LINES_CHAINS[max_tokens]
    llm = _make_llm(max_tokens=max_tokens)
    chain = None
    if llm is not None:
        try:
            from langchain_core.output_parsers import StrOutputParser
            from langchain_core.prompts import ChatPromptTemplate

            prompt = ChatPromptTemplate.from_messages([("system", _LINES_SYSTEM), ("human", _LINES_HUMAN)])
            chain = prompt | llm | StrOutputParser()
        except Exception:
            chain = None
    _LINES_CHAINS[max_tokens] = chain
    return chain


def _build_lines_graph():
    """Compile a LangGraph that runs the /npc-lines prompt→model→JSON chain in a single node (like the
    other flows). Returns the compiled graph, or None if the LLM stack is unavailable / unconfigured."""
    if _make_llm(max_tokens=_lines_max_tokens(1)) is None:
        return None
    try:
        from langgraph.graph import END, START, StateGraph
    except Exception:
        return None

    class GraphState(TypedDict):
        req: NpcLinesInput
        lines: list[str]

    def generate(state: GraphState) -> GraphState:
        r = state["req"]
        line_count = int(r.get("line_count") or 1)
        chain = _lines_chain(_lines_max_tokens(line_count))
        if chain is None:
            return {"req": r, "lines": []}
        tokens = [t for t in (r.get("tokens") or []) if str(t).strip()]
        for s in r.get("speakers") or []:
            if s.get("token") and s["token"] not in tokens:
                tokens.append(s["token"])
        purpose = r.get("purpose") or "line"
        raw = chain.invoke(
            {
                "language": _language_name(r.get("language", "en")),
                "max_chars": int(r.get("max_chars") or 120),
                "line_count": line_count,
                "purpose": purpose,
                "purpose_desc": _PURPOSE_DESC.get(purpose, "A short, friendly line that fits the situation."),
                "place": r.get("place") or "a frontier settlement",
                "theme": r.get("theme") or "(none)",
                "stage": r.get("stage") or "(not relevant)",
                "away_days": int(r.get("away_days") or 0),
                "situation": r.get("situation") or "(an ordinary day)",
                "speakers": _speakers_text(r.get("speakers")),
                "topic_hint": r.get("topic_hint") or "(free)",
                "facts": _bullets(r.get("facts"), "(none)"),
                "tokens": ", ".join(tokens) if tokens else "(none — do not use any braces)",
                "authored": _bullets(r.get("authored"), "(none)"),
            }
        )
        return {"req": r, "lines": _clean_lines(_parse_json_array(raw), line_count)}

    g = StateGraph(GraphState)
    g.add_node("generate", generate)
    g.add_edge(START, "generate")
    g.add_edge("generate", END)
    return g.compile()


_LINES_GRAPH = _build_lines_graph()


def generate_npc_lines(req: NpcLinesInput) -> list[str]:
    """Returns exactly `line_count` lines, or [] (no model configured, a failed call, a malformed or
    wrong-length answer, or an out-of-range request). [] makes the server keep its authored lines."""
    line_count = int(req.get("line_count") or 0)
    max_chars = int(req.get("max_chars") or 0)
    if _LINES_GRAPH is None or not 1 <= line_count <= _MAX_LINE_COUNT or not 1 <= max_chars <= _MAX_LINE_CHARS:
        return []
    try:
        out = _LINES_GRAPH.invoke({"req": req, "lines": []})
        return list(out.get("lines") or [])
    except Exception:
        return []


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
    kind: str


_TEXT_SYSTEM = (
    "You write the posting text for ONE job on a mission board in a sci-fi voxel space "
    "exploration game played by families and kids. Respond with ONLY a JSON object — no "
    "markdown fences:\n"
    '{{"Title": str (<= 48 chars, punchy, no quotes), "Description": str (1-2 sentences, in the '
    "giver's voice, says WHY the job matters)}}\n"
    "Do not change quantities, items or the job itself — they are fixed. Keep it kid-friendly: "
    "bandits are chased away or driven off, never hurt or killed. Write both fields in {language}."
)

_TEXT_HUMAN = (
    "Job giver: {giver_name} at {place} (community of {theme})\n"
    "The job: {job}\n"
    "They pay: {reward_count} x {reward_item}\n\n"
    "Write the JSON now."
)

# The job line per mission kind — bounties must not read like delivery work.
_JOB_LINES = {
    "bounty_camp": (
        "Bandits set up a camp on this world and trouble travellers; drive every bandit out of "
        "the camp (it is marked on the hunter's map), then report back"
    ),
    "bounty_ship": (
        "A raider ship is prowling this star system and shaking down pilots; drive it off, then "
        "report back to the station"
    ),
}


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
        job = _JOB_LINES.get(
            r.get("kind") or "gather",
            "They need {required} x {need_item} delivered to their mission board".format(
                required=r.get("required") or 10,
                need_item=(r.get("need_item") or "supplies").replace("_", " "),
            ),
        )
        raw = chain.invoke(
            {
                "language": _language_name(r.get("language", "en")),
                "giver_name": r.get("giver_name") or "the quartermaster",
                "place": r.get("place") or "a frontier settlement",
                "theme": r.get("theme") or "settlers",
                "job": job,
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
