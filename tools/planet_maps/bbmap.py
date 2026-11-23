"""The .bbmap planet map file (format version 1) — the writer side of PlanetMapCodec.cs.

Little-endian: magic "BBMP", u16 format version, u16 layer flags (3 = elevation + biome), i32 width, i32 height,
u64 FNV-1a 64 of the uncompressed payload, then the payload as raw deflate (no zlib header): width*height int16
elevations in metres, row-major from the SOUTH edge, followed by width*height biome class bytes (0..7).
"""

from __future__ import annotations

import struct
import zlib

import numpy as np

FORMAT_VERSION = 1
LAYERS = 3
MAGIC = b"BBMP"

# The eight biome classes (Shared/World/PlanetMap.cs, PlanetMapBiome).
ICE, TUNDRA, BOREAL, TEMPERATE, STEPPE, DESERT, RAINFOREST, HIGH_MOUNTAINS = range(8)
CLASS_NAMES = ["ice", "tundra", "boreal forest", "temperate", "steppe/savanna", "desert", "rain forest", "high mountains"]

_FNV_OFFSET = 14695981039346656037
_FNV_PRIME = 1099511628211
_MASK = (1 << 64) - 1


def fnv1a64(data: bytes) -> int:
    """FNV-1a 64 over bytes — the content hash the game checks."""
    h = _FNV_OFFSET
    for b in data:
        h = ((h ^ b) * _FNV_PRIME) & _MASK
    return h


def encode(elevation: np.ndarray, biome: np.ndarray) -> bytes:
    """The bytes of a .bbmap file. Both arrays are (height, width) with row 0 the south edge."""
    if elevation.shape != biome.shape or elevation.ndim != 2:
        raise ValueError("elevation and biome must be 2-D arrays of one shape")
    height, width = elevation.shape
    if int(biome.max()) >= 8 or int(biome.min()) < 0:
        raise ValueError("biome classes must be 0..7")
    elev = np.clip(np.rint(elevation), -32768, 32767).astype("<i2")
    payload = elev.tobytes() + biome.astype(np.uint8).tobytes()
    header = MAGIC + struct.pack("<HHiiQ", FORMAT_VERSION, LAYERS, width, height, fnv1a64(payload))
    deflate = zlib.compressobj(9, zlib.DEFLATED, -15)
    return header + deflate.compress(payload) + deflate.flush()


def decode(data: bytes) -> tuple[np.ndarray, np.ndarray]:
    """Reads a .bbmap file back (elevation int16, biome uint8), checking the hash — for the tool's self-check."""
    if data[:4] != MAGIC:
        raise ValueError("not a .bbmap file")
    version, layers, width, height, digest = struct.unpack("<HHiiQ", data[4:24])
    if version != FORMAT_VERSION or layers != LAYERS:
        raise ValueError(f"unsupported .bbmap version {version} / layers {layers}")
    payload = zlib.decompress(data[24:], -15)
    if fnv1a64(payload) != digest:
        raise ValueError("content hash mismatch")
    cells = width * height
    elevation = np.frombuffer(payload[: cells * 2], dtype="<i2").reshape(height, width)
    biome = np.frombuffer(payload[cells * 2 :], dtype=np.uint8).reshape(height, width)
    return elevation, biome
