"""The landmark toolkit: scripts that build the giant landmark templates (data/landmarks/<key>.json) and look at them.

Modules: `geom` (the `Design` class and its primitives), `solids` (pure cell geometry), `shapecode` (shape descriptors,
mirrored from C#), `blockfont` (the 5 x 7 sign font), `templatefile` (reading templates, the stats line), `preview`
(PNG renderer), `selftest`. Landmark scripts live next to them as `<key>.py` with a `build()`; see README.md.

Nothing is imported here on purpose: the scripts import the modules as top-level names (`from geom import Design`), and
importing them through the package as well would load a second copy.
"""
