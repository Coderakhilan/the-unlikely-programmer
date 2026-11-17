"""Cut art_raw/item_icons.png (4x4 grid on magenta) into Assets/_Project/Art/Processed/icons/icon_<name>.png.

    python tools/slice_icons.py art_raw/item_icons.png TheUnlikelyProgrammer/Assets/_Project/Art/Processed/icons

Each cell: magenta keyed out (soft edges de-pinked), trimmed to the drawing, centred on a square canvas, scaled to 96 px.
"""
import os
import sys
from PIL import Image

NAMES = [
    "cable", "key", "note", "slip",
    "printout", "coin", "coffee", "crisps",
    "torch", "keycard", "pendrive", "tape",
    "folder", "hand", "book", "note_lock",
]
SIZE = 96


def key_out(im):
    im = im.convert("RGBA")
    px = im.load()
    w, h = im.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            m = min(r, b) - g          # how magenta the pixel is
            if m > 110:
                px[x, y] = (0, 0, 0, 0)
            elif m > 45:
                k = (m - 45) / 65.0    # edge: fade out and pull the pink back out of it
                c = max(g, min(r, b) - int(m * 0.8))
                px[x, y] = (min(r, c + 20), g, min(b, c), int(a * (1 - k)))
    return im


def main(src, out):
    os.makedirs(out, exist_ok=True)
    sheet = Image.open(src)
    w, h = sheet.size
    cw, ch = w / 4.0, h / 4.0
    for i, name in enumerate(NAMES):
        cx, cy = i % 4, i // 4
        # 4 px in from each edge: the neighbouring drawings sometimes touch the cell line
        cell = sheet.crop((int(cx * cw) + 4, int(cy * ch) + 4, int((cx + 1) * cw) - 4, int((cy + 1) * ch) - 4))
        cell = key_out(cell)
        box = cell.split()[3].point(lambda a: 255 if a > 60 else 0).getbbox()   # ignore faint stray specks
        if box:
            cell = cell.crop(box)
        side = max(cell.size) + 8
        canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        canvas.paste(cell, ((side - cell.size[0]) // 2, (side - cell.size[1]) // 2), cell)
        canvas = canvas.resize((SIZE, SIZE), Image.Resampling.LANCZOS)
        canvas.save(os.path.join(out, "icon_" + name + ".png"))
        print("icon_" + name, cell.size)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
