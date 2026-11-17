"""
Derives walls/furniture collision rectangles from a top-down floor map image.

Classification happens in 8 px cells:
  * walkable = light tile floor or dark blue-grey carpet (large connected areas only)
  * walls, trim, furniture and the outside are near-black or warm brown -> solid
  * doors are the short amber bars drawn in walls; a door cell block is forced walkable
Extra control comes from an optional JSON config (see CONFIG below).

Usage: python tools/make_collision.py <map.png> <out.json> <preview.png> [config.json]
All coordinates are pixels of the ORIGINAL image.

CONFIG keys (all optional):
  "open":    [[x, y, w, h], ...]   force these rectangles walkable (extra doors / gaps)
  "blocked": [[x, y, w, h], ...]   force these rectangles solid (exterior, glass, etc.)
  "no_door": [[x, y, w, h], ...]   ignore auto-detected doors inside these rectangles
  "min_area": cells               smallest walkable region to keep (default 120)
"""
import json
import sys
from collections import deque

from PIL import Image

CELL = 8


def cell_colors(img):
    return img.convert("RGB").resize((img.size[0] // CELL, img.size[1] // CELL), Image.BOX)


def classify(small):
    w, h = small.size
    px = small.load()
    walk = [[False] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            lum = 0.3 * r + 0.59 * g + 0.11 * b
            mx, mn = max(r, g, b), min(r, g, b)
            sat = (mx - mn) / mx if mx else 0
            tile_floor = lum >= 100 and (r - b) < 60 and sat < 0.30
            carpet = 42 <= lum <= 95 and (b - r) >= -10 and sat < 0.34
            walk[y][x] = tile_floor or carpet
    return walk


def amber_cells(img):
    """Cells that are mostly amber/gold: door frames and wall trim."""
    w, h = img.size[0] // CELL, img.size[1] // CELL
    px = img.convert("RGB").load()
    amber = [[False] * w for _ in range(h)]
    for cy in range(h):
        for cx in range(w):
            n = 0
            for dy in range(0, CELL, 2):
                for dx in range(0, CELL, 2):
                    r, g, b = px[cx * CELL + dx, cy * CELL + dy]
                    if r >= 140 and 90 <= g <= 190 and b <= 100 and r - b >= 70:
                        n += 1
            amber[cy][cx] = n >= 4
    return amber


def components(mask, w, h, min_cells=1):
    seen = [[False] * w for _ in range(h)]
    out = []
    for y in range(h):
        for x in range(w):
            if mask[y][x] and not seen[y][x]:
                q = deque([(x, y)])
                seen[y][x] = True
                cells = []
                while q:
                    cx, cy = q.popleft()
                    cells.append((cx, cy))
                    for nx, ny in ((cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1)):
                        if 0 <= nx < w and 0 <= ny < h and mask[ny][nx] and not seen[ny][nx]:
                            seen[ny][nx] = True
                            q.append((nx, ny))
                if len(cells) >= min_cells:
                    out.append(cells)
    return out


def _walk_at(walk, w, h, x, y):
    return 0 <= x < w and 0 <= y < h and walk[y][x]


def find_doors(amber, walk, w, h, no_door):
    """
    Short, thin amber bars that sit BETWEEN two walkable areas = doors (a window trim or lamp
    does not have floor on both sides of it). Returns rectangles (px) covering the doorway.
    """
    doors = []
    for cells in components(amber, w, h):
        xs = [c[0] for c in cells]
        ys = [c[1] for c in cells]
        cw, ch = max(xs) - min(xs) + 1, max(ys) - min(ys) + 1
        cxm, cym = (min(xs) + max(xs)) // 2, (min(ys) + max(ys)) // 2
        rect = None
        if ch >= 2 and cw <= 2 and ch <= 9:                      # vertical bar in a vertical wall
            if any(_walk_at(walk, w, h, cxm - d, cym) for d in (3, 4)) and any(_walk_at(walk, w, h, cxm + d, cym) for d in (3, 4)):
                rect = [min(xs) * CELL - 20, min(ys) * CELL - 4, cw * CELL + 40, ch * CELL + 8]
        elif cw >= 2 and ch <= 2 and cw <= 9:                    # horizontal bar in a horizontal wall
            if any(_walk_at(walk, w, h, cxm, cym - d) for d in (3, 4)) and any(_walk_at(walk, w, h, cxm, cym + d) for d in (3, 4)):
                rect = [min(xs) * CELL - 4, min(ys) * CELL - 20, cw * CELL + 8, ch * CELL + 40]
        if rect is None:
            continue
        cx, cy = rect[0] + rect[2] / 2, rect[1] + rect[3] / 2
        if any(r[0] <= cx <= r[0] + r[2] and r[1] <= cy <= r[1] + r[3] for r in no_door):
            continue
        doors.append(rect)
    return doors


def shape_ok(cells, min_short=6):
    """Real rooms and halls are fat blobs; thin dark bands (wall faces, sills, trim) are not walkable space."""
    xs = [c[0] for c in cells]
    ys = [c[1] for c in cells]
    return min(max(xs) - min(xs) + 1, max(ys) - min(ys) + 1) >= min_short


def clear_patch(reach, w, h, x, y, dx, dy, half, depth):
    """True if a (2*half+1) wide, `depth` deep patch starting at (x, y) heading (dx, dy) is all walkable."""
    for step in range(depth):
        for off in range(-half, half + 1):
            gx = x + dx * step + (off if dx == 0 else 0)
            gy = y + dy * step + (off if dy == 0 else 0)
            if not (0 <= gx < w and 0 <= gy < h and reach[gy][gx]):
                return False
    return True


def connect_rooms(reach, amber, w, h, config):
    """
    Every enclosed room (a walkable area not connected to the main hall) gets one doorway: the shortest
    straight cut through its wall to an already-connected area, preferring cuts near a drawn gold door bar.
    Returns the list of carved rectangles (px).
    """
    bars = []
    for cells in components(amber, w, h):
        xs = [c[0] for c in cells]
        ys = [c[1] for c in cells]
        if max(max(xs) - min(xs), max(ys) - min(ys)) <= 8:
            bars.append(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2))

    carved = []
    skip = config.get("no_connect", [])
    max_depth = config.get("max_wall", 5)
    half = config.get("door_cells", 3)

    for _ in range(40):
        comps = components(reach, w, h)
        if len(comps) <= 1:
            break
        label = {}
        for i, cells in enumerate(comps):
            for c in cells:
                label[c] = i
        main = max(range(len(comps)), key=lambda i: len(comps[i]))
        # grow the connected set: start with main; any other component with a valid tunnel to it
        best = None
        for i, cells in enumerate(comps):
            if i == main or len(cells) < config.get("min_room", 150) or not shape_ok(cells):
                continue
            cx = sum(c[0] for c in cells) / len(cells) * CELL
            cy = sum(c[1] for c in cells) / len(cells) * CELL
            if any(r[0] <= cx <= r[0] + r[2] and r[1] <= cy <= r[1] + r[3] for r in skip):
                continue
            for (x, y) in cells:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    k = 1
                    while k <= max_depth:
                        nx, ny = x + dx * k, y + dy * k
                        if not (0 <= nx < w and 0 <= ny < h):
                            break
                        if reach[ny][nx]:
                            # the passage must be usable: free floor 3 cells deep on both sides of the cut
                            room_side = clear_patch(reach, w, h, x - dx * 2, y - dy * 2, dx, dy, half, 3)
                            hall_side = clear_patch(reach, w, h, nx, ny, dx, dy, half, 3)
                            if label.get((nx, ny)) == main and k >= 2 and room_side and hall_side:
                                mx, my = x + dx * (k / 2), y + dy * (k / 2)
                                bar_d = min((abs(mx - bx) + abs(my - by) for bx, by in bars), default=99)
                                score = k * 6 + min(bar_d, 40) * 1.0
                                if best is None or score < best[0]:
                                    best = (score, i, x, y, dx, dy, k)
                            break
                        k += 1
        if best is None:
            break
        _, i, x, y, dx, dy, k = best
        # carve a strip of width 2*half+1 cells across the wall
        cells_out = []
        for step in range(0, k + 1):
            for off in range(-half, half + 1):
                gx = x + dx * step + (off if dx == 0 else 0)
                gy = y + dy * step + (off if dy == 0 else 0)
                if 0 <= gx < w and 0 <= gy < h:
                    reach[gy][gx] = True
                    cells_out.append((gx, gy))
        xs = [c[0] for c in cells_out]
        ys = [c[1] for c in cells_out]
        carved.append([min(xs) * CELL, min(ys) * CELL, (max(xs) - min(xs) + 1) * CELL, (max(ys) - min(ys) + 1) * CELL])
    # report rooms that stay sealed
    comps = components(reach, w, h)
    if comps:
        main = max(range(len(comps)), key=lambda i: len(comps[i]))
        for i, cells in enumerate(comps):
            if i != main and len(cells) >= config.get("min_room", 150):
                cx = int(sum(c[0] for c in cells) / len(cells) * CELL)
                cy = int(sum(c[1] for c in cells) / len(cells) * CELL)
                print(f"  sealed room (area {len(cells)} cells) around {cx},{cy}")
    return carved


def paint(grid, rect, value, w, h):
    x0, y0 = max(0, int(rect[0]) // CELL), max(0, int(rect[1]) // CELL)
    x1, y1 = min(w, (int(rect[0] + rect[2]) + CELL - 1) // CELL), min(h, (int(rect[1] + rect[3]) + CELL - 1) // CELL)
    for y in range(y0, y1):
        for x in range(x0, x1):
            grid[y][x] = value


def smooth(reach, w, h):
    out = [row[:] for row in reach]
    for y in range(1, h - 1):
        for x in range(1, w - 1):
            n = sum(reach[y + dy][x + dx] for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
            if not reach[y][x] and n >= 3:
                out[y][x] = True
            elif reach[y][x] and n <= 1:
                out[y][x] = False
    return out


def obstacle_rects(reach, w, h):
    solid = [[not reach[y][x] for x in range(w)] for y in range(h)]
    rects, open_runs = [], {}
    for y in range(h + 1):
        runs = set()
        if y < h:
            x = 0
            while x < w:
                if solid[y][x]:
                    x0 = x
                    while x < w and solid[y][x]:
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
    return [[x * CELL, y * CELL, rw * CELL, rh * CELL] for x, y, rw, rh in rects]


def main():
    src, out_json, out_prev = sys.argv[1:4]
    config = json.load(open(sys.argv[4])) if len(sys.argv) > 4 else {}
    img = Image.open(src)
    small = cell_colors(img)
    w, h = small.size

    if "walk" in config:
        # HAND-PLACED MODE: walkable = listed floor rectangles minus listed furniture rectangles.
        reach = [[False] * w for _ in range(h)]
        for r in config["walk"]:
            paint(reach, r, True, w, h)
        for r in config.get("solid", []):
            paint(reach, r, False, w, h)
        write_outputs(img, reach, w, h, [], out_json, out_prev, config)
        return

    walk = classify(small)
    reach = [[False] * w for _ in range(h)]
    thin = 0
    for cells in components(walk, w, h, config.get("min_area", 120)):
        if not shape_ok(cells, config.get("min_short", 6)):
            thin += 1
            continue
        for x, y in cells:
            reach[y][x] = True
    print(f"  dropped {thin} thin wall-band regions")

    doors = find_doors(amber_cells(img), walk, w, h, config.get("no_door", [])) if config.get("auto_doors") else []
    for d in doors + config.get("open", []):
        paint(reach, d, True, w, h)
    for b in config.get("blocked", []):
        paint(reach, b, False, w, h)
    reach = smooth(reach, w, h)
    if config.get("auto_connect", True):
        doors = doors + connect_rooms(reach, amber_cells(img), w, h, config)
        for b in config.get("blocked_after", []):
            paint(reach, b, False, w, h)

    write_outputs(img, reach, w, h, doors, out_json, out_prev, config)


def write_outputs(img, reach, w, h, doors, out_json, out_prev, config):
    rects = obstacle_rects(reach, w, h)
    with open(out_json, "w") as f:
        # flat list [x, y, w, h, x, y, w, h, ...] so Unity's JsonUtility can read it
        json.dump({"cell": CELL, "width": img.size[0], "height": img.size[1], "rects": [v for r in rects for v in r]}, f)
    print(f"{out_json}: {len(rects)} obstacle rects, {len(doors)} auto doors")

    prev = img.convert("RGB").copy()
    px = prev.load()
    for y in range(h):
        for x in range(w):
            if not reach[y][x]:
                for dy in range(CELL):
                    for dx in range(CELL):
                        r, g, b = px[x * CELL + dx, y * CELL + dy]
                        px[x * CELL + dx, y * CELL + dy] = (min(255, r // 2 + 140), g // 3, b // 3)
    for d in doors:
        for x in range(int(d[0]), int(d[0] + d[2])):
            for y in (int(d[1]), int(d[1] + d[3]) - 1):
                if 0 <= x < img.size[0] and 0 <= y < img.size[1]:
                    px[x, y] = (40, 120, 255)
        for y in range(int(d[1]), int(d[1] + d[3])):
            for x in (int(d[0]), int(d[0] + d[2]) - 1):
                if 0 <= x < img.size[0] and 0 <= y < img.size[1]:
                    px[x, y] = (40, 120, 255)
    prev.save(out_prev)


if __name__ == "__main__":
    main()
