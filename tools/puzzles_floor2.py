"""
Floor 2 (Human Resources) puzzle data. Run: python tools/puzzles_floor2.py
Writes TheUnlikelyProgrammer/Assets/_Project/Data/Puzzles/f2_*.json.
Expected rows are computed here from each puzzle's intended answer, so data and answer can't drift.
The floor's computers need Floor 1's SQL (LIKE, IN, NOT, ORDER BY, GROUP BY + COUNT) on top of SELECT/WHERE/AND/OR.
The employee terminal teaches Floor 3's: keys, JOIN ... ON, aliases, LEFT JOIN, LIMIT.
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


def pick(columns, rows, where, select=None, order=None):
    sel = [r for r in rows if where(dict(zip(columns, r)))]
    if order:
        sel = sorted(sel, key=lambda r: order(dict(zip(columns, r))))
    cols = select or columns
    return [[r[columns.index(c)] for c in cols] for r in sel]


n = 0

# ---------------------------------------------------------------- HR open office: four tables, four computers
# t1 (compulsory): where Northgate is. Needs the name from the projector video.
S = ["site", "name", "address", "type"]
sites = [
    ["S-01", "GyaraCorp Tower", "1 Gyara Plaza, City Centre", "Head office"],
    ["S-02", "Northgate Power Plant", "Unit 9, Northgate Industrial Estate, Riverside Road", "Power generation"],
    ["S-03", "Northgate Car Park", "Northgate Road (level 2)", "Parking"],
    ["S-04", "Eastfield Data Store", "14 Kiln Lane, Eastfield", "Archive"],
    ["S-05", "Southbank Canteen Supplies", "Arch 6, Southbank", "Catering"],
]
n += write("f2_t1_northgate", "Site register",
    "KODEX: Plant Room 2B is the Northgate plant (thanks, onboarding video). Where is it, exactly?\n\n"
    "Show site, name and address for sites whose name contains 'Northgate' and whose type is 'Power generation'.",
    "Northgate Power Plant: Unit 9, Northgate Industrial Estate, Riverside Road. Plant Room 2B is across town.",
    [("sites", S, sites)],
    pick(S, sites, lambda r: "Northgate" in r["name"] and r["type"] == "Power generation", ["site", "name", "address"]),
    user="hot.desk.1", needs_flag="f2_clue_video",
    brief_missing="KODEX: The facilities site register. 'Plant Room 2B' isn't listed by that name: it's a nickname.\n\n"
                  "Somebody on this floor must have told the new starters what it's really called.")

# t2 (compulsory): the breaker room can switch it remotely
C = ["ref", "system", "requested", "summary"]
changes = [
    ["CR-31", "Main power", "2024-02-11", "Building switched from the city grid to Northgate (Plant Room 2B)."],
    ["CR-44", "Lifts", "2024-03-02", "Lift 2 out of service. Still."],
    ["CR-52", "Northgate", "2024-06-19", "Remote activation link installed: PLANT-2B line to Floor 2."],
    ["CR-57", "Lawn", "2024-07-01", "Replant the G. It looks like a 6."],
    ["CR-63", "Main power", "2025-01-08", "Whole-building switch-over now done remotely from the Floor 2 breaker panel."],
    ["CR-70", "Coffee", "2025-02-14", "Do not put soup in the coffee machine."],
]
n += write("f2_t2_remote", "Change requests",
    "KODEX: If the power comes from across town, how is the building switched over to it?\n\n"
    "Show ref, requested and summary for change requests whose system is 'Main power' or 'Northgate' (use IN), oldest first.",
    "'Whole-building switch-over now done remotely from the Floor 2 breaker panel.'",
    [("change_requests", C, changes)],
    pick(C, changes, lambda r: r["system"] in ("Main power", "Northgate"), ["ref", "requested", "summary"], order=lambda r: r["requested"]),
    order_matters=True, user="hot.desk.2")

# t3: the cleaning rota: some plants need a 'special clean' (things are hidden in them)
P = ["area", "item", "task", "notes"]
rota = [
    ["Lobby", "Sofas", "Vacuum", "Candidates leave crumbs. And CVs."],
    ["Conference room", "Plant (left of the screen)", "Special clean", "Do NOT tip out the soil. Sift it. Staff keep 'emergency' coins in this one."],
    ["Conference room", "Table", "Polish", "Fingerprints from the 9am."],
    ["Lobby", "Lawn G", "Trim", "It still looks like a 6."],
    ["Long hallway", "Plants", "Water", "Weekly."],
    ["Head of HR's door", "Brass plate", "Special clean", "He checks it with a white glove."],
    ["Printer room", "Floor", "Sweep", "Forty copies of the same page. Recycle."],
]
n += write("f2_t3_plants", "Cleaning rota",
    "KODEX: Facilities' cleaning rota. You're the janitor; this is technically your job.\n\n"
    "Show area, item and notes for every task that starts with 'Special' (use LIKE).",
    "The conference room plant needs a 'special clean': staff hide coins in the soil. Somebody's going to need a janitor.",
    [("cleaning_rota", P, rota)],
    pick(P, rota, lambda r: r["task"].startswith("Special"), ["area", "item", "notes"]), user="hot.desk.3")

# t4: the interview room's lock code (it locks from the outside)
K = ["room", "code", "lock_side", "status"]
codes = [
    ["Interview room", "5150", "outside", "expired"],
    ["Interview room", "7341", "outside", "active"],
    ["Breaker room", "----", "inside", "login only"],
    ["Conference room", "2020", "inside", "active"],
    ["Head of HR's office", "0001", "inside", "active"],
]
n += write("f2_t4_locks", "Door locks",
    "KODEX: HR's door lock list. The interview room locks from the outside, so nobody walks out of a tough interview.\n\n"
    "Show room and code for the 'Interview room' whose status is NOT 'expired'.",
    "Interview room lock code: 7341. It locks from the outside. That could be very useful.",
    [("door_locks", K, codes)],
    pick(K, codes, lambda r: r["room"] == "Interview room" and r["status"] != "expired", ["room", "code"]), user="hot.desk.4")

# ---------------------------------------------------------------- interview room: the restructure file (the leverage)
R = ["name", "department", "decision", "effective", "notes"]
restructure = [
    ["Sofia Mendes", "Human Resources", "keep", "-", "Runs the interviews. Keep her busy."],
    ["Bram Kowal", "People & Facilities", "sack", "Today, 12:00", "Switch off his login at noon. Don't tell him until his exit meeting."],
    ["Everyone on Floor 1", "Administration", "sack", "Next summer", "140 jobs replaced by a cheap call centre. The board has NOT been told."],
    ["Staff pension savings", "Finance", "spend", "Already done", "Staff retirement money used to pay for the Northgate plant. The auditors have NOT been told."],
    ["Janitors", "People & Facilities", "replace", "Next winter", "Cleaning robots. Trial run on Floor 3."],
    ["Marta Quinn", "Reception", "keep", "-", "Knows where everything is. Do not lose her."],
    ["Today's job candidates", "Human Resources", "reject", "Today", "The jobs already went to friends. Do the interviews anyway, it looks good."],
]
n += write("f2_iv_restructure", "Restructure (CONFIDENTIAL)",
    "KODEX: The interviewer left her screen unlocked. A file called 'restructure'.\n\n"
    "Show name, decision and notes for every row whose decision is NOT 'keep'.",
    "Bram Kowal's login is switched off at noon. And a lot of things the board would love to read.",
    [("restructure", R, restructure)],
    pick(R, restructure, lambda r: r["decision"] != "keep", ["name", "decision", "notes"]), user="s.mendes")

# ---------------------------------------------------------------- reception: the duty logins (needs every clue)
D = ["name", "department", "role", "shift", "start_year", "status", "username", "password"]
logins = [
    ["Bram Kowal", "People & Facilities", "Electrical Engineer", "Night", 2014, "active", "b.kowal", "Sparky14!"],
    ["Ines Vidal", "People & Facilities", "Plant Engineer", "Night", 2014, "active", "i.vidal", "Northgate-0914"],
    ["Naveen", "People & Facilities", "Plant Engineer", "Day", 2014, "active", "naveen", "naveen2014"],
    ["Rhea Patel", "People & Facilities", "Electrical Engineer", "Night", 2016, "active", "r.patel", "Voltage!16"],
    ["Tom Ashby", "People & Facilities", "Plant Engineer", "Night", 2014, "suspended", "t.ashby", "letmein"],
    ["Gus Ferro", "People & Facilities", "Plant Technician", "Night", 2014, "active", "g.ferro", "Gus4ever"],
    ["Sofia Mendes", "Human Resources", "HR Officer", "Day", 2017, "active", "s.mendes", "Hire-Me-17"],
    ["Nadia Farouk", "Security", "Guard", "Night", 2022, "active", "n.farouk", "nightwatch"],
    ["Aman L", "Security", "Guard", "Night", 2018, "active", "aman.l", "aman9000"],
    ["Marta Quinn", "Reception", "Receptionist", "Day", 2019, "active", "m.quinn", "Front-Desk-1"],
    ["Leo Brandt", "People & Facilities", "Lift Engineer", "Late", 2014, "active", "l.brandt", "UpAndDown"],
    ["Dev Sharma", "People & Facilities", "Network Engineer", "Night", 2015, "left", "d.sharma", "packets"],
    ["Owen Pike", "Executive Office", "Systems Engineer", "Day", 2014, "active", "o.pike", "Exec-Owen"],
]
clue_texts = [
    ("f2_clue_printer", "Printer room (jammed policy page): the plant switch only takes a login whose role ends in 'Engineer'."),
    ("f2_clue_video", "The onboarding video: Northgate was built in 2014 by the Night operations team, who still switch it. Night shift, started 2014."),
    ("f2_clue_candidate", "The candidate on the sofa: HR said half the logins on that list are dead. Only the 'active' ones work."),
    ("f2_clue_interview", "Interview room file: Bram Kowal is being sacked today. His login dies at noon. Not him."),
]
expected = pick(D, logins, lambda r: r["role"].endswith("Engineer") and r["start_year"] == 2014 and r["shift"] == "Night"
                and r["status"] == "active" and r["name"] != "Bram Kowal", ["username", "password"])
assert len(expected) == 1, expected
n += write("f2_reception_logins", "Duty logins",
    "KODEX: The breaker panel wants an authorised login. Reception keeps the duty list: every login on this floor, most of them useless.\n\n"
    "Find the ONE login that can switch the plant and show its username and password.\n"
    "Every clue I find is a condition. Combine them all with AND (LIKE and NOT will help).",
    "i.vidal / Northgate-0914. Ines Vidal, plant engineer, night crew since 2014. Sorry, Ines.",
    [("duty_logins", D, logins)], expected, user="reception",
    clues=[{"flag": f, "text": t} for f, t in clue_texts])

# ---------------------------------------------------------------- employee terminal: Floor 3's lessons
EMP = ["id", "name", "dept_id"]
DEP = ["id", "dept_name", "floor"]
emp = [[1, "Sofia Mendes", 10], [2, "Ines Vidal", 20], [3, "Marta Quinn", 30], [4, "Owen Pike", 40], [5, "Bram Kowal", 20], [6, "New Starter", "NULL"]]
dep = [[10, "Human Resources", 2], [20, "People & Facilities", 2], [30, "Reception", 2], [40, "Executive Office", 3], [50, "Server Operations", 3]]
tabs = [("employees", EMP, emp), ("departments", DEP, dep)]
dmap = {d[0]: d for d in dep}


def tut(i, title, brief, expected, lessons, success="OK.", order=False, tables=tabs):
    write(f"f2_tut_{i}", title, brief, success, tables, expected, user="maint-0009", order_matters=order, lessons=lessons,
          reward=f"f2_tut_{i}_done")


tut(1, "Lesson 1 · Keys",
    "Two tables. employees.dept_id holds a number that points at departments.id. That pointer is a FOREIGN KEY; "
    "departments.id, unique for every row, is the PRIMARY KEY.\n\n"
    "Try it: show the name of every employee whose dept_id is 20 (whatever department that is).",
    [[e[1]] for e in emp if e[2] == 20], "keys")
tut(2, "Lesson 2 · JOIN",
    "JOIN glues two tables together, row by row, wherever the keys match:\n"
    "SELECT ... FROM employees JOIN departments ON employees.dept_id = departments.id\n"
    "Write table.column when both tables have a column with that name.\n\n"
    "Try it: show each employee's name with their dept_name.",
    [[e[1], dmap[e[2]][1]] for e in emp if e[2] in dmap], "join")
tut(3, "Lesson 3 · Aliases",
    "Long table names get tiring. Give them short names after FROM and JOIN:\n"
    "FROM employees e JOIN departments d ON e.dept_id = d.id\n\n"
    "Try it: show the name of every employee whose department is on floor 3 (use the aliases in WHERE).",
    [[e[1]] for e in emp if e[2] in dmap and dmap[e[2]][2] == 3], "alias")
tut(4, "Lesson 4 · LEFT JOIN",
    "JOIN drops rows with no match. LEFT JOIN keeps every row of the first table, with NULL where nothing matched.\n\n"
    "Try it: show every department's dept_name with the name of anyone in it, including empty departments.\n"
    "(FROM departments LEFT JOIN employees ...)",
    [[d[1], e[1]] for d in dep for e in emp if e[2] == d[0]] + [[d[1], "NULL"] for d in dep if not any(e[2] == d[0] for e in emp)],
    "leftjoin")
tut(5, "Lesson 5 · LIMIT",
    "LIMIT n stops after n rows. With ORDER BY it gives you 'the top n': ORDER BY floor DESC LIMIT 1\n\n"
    "Try it: show the dept_name of the two departments with the highest id.",
    [[d[1]] for d in sorted(dep, key=lambda d: -d[0])[:2]], "limit", order=True,
    success="KODEX: Floor 3 basics, done. The Executive floor is next.")

print("floor 2 puzzles written; expected rows:", n)
