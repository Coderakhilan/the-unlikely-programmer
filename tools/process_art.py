"""
Art import pipeline for The Unlikely Programmer.

Takes the raw art sheets (magenta background) and produces game-ready pixel art:
  * removes the magenta background (and its dark-purple shadows)
  * slices sprite sheets into individual sprites
  * scales everything to the game scale (1 metre = 32 px)
  * snaps colours to a small shared palette (no dithering) so it reads as pixel art

Usage:  python -I tools/process_art.py <raw_dir> <out_dir> [preview_dir]
"""
import json
import os
import sys
from collections import deque

from PIL import Image, ImageFilter

DETAIL = 2                   # 1 = chunky 640x360 look, 2 = 960x540 look (2x2 pixels on a 1080p screen)
GAME_PX_PER_METRE = 32 * DETAIL
CHAR_HEIGHT_PX = 56 * DETAIL           # Donnie is ~1.75 m
ROOM_TARGET_HEIGHT = 360 * DETAIL      # a 3:2 room is one 640x360 screen tall at DETAIL=1
PROP_BOOST = 1.0                       # global prop size multiplier (per-prop tweaks live in prop_sheets below)


# ---------------------------------------------------------------- background removal
def _is_bg(r, g, b):
    return (r - g > 50) and (b - g > 50) and abs(r - b) < 100


def cutout(img):
    """Return RGBA with the magenta background (and purple shadow) made transparent."""
    img = img.convert("RGB")
    w, h = img.size
    src = img.load()
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    dst = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b = src[x, y]
            if not _is_bg(r, g, b):
                dst[x, y] = (r, g, b, 255)
    # remove pink fringe pixels touching the transparent area
    for _ in range(2):
        kill = []
        for y in range(1, h - 1):
            for x in range(1, w - 1):
                r, g, b, a = dst[x, y]
                if a == 0:
                    continue
                if r - g > 22 and b - g > 22:
                    if (dst[x - 1, y][3] == 0 or dst[x + 1, y][3] == 0 or
                            dst[x, y - 1][3] == 0 or dst[x, y + 1][3] == 0):
                        kill.append((x, y))
        for x, y in kill:
            dst[x, y] = (0, 0, 0, 0)
    return out


# ---------------------------------------------------------------- slicing
def find_components(rgba, shrink=4, merge=9):
    """Bounding boxes (full-res) of separate objects on a transparent sheet, row-major sorted."""
    w, h = rgba.size
    alpha = rgba.split()[3]
    small = alpha.resize((w // shrink, h // shrink), Image.BOX).point(lambda v: 255 if v > 0 else 0)
    small = small.filter(ImageFilter.MaxFilter(merge))
    sw, sh = small.size
    data = small.load()
    seen = [[False] * sw for _ in range(sh)]
    boxes = []
    for sy in range(sh):
        for sx in range(sw):
            if data[sx, sy] and not seen[sy][sx]:
                q = deque([(sx, sy)])
                seen[sy][sx] = True
                x0 = x1 = sx
                y0 = y1 = sy
                n = 0
                while q:
                    cx, cy = q.popleft()
                    n += 1
                    x0, x1, y0, y1 = min(x0, cx), max(x1, cx), min(y0, cy), max(y1, cy)
                    for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                        if 0 <= nx < sw and 0 <= ny < sh and data[nx, ny] and not seen[ny][nx]:
                            seen[ny][nx] = True
                            q.append((nx, ny))
                if n >= 20:  # ignore specks
                    boxes.append((x0 * shrink, y0 * shrink, (x1 + 1) * shrink, (y1 + 1) * shrink))
    # tighten to real opaque pixels
    tight = []
    for (x0, y0, x1, y1) in boxes:
        bb = alpha.crop((x0, y0, x1, y1)).getbbox()
        if bb:
            tight.append((x0 + bb[0], y0 + bb[1], x0 + bb[2], y0 + bb[3]))
    return tight


def sort_row_major(boxes, row_tol=None):
    if not boxes:
        return []
    if row_tol is None:
        row_tol = sorted(b[3] - b[1] for b in boxes)[len(boxes) // 2] * 0.5
    boxes = sorted(boxes, key=lambda b: (b[1] + b[3]) / 2)
    rows, cur, cur_y = [], [], None
    for b in boxes:
        cy = (b[1] + b[3]) / 2
        if cur_y is None or abs(cy - cur_y) <= row_tol:
            cur.append(b)
            cur_y = cy if cur_y is None else (cur_y + cy) / 2
        else:
            rows.append(cur)
            cur, cur_y = [b], cy
    rows.append(cur)
    return [sorted(r, key=lambda b: b[0]) for r in rows]


# ---------------------------------------------------------------- scaling + palette
def scale_rgba(img, s):
    w, h = img.size
    nw, nh = max(1, round(w * s)), max(1, round(h * s))
    rgb = img.convert("RGB").resize((nw, nh), Image.BOX)
    a = img.split()[3].resize((nw, nh), Image.BOX).point(lambda v: 255 if v >= 128 else 0)
    out = rgb.convert("RGBA")
    out.putalpha(a)
    return out


def build_palette(images, colors):
    """One shared palette from all opaque pixels of the given RGBA images."""
    pix = []
    for im in images:
        px = im.load()
        w, h = im.size
        for y in range(h):
            for x in range(w):
                r, g, b, a = px[x, y]
                if a:
                    pix.append((r, g, b))
    if not pix:
        return None
    side = int(len(pix) ** 0.5) + 1
    canvas = Image.new("RGB", (side, side), pix[0])
    canvas.putdata(pix + [pix[0]] * (side * side - len(pix)))
    return canvas.quantize(colors=colors, method=Image.MEDIANCUT, dither=Image.NONE)


def apply_palette(img, pal):
    if pal is None:
        return img
    a = img.split()[3]
    q = img.convert("RGB").quantize(palette=pal, dither=Image.NONE).convert("RGB")
    out = q.convert("RGBA")
    out.putalpha(a)
    return out


# ---------------------------------------------------------------- jobs
def process_character_sheet(path, out_path, rows=4, cols=4, colors=64):
    raw = Image.open(path)
    rgba = cutout(raw)
    boxes = sort_row_major(find_components(rgba))
    flat = [b for r in boxes for b in r]
    if len(flat) != rows * cols or any(len(r) != cols for r in boxes):
        print(f"  WARNING {os.path.basename(path)}: expected {rows}x{cols} sprites, found {[len(r) for r in boxes]}")
    crops = [[rgba.crop(b) for b in r] for r in boxes]
    heights = sorted(c.size[1] for r in crops for c in r)
    s = CHAR_HEIGHT_PX / heights[len(heights) // 2]
    scaled = [[scale_rgba(c, s) for c in r] for r in crops]
    pal = build_palette([c for r in scaled for c in r], colors)
    scaled = [[apply_palette(c, pal) for c in r] for r in scaled]

    # centre each frame horizontally on the torso/head (top 60%) so walking frames don't jitter.
    # The cell must fit the widest reach either side of that centre (e.g. Donnie pushing his cart),
    # otherwise a frame spills into its neighbour's cell and shows up as a sliver.
    def torso_x(c):
        px = c.load()
        w, h = c.size
        xs = [x for y in range(int(h * 0.6)) for x in range(w) if px[x, y][3]]
        return (sum(xs) / len(xs)) if xs else w / 2

    half = max(max(torso_x(c), c.size[0] - torso_x(c)) for r in scaled for c in r)
    cw = int(2 * half) + 6
    ch = max(c.size[1] for r in scaled for c in r) + 2
    sheet = Image.new("RGBA", (cw * cols, ch * len(scaled)), (0, 0, 0, 0))
    for ry, r in enumerate(scaled):
        for cx, c in enumerate(r):
            cxm = torso_x(c)
            ox = round(cx * cw + cw / 2 - cxm)
            oy = ry * ch + ch - c.size[1] - 1
            sheet.alpha_composite(c, (ox, oy))
    sheet.save(out_path)
    return {"file": os.path.basename(out_path), "cell_w": cw, "cell_h": ch, "cols": cols, "rows": len(scaled), "scale": round(s, 4)}


def process_prop_sheet(path, out_dir, names, scale, overrides=None, colors=64):
    overrides = overrides or {}
    raw = Image.open(path)
    rgba = cutout(raw)
    flat = [b for r in sort_row_major(find_components(rgba)) for b in r]
    if len(flat) != len(names):
        print(f"  WARNING {os.path.basename(path)}: expected {len(names)} props, found {len(flat)}")
    items = []
    for name, b in zip(names, flat):
        s = scale * overrides.get(name, 1.0)
        items.append((name, scale_rgba(rgba.crop(b), s)))
    pal = build_palette([im for _, im in items], colors)
    meta = []
    os.makedirs(out_dir, exist_ok=True)
    for name, im in items:
        im = apply_palette(im, pal)
        im.save(os.path.join(out_dir, name + ".png"))
        meta.append({"name": name, "w": im.size[0], "h": im.size[1]})
    return meta


FLOOR_MAP_SCALE = 1.28   # floor maps are drawn at ~50 px per metre; the game uses 64 px per metre
FLOOR_MAP_SCALES = {"server_room": 0.6}   # the server room was drawn ~2x too big relative to the people in it


def process_floor_map(path, out_path, colors=160):
    """Top-down floor maps: resample to game scale and limit the palette."""
    raw = Image.open(path).convert("RGB")
    scale = FLOOR_MAP_SCALES.get(os.path.splitext(os.path.basename(path))[0], FLOOR_MAP_SCALE)
    nw, nh = round(raw.size[0] * scale), round(raw.size[1] * scale)
    big = raw.resize((nw, nh), Image.LANCZOS)
    q = big.quantize(colors=colors, method=Image.MEDIANCUT, dither=Image.NONE).convert("RGB")
    q.save(out_path)
    return {"file": os.path.basename(out_path), "w": nw, "h": nh, "scale": scale}


def process_room(path, out_path, colors=96):
    raw = Image.open(path).convert("RGB")
    s = ROOM_TARGET_HEIGHT / min(raw.size[1], 1024) if raw.size[0] / raw.size[1] < 2.5 else ROOM_TARGET_HEIGHT / 1024
    nw, nh = round(raw.size[0] * s), round(raw.size[1] * s)
    small = raw.resize((nw, nh), Image.BOX)
    q = small.quantize(colors=colors, method=Image.MEDIANCUT, dither=Image.NONE).convert("RGB")
    q.save(out_path)
    return {"file": os.path.basename(out_path), "w": nw, "h": nh, "scale": round(s, 4)}


# ---------------------------------------------------------------- driver
def preview(img, path, zoom=4):
    bg = Image.new("RGBA", img.size, (90, 90, 90, 255))
    bg.alpha_composite(img.convert("RGBA"))
    bg.resize((img.size[0] * zoom, img.size[1] * zoom), Image.NEAREST).convert("RGB").save(path)


def main():
    raw_dir, out_dir = sys.argv[1], sys.argv[2]
    prev_dir = sys.argv[3] if len(sys.argv) > 3 else None
    os.makedirs(out_dir, exist_ok=True)
    if prev_dir:
        os.makedirs(prev_dir, exist_ok=True)
    meta = {"characters": [], "props": {}, "rooms": []}

    for fname in sorted(os.listdir(raw_dir)):
        name, ext = os.path.splitext(fname)
        src = os.path.join(raw_dir, fname)
        if name.startswith("char_") and ext.lower() == ".png":
            print("character", name)
            m = process_character_sheet(src, os.path.join(out_dir, name + ".png"))
            meta["characters"].append(m)
            if prev_dir:
                preview(Image.open(os.path.join(out_dir, name + ".png")), os.path.join(prev_dir, name + "_x4.png"))

    prop_sheets = {
        "props_office_a": (["desk", "desk_computer", "chair", "filing_cabinet", "bookshelf", "plant_small", "plant_large",
                            "trash_empty", "trash_full", "water_cooler", "coffee_machine", "printer", "whiteboard"], 0.15 * DETAIL * PROP_BOOST,
                           {"desk": 1.35, "desk_computer": 1.35}),  # desks drawn bigger so the computer reads next to Donnie
        "props_janitor": (["cart_empty", "cart_loaded", "mop_bucket", "wet_floor_sign", "usb_cable", "flashlight"], 0.11 * DETAIL * PROP_BOOST,
                          {"usb_cable": 0.45, "flashlight": 0.45, "wet_floor_sign": 0.8}),
    }
    for sheet, (names, scale, ov) in prop_sheets.items():
        src = os.path.join(raw_dir, sheet + ".png")
        if os.path.exists(src):
            print("props", sheet)
            meta["props"][sheet] = process_prop_sheet(src, os.path.join(out_dir, "props", sheet), names, scale, ov)

    for room in ["room_f0_lobby", "room_f0_corridor", "room_f0_openoffice", "room_f0_supply", "room_f0_terminal"]:
        src = os.path.join(raw_dir, room + ".png")
        if os.path.exists(src):
            print("room", room)
            m = process_room(src, os.path.join(out_dir, room + ".png"))
            meta["rooms"].append(m)

    floor_maps = {"room_f0": "floor0", "room_f1": "floor1", "room_f2": "floor2", "room_f3": "floor3", "server_room": "server"}
    for raw_name, out_name in floor_maps.items():
        src = os.path.join(raw_dir, raw_name + ".png")
        if os.path.exists(src):
            print("floor map", raw_name, "->", out_name)
            meta["rooms"].append(process_floor_map(src, os.path.join(out_dir, out_name + ".png")))

    # ---- dialogue portraits: fixed grids, rows = expressions (or people), columns = angles
    angles = ["", "_side", "_profile", "_down"]
    portrait_rows = {
        "donnie": ["neutral", "worried", "angry", "shocked"],
        "betty": ["neutral", "sad", "angry", "shocked"],
        "ceo": ["neutral", "smug", "stern", "angry"],
        "dan": ["neutral", "tired", "asleep", "annoyed"],
        "liam": ["neutral", "drunk", "angry", "shocked"],
        "ryker": ["neutral", "friendly", "serious", "shocked"],
        "kidnappers": ["yuvie", "adi", "irshad"],
    }
    pdir = os.path.join(out_dir, "portraits")
    os.makedirs(pdir, exist_ok=True)
    for who, rows in portrait_rows.items():
        src = os.path.join(raw_dir, who + " potrait reactions.png")
        if not os.path.exists(src):
            continue
        print("portraits", who)
        sheet = cutout(Image.open(src))
        cw, ch = sheet.size[0] / 4, sheet.size[1] / len(rows)
        # rows in these sheets aren't evenly spaced: cut at the emptiest line near each expected boundary,
        # so no bust loses its shoulders and no neighbour's hair creeps in
        alpha = sheet.split()[3]
        aw, ah = alpha.size
        apx = alpha.load()
        def fill(y):
            return sum(1 for x in range(0, aw, 2) if apx[x, y])
        cuts = [0]
        for k in range(1, len(rows)):
            lo, hi = int(k * ch - ch * 0.3), int(k * ch + ch * 0.3)
            cuts.append(min(range(max(1, lo), min(ah - 1, hi)), key=lambda y: (fill(y), abs(y - k * ch))))
        cuts.append(ah)
        for r, row_name in enumerate(rows):
            for c, ang in enumerate(angles):
                cell = sheet.crop((round(c * cw), cuts[r], round((c + 1) * cw), cuts[r + 1]))
                bb = cell.getbbox()
                if not bb:
                    continue
                # keep the full cell width (so busts line up) but trim empty rows above/below
                cell = cell.crop((0, bb[1], cell.size[0], bb[3]))
                cell = scale_rgba(cell, 0.5)
                name = (row_name if who == "kidnappers" else f"{who}_{row_name}") + ang
                cell.save(os.path.join(pdir, name + ".png"))

    # ---- story illustrations (intro flashback, cutscenes, endings): kept smooth, just resized to 1280 wide
    cdir = os.path.join(out_dir, "cutscenes")
    os.makedirs(cdir, exist_ok=True)
    for fname in sorted(os.listdir(raw_dir)):
        name, ext = os.path.splitext(fname)
        if ext.lower() != ".png" or "potrait" in name or name.startswith(("char_", "props_", "room_", "server_")):
            continue
        slug = "".join(ch if ch.isalnum() else "_" for ch in name.lower()).strip("_")
        while "__" in slug:
            slug = slug.replace("__", "_")
        im = Image.open(os.path.join(raw_dir, fname)).convert("RGB")
        im = im.resize((1280, round(im.size[1] * 1280 / im.size[0])), Image.LANCZOS)
        im.save(os.path.join(cdir, slug + ".png"))
        print("cutscene", slug)

    # ---- computers: left half = beige employee terminal, right half = black employee PC; rows front/back/left/right
    src = os.path.join(raw_dir, "props_computers.png")
    if os.path.exists(src):
        print("computers")
        sheet = cutout(Image.open(src))
        ddir = os.path.join(out_dir, "props", "props_computers")
        os.makedirs(ddir, exist_ok=True)
        half = sheet.size[0] / 2
        cw, ch = half / 4, sheet.size[1] / 4
        for k, kind in enumerate(["terminal", "pc"]):
            for r, facing in enumerate(["front", "back", "left", "right"]):
                for c in range(4):
                    x0 = k * half + c * cw
                    cell = sheet.crop((round(x0), round(r * ch), round(x0 + cw), round((r + 1) * ch)))
                    bb = cell.getbbox()
                    if bb:
                        scale_rgba(cell.crop(bb), 0.3).save(os.path.join(ddir, f"{kind}_{facing}_{c}.png"))

    with open(os.path.join(out_dir, "art_meta.json"), "w") as f:
        json.dump(meta, f, indent=2)
    print("done")


if __name__ == "__main__":
    main()
