#!/usr/bin/env python3
"""Generate the glyph-advance table the headless UI text-fit test measures with (#3263).

The Unity client draws its legacy uGUI text with the bundled Rajdhani-Medium and, for every character Rajdhani lacks,
the fallback fonts in the order of `fallbackFontReferences` in `client/Assets/Resources/fonts/Rajdhani-Medium.ttf.meta`
(Noto Sans, Noto Sans Symbols 2, Noto Sans SC, JP, KR). PR CI never runs Unity, so the test that keeps the editors'
labels inside their buttons (`tests/BlocksBeyondTheStars.Client.Tests/EditorTextFitTests.cs`) cannot ask Unity's
TextGenerator how wide a label is. It sums these advances instead — rounded per glyph at the font size, like Unity's
`shouldRoundAdvanceValue` importer setting, without kerning (legacy uGUI text has none). Calibrated against widths
measured in Unity (ChipRowLayoutTests: "Lagerstätten 12" 139 px, "Месторождения 12" 201 px at 22 px — estimated 141
and 201): within ±2 %.

The table holds every code point of the scripts the game's locales use (Latin, Greek, Cyrillic, punctuation, arrows
and symbols, CJK punctuation, kana, fullwidth forms) as runs of equal advance. The CJK ideographs and the Hangul
syllables are one uniform run each — the bundled CJK fonts are subsets, but every glyph of those blocks has the same
advance (verified here), so a character a future translation adds is measured right before the subset is rebuilt.

Re-run after changing or re-subsetting a font:

  uv run --no-project --with fonttools python tools/gen_font_advances.py

and commit the regenerated `tests/BlocksBeyondTheStars.Client.Tests/UiFontAdvances.g.cs`.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

from fontTools.ttLib import TTFont

REPO = Path(__file__).resolve().parent.parent
FONTS = REPO / "client" / "Assets" / "Resources" / "fonts"
PRIMARY = "Rajdhani-Medium.ttf"
OUT = REPO / "tests" / "BlocksBeyondTheStars.Client.Tests" / "UiFontAdvances.g.cs"

# Scripts the game's text may use (code point ranges, inclusive). Everything else measures as 1 em in the test.
TABLE_RANGES = [
    (0x0020, 0x052F),  # Latin, Latin-1, Latin Extended-A/B, IPA, Greek, Cyrillic (+ supplement)
    (0x1E00, 0x1EFF),  # Latin Extended Additional
    (0x2000, 0x2BFF),  # punctuation, super/subscripts, currency, letterlike, arrows, maths, shapes, symbols, dingbats
    (0x3000, 0x30FF),  # CJK symbols and punctuation, hiragana, katakana
    (0x3130, 0x318F),  # Hangul compatibility jamo
    (0xFF00, 0xFFEF),  # halfwidth and fullwidth forms
]
# Blocks emitted as ONE run when every glyph the fonts have in them shares an advance.
UNIFORM_BLOCKS = [
    (0x3400, 0x4DBF),  # CJK Unified Ideographs Extension A
    (0x4E00, 0x9FFF),  # CJK Unified Ideographs
    (0xAC00, 0xD7A3),  # Hangul syllables
    (0xF900, 0xFAFF),  # CJK Compatibility Ideographs
]


def fallback_chain() -> list[str]:
    """The font files in uGUI's lookup order: the primary font, then its fallbacks as listed in its .meta file."""
    guid_to_file = {}
    for meta in FONTS.glob("*.meta"):
        m = re.search(r"^guid: ([0-9a-f]+)$", meta.read_text(encoding="utf-8"), re.MULTILINE)
        if m:
            guid_to_file[m.group(1)] = meta.name[: -len(".meta")]
    meta_text = (FONTS / (PRIMARY + ".meta")).read_text(encoding="utf-8")
    fallbacks = re.findall(r"- \{fileID: \d+, guid: ([0-9a-f]+), type: 3\}", meta_text)
    return [PRIMARY] + [guid_to_file[g] for g in fallbacks]


def load(chain: list[str]):
    fonts = []
    for name in chain:
        font = TTFont(str(FONTS / name), lazy=True)
        upem = font["head"].unitsPerEm
        fonts.append((name, font.getBestCmap(), font["hmtx"], upem))
    return fonts


def advance(fonts, cp: int):
    """The advance of a code point in 1/1000 em from the first font that has it, or None."""
    for _, cmap, hmtx, upem in fonts:
        if cp in cmap:
            return round(hmtx[cmap[cp]][0] * 1000 / upem)
    return None


def runs_for(fonts) -> list[tuple[int, int, int]]:
    points: dict[int, int] = {}
    for lo, hi in TABLE_RANGES:
        for cp in range(lo, hi + 1):
            adv = advance(fonts, cp)
            if adv is not None:
                points[cp] = adv
    runs: list[list[int]] = []
    for cp in sorted(points):
        adv = points[cp]
        if runs and runs[-1][1] == cp - 1 and runs[-1][2] == adv:
            runs[-1][1] = cp
        else:
            runs.append([cp, cp, adv])
    for lo, hi in UNIFORM_BLOCKS:
        advs = {advance(fonts, cp) for cp in range(lo, hi + 1)} - {None}
        if len(advs) > 1:
            sys.exit(f"block U+{lo:04X}..U+{hi:04X} is not uniform ({sorted(advs)}) — list its glyphs in TABLE_RANGES")
        if advs:
            runs.append([lo, hi, advs.pop()])
    return sorted((r[0], r[1], r[2]) for r in runs)


def line_height_em(fonts) -> float:
    """uGUI's line box for the primary font: (hhea ascender − descender + line gap) / em."""
    font = TTFont(str(FONTS / PRIMARY), lazy=True)
    hhea = font["hhea"]
    return (hhea.ascent - hhea.descent + hhea.lineGap) / font["head"].unitsPerEm


def render(chain: list[str], runs, lh: float) -> str:
    body = []
    for i in range(0, len(runs), 8):
        chunk = runs[i:i + 8]
        body.append("        " + " ".join(f"0x{a:04X}, 0x{b:04X}, {c}," for a, b, c in chunk))
    return "\n".join([
        "// <auto-generated>",
        "//   Generated by tools/gen_font_advances.py from client/Assets/Resources/fonts — do not edit by hand.",
        "//   Re-run: uv run --no-project --with fonttools python tools/gen_font_advances.py",
        "// </auto-generated>",
        "namespace BlocksBeyondTheStars.Client.Tests;",
        "",
        "/// <summary>Glyph advances of the client's uGUI font chain (" + " → ".join(chain) + "), for the headless",
        "/// text-fit test (#3263). <see cref=\"Runs\"/> holds triples (first code point, last code point, advance in 1/1000",
        "/// em), sorted and non-overlapping.</summary>",
        "internal static class UiFontAdvances",
        "{",
        "    /// <summary>The primary font's line box per em (hhea ascender − descender + line gap).</summary>",
        f"    public const float LineHeightEm = {lh:.3f}f;",
        "",
        "    public static readonly int[] Runs =",
        "    {",
        *body,
        "    };",
        "}",
        "",
    ])


def main() -> None:
    chain = fallback_chain()
    fonts = load(chain)
    runs = runs_for(fonts)
    OUT.write_text(render(chain, runs, line_height_em(fonts)), encoding="utf-8", newline="\n")
    print(f"{OUT.relative_to(REPO)}: {len(runs)} runs from {', '.join(chain)}")


if __name__ == "__main__":
    main()
