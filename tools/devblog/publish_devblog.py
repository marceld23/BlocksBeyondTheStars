# /// script
# requires-python = ">=3.11"
# dependencies = ["requests"]
# ///
"""Publish devblog articles from devblog-artikel.md as Wix blog DRAFTS.

Parses the markdown drafts (devblog-artikel.md in this folder), converts each
article body to Ricos via the Wix conversion API and creates draft posts
(never publishes). Already-posted articles are tracked in posted.json and
skipped on re-runs.

English translations: devblog-artikel-en.md mirrors the German file
article-by-article (same order). --translate-en creates EN drafts linked to
their German counterpart via a shared translationId (Wix Multilingual).

Usage (from tools/devblog/):
  uv run publish_devblog.py --list          # parse only, show what would be posted
  uv run publish_devblog.py --limit 1       # post the first unposted article (DE)
  uv run publish_devblog.py                 # post all unposted articles (DE)
  uv run publish_devblog.py --translate-en  # create linked EN drafts where available
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
import uuid
from pathlib import Path

import requests

SCRIPT_DIR = Path(__file__).resolve().parent
ARTICLES_MD = SCRIPT_DIR / "devblog-artikel.md"
ARTICLES_EN_MD = SCRIPT_DIR / "devblog-artikel-en.md"
STATE_FILE = SCRIPT_DIR / "posted.json"
ENV_FILE = SCRIPT_DIR / ".env"

API = "https://www.wixapis.com"
RICOS_PLUGINS = [
    "HEADING", "LINK", "IMAGE", "DIVIDER", "CODE_BLOCK", "TEXT_COLOR",
    "TEXT_HIGHLIGHT", "TABLE", "INDENT", "EMOJI",
]
# German category label -> English category label (both exist in the blog)
CATEGORY_DE_TO_EN = {
    "Technik": "Technology",
    "Entwicklung": "Development",
    "Hinter den Kulissen": "Behind the Scenes",
    "Updates": "Updates",
    "Story-Teaser": "Story Teasers",
    "Mitmachen": "Get Involved",
}

# Backdated publish dates, index-aligned with the DRAFT articles in file
# order (devblog-artikel.md). Chosen to sit thematically next to the matching
# releases/events on the existing blog timeline. EN posts get +5 minutes.
PUBLISH_DATES = [
    "2026-07-05T15:20:00Z",  # 1  Hosted Worlds (hosted-worlds era, before v0.7.0)
    "2026-07-05T20:45:00Z",  # 2  Kleiner VPS als Spieleserver-Flotte
    "2026-07-07T17:25:00Z",  # 3  Release-Pipeline (deploy era)
    "2026-06-30T18:05:00Z",  # 4  Voxel-Spiel im Browser (after v0.6.2 browser peek)
    "2026-07-06T19:40:00Z",  # 5  Server-Wartung (maintenance announcements)
    "2026-07-06T15:30:00Z",  # 6  Passwörter/Rate-Limits (v0.7.1 safety day)
    "2026-06-25T16:40:00Z",  # 7  MIT zu AGPL (day after open-source post)
    "2026-06-27T19:30:00Z",  # 8  Shader fehlten (post graphics overhaul)
    "2026-06-26T17:50:00Z",  # 9  Grafik-Overhaul (day after v0.5.0 glow-up)
    "2026-06-27T14:15:00Z",  # 10 Licht in Voxelwelt
    "2026-06-22T17:05:00Z",  # 11 Jeder Block solide (early gotcha)
    "2026-06-23T18:20:00Z",  # 12 KI-Texturen und KI-Sounds
    "2026-06-21T18:35:00Z",  # 13 Netcode-Fallen (early dev)
    "2026-07-02T16:45:00Z",  # 14 1000 Tests
    "2026-07-08T17:50:00Z",  # 15 Öffentliche Welten nur mit Passwort (after v0.7.3)
    "2026-07-07T21:05:00Z",  # 16 Startplanet nicht mehr grau
    "2026-07-08T20:35:00Z",  # 17 Schatzkarten ohne KI
    "2026-07-04T20:10:00Z",  # 18 VEGA und das KI-Backend
    "2026-06-28T18:40:00Z",  # 19 Zähmen, Allianzen, Teleporter
    "2026-07-03T20:50:00Z",  # 20 Hunger, Algen (before algae-tank release)
    "2026-07-04T17:35:00Z",  # 21 Making-of Energiezaun
    "2026-07-03T18:30:00Z",  # 22 Rundere Formen (before v0.7.0 organic look)
    "2026-07-09T16:55:00Z",  # 23 Kindgerecht by Design
    "2026-07-09T19:45:00Z",  # 24 Dinge, die wir gelernt haben
    "2026-06-27T21:10:00Z",  # 25 Open Source von Anfang an (first-contributor day)
    "2026-06-22T20:15:00Z",  # 26 Tier IM Schiff (v0.4.1 stowaways day)
    "2026-06-26T15:25:00Z",  # 27 Ist das Wasser echt (after glow-up)
    "2026-06-28T20:55:00Z",  # 28 Justus Fabrik-Imperium (factories era)
    "2026-07-09T21:20:00Z",  # 29 Freunde zerlegen unser Spiel (playtest era)
    "2026-06-30T20:40:00Z",  # 30 Vom Einfall zur Spielmechanik
    "2026-06-21T15:10:00Z",  # 31 Spiel für mein Kind
    "2026-07-01T19:15:00Z",  # 32 Abends nach dem Job
    "2026-06-24T19:05:00Z",  # 33 Arcade: 20 Minispiele
    "2026-06-29T17:20:00Z",  # 34 Musik, Sounds und Texturen
    "2026-07-10T17:05:00Z",  # 35 Story-Teaser: Das VEGA-Protokoll (today, after school post)
    "2026-07-10T22:00:00Z",  # 36 Baumhaus-Prinzip (right after the v0.7.5 news; file order!)
    "2026-07-10T21:30:00Z",  # 37 Version 0.7.5 news (already live; sits after the export section in the md)
    "2026-07-12T20:30:00Z",  # 38 Version 0.7.6 news (already live; posted right after the v0.7.6 release)
]
EN_EXTRA_MINUTES = 5


def load_env() -> dict[str, str]:
    env: dict[str, str] = {}
    for line in ENV_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, value = line.partition("=")
        env[key.strip()] = value.strip()
    missing = [k for k in ("WIX_API_KEY", "WIX_SITE_ID") if not env.get(k)]
    if missing:
        sys.exit(f"Missing in .env: {', '.join(missing)}")
    return env


META_RE = re.compile(r"\*\*(Kategorie|Category|Veröffentlicht|Published):\*\*\s*(.+)")
# top-level headers that structure the file (vs. h1/h2 inside an article body)
SECTION_PREFIXES = ("# Kategorie", "# Category", "# Bereits", "# Already", "# Devblog")


def parse_articles(path: Path) -> list[dict]:
    """Split a drafts file into articles at '## ' headings.

    A '## ' line only starts a new article if a **Kategorie:**/**Category:**
    line follows within a few lines — headings inside exported post bodies
    stay part of the body. Articles with a **Veröffentlicht:**/**Published:**
    line are already live on the blog and are never (re)posted.
    """
    lines = path.read_text(encoding="utf-8").splitlines()

    def starts_article(i: int) -> bool:
        if not lines[i].startswith("## "):
            return False
        for j in range(i + 1, min(i + 7, len(lines))):
            if lines[j].startswith("#"):
                return False
            m = META_RE.match(lines[j])
            if m and m.group(1) in ("Kategorie", "Category"):
                return True
        return False

    articles = []
    current: dict | None = None
    for i, line in enumerate(lines):
        if line.startswith("# ") and (current is None
                                      or any(line.startswith(p) for p in SECTION_PREFIXES)):
            current = None
            continue
        if starts_article(i):
            current = {"title": line[3:].strip(), "category": None,
                       "published": None, "body": []}
            articles.append(current)
            continue
        if current is None:
            continue
        m = META_RE.match(line)
        if m:
            key, value = m.group(1), m.group(2).strip()
            if key in ("Kategorie", "Category"):
                current["category"] = value
            else:
                current["published"] = value
            continue
        current["body"].append(line)

    for a in articles:
        body = "\n".join(a["body"])
        # drop the trailing '---' separator between articles
        body = re.sub(r"\n---\s*$", "", body.strip()).strip()
        a["body"] = body
    return [a for a in articles if a["body"] and a["category"]]


def add_paragraph_spacing(document: dict) -> dict:
    """Insert an empty paragraph between consecutive top-level paragraphs
    so published posts show a visible blank line between them."""
    def empty_paragraph() -> dict:
        return {"type": "PARAGRAPH", "id": "", "nodes": [], "paragraphData": {}}

    spaced: list[dict] = []
    nodes = document.get("nodes", [])
    for i, node in enumerate(nodes):
        spaced.append(node)
        nxt = nodes[i + 1] if i + 1 < len(nodes) else None
        if node.get("type") == "PARAGRAPH" and nxt and nxt.get("type") == "PARAGRAPH":
            spaced.append(empty_paragraph())
    document["nodes"] = spaced
    return document


class WixClient:
    def __init__(self, env: dict[str, str]):
        self.session = requests.Session()
        self.session.headers.update({
            "Authorization": env["WIX_API_KEY"],
            "wix-site-id": env["WIX_SITE_ID"],
            "Content-Type": "application/json",
        })
        self.member_id = env.get("WIX_MEMBER_ID", "")

    def _post(self, path: str, payload: dict) -> dict:
        r = self.session.post(f"{API}{path}", json=payload, timeout=60)
        if not r.ok:
            sys.exit(f"POST {path} -> HTTP {r.status_code}: {r.text[:500]}")
        return r.json()

    def owner_member_id(self) -> str:
        if self.member_id:
            return self.member_id
        r = self.session.get(f"{API}/members/v1/members", timeout=60)
        r.raise_for_status()
        members = r.json()["members"]
        if len(members) != 1:
            sys.exit(f"Expected exactly 1 site member, got {len(members)}; "
                     f"set WIX_MEMBER_ID in .env explicitly.")
        self.member_id = members[0]["id"]
        return self.member_id

    def categories(self, language: str) -> dict[str, str]:
        r = self.session.get(f"{API}/blog/v3/categories?paging.limit=100", timeout=60)
        r.raise_for_status()
        return {
            c["label"]: c["id"]
            for c in r.json()["categories"]
            if c.get("language") == language
        }

    def markdown_to_ricos(self, markdown: str) -> dict:
        resp = self._post(
            "/ricos/v1/ricos-document/convert/to-ricos",
            {"markdown": markdown, "options": {"plugins": RICOS_PLUGINS}},
        )
        return add_paragraph_spacing(resp["document"])

    def create_draft(self, title: str, ricos: dict, category_id: str,
                     language: str, translation_id: str) -> dict:
        resp = self._post(
            "/blog/v3/draft-posts",
            {
                "draftPost": {
                    "title": title,
                    "memberId": self.owner_member_id(),
                    "richContent": ricos,
                    "categoryIds": [category_id],
                    "language": language,
                    "translationId": translation_id,
                    "commentingEnabled": True,
                },
                "publish": False,
            },
        )
        return resp["draftPost"]

    def delete_draft(self, draft_id: str) -> None:
        r = self.session.delete(f"{API}/blog/v3/draft-posts/{draft_id}", timeout=60)
        if not r.ok and r.status_code != 404:
            sys.exit(f"DELETE draft {draft_id} -> HTTP {r.status_code}: {r.text[:300]}")

    def publish(self, draft_id: str) -> str:
        resp = self._post(f"/blog/v3/draft-posts/{draft_id}/publish", {})
        return resp["postId"]

    def backdate(self, post_id: str, iso_date: str) -> None:
        r = self.session.patch(
            f"{API}/blog/v3/draft-posts/{post_id}",
            json={"draftPost": {"id": post_id, "firstPublishedDate": iso_date},
                  "action": "UPDATE_PUBLICATION"},
            timeout=60,
        )
        if not r.ok:
            sys.exit(f"backdate {post_id} -> HTTP {r.status_code}: {r.text[:300]}")


def save_state(state: dict) -> None:
    STATE_FILE.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")


def post_german(wix: WixClient, state: dict, limit: int) -> None:
    articles = [a for a in parse_articles(ARTICLES_MD) if not a["published"]]
    pending = [a for a in articles if a["title"] not in state]
    print(f"{len(articles)} DE draft articles parsed, {len(pending)} not yet posted.")

    cats = wix.categories("de")
    missing = {a["category"] for a in pending} - set(cats)
    if missing:
        sys.exit(f"Categories missing in the blog (create them first): {missing}")

    if limit:
        pending = pending[:limit]

    for a in pending:
        ricos = wix.markdown_to_ricos(a["body"])
        translation_id = str(uuid.uuid4())
        draft = wix.create_draft(a["title"], ricos, cats[a["category"]],
                                 "de", translation_id)
        state[a["title"]] = {
            "draftPostId": draft["id"],
            "category": a["category"],
            "translationId": draft.get("translationId", translation_id),
        }
        save_state(state)
        print(f"  DE draft: [{a['category']}] {a['title']} -> {draft['id']}")
        time.sleep(0.5)  # stay clear of rate limits


def post_english(wix: WixClient, state: dict, limit: int) -> None:
    if not ARTICLES_EN_MD.exists():
        sys.exit(f"{ARTICLES_EN_MD.name} not found — add English translations first.")
    de_articles = [a for a in parse_articles(ARTICLES_MD) if not a["published"]]
    en_articles = [a for a in parse_articles(ARTICLES_EN_MD) if not a["published"]]
    cats_en = wix.categories("en")

    # EN file mirrors the DE file by order; it may be shorter while
    # translations are still being added.
    pairs = list(zip(de_articles, en_articles))
    pending = []
    for de, en in pairs:
        entry = state.get(de["title"])
        if entry is None:
            print(f"  skipped (DE not posted yet): {de['title']}")
            continue
        if entry.get("enDraftId"):
            continue
        pending.append((de, en, entry))

    print(f"{len(en_articles)} EN articles available, {len(pending)} to post.")
    if limit:
        pending = pending[:limit]

    for de, en, entry in pending:
        category_en = CATEGORY_DE_TO_EN.get(de["category"], en["category"])
        if category_en not in cats_en:
            sys.exit(f"EN category missing in the blog: {category_en}")
        ricos = wix.markdown_to_ricos(en["body"])
        draft = wix.create_draft(en["title"], ricos, cats_en[category_en],
                                 "en", entry["translationId"])
        entry["enDraftId"] = draft["id"]
        entry["enTitle"] = en["title"]
        save_state(state)
        linked = draft.get("translationId") == entry["translationId"]
        print(f"  EN draft: [{category_en}] {en['title']} -> {draft['id']}"
              f" (linked: {linked})")
        time.sleep(0.5)


def add_minutes(iso_date: str, minutes: int) -> str:
    from datetime import datetime, timedelta
    dt = datetime.fromisoformat(iso_date.replace("Z", "+00:00")) + timedelta(minutes=minutes)
    return dt.isoformat().replace("+00:00", "Z")


def go_live(wix: WixClient, state: dict, limit: int) -> None:
    """Create (if needed), publish and backdate all DE+EN article pairs."""
    de_articles = [a for a in parse_articles(ARTICLES_MD) if not a["published"]]
    en_articles = [a for a in parse_articles(ARTICLES_EN_MD) if not a["published"]]
    if len(PUBLISH_DATES) != len(de_articles):
        sys.exit(f"PUBLISH_DATES has {len(PUBLISH_DATES)} entries but there are "
                 f"{len(de_articles)} DE draft articles — fix the table first.")
    if len(en_articles) != len(de_articles):
        sys.exit(f"EN translations incomplete: {len(en_articles)} EN vs "
                 f"{len(de_articles)} DE draft articles.")

    cats_de = wix.categories("de")
    cats_en = wix.categories("en")

    todo = list(zip(de_articles, en_articles, PUBLISH_DATES))
    if limit:
        todo = [t for t in todo
                if not state.get(t[0]["title"], {}).get("liveEn")][:limit]

    for de, en, date_de in todo:
        entry = state.setdefault(de["title"], {})
        date_en = add_minutes(date_de, EN_EXTRA_MINUTES)

        if not entry.get("draftPostId"):
            ricos = wix.markdown_to_ricos(de["body"])
            draft = wix.create_draft(de["title"], ricos, cats_de[de["category"]],
                                     "de", str(uuid.uuid4()))
            entry.update(draftPostId=draft["id"], category=de["category"],
                         translationId=draft.get("translationId"))
            save_state(state)
        if not entry.get("enDraftId"):
            category_en = CATEGORY_DE_TO_EN.get(de["category"], en["category"])
            ricos = wix.markdown_to_ricos(en["body"])
            draft = wix.create_draft(en["title"], ricos, cats_en[category_en],
                                     "en", entry["translationId"])
            entry.update(enDraftId=draft["id"], enTitle=en["title"])
            save_state(state)

        if not entry.get("liveDe"):
            wix.publish(entry["draftPostId"])
            wix.backdate(entry["draftPostId"], date_de)
            entry["liveDe"] = date_de
            save_state(state)
        if not entry.get("liveEn"):
            wix.publish(entry["enDraftId"])
            wix.backdate(entry["enDraftId"], date_en)
            entry["liveEn"] = date_en
            save_state(state)

        print(f"  LIVE {date_de[:16]}  [{de['category']}] {de['title'][:55]}  (+EN)")
        time.sleep(0.5)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--list", action="store_true", help="parse only, create nothing")
    parser.add_argument("--limit", type=int, default=0, help="max number of drafts to create")
    parser.add_argument("--translate-en", action="store_true",
                        help="create EN drafts (linked via translationId) instead of DE")
    parser.add_argument("--go-live", action="store_true",
                        help="create+publish+backdate all DE/EN article pairs")
    args = parser.parse_args()

    if args.list:
        for path in (ARTICLES_MD, ARTICLES_EN_MD):
            if not path.exists():
                continue
            state = json.loads(STATE_FILE.read_text(encoding="utf-8")) if STATE_FILE.exists() else {}
            articles = parse_articles(path)
            drafts = [a for a in articles if not a["published"]]
            print(f"{path.name}: {len(articles)} articles "
                  f"({len(drafts)} drafts, {len(articles) - len(drafts)} published)")
            for a in articles:
                tag = f" — published {a['published']}" if a["published"] else ""
                print(f"  [{a['category']}] {a['title']} ({len(a['body'])} chars){tag}")
        return

    env = load_env()
    wix = WixClient(env)
    state = json.loads(STATE_FILE.read_text(encoding="utf-8")) if STATE_FILE.exists() else {}

    if args.go_live:
        go_live(wix, state, args.limit)
    elif args.translate_en:
        post_english(wix, state, args.limit)
    else:
        post_german(wix, state, args.limit)

    print("Done. Review the drafts in the Wix dashboard (Blog -> Posts -> Drafts).")


if __name__ == "__main__":
    main()
