# ADR 0012 — Date-based (CalVer) versioning: `YYYY.MM.N`

- **Status:** Accepted
- **Date:** 2026-07-27
- **Context source:** [#550](https://github.com/marceld23/BlocksBeyondTheStars/issues/550) (the full
  impact analysis lives in the local, git-ignored `analysis/versioning-scheme-calver.md`)

## Context

Releases were versioned SemVer-style (`0.9.1`), with the git tag as the single source of truth
(ADR 0010: tag → GameCI `bundleVersion` → launcher `-p:Version` → Velopack `--packVersion`).
The project ships frequent, small releases (six in July 2026 alone); the `0.minor.patch`
distinction carried little information, while "when is this build from?" is the question players,
bug reports and the website actually ask. The wish was a fully dated scheme like `2026.07.27.01`.

Two hard constraints rule the wished-for format out:

1. **Velopack accepts only strict 3-part SemVer2** — no 4-part versions, no leading zeros
   (FAQ-explicit). This governs `vpk pack`, the update feed (`releases.win.json`) and the
   client's `GithubSource` update check (#543), i.e. the entire auto-update path.
2. **Windows Installer `ProductVersion`** allows at most `255.255.65535` — a 4-digit year
   overflows the MSI major field regardless of the rest of the format.

Additionally, every parser in the chain (Velopack, `System.Version`, MSBuild) normalizes leading
zeros away, so any zero-padded scheme guarantees a mismatch between displayed and parsed versions.

## Decision

1. **Versions are `YYYY.MM.N`** — year, month (no leading zero), release counter within that
   month, restarting at `.1` each month. Example: `2026.7.1`, `2026.7.2`, then `2026.8.1`.
   The counter counts *every* release of the month — including, in the July 2026 transition
   month, the 18 SemVer releases that already shipped (v0.7.0 on 2026-07-05 through v0.9.1 on
   2026-07-27). **The first CalVer release in July 2026 is therefore `v2026.7.19`.** From the
   following month on this special case disappears: every release is a CalVer release and the
   counter simply restarts at `.1` (August: `v2026.8.1`).
   Tags keep the `v` prefix (`v2026.7.2`) — the release trigger glob (`v*`) and the CHANGELOG
   compare links depend on it.
2. **The scheme must remain valid 3-part SemVer2 forever**: never a fourth part, never leading
   zeros, `-suffix` reserved for dev/pre-release isolation (`publish-client-installer.ps1` routes
   any `-`-version to the separate `BlocksBeyondTheStarsDev` packId).
3. **MSI only: the year is mapped down** by `publish-client-installer.ps1` (`2026.7.1` → `26.7.1`)
   to fit the 255 cap. The mapped form is visible solely in the MSI's Apps & Features entry;
   Setup.exe, the portable zip, the Velopack feed and the in-game UI all show the full version.
4. **Everything else stays unchanged by design**: the release workflow's tag validation regex,
   the What's-new tooling (`export_whatsnew.py`, `WhatsNewContentTests`), GameCI versioning and
   the Docker tags all accept any 3-part numeric version, and ordering across the switch is
   monotonic (`2026.7.1 > 0.9.1`), so update detection works immediately.

## Consequences

- **The switch is one-way.** Once a `2026.*` release ships, every smaller version is a downgrade
  for Velopack and will never be offered as an update — there is no path back to `0.x`/`1.x`,
  and no "1.0" milestone version exists in this scheme. Accepted deliberately.
- Releases self-date: the version answers "how old is this build?" without consulting the
  changelog. The devblog and CHANGELOG remain the record of *what* changed.
- The CHANGELOG statement "follows Semantic Versioning" is replaced by this scheme (headings
  like `## [2026.7.2] — 2026-07-28` are slightly redundant with their date — harmless).
- **Installing the new MSI over an old `0.x` MSI keeps working** (verified against vpk 1.2.0's
  MSI source): the `UpgradeCode` is a deterministic hash of the packId (unchanged), the mapped
  ProductVersion stays monotonic (`26.7.1 > 0.9.1`), file replacement is governed by the
  binaries' FileVersion (`2026.x > 0.9.x`), and the Apps & Features entry is Velopack's own
  registry key keyed by the packId — the newer install simply overwrites it, so no duplicate
  entry appears. Note vpk 1.2.0's MSI has no WiX `MajorUpgrade` table at all, so MSI-over-MSI
  behaves identically to how it already did between `0.x` versions — the scheme switch changes
  nothing here. Still: verify once by installing the first CalVer MSI over a `0.9.1` MSI install.
- The public website (Wix) polls the GitHub release `tag_name` verbatim and does not parse it —
  verified safe. Anything new consuming versions must parse them as SemVer, never split on a
  fixed part count of the *string* with padding assumptions.
- `tools/devblog/set_dates.py` (legacy, unused since devblog entry 40) still assumes a `0.x`
  prefix and would need widening if ever revived.
