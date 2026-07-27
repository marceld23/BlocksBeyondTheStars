# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Shared editing helpers for the data/*.json content files (used by the merge tools).

Two things the plain `json` module cannot do here:

* **Read what the game reads.** `ContentLoader.JsonOptions` sets `ReadCommentHandling = Skip` and
  `AllowTrailingCommas`, so the shipped files legally contain `// ...` comment lines (recipes.json has
  several). `json.loads` chokes on them — merge_recipe.py used to die before writing anything (#520).
* **Write without trashing the file.** These files are hand-maintained: one entry per line, aligned
  columns, explanatory comments. A `json.dumps(indent=2)` round-trip reformats all of it (a one-material
  merge produced a 5000-line diff) and drops every comment. So an entry is spliced in as a single line
  and the rest of the file stays byte-identical.
"""
import json
import re
from pathlib import Path


def loads_relaxed(text):
    """Parses content JSON the way the game does: whole-line `//` comments and trailing commas allowed.

    Only comments that make up a whole line are stripped, so a `//` inside a string value survives.
    """
    text = re.sub(r"^[ \t]*//.*$", "", text, flags=re.M)
    text = re.sub(r",(\s*[}\]])", r"\1", text)
    return json.loads(text)


def load_entries(path):
    """The array in a content file as a list of dicts (empty when the file does not exist)."""
    path = Path(path)
    return loads_relaxed(path.read_text(encoding="utf-8")) if path.exists() else []


def render_entry(obj):
    """One entry as a single line in the house style: `{ "key": "x", "drops": [ { ... } ] }`."""
    if isinstance(obj, dict):
        body = ", ".join(f"{json.dumps(k)}: {render_entry(v)}" for k, v in obj.items())
        return "{ " + body + " }" if body else "{}"
    if isinstance(obj, list):
        return "[ " + ", ".join(render_entry(v) for v in obj) + " ]" if obj else "[]"
    return json.dumps(obj, ensure_ascii=False)


def _newline(text):
    return "\r\n" if "\r\n" in text else "\n"


def _entry_line_index(lines, key):
    """Index of the top-level entry line holding `"key": "<key>"`, or None."""
    marker = f'"key": "{key}"'
    for i, line in enumerate(lines):
        if marker in line and line.lstrip().startswith("{"):
            return i
    return None


def upsert_entry(path, key, entry, indent="  "):
    """Replaces (or appends) one entry in a content file, leaving every other byte alone.

    Returns True when the entry was newly added, False when an existing one was replaced.
    """
    path = Path(path)
    text = path.read_text(encoding="utf-8") if path.exists() else "[\n]\n"
    nl = _newline(text)
    lines = text.split(nl)
    rendered = indent + render_entry(entry)

    at = _entry_line_index(lines, key)
    if at is not None:
        lines[at] = rendered + ("," if lines[at].rstrip().endswith(",") else "")
        path.write_text(nl.join(lines), encoding="utf-8", newline="")
        return False

    close = max(i for i, line in enumerate(lines) if line.strip() == "]")
    prev = max((i for i in range(close) if lines[i].strip()), default=None)
    if prev is not None and not lines[prev].rstrip().endswith((",", "[")):
        lines[prev] = lines[prev].rstrip() + ","
    lines.insert(close, rendered)
    path.write_text(nl.join(lines), encoding="utf-8", newline="")
    return True


def upsert_ore_vein(path, vein, planet_matches):
    """Adds/replaces one ore vein in every matching planet's `ores` list in data/planets.json.

    Planets without an ore list (the `orbital_station` / `ship_interior` pseudo-planets) are skipped —
    they are not generated terrain. Returns (touched, skipped) planet key lists.
    """
    path = Path(path)
    text = path.read_text(encoding="utf-8")
    nl = _newline(text)
    lines = text.split(nl)
    planets = {p["key"]: p for p in loads_relaxed(text)}

    wanted = [k for k, p in planets.items() if planet_matches(p)]
    touched = [k for k in wanted if p_ores(planets[k])]
    skipped = [k for k in wanted if not p_ores(planets[k])]

    out, current, in_ores, replaced = [], None, False, False
    for line in lines:
        stripped = line.strip()
        header = re.match(r'"key":\s*"([a-z_0-9]+)"', stripped)
        if header:
            current = header.group(1)
        # Only a multi-line list ("ores": [ … one vein per line … ]) can be spliced; an inline one would
        # close on this same line and leave the state machine running into the next planet.
        if '"ores":' in stripped and stripped.endswith("["):
            in_ores, replaced = current in touched, False

        if in_ores and f'"block": "{vein["block"]}"' in stripped:
            # An earlier merge of the same material — replace that line in place.
            out.append(re.sub(r"\{.*\}", render_entry(vein), line))
            replaced = True
            continue

        if in_ores and stripped.startswith("]"):
            if not replaced:
                if out and not out[-1].rstrip().endswith(("[", ",")):
                    out[-1] = out[-1].rstrip() + ","
                indent = " " * (len(line) - len(line.lstrip()) + 2)
                out.append(indent + render_entry(vein))
            in_ores = False

        out.append(line)

    path.write_text(nl.join(out), encoding="utf-8", newline="")
    return touched, skipped


def p_ores(planet):
    return planet.get("ores") is not None


def add_locale_key(path, key, value):
    """Appends `"key": "value"` to a locale file if the key is missing; returns True when added.

    Same reason as `upsert_entry`: the locale files are hand-grouped with blank lines between sections,
    which a full JSON round-trip would flatten.
    """
    path = Path(path)
    text = path.read_text(encoding="utf-8")
    if json.loads(text).get(key) is not None:
        return False

    nl = _newline(text)
    lines = text.split(nl)
    close = max(i for i, line in enumerate(lines) if line.strip() == "}")
    prev = max((i for i in range(close) if lines[i].strip()), default=None)
    if prev is not None and not lines[prev].rstrip().endswith((",", "{")):
        lines[prev] = lines[prev].rstrip() + ","
    lines.insert(close, f"  {json.dumps(key)}: {json.dumps(value, ensure_ascii=False)}")
    path.write_text(nl.join(lines), encoding="utf-8", newline="")
    return True
