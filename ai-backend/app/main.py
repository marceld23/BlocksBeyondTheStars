"""Spacecraft optional AI mission backend.

A separate, optional Python service (technical requirements / anf_mission_editor.md §3).
It returns a structured MissionPlan that the authoritative C#/.NET server then validates,
clamps and publishes. This MVP uses a deterministic, template-based generator as a stand-in
for a real LLM; the HTTP contract is what matters and the LLM can be slotted in later.

The JSON it returns uses PascalCase keys and PascalCase enum values to match the C#
MissionPlan model exactly.

Run (with uv):
    uv run uvicorn app.main:app --host 127.0.0.1 --port 8077
"""
from __future__ import annotations

from fastapi import FastAPI
from pydantic import BaseModel

app = FastAPI(title="Spacecraft AI Mission Backend", version="0.1.0")


class MissionRequest(BaseModel):
    context: str = ""


# Keyword -> (objective type, target, required, reward item, reward count)
_TEMPLATES = {
    "iron": ("Mine", "iron_ore", 10, "iron_plate", 5),
    "copper": ("Mine", "copper_ore", 10, "cable", 4),
    "cable": ("Deliver", "cable", 5, "data_fragment", 1),
    "titanium": ("Mine", "titanium_ore", 8, "data_fragment", 2),
    "ice": ("Mine", "ice", 12, "data_fragment", 1),
}
_DEFAULT = ("Mine", "stone", 16, "iron_plate", 3)


def _pick(context: str):
    lowered = (context or "").lower()
    for keyword, template in _TEMPLATES.items():
        if keyword in lowered:
            return template
    return _DEFAULT


@app.get("/health")
def health() -> dict:
    return {"status": "ok"}


@app.post("/mission-plan")
def mission_plan(req: MissionRequest) -> dict:
    obj_type, target, required, reward_item, reward_count = _pick(req.context)

    # The "creative layer" would be LLM-written; here it is a simple, on-theme template.
    return {
        "Title": f"Field Order: {target.replace('_', ' ').title()}",
        "Description": (
            f"Command needs {required} {target.replace('_', ' ')}. "
            f"{req.context.strip()[:160]}".strip()
        ),
        "GiverName": "Outpost Control",
        "StartDialog": "We have a job that fits your gear. Bring back what we need.",
        "CompleteDialog": "Good work, pilot. The supplies will help everyone here.",
        "Difficulty": "normal",
        "Objectives": [
            {"Type": obj_type, "Target": target, "Required": required}
        ],
        "SuggestedRewards": [
            {"Item": reward_item, "Count": reward_count}
        ],
    }
