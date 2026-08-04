# Server testing — writing your first test for the .NET suite

> **Status: contributor guide.** Written 2026-08-04 after contributor feedback on issue #571
> ("I don't have enough context to write meaningful tests"). [CLIENT_TESTING.md](CLIENT_TESTING.md)
> covers how the Unity client is tested; **this doc covers the server/shared suite**
> (`tests/BlocksBeyondTheStars.Tests`) — where things are, which existing tests to copy from,
> and what "meaningful" means for the kinds of code you'll be testing.

## The one-minute version

```powershell
dotnet test -c Release --filter Category!=Slow   # fast daily loop (~3 min)
dotnet test                                      # everything, incl. ~31 Slow soak tests
./scripts/test-coverage.ps1 -Open                # full coverage report (slow, ~30 min)
```

Add a file to `tests/BlocksBeyondTheStars.Tests/`, name it `<Thing>Tests.cs`, use plain xUnit
`[Fact]`/`[Theory]`. No registration needed anywhere — CI shards test classes across runners
automatically ([`scripts/partition-tests.py`](../../scripts/partition-tests.py)), so a new class
just gets picked up.

## Three exemplars to copy from

Most of the 220+ test files are integration-style tests around a full `GameServer` — **don't
start there**. These three show the ladder, simplest first:

1. **Pure logic, no fixtures** — [`NameGeneratorTests.cs`](../../tests/BlocksBeyondTheStars.Tests/NameGeneratorTests.cs).
   Asserts determinism (same seed → same output) and *shape* properties ("two capitalised
   words") over a seed range. This is the pattern for `Vector3i`, `ChunkCoord`, noise,
   `Localizer`, `FrequencyExtensions`, `ServerPresets`, `MissionValidator`.
2. **Needs game content** — [`BlockShapeTests.cs`](../../tests/BlocksBeyondTheStars.Tests/BlockShapeTests.cs).
   Loads the real data-driven content once in the constructor and uses a Guid-named temp dir
   with `IDisposable` cleanup.
3. **Full server** — [`GameServerIntegrationTests.cs`](../../tests/BlocksBeyondTheStars.Tests/GameServerIntegrationTests.cs).
   Constructs a real `GameServer` with a loopback transport and a SQLite repo. Only needed
   when the behaviour under test *is* the server loop (ticks, intents, persistence).

## Fixtures & helpers you would otherwise not find

- **`TestPaths.DataDir()`** ([TestPaths.cs](../../tests/BlocksBeyondTheStars.Tests/TestPaths.cs))
  walks up from the test output directory to the repo's `data/` folder — so tests run against
  the *real* shipped content from any build output location. The standard opener is:

  ```csharp
  private static GameContent Load() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());
  ```

  `GameContent` is the loaded form of `data/*.json` (blocks, items, recipes, planets …); most
  validators and generators take it as a parameter, and loading the real thing is both easier
  and more honest than mocking it.
- **`TestLocales.Load("en" | "de")`** ([TestLocales.cs](../../tests/BlocksBeyondTheStars.Tests/TestLocales.cs))
  reads the real locale tables — use it to assert a feature's keys exist in **both** languages
  (a missing key renders as literal `[some.key]` in game instead of failing loudly).
- **Temp state**: `Path.Combine(Path.GetTempPath(), "bbts_<topic>_" + Guid.NewGuid().ToString("N"))`
  plus `IDisposable` cleanup — never share paths between tests; the suite runs 4-way parallel.

## What "meaningful" means here (the tautology trap)

A lot of the untested surface is small pure functions whose *constants are the implementation* —
e.g. `FrequencyExtensions.Probability(Rare) == 0.15`. A test that just restates the switch arm
mirrors the code and verifies nothing. **Assert the invariants instead** — the properties that
must survive any future retuning of the numbers:

| Target kind | Meaningful assertions |
|---|---|
| Factor tables (`FrequencyExtensions`) | `Off` is exactly `0` where "off must mean off"; values are **monotone** in enum order; the enum's default level maps to `1.0` for factors documented as "existing worlds unchanged" |
| Primitive types (`Vector3i`, `ChunkCoord`) | equality ↔ hash-code consistency, roundtrips (pack/unpack, parse/format), arithmetic identities |
| Lookup tables (`ServerPresets`, locale keys) | case-insensitivity, unknown name → `null` (not throw), every advertised `Names` entry resolves |
| Validators (`MissionValidator`) | each documented error case produces a problem; a known-good definition produces **zero** problems |
| `Localizer` | active-locale hit, English fallback, unknown key → `[key]` wrap, empty key → empty string |
| Noise / RNG / generators | same seed → same output; output range; seam continuity at the world wrap (see [WORLD_WRAP.md](WORLD_WRAP.md)); **never compare trig-derived floats or golden-hash them** — `sin`/`cos` differ between Windows and Linux libm and the hash will pass locally and fail in CI. Hash integers, or assert structural properties |

If you can't tell which invariants are intended: the XML doc comment on the type is the public
authority on behaviour (see the note on internal specs below), and asking on the issue is
always fine — pointing at an undocumented spot is itself a useful contribution.

## Where behaviour is specified

- The **XML doc comments** on the types themselves — these are kept as the public source of
  truth for intended behaviour.
- The [docs/developer/ index](README.md) has a deep-dive per area (worldgen →
  [WORLD_GENERATION.md](WORLD_GENERATION.md), topology → [WORLD_WRAP.md](WORLD_WRAP.md), …).
- Some comments cite `anf_*.md` files ("technical requirements"). Those are the project's
  **internal German design specs from before open-sourcing; they are not in the public repo.**
  Treat the English summary in the doc comment as the authoritative statement of intent — and
  if a comment leans on such a citation without summarising the rule, that's a doc bug worth
  reporting (or fixing in your PR).

## CI rules that will bite you

CI treats **warnings as errors** (Roslyn + Meziantou + VS.Threading analyzers). The recurring
first-PR traps:

- Async test methods need an `Async` suffix (**VSTHRD200**).
- Don't `await` a `TaskCompletionSource.Task` in tests (**VSTHRD003**) — use a `SemaphoreSlim`.
- Any test not marked `[Trait("Category", "Slow")]` must finish **well under 120 s** — the PR
  gate ([`scripts/check-test-durations.py`](../../scripts/check-test-durations.py)) fails on it.
  Use the `Slow` trait for soak/heavy-worldgen tests; they still run on every push to `main`.
- Don't rely on `dotnet test -v minimal` output to check for warnings — it hides them. Do a
  clean `dotnet build -warnaserror` (see AGENTS.md "Local verification after changes").
- The suite pins `maxParallelThreads: 4` ([xunit.runner.json](../../tests/BlocksBeyondTheStars.Tests/xunit.runner.json))
  — heavy worldgen tests funnel through shared caches; don't "fix" a slow local run by raising it.
