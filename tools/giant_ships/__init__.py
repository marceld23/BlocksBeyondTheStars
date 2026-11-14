"""The GIANT SHIP scripts (#3617): one `<key>.py` per ship with a `build()` that returns a `ships.ShipDesign`; the file
`data/giant_ships/<key>.json` takes its name from the key. tools/gen_giant_ships.py runs them (see README.md here).
Scripts whose name starts with `_` (the toolkit demo `_demo_hull`) are run only when named with `--only`.

Importing this package puts tools/landmarks on sys.path, so a script imports the toolkit as top-level modules
(`from ships import ShipDesign, Mat, ship_section`) — the same single copy `geom`, `solids` and `preview` use.
"""
import sys
from pathlib import Path

_LANDMARKS = str(Path(__file__).resolve().parents[1] / "landmarks")
if _LANDMARKS not in sys.path:
    sys.path.insert(0, _LANDMARKS)
