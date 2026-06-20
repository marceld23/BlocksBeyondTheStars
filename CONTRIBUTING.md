# Contributing to Blocks Beyond the Stars

Thanks for your interest! **Blocks Beyond the Stars** is a family project that we are
opening up to the community — and we would love your help. There are three ways to join
in, from "no setup at all" to "write some code".

> Repository: <https://github.com/marceld23/BlocksBeyondTheStars>

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

## Questions

Not sure where to start, or whether an idea fits? Open an issue and ask — we are happy to help.

By contributing, you agree that your contributions are licensed under the project's
[MIT License](LICENSE).
