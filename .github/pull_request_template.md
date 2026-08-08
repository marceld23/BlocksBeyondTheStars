<!-- Thanks for contributing! Small, focused PRs are easiest to review and merge. -->

## What does this change?

<!-- A short description of the change and why. -->

## Related issue

<!-- e.g. "Closes #123". Optional. -->

## Checklist

- [ ] I built and ran the tests before pushing (`dotnet build BlocksBeyondTheStars.sln` and `dotnet test`) — they pass.
- [ ] Player-facing text is localized, not hardcoded — new keys added to both `data/locales/en.json` and `de.json` (the mandatory pair; `fr`/`es`/`it` fall back to English per missing key).
- [ ] The server stays authoritative — the client doesn't decide resources, inventory, crafting, ship state, oxygen, damage, blueprints or travel.
- [ ] I updated `TODO.md` and any `docs/` that my change makes stale (if applicable).

<!-- See CONTRIBUTING.md and AGENTS.md for the full contributor guide. -->
