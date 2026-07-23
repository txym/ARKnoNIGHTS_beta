"""Generate the UI-005 transparent clock icon from the existing atlas without changing its source.

Uses only Python's standard library.  The atlas is PNG RGBA and Unity's sprite rect is x=2,y=30,w=22,h=22
with a bottom-left origin; the result keeps luminance as alpha so anti-aliased clock edges remain smooth.
"""
from __future__ import annotations

import struct
import sys
import zlib
from pathlib import Path

PNG = b"\x89PNG\r\n\x1a\n"


def paeth(a: int, b: int, c: int) -> int:
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    return a if pa <= pb and pa <= pc else b if pb <= pc else c


def read_rgba(path: Path) -> tuple[int, int, bytearray]:
    raw = path.read_bytes()
    if raw[:8] != PNG:
        raise ValueError("source is not a PNG")
    pos, width, height, color_type, compressed = 8, None, None, None, bytearray()
    while pos < len(raw):
        length = struct.unpack(">I", raw[pos:pos + 4])[0]
        kind, value = raw[pos + 4:pos + 8], raw[pos + 8:pos + 8 + length]
        pos += 12 + length
        if kind == b"IHDR":
            width, height, bit_depth, color_type, compression, filtering, interlace = struct.unpack(">IIBBBBB", value)
            if (bit_depth, color_type, compression, filtering, interlace) != (8, 6, 0, 0, 0):
                raise ValueError("expected non-interlaced RGBA8 PNG")
        elif kind == b"IDAT":
            compressed.extend(value)
        elif kind == b"IEND":
            break
    if width is None or height is None:
        raise ValueError("missing IHDR")
    scanlines, stride = zlib.decompress(compressed), width * 4
    prior, pixels = bytearray(stride), bytearray()
    offset = 0
    for _ in range(height):
        mode, current = scanlines[offset], bytearray(scanlines[offset + 1:offset + 1 + stride])
        offset += stride + 1
        for index in range(stride):
            left = current[index - 4] if index >= 4 else 0
            above = prior[index]
            upper_left = prior[index - 4] if index >= 4 else 0
            if mode == 1: current[index] = (current[index] + left) & 255
            elif mode == 2: current[index] = (current[index] + above) & 255
            elif mode == 3: current[index] = (current[index] + ((left + above) // 2)) & 255
            elif mode == 4: current[index] = (current[index] + paeth(left, above, upper_left)) & 255
            elif mode != 0: raise ValueError(f"unsupported PNG filter {mode}")
        pixels.extend(current)
        prior = current
    return width, height, pixels


def chunk(kind: bytes, value: bytes) -> bytes:
    return struct.pack(">I", len(value)) + kind + value + struct.pack(">I", zlib.crc32(kind + value) & 0xFFFFFFFF)


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    source = root / "Assets/Resources/UI/Texture/SpriteAtlasTexture-CHARACTER_SORT_TYPE_ICON_0-128x128-fmt34_alpha.png"
    output = root / "Assets/Resources/UI/Texture/BattleStatusPanelClockIcon_Transparent.png"
    width, height, pixels = read_rgba(source)
    x, unity_y, crop_width, crop_height = 2, 30, 22, 22
    top = height - unity_y - crop_height
    if x + crop_width > width or top < 0:
        raise ValueError("clock crop falls outside the source atlas")
    rows = bytearray()
    for y in range(top, top + crop_height):
        rows.append(0)
        for column in range(x, x + crop_width):
            offset = (y * width + column) * 4
            red, green, blue, alpha = pixels[offset:offset + 4]
            luminance = (red * 299 + green * 587 + blue * 114) // 1000
            rows.extend((255, 255, 255, luminance * alpha // 255))
    encoded = PNG + chunk(b"IHDR", struct.pack(">IIBBBBB", crop_width, crop_height, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(rows, 9)) + chunk(b"IEND", b"")
    output.write_bytes(encoded)
    print(f"generated={output.relative_to(root)}; size={crop_width}x{crop_height}; source={source.relative_to(root)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
