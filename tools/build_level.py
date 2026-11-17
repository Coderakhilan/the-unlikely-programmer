"""
Builds one floor's level data from a hand-placed config: the corridor/hall collision plus every room as its own
enlarged interior (cropped from the floor map, shown at 2x), each with its own collision, doors and spawn points.

Usage:
  python tools/build_level.py <floor_id> <raw_map.png> <processed_map.png> <config.json> <unity_assets_dir> <preview_dir>

Config (all coordinates are pixels of the RAW map image):
  "hall_walk":  [[x,y,w,h], ...]   corridor floor
  "hall_solid": [[x,y,w,h], ...]   corridor furniture
  "rooms": [ { "id": "f1_meeting", "name": "Meeting room",
               "walk":  [[x,y,w,h], ...],   first rect = the part of the room next to its door(s)
               "solid": [[x,y,w,h], ...],   furniture (shrunk by "inset" px so you don't snag on corners)
               "doors": [[x,y,w,h], ...] } ] the doorway gap(s) in the wall
  "inset": 3, "room_zoom": 2

Outputs:
  Data/Collision/<floor_id>.json        hall obstacles + door hotspots/spawns on the floor
  Data/Collision/rooms/<room_id>.json   room obstacles + exit hotspots/spawns (room-local raw pixels)
  Art/Processed/rooms/<room_id>.png     enlarged room background
"""
import json
import os
import sys
from collections import deque

from PIL import Image, ImageDraw

FLOOR_CELL = 8
ROOM_CELL = 4
MARGIN = 28          # raw px of wall shown around a room's floor
DOOR_DEPTH = 26      # thickness of a door hotspot
ROOM_WALL_CUT = 6    # raw px of wall around each room that the corridor can't walk into


def raster(walk, solid, x0, y0, w, h, cell, inset=0):
    gw, gh = (w + cell - 1) // cell, (h + cell - 1) // cell
    grid = [[False] * gw for _ in range(gh)]

    def paint(r, value, shrink):
        rx, ry, rw, rh = r[0] - x0 + shrink, r[1] - y0 + shrink, r[2] - 2 * shrink, r[3] - 2 * shrink
        if rw <= 0 or rh <= 0:
            return
        cx0, cy0 = max(0, int(rx) // cell), max(0, int(ry) // cell)
        cx1, cy1 = min(gw, (int(rx + rw) + cell - 1) // cell), min(gh, (int(ry + rh) + cell - 1) // cell)
        for gy in range(cy0, cy1):
            for gx in range(cx0, cx1):
                grid[gy][gx] = value

    for r in walk:
        paint(r, True, 0)
    for r in solid:
        paint(r, False, inset)
    return grid, gw, gh


def obstacle_rects(grid, gw, gh, cell):
    rects, open_runs = [], {}
    for y in range(gh + 1):
        runs = set()
        if y < gh:
            x = 0
            while x < gw:
                if not grid[y][x]:
                    x0 = x
                    while x < gw and not grid[y][x]:
                        x += 1
                    runs.add((x0, x))
                else:
                    x += 1
        for run in list(open_runs):
            if run not in runs:
                rects.append((run[0], open_runs[run], run[1] - run[0], y - open_runs[run]))
                del open_runs[run]
        for run in runs:
            open_runs.setdefault(run, y)
    return [[x * cell, y * cell, rw * cell, rh * cell] for x, y, rw, rh in rects]


def nearest_free(grid, gw, gh, cell, x, y, clear=1):
    """Snap a point (local px) to the centre of the nearest walkable cell with `clear` cells of space around it."""
    sx, sy = min(gw - 1, max(0, int(x) // cell)), min(gh - 1, max(0, int(y) // cell))
    q, seen = deque([(sx, sy)]), {(sx, sy)}
    while q:
        cx, cy = q.popleft()
        # require a little clearance (3x3 cells) so the player doesn't spawn wedged against a wall
        rng = range(-clear, clear + 1)
        if all(0 <= cx + dx < gw and 0 <= cy + dy < gh and grid[cy + dy][cx + dx] for dx in rng for dy in rng):
            return [cx * cell + cell // 2, cy * cell + cell // 2]
        for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
            if 0 <= nx < gw and 0 <= ny < gh and (nx, ny) not in seen:
                seen.add((nx, ny))
                q.append((nx, ny))
    return [int(x), int(y)]


def door_side(door, interior):
    dcx, dcy = door[0] + door[2] / 2, door[1] + door[3] / 2
    ix0, iy0, ix1, iy1 = interior[0], interior[1], interior[0] + interior[2], interior[1] + interior[3]
    dx = ix0 - dcx if dcx < ix0 else (dcx - ix1 if dcx > ix1 else 0)
    dy = iy0 - dcy if dcy < iy0 else (dcy - iy1 if dcy > iy1 else 0)
    if dx >= dy:
        return "left" if dcx < ix0 else "right"
    return "top" if dcy < iy0 else "bottom"


def door_geometry(door, interior):
    """Returns (floor hotspot, outside spawn, room exit hotspot, inside spawn) in raw px."""
    x, y, w, h = door
    cx, cy = x + w / 2, y + h / 2
    ix0, iy0, ix1, iy1 = interior[0], interior[1], interior[0] + interior[2], interior[1] + interior[3]
    side = door_side(door, interior)
    if side == "left":      # corridor is to the left of the room
        return [x - DOOR_DEPTH + 6, y, DOOR_DEPTH, h], [x - 30, cy], [x, y, w + 6, h], [ix0 + 16, cy]
    if side == "right":
        return [x + w - 6, y, DOOR_DEPTH, h], [x + w + 30, cy], [x - 6, y, w + 6, h], [ix1 - 16, cy]
    if side == "top":
        return [x, y - DOOR_DEPTH + 6, w, DOOR_DEPTH], [cx, y - 26], [x, y, w, h + 6], [cx, iy0 + 16]
    return [x, y + h - 6, w, DOOR_DEPTH], [cx, y + h + 30], [x, y - 6, w, h + 6], [cx, iy1 - 8]


def overlay(img, grid, gw, gh, cell, x0, y0, zoom, marks):
    crop = img.crop((x0, y0, x0 + gw * cell, y0 + gh * cell)).convert("RGB")
    px = crop.load()
    for gy in range(gh):
        for gx in range(gw):
            if not grid[gy][gx]:
                for dy in range(cell):
                    for dx in range(cell):
                        X, Y = gx * cell + dx, gy * cell + dy
                        if X < crop.size[0] and Y < crop.size[1]:
                            r, g, b = px[X, Y]
                            px[X, Y] = (min(255, r // 2 + 140), g // 3, b // 3)
    crop = crop.resize((crop.size[0] * zoom, crop.size[1] * zoom), Image.NEAREST)
    d = ImageDraw.Draw(crop)
    for kind, v in marks:
        if kind == "rect":
            d.rectangle([(v[0] - x0) * zoom, (v[1] - y0) * zoom, (v[0] + v[2] - x0) * zoom, (v[1] + v[3] - y0) * zoom], outline=(40, 140, 255), width=2)
        else:
            sx, sy = (v[0] - x0) * zoom, (v[1] - y0) * zoom
            d.ellipse([sx - 5, sy - 5, sx + 5, sy + 5], fill=(40, 255, 80))
    return crop


def main():
    floor_id, raw_path, processed_path, config_path, assets, prev_dir = sys.argv[1:7]
    cfg = json.load(open(config_path))
    raw = Image.open(raw_path).convert("RGB")
    processed = Image.open(processed_path).convert("RGB")
    W, H = raw.size
    floor_scale = processed.size[0] / W
    zoom = cfg.get("room_zoom", 2)
    inset = cfg.get("inset", 3)
    actor = cfg.get("actor_scale", float(zoom))   # how big Donnie is drawn inside rooms
    # show enough wall above a room that Donnie's head (112 px sprite) never leaves the picture
    margin_top = int(112 * actor / (floor_scale * zoom)) + 10

    coll_dir = os.path.join(assets, "Data", "Collision")
    rooms_coll = os.path.join(coll_dir, "rooms")
    rooms_art = os.path.join(assets, "Art", "Processed", "rooms")
    for d in (coll_dir, rooms_coll, rooms_art, prev_dir):
        os.makedirs(d, exist_ok=True)

    # ---------------- hall ----------------
    hall, gw, gh = raster(cfg["hall_walk"], cfg.get("hall_solid", []), 0, 0, W, H, FLOOR_CELL)
    # Rooms are only entered through their doors: cut every room (plus a few px of its wall) out of the corridor.
    for room in cfg.get("rooms", []):
        for r in room["walk"]:
            g = ROOM_WALL_CUT
            rx0, ry0 = max(0, (r[0] - g) // FLOOR_CELL), max(0, (r[1] - g) // FLOOR_CELL)
            rx1 = min(gw, (r[0] + r[2] + g + FLOOR_CELL - 1) // FLOOR_CELL)
            ry1 = min(gh, (r[1] + r[3] + g + FLOOR_CELL - 1) // FLOOR_CELL)
            for gy in range(ry0, ry1):
                for gx in range(rx0, rx1):
                    hall[gy][gx] = False
    floor_doors, marks = [], []
    room_previews = []

    for room in cfg.get("rooms", []):
        interior = room["walk"][0]
        exits = []
        for i, door in enumerate(room["doors"]):
            hot, out_spawn, exit_hot, in_spawn = door_geometry(door, interior)
            if room.get("exit_hotspots"):
                exit_hot = room["exit_hotspots"][i]
            out_spawn = nearest_free(hall, gw, gh, FLOOR_CELL, out_spawn[0], out_spawn[1])
            floor_doors.append({"room": room["id"], "index": i, "name": room["name"],
                                "hotspot": [int(v) for v in hot], "spawn": out_spawn,
                                "locked": room.get("locked", "")})
            marks += [("rect", hot), ("dot", out_spawn)]
            exits.append((i, exit_hot, in_spawn))
        if room.get("locked"):
            continue  # a locked door: no interior to build

        # ---------------- room interior ----------------
        pts = room["walk"] + room["doors"]
        x0 = max(0, min(r[0] for r in pts) - MARGIN)
        y0 = max(0, min(r[1] for r in pts) - margin_top)
        x1 = min(W, max(r[0] + r[2] for r in pts) + MARGIN)
        y1 = min(H, max(r[1] + r[3] for r in pts) + MARGIN)
        cw, ch = x1 - x0, y1 - y0

        grid, rgw, rgh = raster(room["walk"], room.get("solid", []), x0, y0, cw, ch, ROOM_CELL, inset)
        room_exits, room_marks = [], []
        for i, exit_hot, in_spawn in exits:
            spawn = nearest_free(grid, rgw, rgh, ROOM_CELL, in_spawn[0] - x0, in_spawn[1] - y0, clear=3)
            local_hot = [int(exit_hot[0] - x0), int(exit_hot[1] - y0), int(exit_hot[2]), int(exit_hot[3])]
            room_exits.append({"index": i, "hotspot": local_hot, "spawn": spawn})
            room_marks += [("rect", [exit_hot[0], exit_hot[1], exit_hot[2], exit_hot[3]]), ("dot", [spawn[0] + x0, spawn[1] + y0])]

        # enlarged background: crop the processed floor map and double it (crisp nearest-neighbour)
        box = (round(x0 * floor_scale), round(y0 * floor_scale), round(x1 * floor_scale), round(y1 * floor_scale))
        bg = processed.crop(box)
        bg = bg.resize((bg.size[0] * zoom, bg.size[1] * zoom), Image.NEAREST)
        bg.save(os.path.join(rooms_art, room["id"] + ".png"))

        rects = obstacle_rects(grid, rgw, rgh, ROOM_CELL)
        with open(os.path.join(rooms_coll, room["id"] + ".json"), "w") as f:
            json.dump({"id": room["id"], "name": room["name"], "floor": floor_id, "cell": ROOM_CELL,
                       "width": cw, "height": ch, "scale": bg.size[0] / cw, "actor": actor, "origin": [x0, y0],
                       "rects": [v for r in rects for v in r], "exits": room_exits}, f)
        room_previews.append(overlay(raw, grid, rgw, rgh, ROOM_CELL, x0, y0, 2, room_marks))
        print(f"  room {room['id']}: {cw}x{ch} raw px, {len(rects)} obstacle rects, {len(room_exits)} door(s)")

    rects = obstacle_rects(hall, gw, gh, FLOOR_CELL)
    with open(os.path.join(coll_dir, floor_id + ".json"), "w") as f:
        json.dump({"cell": FLOOR_CELL, "width": W, "height": H, "scale": floor_scale,
                   "rects": [v for r in rects for v in r], "doors": floor_doors,
                   "shade": [int(v) for room in cfg.get("rooms", []) for r in room["walk"] for v in r]}, f)
    print(f"{floor_id}: hall {len(rects)} obstacle rects, {len(floor_doors)} room doors")

    overlay(raw, hall, gw, gh, FLOOR_CELL, 0, 0, 1, marks).save(os.path.join(prev_dir, floor_id + "_hall.png"))
    if room_previews:
        sheet_w = 1600
        rows, row, x, rh = [], [], 0, 0
        for p in room_previews:
            if x + p.size[0] > sheet_w and row:
                rows.append((row, rh))
                row, x, rh = [], 0, 0
            row.append(p)
            x += p.size[0] + 8
            rh = max(rh, p.size[1])
        rows.append((row, rh))
        sheet = Image.new("RGB", (sheet_w, sum(r[1] + 8 for r in rows)), (20, 20, 20))
        yy = 0
        for row, rh in rows:
            xx = 0
            for p in row:
                sheet.paste(p, (xx, yy))
                xx += p.size[0] + 8
            yy += rh + 8
        sheet.save(os.path.join(prev_dir, floor_id + "_rooms.png"))


if __name__ == "__main__":
    main()
