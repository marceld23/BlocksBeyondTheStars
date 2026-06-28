# ADR 0011 — CodeQL code scanning strategy

- **Status:** Accepted
- **Date:** 2026-06-21
- **Context source:** [`.github/workflows/codeql.yml`](../../../.github/workflows/codeql.yml)

## Context

The repo runs GitHub CodeQL "advanced setup" as a committed workflow (`codeql.yml`) across C#,
Python and GitHub Actions. (A `javascript-typescript` language was analysed until 2026-06-28, when the
last JS — the embedded-browser wiki/minigames — was removed; see the update note below.) Three things needed deciding: which query suite to
run, how to analyse C# given that the codebase is split between headless .NET projects and a large
Unity client, and how to treat that Unity client in the C# scan. An initial broad configuration
produced ~900 mostly-stylistic alerts and a "Low C# analysis quality" diagnostic (only ~77 % of
calls resolved, below CodeQL's 85 % threshold), which together obscured real findings.

## Decision

1. **Run the `security-extended` query suite, not `security-and-quality`.** The quality queries are
   redundant here — code quality is already enforced by the Roslyn analyzers (`Directory.Build.props`:
   `EnableNETAnalyzers` + Meziantou + VS.Threading, with `ci.yml` building `-warnaserror` at 0
   warnings). The quality suite added ~689 note-level alerts (and false-positive errors such as
   `cs/invalid-string-formatting` on localized `string.Format`) with no security value.
2. **Analyse C# in `manual` build mode, compiling `BlocksBeyondTheStars.CI.slnf`.** Compiling the
   solution lets CodeQL trace the compiler and fully resolve types and call targets on the
   security-relevant code (`GameServer`, `Networking`, `Persistence`, `Api`, `Client.Core`). The
   earlier buildless (`build-mode: none`) scan could not, because the Unity client references
   `UnityEngine` assemblies that are absent without the editor.
3. **The Unity client C# (`client/Assets/`) is out of scope for C# scanning, by construction.**
   Manual build mode extracts only what the compiler compiles, and those ~43k lines compile only in
   the Unity editor — so they drop out without an explicit `paths-ignore`. The trade is deliberate:
   that code is `MonoBehaviour` rendering/UI with little security surface. Its one untrusted-input area
   (the in-game wiki + minigames parsing) lives in `Client.Core`, which **is** part of `CI.slnf` and so
   is fully analysed by the C# scan. `CI.slnf` is the same filter `ci.yml` uses (every .NET project
   except the Windows-only WinForms launcher), so it builds on the Linux runner.
4. **All workflow Actions are version-pinned for supply-chain safety and the Node 20 runtime
   deprecation.** First-party `actions/*` and `github/codeql-action` use their Node 24 major tags;
   third-party actions (docker/*, game-ci/unity-builder, softprops/action-gh-release,
   astral-sh/ruff-action) are pinned to a commit **SHA** with the version in a trailing comment.

## Consequences

- Security findings are low-noise and high-confidence; the C# CodeQL job costs ~2–3 min more because
  it now compiles `CI.slnf`.
- New .NET projects are scanned automatically once they are part of `CI.slnf`; Unity-only client
  scripts are **not** CodeQL-scanned (accepted — low security surface; the untrusted-input parsing now
  lives in the analysed `Client.Core`).

## Update (2026-06-28)

The HTML/JS embedded-browser wiki + minigames had already been rewritten in native C# (PR #58); on
2026-06-28 the now-dead `web/` source was deleted, leaving **no JS/TS in the repo**. The
`javascript-typescript` language was therefore removed from the `codeql.yml` matrix (a zero-source
language errors the analysis). It was never a *required* status check, so branch protection was
unaffected. The untrusted-input rationale in Decision §3 still holds — that parsing is now C# in
`Client.Core` and covered by the C# scan.
- The C# build step uses no `-warnaserror`: this gate is for security analysis, and `ci.yml` already
  enforces warnings-as-errors separately.
- `codeql.yml` is the single source of truth for the scanning configuration; this ADR records *why*.
