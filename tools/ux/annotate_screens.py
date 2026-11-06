#!/usr/bin/env python3
"""
Anota las capturas de pantalla de una ronda de revisión UX con marcadores numerados
(los números corresponden a las tablas "Elementos" del Manual de pantallas).

    python3 tools/ux/annotate_screens.py tools/ux/markers_v0.1.2.json

El JSON define, por captura: marcadores [número, x, y] en píxeles de la captura original,
y zonas a ocultar [x1, y1, x2, y2] (p. ej. el usuario de Windows en el diálogo de comentarios).
Las capturas originales se guardan YA enmascaradas en docs/ux/capturas/<versión>/.
Requiere: Pillow.
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MARK = (192, 0, 0)          # rojo marcador
WHITE = (255, 255, 255)


def font(size, bold=True):
    for path in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
                 "C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def main(cfg_path):
    cfg = json.load(open(cfg_path, encoding="utf8"))
    src = os.path.join(ROOT, cfg["source"])
    out = os.path.join(ROOT, cfg["output"])
    os.makedirs(out, exist_ok=True)

    # 1) Enmascarar datos personales en la captura original (in place)
    for name, boxes in cfg.get("mask", {}).items():
        p = os.path.join(src, f"{name}.png")
        im = Image.open(p).convert("RGB")
        d = ImageDraw.Draw(im)
        for x1, y1, x2, y2 in boxes:
            d.rectangle([x1, y1, x2, y2], fill=(232, 234, 238))
            d.text((x1 + 4, y1 + 1), "usuario", fill=(107, 114, 128), font=font(11, bold=False))
        im.save(p, optimize=True)

    # 2) Marcadores numerados
    for name, marks in cfg["markers"].items():
        im = Image.open(os.path.join(src, f"{name}.png")).convert("RGB")
        scale = max(1.0, im.width / 1919)             # tamaño de marcador relativo a la captura
        r = int((15 if im.width > 1000 else 11) * scale)
        f = font(int(r * 1.15))
        d = ImageDraw.Draw(im)
        for n, x, y in marks:
            d.ellipse([x - r - 2, y - r - 2, x + r + 2, y + r + 2], fill=WHITE)
            d.ellipse([x - r, y - r, x + r, y + r], fill=MARK)
            t = str(n)
            w, h = d.textbbox((0, 0), t, font=f)[2:]
            d.text((x - w / 2, y - h / 2 - 1), t, fill=WHITE, font=f)
        im.save(os.path.join(out, f"{name}.png"), optimize=True)
        print("anotada", name, len(marks), "marcadores")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "markers_v0.1.2.json"))
