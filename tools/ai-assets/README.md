# AI asset tools (text → sound / image)

Two small [uv](https://docs.astral.sh/uv/) Python tools that turn a **text description** into a
game asset with AI:

- **`gen_sound.py`** — text → **sound effect** via the **ElevenLabs** text-to-sound-effects API.
- **`gen_image.py`** — text → **image / texture** via the **OpenAI** image API (`gpt-image-1-mini`
  is the cheapest model; the smallest API output is 1024×1024, which we downscale locally).

These produce real assets to replace the game's procedural placeholders (block textures, UI icons,
SFX). Every bundled asset must be recorded in the repo's `NOTICES.md`.

## Cost discipline (important)

- Each tool generates **one file per run** — never a batch. This keeps every paid request visible.
- **Approval before spending:** when Claude wants to generate assets, it will propose the exact
  command(s) and the **estimated cost per file** and wait for your go-ahead, so you can gauge the
  spend before any API call is made. Run them yourself, or approve Claude running them one at a time.
- Keys live only in a local, git-ignored `.env` — they are never committed.

## Setup

```powershell
cd tools/ai-assets
uv sync                      # creates .venv and installs deps from pyproject.toml
Copy-Item .env.example .env  # then paste your keys into .env
```

`.env`:

```
OPENAI_API_KEY=sk-...
ELEVENLABS_API_KEY=...
```

## Usage

Cheap small **texture** (32px pixel-art, downscaled from a 1024 low-quality render ≈ $0.005):

```powershell
uv run gen_image.py --prompt "seamless 32px stone block texture, top-down, grey, pixel art" `
    --out out/stone.png --downscale 32 --nearest
```

A UI **icon** (256px, smooth):

```powershell
uv run gen_image.py --prompt "minimal flat icon of an oxygen tank, single colour, transparent bg" `
    --out out/icon_oxygen.png --downscale 256
```

A short **sound effect**:

```powershell
uv run gen_sound.py --prompt "short mechanical mining drill hitting rock" --out out/mine.mp3 --duration 1.0
```

### `gen_image.py` options

| flag | default | notes |
|---|---|---|
| `--prompt` | (required) | text description |
| `--out` | (required) | output PNG path |
| `--model` | `gpt-image-1-mini` | cheapest model; `gpt-image-2` is the flagship |
| `--quality` | `low` | `low` / `medium` / `high` (cost rises ~5–25×) |
| `--size` | `1024x1024` | `1024x1024` / `1024x1536` / `1536x1024` (non-square ≈ 1.5× cost) |
| `--downscale` | `0` | resize result to N×N px (e.g. 32, 64, 256); 0 keeps full size |
| `--nearest` | off | crisp nearest-neighbour downscale (pixel-art); default is smooth |

Approximate cost is printed before the call (verify at <https://openai.com/api/pricing>).
`gpt-image-1-mini` / `low` / `1024×1024` ≈ **$0.005** per image.

### `gen_sound.py` options

| flag | default | notes |
|---|---|---|
| `--prompt` | (required) | text description of the sound |
| `--out` | (required) | output `.mp3` path |
| `--duration` | `0` | seconds (0.5–30); 0 lets the model choose — keep SFX short |
| `--influence` | `0.3` | `prompt_influence` 0..1 |
| `--loop` | off | seamless loop (`eleven_text_to_sound_v2`) |
| `--model` | `eleven_text_to_sound_v2` | |
| `--format` | `mp3_44100_128` | `output_format` |

ElevenLabs bills sound effects in **credits** (≈ per second of audio) — check your plan.

## Notes

- Outputs default under `out/` (git-ignored). Move chosen assets into the game (`data/…` or the
  Unity client) deliberately, and log each in `NOTICES.md` with its source + licence/terms.
- The tools never run on import and make **no** network calls unless you invoke them with a prompt.
