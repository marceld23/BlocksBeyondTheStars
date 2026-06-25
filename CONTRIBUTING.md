# Contributing to Blocks Beyond the Stars

Thanks for your interest! **Blocks Beyond the Stars** is a family project that we are
opening up to the community — and we would love your help. There are three ways to join
in, from "no setup at all" to "write some code".

Everyone is welcome here. Please be kind — see our short
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
   Editor). See [docs/developer/DEVELOPER.md](docs/developer/DEVELOPER.md).
3. **Open a pull request** against `main` with a short description of the change and why.
   Small, focused PRs are easier to review and merge.

Once the PR is open, [CI](.github/workflows/ci.yml) automatically builds and runs the headless
.NET test suites on every push — and **treats warnings as errors**, so keep the build warning-clean.
The Unity tiers aren't in CI; run `./scripts/run-tests.ps1 -Suites All` locally before a
client-affecting change.

### A few rules that keep the project consistent

These mirror [AGENTS.md](AGENTS.md) (the deeper contributor guide — please skim it):

- **Server is authoritative.** The Unity client is presentation and input; the .NET server
  is the truth of the game world. Never make the client decide resources, inventory,
  crafting, ship state, oxygen, damage, blueprints or travel.
- **Text language.** Documentation and code comments are **English**. In-game player-facing
  text is **bilingual (German + English)** via localization keys in `data/locales/{en,de}.json`
  — never hardcode player-facing strings.
- **Data-driven content.** Blocks, items, recipes, ship modules, tech nodes and planets live
  in `data/*.json`; adding content should not require touching game logic.
- **Keep `Shared`/`WorldGeneration` `netstandard2.1`-clean** so the Unity client can consume them.
- **Update [TODO.md](TODO.md)** — it is the single Done/Open status doc — when your change
  affects it, and update any doc in `docs/` that your change makes stale.

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

## Questions

Not sure where to start, or whether an idea fits? Open an issue and ask — we are happy to help.
