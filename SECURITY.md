# Security Policy

**Blocks Beyond the Stars** is a hobby/family project, but we still take security seriously —
the server is self-hostable, so a vulnerability could affect someone running their own world.

## Reporting a vulnerability

**Please do not report security issues in public GitHub issues.**

Instead, use GitHub's private vulnerability reporting:

1. Go to the repository's **Security** tab.
2. Click **Report a vulnerability** (under *Private vulnerability reporting*).
3. Describe the issue, how to reproduce it, and the potential impact.

This keeps the report private between you and the maintainers until a fix is available.

We'll try to acknowledge your report within a few days. Since this is a spare-time project,
please be patient with fixes — we'll keep you in the loop.

## Scope

Things we especially care about:

- The authoritative game server (`src/BlocksBeyondTheStars.GameServer`) and its network
  protocol — the client is untrusted, so the server must never trust client input.
- The admin web API/dashboard (`src/BlocksBeyondTheStars.Api`).
- The savegame/persistence layer.

The optional AI backend (`ai-backend/`) talks to third-party LLM providers; please report
anything that could leak credentials or player data.

## Supported versions

This is a single actively-developed line of work — security fixes land on `main` and in the
next release. There are no separately maintained older release branches.

Thank you for helping keep the project and its players safe. 🛡️
