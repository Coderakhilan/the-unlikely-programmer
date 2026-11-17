"""
Floor 2's map drew the GyaraCorp mark wrong (a round G). This puts the real mark from Floor 1 (octagonal G with
the bar through it) on the reception wall, and gives the lawn hedge G its left-hand bar.
Usage: python tools/patch_f2_logo.py   (reads art_raw/originals/room_f2.png, writes art_raw/room_f2.png)
"""
import os

from PIL import Image

SRC, OUT, REF = "art_raw/originals/room_f2.png", "art_raw/room_f2.png", "art_raw/room_f1.png"


def lum(p):
    return 0.3 * p[0] + 0.59 * p[1] + 0.11 * p[2]


def main():
    img = Image.open(SRC).convert("RGB")
    ref = Image.open(REF).convert("RGB")
    px, rp = img.load(), ref.load()

    # 1) wall: rub out the round G (blend from the wall around it, keeping the plaster's grain),
    #    then paint Floor 1's mark in its place
    x0, y0, x1, y1 = 740, 88, 798, 129
    orig = img.copy().load()
    for y in range(y0, y1):
        for x in range(x0, x1):
            tx, ty = (x - x0) / (x1 - x0), (y - y0) / (y1 - y0)
            l, r, t, b = orig[x0 - 2, y], orig[x1 + 1, y], orig[x, y0 - 2], orig[x, y1 + 1]
            smooth = [((l[i] * (1 - tx) + r[i] * tx) + (t[i] * (1 - ty) + b[i] * ty)) / 2 for i in range(3)]
            # grain: a bit of wall texture from the same row, left of the sign
            g, gl = orig[x - 70, y], orig[x - 71, y - 1]
            px[x, y] = tuple(max(0, min(255, int(smooth[i] + (g[i] - gl[i]) * 0.5))) for i in range(3))
    fx0, fy0, fx1, fy1 = 742, 93, 796, 135               # Floor 1 mark with a margin of wall
    wall_ref = rp[fx0, fy0]
    scale = 0.86                                         # Floor 2's sign is a little smaller
    w, h = round((fx1 - fx0) * scale), round((fy1 - fy0) * scale)
    mark = ref.crop((fx0, fy0, fx1, fy1)).resize((w, h), Image.LANCZOS)
    mp = mark.load()
    cx, cy = 769 - w // 2, 90
    for y in range(h):
        for x in range(w):
            m = mp[x, y]
            dark = max(0.0, min(1.0, (lum(wall_ref) - lum(m) - 25) / 60.0))
            if dark <= 0:
                continue
            X, Y = cx + x, cy + y
            base = px[X, Y]
            # keep the mark's own shade, lit like Floor 2's wall
            shade = tuple(int(m[i] * lum(base) / max(1, lum(wall_ref))) for i in range(3))
            px[X, Y] = tuple(int(base[i] * (1 - dark) + shade[i] * dark) for i in range(3))

    # 2) lawn: the hedge G gets the bar sticking out on the left, like the logo
    bar = img.crop((678, 527, 724, 555))
    mask = Image.new("L", bar.size, 255)
    mp2 = mask.load()
    for y in range(bar.size[1]):
        for x in range(bar.size[0]):
            e = min(x, bar.size[0] - 1 - x, y, bar.size[1] - 1 - y)
            mp2[x, y] = min(255, 70 + e * 60)
    img.paste(bar, (600, 527), mask)
    tmp = OUT + ".tmp.png"
    img.save(tmp)
    os.replace(tmp, OUT)
    print("patched", OUT)


if __name__ == "__main__":
    main()
