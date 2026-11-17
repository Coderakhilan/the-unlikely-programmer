"""
Draws a labelled coordinate grid over a map (or a crop of it) so walkable areas can be placed by hand.
Usage: python tools/grid_overlay.py <map.png> <out.png> [x0 y0 x1 y1] [zoom]
Grid lines every 32 px (bold every 128), labels in ORIGINAL map pixels.
"""
import sys

from PIL import Image, ImageDraw


def main():
    src, out = sys.argv[1], sys.argv[2]
    img = Image.open(src).convert("RGB")
    if len(sys.argv) >= 7:
        x0, y0, x1, y1 = (int(v) for v in sys.argv[3:7])
    else:
        x0, y0, x1, y1 = 0, 0, img.size[0], img.size[1]
    zoom = float(sys.argv[7]) if len(sys.argv) >= 8 else 1.0
    crop = img.crop((x0, y0, x1, y1))
    crop = crop.resize((int(crop.size[0] * zoom), int(crop.size[1] * zoom)), Image.NEAREST)
    d = ImageDraw.Draw(crop)
    step = 32
    for gx in range((x0 // step) * step, x1 + 1, step):
        if gx < x0:
            continue
        sx = (gx - x0) * zoom
        bold = gx % 128 == 0
        d.line([(sx, 0), (sx, crop.size[1])], fill=(0, 255, 255) if bold else (0, 140, 160), width=2 if bold else 1)
        if bold:
            for ly in range(0, crop.size[1], 160):
                d.text((sx + 2, ly + 2), str(gx), fill=(255, 255, 0))
    for gy in range((y0 // step) * step, y1 + 1, step):
        if gy < y0:
            continue
        sy = (gy - y0) * zoom
        bold = gy % 128 == 0
        d.line([(0, sy), (crop.size[0], sy)], fill=(0, 255, 255) if bold else (0, 140, 160), width=2 if bold else 1)
        if bold:
            for lx in range(0, crop.size[0], 200):
                d.text((lx + 2, sy + 2), str(gy), fill=(255, 255, 0))
    crop.save(out)


if __name__ == "__main__":
    main()
