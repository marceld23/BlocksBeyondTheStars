# data-online — content the client fetches on demand

Everything under [`data/`](../data/) is copied into the client's StreamingAssets at build time
(`scripts/sync-client-libs.{ps1,sh}`): it ships in every installer, and the browser build prefetches
all of it before the first frame. That is right for the game's own content and wrong for text that
only a part of the players ever reads.

Files in **this** folder are tracked in the repository and **never bundled**. The client fetches them
raw from the `main` branch when it needs them, so they can also be newer than the installed build.
Whatever lives here must have a fallback inside the game, because a player can be offline.

## `whatsnew/<code>.json` — the release notes in the other languages

The in-game "What's new?" feed, [`data/whatsnew.json`](../data/whatsnew.json), carries German and
English. Every other game language has one file here:

```json
{
  "language": "fr",
  "entries": [
    { "version": "2026.10.3", "title": "…", "body": "…" }
  ]
}
```

- `version` matches an entry of `data/whatsnew.json`. The client lays `title` and `body` over that
  entry when the player's language is `<code>` (`UiWhatsNew.cs`).
- A file may cover any subset of the releases. A release it does not have, a language without a file
  and a player without a connection all read English — nothing has to be complete.
- `title` is the part after `Version X.Y.Z – ` (the dialog prints the version itself); `body` uses the
  same small markdown subset as the feed (`**bold**`, `*italic*`, `- ` bullets, `###` headings).

**Do not edit these files by hand.** They are the devblog release posts in the other website
languages: the maintainer's blog-sync tooling translates every new DE+EN post, publishes it on the
blog and writes these files (`sync.py finish --whatsnew-repo …`, see the release section of
[AGENTS.md](../AGENTS.md)). `tools/devblog/export_whatsnew.py --languages-only` can rebuild them from a
translation store; it merges into what is committed, so nothing is lost when a machine has only part of
the translations. `WhatsNewContentTests` guards their shape.
