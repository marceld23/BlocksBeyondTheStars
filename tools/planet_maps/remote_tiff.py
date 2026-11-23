"""Reads a subset of rows from a large global GeoTIFF over HTTP range requests, so a map needs only a few tens of
megabytes of a several-hundred-megabyte mosaic (#3979).

Supported layout (what the USGS Astrogeology global mosaics use): classic little-endian TIFF, one band, uncompressed
signed 16-bit samples, one row per strip, the strips stored one after the other, simple-cylindrical over the whole body
(row 0 = 90 N). Anything else raises, so a changed source file is noticed instead of misread.
"""

from __future__ import annotations

import http.client
import re
import struct
import time
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass

import numpy as np

USER_AGENT = "planet-maps-tool/1.0"
HEADER_BYTES = 1 << 18  # the IFD, the strip tables and the GeoTIFF tags sit in front of the first strip

_TYPE_SIZES = {1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 7: 1, 11: 4, 12: 8, 16: 8}
_TYPE_FORMATS = {1: "B", 2: "c", 3: "H", 4: "I", 5: "II", 7: "B", 11: "f", 12: "d", 16: "Q"}


@dataclass(frozen=True)
class TiffLayout:
    url: str  # after redirects, so every range request goes straight to the file
    width: int
    height: int
    first_row: int  # file offset of row 0
    scale: float  # metres per raw unit (the GDAL SCALE metadata, 1 when absent)
    nodata: int | None
    west_longitude: float  # the longitude of column 0's west edge, degrees


def _fetch(url: str, start: int, end: int, attempts: int = 5) -> tuple[bytes, str]:
    """Bytes start..end (inclusive) of url and the final URL; retried with a growing pause, since a long run of small
    requests now and then meets a dropped connection."""
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Range": f"bytes={start}-{end}"})
    for attempt in range(attempts):
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                if response.status != 206:
                    raise ValueError(f"{url}: the server ignored the range request (HTTP {response.status})")
                return response.read(), response.geturl()
        except (OSError, http.client.HTTPException):
            if attempt == attempts - 1:
                raise
            time.sleep(2.0 * (attempt + 1))
    raise AssertionError("unreachable")


def _tags(data: bytes) -> dict[int, tuple]:
    """The first IFD's tags as tuples of values (ASCII tags as one str)."""
    if data[:4] != b"II*\x00":
        raise ValueError("not a classic little-endian TIFF")
    ifd = struct.unpack_from("<I", data, 4)[0]
    count = struct.unpack_from("<H", data, ifd)[0]
    tags: dict[int, tuple] = {}
    for i in range(count):
        tag, typ, n, value = struct.unpack_from("<HHI4s", data, ifd + 2 + 12 * i)
        size = _TYPE_SIZES.get(typ, 1) * n
        raw = value if size <= 4 else data[struct.unpack("<I", value)[0] :][:size]
        if len(raw) < size:
            raise ValueError(f"TIFF tag {tag} lies beyond the first {HEADER_BYTES} bytes")
        if typ == 2:
            tags[tag] = (raw[:size].rstrip(b"\x00").decode("ascii", "replace"),)
        elif typ in _TYPE_FORMATS:
            tags[tag] = struct.unpack("<" + _TYPE_FORMATS[typ] * n, raw[:size])
    return tags


def read_layout(url: str) -> TiffLayout:
    """Reads and checks the header of the GeoTIFF at url."""
    data, final_url = _fetch(url, 0, HEADER_BYTES - 1)
    tags = _tags(data)
    width, height = tags[256][0], tags[257][0]
    checks = {"16-bit samples": tags[258] == (16,), "uncompressed": tags[259] == (1,), "one band": tags[277] == (1,),
              "signed": tags.get(339) == (2,), "one row per strip": tags[278] == (1,)}
    offsets = np.asarray(tags[273], dtype=np.int64)
    checks["rows stored in order"] = len(offsets) == height and bool(np.all(np.diff(offsets) == 2 * width))
    if failed := [name for name, ok in checks.items() if not ok]:
        raise ValueError(f"{url}: unsupported TIFF layout ({', '.join(failed)})")
    scale_match = re.search(r'name="SCALE"[^>]*>([-0-9.eE]+)<', tags.get(42112, ("",))[0])
    nodata = tags.get(42113, ("",))[0].strip()
    pixel_x, tie_x = tags[33550][0], tags[33922][3]
    return TiffLayout(
        url=final_url,
        width=width,
        height=height,
        first_row=int(offsets[0]),
        scale=float(scale_match.group(1)) if scale_match else 1.0,
        nodata=int(float(nodata)) if nodata else None,
        west_longitude=360.0 * tie_x / (width * pixel_x),
    )


def read_rows(layout: TiffLayout, rows: list[int], workers: int = 6) -> np.ndarray:
    """The given rows (len(rows) x width, raw int16), fetched in parallel, in the order asked."""
    row_bytes = 2 * layout.width

    def one(row: int) -> np.ndarray:
        start = layout.first_row + row * row_bytes
        data, _ = _fetch(layout.url, start, start + row_bytes - 1)
        if len(data) != row_bytes:
            raise OSError(f"row {row}: got {len(data)} of {row_bytes} bytes")
        return np.frombuffer(data, dtype="<i2")

    with ThreadPoolExecutor(max_workers=workers) as pool:
        return np.stack(list(pool.map(one, rows)))
