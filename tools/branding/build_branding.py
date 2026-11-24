"""Build the app icon and splash screen from the SVG sources in assets/branding (plan 25).

    py tools/branding/build_branding.py [--inkscape=PATH] [--preview]

Writes into src/FactorySim.App/branding/:
  icon.ico    16, 24 (small icon) and 32, 48, 64, 128, 256 px (full icon), PNG-compressed
  splash.png  the light wordmark, 640 px wide, centred on a transparent 720 x 200 canvas
  icon.svg    byte-for-byte copy of assets/branding/factorysim-icon.svg

--preview also writes the 16, 24, 32 and 256 px renders to docs/assets-preview/.

Inkscape is only needed to rebuild these files, not to run the app. Standard library only.
"""

from __future__ import annotations

import argparse
import shutil
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SOURCE = REPO / "assets" / "branding"
TARGET = REPO / "src" / "FactorySim.App" / "branding"
PREVIEW = REPO / "docs" / "assets-preview"
DEFAULT_INKSCAPE = r"C:\Program Files\Inkscape\bin\inkscape.com"

SMALL_SIZES = (16, 24)
FULL_SIZES = (32, 48, 64, 128, 256)
SPLASH_SIZE = (720, 200)
SPLASH_LOGO_WIDTH = 640

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
# Chunks that change how a viewer maps the colours; dropped so the splash matches bg_color.
COLOUR_CHUNKS = {b"gAMA", b"iCCP", b"cHRM", b"sRGB"}


def render(inkscape: str, svg: Path, png: Path, width: int, height: int | None = None) -> None:
    args = [inkscape, str(svg), "--export-type=png", f"--export-filename={png}",
            "--export-png-color-mode=RGBA_8", f"--export-width={width}"]
    if height is not None:
        args.append(f"--export-height={height}")
    subprocess.run(args, check=True, capture_output=True)
    if not png.exists():
        raise RuntimeError(f"Inkscape did not write {png}")


def png_chunks(data: bytes):
    if not data.startswith(PNG_SIGNATURE):
        raise ValueError("not a PNG")
    pos = len(PNG_SIGNATURE)
    while pos < len(data):
        length, kind = struct.unpack(">I4s", data[pos:pos + 8])
        yield kind, data[pos:pos + 12 + length]
        pos += 12 + length


def strip_colour_chunks(data: bytes) -> bytes:
    return PNG_SIGNATURE + b"".join(raw for kind, raw in png_chunks(data) if kind not in COLOUR_CHUNKS)


def png_header(data: bytes) -> tuple[int, int, int]:
    for kind, raw in png_chunks(data):
        if kind == b"IHDR":
            width, height, _depth, colour_type = struct.unpack(">IIBB", raw[8:18])
            return width, height, colour_type
    raise ValueError("PNG without IHDR")


def build_ico(images: list[tuple[int, bytes]]) -> bytes:
    """ICO with PNG-compressed images; 256 px is stored as 0 in the directory."""
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = len(header) + 16 * len(images)
    entries, blobs = [], []
    for size, png in images:
        byte = 0 if size >= 256 else size
        entries.append(struct.pack("<BBBBHHII", byte, byte, 0, 0, 1, 32, len(png), offset))
        blobs.append(png)
        offset += len(png)
    return header + b"".join(entries) + b"".join(blobs)


def splash_wrapper(logo_svg: Path) -> str:
    """The light logo placed in the middle of a transparent 720 x 200 canvas."""
    text = logo_svg.read_text(encoding="utf-8")
    open_tag_end = text.index(">", text.index("<svg")) + 1
    inner = text[open_tag_end:text.rindex("</svg>")]
    view_w, view_h = 2008.7, 512.0
    if f'viewBox="0 0 {view_w} {int(view_h)}"' not in text:
        raise ValueError(f"unexpected viewBox in {logo_svg.name}; update splash_wrapper")
    width, height = SPLASH_SIZE
    logo_h = SPLASH_LOGO_WIDTH * view_h / view_w
    x = (width - SPLASH_LOGO_WIDTH) / 2
    y = (height - logo_h) / 2
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
            f'viewBox="0 0 {width} {height}">'
            f'<svg x="{x}" y="{y:.2f}" width="{SPLASH_LOGO_WIDTH}" height="{logo_h:.2f}" '
            f'viewBox="0 0 {view_w} {int(view_h)}">{inner}</svg></svg>\n')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--inkscape", default=DEFAULT_INKSCAPE, help="path to inkscape(.com)")
    parser.add_argument("--preview", action="store_true", help="also write renders to docs/assets-preview/")
    args = parser.parse_args()

    if not Path(args.inkscape).exists() and shutil.which(args.inkscape) is None:
        print(f"Inkscape not found: {args.inkscape} (use --inkscape=PATH)", file=sys.stderr)
        return 2

    TARGET.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="fs_branding_") as tmp_name:
        tmp = Path(tmp_name)

        images: list[tuple[int, bytes]] = []
        for size in SMALL_SIZES + FULL_SIZES:
            svg = SOURCE / ("factorysim-icon-small.svg" if size in SMALL_SIZES else "factorysim-icon.svg")
            png = tmp / f"icon_{size}.png"
            render(args.inkscape, svg, png, size, size)
            data = strip_colour_chunks(png.read_bytes())
            if png_header(data)[:2] != (size, size):
                raise RuntimeError(f"{png.name} is not {size} x {size}")
            images.append((size, data))
            if args.preview and size in (16, 24, 32, 256):
                PREVIEW.mkdir(parents=True, exist_ok=True)
                (PREVIEW / f"branding_icon_{size}.png").write_bytes(data)
        (TARGET / "icon.ico").write_bytes(build_ico(images))

        wrapper = tmp / "splash.svg"
        wrapper.write_text(splash_wrapper(SOURCE / "factorysim-logo-light.svg"), encoding="utf-8")
        splash = tmp / "splash.png"
        render(args.inkscape, wrapper, splash, *SPLASH_SIZE)
        data = strip_colour_chunks(splash.read_bytes())
        if png_header(data) != (*SPLASH_SIZE, 6):
            raise RuntimeError(f"splash is {png_header(data)}, expected {SPLASH_SIZE} RGBA")
        (TARGET / "splash.png").write_bytes(data)

    shutil.copyfile(SOURCE / "factorysim-icon.svg", TARGET / "icon.svg")
    print(f"Wrote icon.ico ({len(images)} sizes), splash.png and icon.svg to {TARGET.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
