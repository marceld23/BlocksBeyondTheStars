# wix-i18n — website translation audit + import

Tools for managing the website's translations (Wix Multilingual) via the
Wix REST API, without Wix's machine translation. Translations are produced
locally (Claude), reviewed, then imported.

## Setup

Needs `WIX_API_KEY` + `WIX_SITE_ID` in `.env` here (or reuses
`tools/devblog/.env`). The API key needs the Wix Multilingual read/write
scopes (already present on the devblog key).

## Workflow

1. `uv run audit_translations.py` — read-only. Dumps schemas + per-locale
   contents and writes `out/report.md` (gap report: missing / partial /
   stale items per target locale) and `out/todo-<locale>.json`.
2. Translate: copy `todo-<locale>.json` to `translations-<locale>.json` and
   replace each field's `text` with the translation (keep HTML markup
   intact; keep game terms like "Blocks Beyond the Stars" or "VEGA"
   untranslated; translate image alt texts SEO-consciously).
3. Review with Marcel (side-by-side file).
4. `uv run import_translations.py out/translations-<locale>.json` — dry run,
   shows every write. Add `--apply` to actually write. Fields are written
   `published: true`; a HIDDEN locale stays invisible to visitors until its
   visibility is flipped (dashboard or Update Locale API).

`out/` and all website content dumps are gitignored — only the scripts live
in the repo.

## Notes

- Page SEO settings (meta title/description, URL slugs) are handled by
  Wix's SEO system, not the Translation Content API — see the analysis doc
  for the current findings.
- Blog posts are translated separately via `tools/devblog`
  (translationId-linked DE/EN drafts).
- hreflang + language subdirectories (`/en/`, `/it/`) are managed by Wix
  automatically for multilingual sites.
