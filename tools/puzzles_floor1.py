"""
Floor 1 (Administration) puzzle data. Run: python tools/puzzles_floor1.py
Writes TheUnlikelyProgrammer/Assets/_Project/Data/Puzzles/f1_*.json.
Each puzzle's expected rows are computed from its intended solution here, so data and answer can't drift.
The SQL used: SELECT / FROM / WHERE / AND / OR (Floor 0's lessons). The terminal lessons use LIKE, IN, NOT, ORDER BY, GROUP BY.
"""
import json, os, re

OUT = os.path.join(os.path.dirname(__file__), "..", "TheUnlikelyProgrammer", "Assets", "_Project", "Data", "Puzzles")


def puzzle(pid, title, brief, success, table, columns, rows, where, select=None, user="", order_matters=False, lessons="", note="",
           needs_flag="", brief_missing=""):
    """where: python predicate over a row dict; select: list of columns (None = all)."""
    cols = select or columns
    exp = [[r[columns.index(c)] for c in cols] for r in rows if where(dict(zip(columns, r)))]
    d = {"id": pid, "title": title, "brief": brief, "successMessage": success, "rewardFlag": pid + "_solved",
         "orderMatters": order_matters, "unlocksLessons": lessons, "note": note, "user": user,
         "needsFlag": needs_flag, "briefMissing": brief_missing,
         "tables": [{"name": table, "columns": columns, "rows": [{"cells": [str(c) for c in r]} for r in rows]}],
         "expectedRows": [{"cells": [str(c) for c in r]} for r in exp]}
    with open(os.path.join(OUT, pid + ".json"), "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1, ensure_ascii=False)
    return len(exp)


# ---------------------------------------------------------------- the floor's computers

n = puzzle("f1_sw_bookings", "The meeting room",
    "KODEX: The employee terminal on this floor is in the meeting room. Is anyone using it?\n\n"
    "Show every column for bookings of the 'Meeting room' on 'Tuesday'.",
    "Linda and Ken have the meeting room all day. The terminal's in there. So that's a problem.",
    "room_bookings", ["room", "day", "booked_by", "purpose"], [
        ["Meeting room", "Tuesday", "Linda Park", "Quarterly review (all day)"],
        ["Meeting room", "Tuesday", "Ken Watts", "Quarterly review (all day)"],
        ["Meeting room", "Monday", "Joey Marsh", "Minutes training"],
        ["Meeting room", "Friday", "Facilities", "Plant room walkthrough - MOVED to HR"],
        ["Office A", "Tuesday", "Grace Lee", "Off sick"],
        ["Lounge", "Tuesday", "Joey Marsh", "Working from the lounge"],
        ["Office F", "Tuesday", "Tomas Reyes", "Supplier calls"],
    ], lambda r: r["room"] == "Meeting room" and r["day"] == "Tuesday", user="shared.desk.sw")

n += puzzle("f1_se_tickets", "Anything electrical?",
    "KODEX: They want to know who runs the building's power. The helpdesk queue is a start.\n\n"
    "Show every column for tickets on floor 1 with category 'Electrical' that are still 'Open'.",
    "Ticket T-114: flickering lights in Office E. The electrician's door code was left in the notes: 2468.",
    "helpdesk", ["ticket", "floor", "room", "category", "status", "notes"], [
        ["T-101", 1, "Lounge", "Plumbing", "Open", "Coffee machine leaking. Again."],
        ["T-108", 1, "Office B", "Electrical", "Closed", "Desk lamp. It was unplugged."],
        ["T-114", 1, "Office E", "Electrical", "Open", "Lights flicker. Electrician access code for Office E: 2468. Escalated to People & Facilities (Floor 2)."],
        ["T-117", 2, "Plant room 2B", "Electrical", "Open", "Generator test overdue."],
        ["T-120", 1, "Office D", "IT", "Open", "Printer 'possessed'."],
        ["T-121", 1, "Washroom", "Plumbing", "Open", "Joey."],
    ], lambda r: r["floor"] == 1 and r["category"] == "Electrical" and r["status"] == "Open", user="shared.desk.se")

n += puzzle("f1_a_doorcodes", "Office B",
    "KODEX: Admin keeps the door codes for the offices. Office B is Joey's.\n\n"
    "Show every column for 'Office B' codes whose status is 'active'.",
    "Office B's code is 3172.",
    "door_codes", ["office", "code", "set_by", "status"], [
        ["Office A", "1985", "Linda Park", "active"],
        ["Office B", "8810", "Linda Park", "expired"],
        ["Office B", "3172", "Linda Park", "active"],
        ["Office D", "1204", "Linda Park", "expired"],
        ["Manager office", "----", "Linda Park", "key only"],
    ], lambda r: r["office"] == "Office B" and r["status"] == "active", user="grace.lee")

n += puzzle("f1_b_inbox", "Joey's inbox",
    "KODEX: Joey's mail is open. Anything from the people who'd know about the building?\n\n"
    "Show the subject and body of every mail whose sender is 'Facilities' or 'Linda Park'.",
    "Facilities moved to HR as 'People & Facilities'. And Joey has a... reputation.",
    "inbox", ["sender", "subject", "body"], [
        ["Facilities", "We've moved!", "Facilities is now People & Facilities, run from HR on Floor 2. Power, lifts and the cleaning rota: all requests go to them."],
        ["Linda Park", "Meeting etiquette", "Joey. Four bathroom breaks in one meeting. Ken timed them. Ken has a spreadsheet."],
        ["Linda Park", "Keys", "Office C's spare key is in your drawer. Stop 'borrowing' Omar's stapler."],
        ["Omar Haddad", "Stapler", "I know it was you."],
        ["Canteen", "Tuesday special", "Chilli. Sorry in advance."],
    ], lambda r: r["sender"] in ("Facilities", "Linda Park"), select=["subject", "body"], user="joey.marsh")

n += puzzle("f1_c_invoices", "Vendor V-207",
    "KODEX: The slip you found says the electricians, Voltline Electrical, are vendor V-207.\n\n"
    "Show every column for invoices from vendor_id 'V-207' that are 'unpaid'.",
    "Voltline's power bill isn't Admin's: it goes to People & Facilities. And Priya's door code is 5590.",
    "invoices", ["invoice", "vendor_id", "amount", "status", "memo"], [
        ["INV-3290", "V-207", 4200, "paid", "Office E lighting call-out."],
        ["INV-3307", "V-207", 18400, "unpaid", "Building power + generator service. NOT OURS: forward to People & Facilities (HR). Priya - your door code is 5590 now."],
        ["INV-3311", "V-115", 640, "unpaid", "Coffee machine. Again."],
        ["INV-3315", "V-031", 2150, "unpaid", "Chairs nobody has sat in."],
        ["INV-3318", "V-207", 950, "paid", "Lift inspection."],
    ], lambda r: r["vendor_id"] == "V-207" and r["status"] == "unpaid", user="omar.haddad",
    needs_flag="searched_f1_se_drawer",
    brief_missing="KODEX: Accounts pays the contractors. To find the electricians' bills you need their vendor ID, "
                  "and it isn't on this computer.\n\nIt's probably written down somewhere on this floor.")

n += puzzle("f1_d_budget", "Who pays for the power?",
    "KODEX: Budgets show who really owns something.\n\n"
    "Show the department, cost_centre and floor for the item 'Building power'.",
    "Building power is paid by People & Facilities, cost centre PF-02, floor 2.",
    "budget", ["item", "department", "cost_centre", "floor", "amount"], [
        ["Office supplies", "Administration", "AD-01", 1, 3200],
        ["Building power", "People & Facilities", "PF-02", 2, 212000],
        ["Lifts", "People & Facilities", "PF-02", 2, 38000],
        ["Coffee", "Administration", "AD-01", 1, 9100],
        ["Executive dining", "Executive Office", "EX-01", 3, 41000],
    ], lambda r: r["item"] == "Building power", select=["department", "cost_centre", "floor"], user="priya.nair")

n += puzzle("f1_e_archive", "The plans",
    "KODEX: Records keeps the building's paperwork.\n\n"
    "Show the contents and location of the boxes whose contents are 'Electrical plans' or 'Generator manual'.",
    "Both moved to Plant room 2B, on the HR floor.",
    "archive", ["box", "contents", "location"], [
        ["A-12", "Lease agreements", "Shelf 3"],
        ["A-19", "Electrical plans", "Moved to Plant room 2B (HR, Floor 2) by Facilities"],
        ["A-20", "Generator manual", "Moved to Plant room 2B (HR, Floor 2) by Facilities"],
        ["A-31", "Christmas party photos", "Shelf 1 (do not open)"],
        ["A-40", "Fire drill records", "Shelf 2"],
    ], lambda r: r["contents"] in ("Electrical plans", "Generator manual"), select=["contents", "location"], user="ken.watts")

n += puzzle("f1_f_keys", "The spare key",
    "KODEX: Manager Linda's office is locked. Procurement logs every key.\n\n"
    "Show the holder of the key for door 'Manager office' whose status is 'spare'.",
    "The spare's on the shelf in the lounge.",
    "key_register", ["key_name", "door", "holder", "status"], [
        ["K-01", "Manager office", "Linda Park", "issued"],
        ["K-02", "Manager office", "Lounge shelf (top)", "spare"],
        ["K-07", "Office C", "Omar Haddad", "issued"],
        ["K-08", "Office C", "Joey Marsh (drawer)", "spare"],
        ["K-15", "Stationery cupboard", "Tomas Reyes", "issued"],
    ], lambda r: r["door"] == "Manager office" and r["status"] == "spare", select=["holder"], user="tomas.reyes")

n += puzzle("f1_m_systems", "Main power",
    "KODEX: Manager Linda's building systems sheet. Use the cost centre from Office D.\n\n"
    "Show every column for the system 'Main power' with that cost_centre.",
    "Main power: People & Facilities, Plant room 2B, Floor 2. Human Resources.",
    "building_systems", ["system", "cost_centre", "managed_by", "floor", "room"], [
        ["Main power", "AD-01", "Administration (until March)", 1, "Riser cupboard"],
        ["Main power", "PF-02", "People & Facilities", 2, "Plant room 2B"],
        ["Backup generator", "PF-02", "People & Facilities", 2, "Plant room 2B"],
        ["Lifts", "PF-02", "People & Facilities", 0, "Lift motor room"],
        ["Server cooling", "EX-01", "Executive Office", 3, "Server room"],
    ], lambda r: r["system"] == "Main power" and r["cost_centre"] == "PF-02", user="linda.park (Office Manager)")

# ---------------------------------------------------------------- the employee terminal (lessons for Floor 2)

HR = ["name", "department", "role", "floor", "start_year"]
hr_rows = [
    ["Sofia Mendes", "Human Resources", "HR Officer", 2, 2017],
    ["Sam Okafor", "Human Resources", "Recruiter", 2, 2021],
    ["Bea Lindqvist", "People & Facilities", "Facilities Lead", 2, 2015],
    ["Bram Kowal", "People & Facilities", "Electrician", 2, 2019],
    ["Saad", "Human Resources", "Payroll", 2, 2020],
    ["Aman L", "Security", "Guard", 0, 2018],
    ["Nadia Farouk", "Security", "Guard", 2, 2022],
    ["Tess Albright", "Human Resources", "Head of HR", 2, 2012],
]

def lesson(n_, title, brief, solution_where, lessons, select=None, order=None, success="OK.", rows=hr_rows, columns=HR, grouped=None):
    pid = f"f1_tut_{n_}"
    if grouped is not None:
        exp = grouped
        d = {"id": pid, "title": title, "brief": brief, "successMessage": success, "rewardFlag": pid + "_done",
             "orderMatters": order is not None, "unlocksLessons": lessons, "user": "maint-0009",
             "tables": [{"name": "staff", "columns": columns, "rows": [{"cells": [str(c) for c in r]} for r in rows]}],
             "expectedRows": [{"cells": [str(c) for c in r]} for r in exp]}
        with open(os.path.join(OUT, pid + ".json"), "w", encoding="utf-8") as f:
            json.dump(d, f, indent=1, ensure_ascii=False)
        return
    sel = [r for r in rows if solution_where(dict(zip(columns, r)))]
    if order: sel = sorted(sel, key=order)
    cols = select or columns
    exp = [[r[columns.index(c)] for c in cols] for r in sel]
    d = {"id": pid, "title": title, "brief": brief, "successMessage": success, "rewardFlag": pid + "_done",
         "orderMatters": order is not None, "unlocksLessons": lessons, "user": "maint-0009",
         "tables": [{"name": "staff", "columns": columns, "rows": [{"cells": [str(c) for c in r]} for r in rows]}],
         "expectedRows": [{"cells": [str(c) for c in r]} for r in exp]}
    with open(os.path.join(OUT, pid + ".json"), "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1, ensure_ascii=False)

lesson(1, "Lesson 1 · LIKE",
    "LIKE matches a pattern instead of exact text. % means 'anything here', _ means 'exactly one character'.\n"
    "WHERE name LIKE 'S%' finds names starting with S.\n\nTry it: show the names of everyone whose role ends in 'Officer'.",
    lambda r: r["role"].endswith("Officer"), "like", select=["name"])
lesson(2, "Lesson 2 · IN",
    "IN checks against a list, instead of chaining ORs: WHERE floor IN (0, 2)\n\n"
    "Try it: show the name and department of everyone in 'Security' or 'People & Facilities', using IN.",
    lambda r: r["department"] in ("Security", "People & Facilities"), "in", select=["name", "department"])
lesson(3, "Lesson 3 · NOT",
    "NOT flips a condition: WHERE NOT floor = 2, or name NOT LIKE 'S%', or department NOT IN (...).\n\n"
    "Try it: show the names of everyone NOT in 'Human Resources'.",
    lambda r: r["department"] != "Human Resources", "not", select=["name"])
lesson(4, "Lesson 4 · ORDER BY",
    "ORDER BY sorts the results. Add DESC for biggest-first: ORDER BY start_year DESC\n\n"
    "Try it: show name and start_year for the 'Human Resources' department, oldest start_year first.",
    lambda r: r["department"] == "Human Resources", "orderby", select=["name", "start_year"], order=lambda r: r[4])
# GROUP BY department with COUNT(*), sorted by department (order not required)
counts = {}
for r in hr_rows: counts[r[1]] = counts.get(r[1], 0) + 1
lesson(5, "Lesson 5 · GROUP BY",
    "GROUP BY squashes rows that share a value into one, and COUNT(*) counts how many were squashed:\n"
    "SELECT floor, COUNT(*) FROM staff GROUP BY floor\n\nTry it: show each department and how many people are in it.",
    None, "groupby", grouped=[[k, v] for k, v in counts.items()],
    success="KODEX: Floor 2 basics, done. Human Resources is next.")

print("floor 1 puzzles written; expected rows:", n)
