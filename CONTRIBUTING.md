# Contributing to Blocks Beyond the Stars

Thanks for your interest! **Blocks Beyond the Stars** is a family project that we are
opening up to the community — and we would love your help. There are three ways to join
in, from "no setup at all" to "write some code".

Everyone is welcome here — players who tell us what's confusing, hobby and professional
programmers, and experts from every craft (graphics, music, game design, UX, translation …).
Feedback-only contributions count fully; if you want to know what you'd be joining, the project's
[vision](docs/strategy/vision.md), [mission](docs/strategy/mission.md) and
[roadmap](docs/strategy/roadmap.md) are short reads.

Please be kind — see our short
[Code of Conduct](CODE_OF_CONDUCT.md) (it's basically "be nice to one another").

> Repository: <https://github.com/marceld23/BlocksBeyondTheStars>
> · Website: <https://www.blocksbeyondthestars.com/en>

## 1. Play it (and have fun)

The simplest contribution: download a build, play, and tell us what you think. Every hour
someone spends playing helps us find rough edges. No account or setup required.

## 2. Report a bug

Found something broken or confusing? Open a **GitHub issue**:
<https://github.com/marceld23/BlocksBeyondTheStars/issues>

A good report makes a bug fixable. Please include:

- **What you did** — the steps that led to it (as exactly as you can).
- **What you expected** to happen.
- **What actually happened** — and a screenshot if it helps.
- **Where** — singleplayer or on a server, which planet/screen, roughly when.

Search the existing issues first in case it is already reported — a 👍 on an existing issue
is useful too.

## 3. Contribute code (pull requests)

If you are a developer, we welcome pull requests.

1. **Fork** the repo and create a branch off `main`.
2. **Build and test** before you push:
   ```powershell
   dotnet build BlocksBeyondTheStars.sln   # build everything
   dotnet test                             # run all xUnit tests (keep them green)
   ```
   The playable Windows client is built with `scripts/build-client.ps1` (requires the Unity
   Editor). See [docs/developer/DEVELOPER.md](docs/developer/DEVELOPER.md). If you want to
   *write* tests (a great first contribution), start with
   [docs/developer/SERVER_TESTING.md](docs/developer/SERVER_TESTING.md).
3. **Open a pull request** against `main` with a short description of the change and why.
   Small, focused PRs are easier to review and merge.

Once the PR is open, [CI](.github/workflows/ci.yml) automatically builds and runs the headless
.NET test suites on every push — and **treats warnings as errors**, so keep the build warning-clean.
PR checks run a **fast tier** that skips the ~31 tests marked `[Trait("Category", "Slow")]`; the **full
suite** still runs on every push to `main` and again before each release, so nothing slips through.
The Unity tiers aren't in CI; run `./scripts/run-tests.ps1 -Suites All` locally before a
client-affecting change.

### A few rules that keep the project consistent

These mirror [AGENTS.md](AGENTS.md) (the deeper contributor guide — please skim it):

- **Server is authoritative.** The Unity client is presentation and input; the .NET server
  is the truth of the game world. Never make the client decide resources, inventory,
  crafting, ship state, oxygen, damage, blueprints or travel.
- **Text language.** Documentation and code comments are **English**. In-game player-facing
  text is **localized** via localization keys in `data/locales/*.json` — never hardcode
  player-facing strings. New keys go into `en.json` **and** `de.json`: that pair is mandatory
  and must stay complete. Every other language sits on top of it and falls back to English per
  missing key — French and Spanish are complete too, Italian is in progress
  (see [Translating the game](#translating-the-game)).
- **Data-driven content.** Blocks, items, recipes, ship modules, tech nodes and planets live
  in `data/*.json`; adding content should not require touching game logic.
- **Keep `Shared`/`WorldGeneration` `netstandard2.1`-clean** so the Unity client can consume them.
- **Update [TODO.md](TODO.md)** — it is the single Done/Open status doc — when your change
  affects it, and update any doc in `docs/` that your change makes stale.

## Translating the game

You don't need to be a programmer to add a language, and you don't need to finish it. Every language
falls back to English **per missing key**, so a file with one key group in it works in the game — which is
exactly how Italian ([`data/locales/it.json`](data/locales/it.json)) is being built: one group per PR.

The full end-to-end guide — every text surface (locale tables, story packs, wiki, What's New, AI
backend, portal), the style rules, in-game testing, and the maintainer wiring for a brand-new
language — lives in [docs/developer/TRANSLATION_GUIDE.md](docs/developer/TRANSLATION_GUIDE.md).

**How to help with an existing language**

1. Pick a key group and see what's missing:
   ```bash
   python3 tools/locale_report.py it                    # coverage per key group
   python3 tools/locale_report.py it --missing item      # the untranslated keys + their English text
   ```
2. Paste those keys into the locale file, translate the **values only**, and keep them in the same order
   as `en.json` so future diffs stay readable.
3. Open a PR with that one group. Small is genuinely better here.

**What CI checks** (`CommunityLocaleTests`) — it will never ask you to be complete, only correct:

- keys must exist in `en.json` — a typo'd key is text nothing will ever show
- placeholders like `{0}` or `{item}` must survive; word order may move and the placeholder moves with it
- no empty values: an empty string *shadows* the English fallback and renders as blank UI, so leave the
  key out instead

Sanity-check the whole thing locally with `python3 tools/locale_report.py --check` (stdlib only, no setup).

**Adding a brand-new language** needs one small code change to go with your file — `GameLocale` in
[`GameLocale.cs`](src/BlocksBeyondTheStars.Shared/Localization/GameLocale.cs). Open an issue first and
we'll wire it up; the loader picks the file up from there. A language appears in the in-game settings menu
once enough of the interface is covered — until then it loads for anyone who selects it by hand.

## Licensing & the Contributor License Agreement (CLA)

**Why this exists — the honest version.** Blocks Beyond The Stars is a father-and-son
project. Our dream is to one day see it on **Steam and consoles (Xbox)**. Closed platforms
like Xbox **cannot ship a pure AGPL build**, so we need the right to also license the code
commercially for those specific platforms. The CLA is what lets Justus's console dream come
true — while the public version stays free and open forever.

**Our promise to the community.** We guarantee the GitHub version always stays **free,
AGPL-licensed and current**. The proprietary license is used **only** for the closed console
networks (Xbox / console certification), **never** to take the open version away.

**What that means for your contribution.** The project is licensed under the
**[GNU AGPL-3.0-or-later](LICENSE)**, and your contributions are accepted under that license
too (inbound = outbound). In addition, by contributing you agree to our
**[Contributor License Agreement](docs/legal/CLA.md)**, which grants us the right to also
relicense the code commercially for the closed platforms described above. This asymmetry is
deliberate and stated openly — it is the only way a copyleft open-source game can also reach
consoles.

**How signing works.** It's one click. The first time you open a pull request, the
**CLAassistant** bot comments with a link; you sign in with your GitHub account, accept, and
your PR is unblocked. No paperwork, no email.

## Hosting & publishing forks

Because the game is open source under the **[AGPL-3.0](LICENSE)**, you are very welcome to
run, fork, and host your own builds — including public web deployments (Glitch, itch.io, your
own site, and so on). That's genuinely encouraged: more places to play means more people
discovering the project. 🎉

We only ask for two things so players can tell community builds apart from official releases:

1. **Mark it as an unofficial fork / community build.** If you host or publish the game,
   please make it clearly recognizable as a fork and not an official release. (AGPL-3.0 §7(c)
   expressly allows requiring that modified or republished versions be marked as different from
   the original — so this is in the spirit of the license, not an extra restriction on the code.)
2. **Official branding needs maintainer sign-off.** Using the project **name, logo, store
   listings, or official artwork** to present something *as* the official version requires prior
   approval from the maintainers. This is a **trademark** matter, which is separate from the
   code license — the AGPL covers the code and grants no rights in the project's name or branding.

None of this limits your AGPL rights to the code itself. And if you'd like your build to become
an official channel, please open an issue — we would much rather collaborate than say no. 🙂

## Questions

Not sure where to start, or whether an idea fits? Open an issue and ask — we are happy to help.
