"""
Floor 3 (Executive Offices) puzzle data. Run: python tools/puzzles_floor3.py
Writes TheUnlikelyProgrammer/Assets/_Project/Data/Puzzles/f3_*.json.
Expected rows are computed here from each puzzle's intended answer, so data and answer can't drift.

Today's server room code is split across the floor's computers. Each green computer serves ONE table to the network:
    boardroom, long table   -> staff       (staff_id, name, title)
    boardroom, side table   -> rota        (rota_day, holder)              holder -> staff.staff_id
    open office, left desk  -> leave       (leave_id, on_leave, leave_day, cover)   on_leave, cover -> staff.staff_id
    open office, right desk -> badges      (badge_no, owner, status)       owner -> staff.staff_id
    executive office B      -> code_parts  (part_id, card, position, cipher)   card -> badges.badge_no
A computer only sees its own table plus the one machine the link station (Executive office A) points it at.
The key map (the pen drive, printed in HR's printer room) says which column points where.

Three joins, three computers:
    1  rota + staff        whose code is it today (Tuesday)?      Priya Shah, 118
    2  leave + staff       Priya's off; who covers?               Marcus Hale, 205
    3  code_parts + badges Marcus's active badge: the 4 parts      V Q 3 L  (encrypted; decrypts to 7302)
Each puzzle file carries every table; Floor3Story hands the computer only its own + the linked one.

The employee terminal (reception desk) teaches the server room's SQL: INSERT INTO ... VALUES, INSERT INTO ... SELECT, DELETE.
"""
import json, os

OUT = os.path.join(os.path.dirname(__file__), "..", "TheUnlikelyProgrammer", "Assets", "_Project", "Data", "Puzzles")


def write(pid, title, brief, success, tables, expected, user="", order_matters=False, lessons="", clues=None,
          needs_flag="", brief_missing="", reward=None):
    d = {"id": pid, "title": title, "brief": brief, "successMessage": success, "rewardFlag": reward or pid + "_solved",
         "orderMatters": order_matters, "unlocksLessons": lessons, "note": "", "user": user,
         "needsFlag": needs_flag, "briefMissing": brief_missing, "clues": clues or [],
         "tables": [{"name": n, "columns": c, "rows": [{"cells": [str(x) for x in r]} for r in rows]} for n, c, rows in tables],
         "expectedRows": [{"cells": [str(x) for x in r]} for r in expected]}
    with open(os.path.join(OUT, pid + ".json"), "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1, ensure_ascii=False)
    assert expected, pid + " has no expected rows"
    return len(expected)


n = 0

# ---------------------------------------------------------------- the five tables
STAFF = ["staff_id", "name", "title"]
staff = [
    [101, "Helena Voss", "Chief Financial Officer"],
    [104, "Graham Pell", "Head of Legal"],
    [109, "Venky Eleven", "Chief Operating Officer"],
    [112, "Rupert Lane", "Chief of Staff"],
    [118, "Priya Shah", "Head of Security Systems"],
    [123, "Dev Okafor", "Executive Assistant"],
    [205, "Marcus Hale", "Network Manager"],
    [207, "Nina Brandt", "IT Director"],
    [210, "Adhil B", "Facilities Director"],
    [214, "Tess Morrow", "Investor Relations"],
]
ROTA = ["rota_day", "holder"]
rota = [["Monday", 207], ["Tuesday", 118], ["Wednesday", 101], ["Thursday", 205], ["Friday", 109], ["Saturday", 210], ["Sunday", 123]]
LEAVE = ["leave_id", "on_leave", "leave_day", "cover"]
leave = [
    ["L-31", 101, "Friday", 104],
    ["L-32", 109, "Tuesday", 214],
    ["L-33", 118, "Tuesday", 205],
    ["L-34", 207, "Wednesday", 118],
    ["L-35", 123, "Tuesday", 112],
    ["L-36", 118, "Thursday", 207],
]
BADGES = ["badge_no", "owner", "status"]
badges = [
    ["B-0150", 104, "active"], ["B-0129", 214, "revoked"], ["B-0290", 101, "active"], ["B-0333", 109, "active"],
    ["B-0355", 112, "active"], ["B-0388", 207, "active"], ["B-0412", 123, "active"], ["B-0441", 118, "active"],
    ["B-0502", 205, "revoked"], ["B-0601", 210, "active"], ["B-0617", 205, "active"], ["B-0702", 214, "active"],
]
PARTS = ["part_id", "card", "position", "cipher"]
code = {"B-0617": "VQ3L", "B-0502": "K8WR", "B-0441": "H2XT", "B-0388": "M5PA", "B-0290": "J7NE"}
parts, pid_n = [], 7101
order = [3, 1, 4, 2]   # stored out of order, so ORDER BY matters
for card, word in code.items():
    for pos in order:
        parts.append([f"P-{pid_n}", card, pos, word[pos - 1]])
        pid_n += 7
ALL = [("staff", STAFF, staff), ("rota", ROTA, rota), ("leave", LEAVE, leave), ("badges", BADGES, badges), ("code_parts", PARTS, parts)]

KEYMAP = ("Key map (printout): rota.holder, leave.on_leave, leave.cover and badges.owner all hold a staff_id (they point at "
          "staff.staff_id). code_parts.card holds a badge_no (it points at badges.badge_no).")
clues = [{"flag": "f3_keymap", "text": KEYMAP}]
NEED_KEYMAP = ("\n\nFIRST: I can see the tables, but not which column connects to which. I need the key map. "
               "There was a pen drive taped under the desk drawer in Executive office A. Nothing up here prints; "
               "the printer room on HR (Floor 2) does.")

# 1: whose day is it?
sid = {s[0]: s for s in staff}
holder = [r for r in rota if r[0] == "Tuesday"][0][1]
n += write("f3_p1_rota", "Task 1 of 3 · Whose code is it today?",
    "TASK 1 of 3 · Whose code is it today?\n"
    "The server room code belongs to a different person every day. This computer's table, rota, says who: "
    "holder is that person's staff_id. The names are in staff, on the Boardroom PC (long table).\n\n"
    "HOW\n1. Link station (Executive office A): make THIS computer read from the Boardroom PC (long table).\n"
    "2. Here: JOIN rota to staff, matching the columns the key map gives.\n\n"
    "SHOW: staff_id and name of the holder on 'Tuesday' (today).",
    f"{sid[holder][1]}, staff {holder}. Tuesday's code is hers.",
    ALL, [[holder, sid[holder][1]]], user="exec.board.2", clues=clues,
    needs_flag="f3_keymap", brief_missing="TASK 1 of 3 · Whose code is it today?\nThis computer holds the server room code rota." + NEED_KEYMAP)

# 2: she's off; who covers?
cover = [l for l in leave if l[1] == holder and l[2] == "Tuesday"][0][3]
n += write("f3_p2_cover", "Task 2 of 3 · Who's covering?",
    f"TASK 2 of 3 · Who's covering?\n"
    f"{sid[holder][1]} (staff {holder}) holds today's code, but she's on leave, so whoever covers her holds it. "
    "This computer's table, leave: on_leave = who is away, leave_day = which day, cover = who covers. "
    "Both are staff_ids; the names are in staff, on the Boardroom PC (long table).\n\n"
    "HOW\n1. Link station: make THIS computer read from the Boardroom PC (long table).\n"
    "2. Here: JOIN leave to staff on the COVER column (not on_leave).\n\n"
    f"SHOW: staff_id and name of whoever covers staff {holder} on 'Tuesday'.",
    f"{sid[cover][1]}, staff {cover}. He's covering. So it's his code.",
    ALL, [[cover, sid[cover][1]]], user="exec.assist.1", clues=clues,
    needs_flag="f3_ready_p2",
    brief_missing="TASK 2 of 3 · Who's covering?\nThe leave register. Nothing to look up yet: TASK 1 (Boardroom PC, side table) says whose code it is today."
                  "\n(And the key map has to be printed.)")

# 3: the four parts of his code
cards = {b[0] for b in badges if b[1] == cover and b[2] == "active"}
expected = [[p[2], p[3]] for p in sorted(parts, key=lambda p: p[2]) if p[1] in cards]
assert "".join(r[1] for r in expected) == "VQ3L", expected
n += write("f3_p3_code", "Task 3 of 3 · The code",
    f"TASK 3 of 3 · The code\n"
    f"{sid[cover][1]} (staff {cover}) holds today's code. It's stored in 4 parts in this computer's table, code_parts, filed under a "
    "BADGE number (card), not a person. Which badge is his? That's in badges, on the Open office PC (right desk). "
    "He has two badges: an old 'revoked' one and an 'active' one. Only the active one counts.\n\n"
    "HOW\n1. Link station: make THIS computer read from the Open office PC (right desk).\n"
    "2. Here: JOIN code_parts to badges (card = badge_no).\n\n"
    f"SHOW: position and cipher of the parts on staff {cover}'s ACTIVE badge, ORDER BY position.",
    "V, Q, 3, L. Encrypted. The link station decrypts it, with an executive keycard.",
    ALL, expected, user="exec.office.b", order_matters=True, clues=clues,
    needs_flag="f3_ready_p3",
    brief_missing="TASK 3 of 3 · The code\nCode parts, filed by badge. Nothing to look up yet: TASKS 1 and 2 say who holds today's code."
                  "\n(And the key map has to be printed.)")

# ---------------------------------------------------------------- employee terminal: the server room's lessons
VIS = ["id", "name", "status"]
vis = [[1, "Ada Fenn", "in"], [2, "Rob Sato", "left"], [3, "Mia Holt", "in"], [4, "Ivo Marsh", "left"]]
ARC = ["id", "name"]
arc = [[90, "Old Visitor"]]


def tut(i, title, brief, expected, lessons, success="OK.", order=False, tables=None):
    write(f"f3_tut_{i}", title, brief, success, tables or [("visitors", VIS, vis), ("archive", ARC, arc)], expected,
          user="maint-0009", order_matters=order, lessons=lessons, reward=f"f3_tut_{i}_done")


tut(1, "Lesson 1 · INSERT",
    "Reading is over. This one WRITES.\n"
    "INSERT INTO table (columns) VALUES (values) adds a row. Text in quotes, numbers without, same order as the columns.\n\n"
    "Try it: add visitor 5, 'Sam Ortiz', status 'in' to visitors.",
    vis + [[5, "Sam Ortiz", "in"]], "insert")
tut(2, "Lesson 2 · INSERT ... SELECT",
    "No need to type every value: a SELECT can find the rows to copy.\n"
    "INSERT INTO archive (id, name) SELECT id, name FROM visitors WHERE ...\n\n"
    "Try it: copy the id and name of every visitor whose status is 'left' into archive.",
    arc + [[v[0], v[1]] for v in vis if v[2] == "left"], "insertselect")
tut(3, "Lesson 3 · DELETE",
    "DELETE FROM table WHERE ... removes rows. There is no undo. Without WHERE it removes EVERY row.\n"
    "KODEX strongly recommends never running that unless you know exactly what you're doing. (It's on a poster somewhere.)\n\n"
    "Try it: delete every visitor whose status is 'left'.",
    [v for v in vis if v[2] != "left"], "delete",
    success="KODEX: Writing data, done. You won't need it on this floor. Where you're going, you might.")

print("floor 3 puzzles written; expected rows:", n)
