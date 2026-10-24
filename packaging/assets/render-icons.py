#!/usr/bin/env python3
"""Regenerates the PNG, ICO and ICNS app icons from the SVGs in this folder.

Needs Pillow and a Chromium/Chrome binary (set CHROME, default: chromium on PATH).
  lumen.svg        full-bleed tile (Windows, Linux)
  lumen-small.svg  same with a wider gap, used for 16-32 px
  lumen-macos.svg  tile with the macOS grid margin (icns)
"""
import os
import subprocess
import tempfile
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
CHROME = os.environ.get("CHROME", "chromium")


def render(svg: str, px: int) -> Image.Image:
    with tempfile.TemporaryDirectory() as tmp:
        page = Path(tmp) / "p.html"
        out = Path(tmp) / "o.png"
        page.write_text(
            f'<body style="margin:0;background:transparent"><img src="{(HERE / svg).as_uri()}" width="{px}" height="{px}" style="display:block"></body>'
        )
        subprocess.run(
            [CHROME, "--headless=new", "--no-sandbox", "--disable-gpu", "--default-background-color=00000000",
             f"--window-size={px},{px}", "--hide-scrollbars", f"--screenshot={out}", page.as_uri()],
            check=True, capture_output=True)
        return Image.open(out).convert("RGBA")


def tile(px: int) -> Image.Image:
    return render("lumen-small.svg" if px <= 32 else "lumen.svg", px)


for n in (128, 256, 512):
    tile(n).save(HERE / f"lumen-{n}.png", optimize=True)

sizes = [16, 24, 32, 48, 64, 128, 256]
tile(256).save(HERE / "lumen.ico", format="ICO", sizes=[(s, s) for s in sizes],
               append_images=[tile(s) for s in sizes if s != 256])

mac = render("lumen-macos.svg", 1024)
mac.save(HERE / "lumen.icns", format="ICNS",
         append_images=[mac.resize((s, s), Image.LANCZOS) for s in (16, 32, 64, 128, 256, 512)])
