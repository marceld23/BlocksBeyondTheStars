# /// script
# requires-python = ">=3.11"
# dependencies = ["requests"]
# ///
"""Create secondary site locales (HIDDEN) for the given language codes.

Skips locales that already exist. Usage:
  uv run create_locales.py es fr nl pl pt tr ru uk ja ko zh
"""
import sys

from audit_translations import API, fetch_locales, load_env, make_session

sys.stdout.reconfigure(encoding="utf-8")


def main() -> None:
    codes = sys.argv[1:]
    if not codes:
        sys.exit("usage: create_locales.py <languageCode> [...]")
    s = make_session(load_env())
    existing = {l["languageCode"] for l in fetch_locales(s)}
    for code in codes:
        if code in existing:
            print(f"{code}: exists, skipped")
            continue
        r = s.post(f"{API}/locales/v2/locale",
                   json={"locale": {"languageCode": code, "visibility": "HIDDEN"}})
        if r.status_code == 200:
            loc = r.json()["locale"]
            print(f"{code}: created HIDDEN (id={loc['id']}, display={loc.get('effectiveDisplayName')})")
        else:
            print(f"{code}: FAILED {r.status_code} {r.text[:200]}")


if __name__ == "__main__":
    main()
