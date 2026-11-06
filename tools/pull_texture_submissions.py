# Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
# SPDX-License-Identifier: AGPL-3.0-or-later
# This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
"""Pull the textures players submitted from the game out of the report inbox (#1966).

Kept as a thin wrapper: the tool is now tools/pull_submissions.py, which pulls ships, station/town modules and
avatar skins too (#3262). This is exactly `python tools/pull_submissions.py --kind texture [--fetch] ...`:

    python tools/pull_texture_submissions.py              # list what is waiting
    python tools/pull_texture_submissions.py --fetch      # unpack into analysis/texture-submissions/<id>_<key>/
    python tools/merge_texture.py analysis/texture-submissions/<id>_<key>
"""
import sys

from pull_submissions import main

if __name__ == "__main__":
    main(sys.argv[1:], default_kind="texture")
