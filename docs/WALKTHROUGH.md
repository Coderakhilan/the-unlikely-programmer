# The Unlikely Programmer: full walkthrough

**Spoilers ahead: this is the whole game.** Every floor, every query, every choice, all four endings and all eight stars.

Each computer's question appears in the **Task** tab of KODEX on Donnie's phone. The queries below are the tested
answers. The game checks the rows your query returns, not the exact text, so any query that returns the same rows
also works.

## Contents

- [Controls](#controls)
- [Floor 0: General](#floor-0-general)
- [Floor 1: Administration](#floor-1-administration)
- [Floor 2: Human Resources](#floor-2-human-resources)
- [Floor 3: Executive Offices](#floor-3-executive-offices)
- [The server room: the four endings](#the-server-room-the-four-endings)
- [The eight stars](#the-eight-stars)
- [Difficulties](#difficulties)
- [Extras](#extras)

## Controls

| Key | Action |
|---|---|
| WASD / arrow keys | Move |
| E | Use, talk, next line |
| Q | Push the cart / let go of it |
| F | Mop (cart in hand) · flick the torch (in the dark) |
| 1–9 | Put an item in your hand |
| Tab | Phone |
| Esc | Pause / back |
| B | *The Theory of Everything* (Vibe only) |

On computers: type the query, **Enter** runs it, **Shift+Enter** adds a new line, **Tab** switches between the Task and
Knowledge pages, **Esc** leaves.

---

## Floor 0: General

1. **Janitor's closet** (top left of the lobby): use the bin to get your cart out. Press **Q** next to it to take hold.
2. Push the cart into **Dan's office** (east side of the lobby). Hold **F** next to the spill at the far right to mop it.
   Dan and Ryker talk (Dan is teaching the new intern), then they leave.
3. Head for the stairs (top right of the lobby): the kidnappers call about Betty.
4. Back in Dan's office, use **Dan's computer**. Let go of the cart first: you can't use computers with the cart in
   hand. Find everything about the new intern:

   ```sql
   SELECT * FROM employees WHERE role = 'Intern'
   ```

5. Leave Dan's office: another call. You need a **USB-C cable**.
6. **Staff lounge** (west side): the people there mention the vending machine, which needs a coin. Your locker in the
   janitor's closet has a coin. With the coin in hand (its number key), use the vending machine to get the USB-C cable.
7. Another call. **Tab** opens the phone: open Messages, read the kidnappers' message and install **KODEX**.
8. **Employee terminal** on the front desk. Wait until the receptionist walks off on her rounds, put the USB-C cable in
   your hand and use the terminal. KODEX hacks it and gives you **Floor 1 clearance**. Then five lessons:

   ```sql
   SELECT * FROM staff
   SELECT name, department FROM staff
   SELECT name FROM staff WHERE floor = 1
   SELECT name FROM staff WHERE department = 'Accounts' AND floor = 1
   SELECT name FROM staff WHERE department = 'Records' OR department = 'Human Resources'
   ```

9. Take the elevator to Floor 1.

---

## Floor 1: Administration

**Before you start:** get the cart from the Floor 1 janitor's closet (bottom left; its door faces the corridor). It's
optional but recommended, since staff leave a working janitor alone.

1. **Open office (west)**, patrolled. Search the left cabinet: **Office A key**.
   Optional computer (story: the meeting room is booked all day by Linda and Ken):

   ```sql
   SELECT * FROM room_bookings WHERE room = 'Meeting room' AND day = 'Tuesday'
   ```

2. **Open office (east)**, patrolled. Search the left cabinet: a slip that says **vendor V-207**.
   Computer: gives Office E's code, **2468**.

   ```sql
   SELECT * FROM helpdesk WHERE floor = 1 AND category = 'Electrical' AND status = 'Open'
   ```

3. **Office A** (Office A key). Computer: gives Office B's code, **3172**.

   ```sql
   SELECT * FROM door_codes WHERE office = 'Office B' AND status = 'active'
   ```

4. **Office B** (keypad 3172). Search the drawers: **Office C key**.
   Computer (story: Facilities became part of HR, and Joey's reputation):

   ```sql
   SELECT subject, body FROM inbox WHERE sender = 'Facilities' OR sender = 'Linda Park'
   ```

5. **Office C** (Office C key; Omar patrols). Computer: needs the V-207 slip; gives Office D's code, **5590**.

   ```sql
   SELECT * FROM invoices WHERE vendor_id = 'V-207' AND status = 'unpaid'
   ```

6. **Office D** (keypad 5590). Computer: gives cost centre **PF-02** (a printout goes into your pockets).

   ```sql
   SELECT department, cost_centre, floor FROM budget WHERE item = 'Building power'
   ```

7. **Office E** (keypad 2468). Search the right-hand shelf: **Office F key**.
   Computer (story: the plans moved to HR):

   ```sql
   SELECT contents, location FROM archive WHERE contents = 'Electrical plans' OR contents = 'Generator manual'
   ```

8. **Office F** (Office F key; Tomas patrols). Computer: the manager's spare key is in lounge locker 4.

   ```sql
   SELECT holder FROM key_register WHERE door = 'Manager office' AND status = 'spare'
   ```

9. **Lounge**: use the staff lockers to get the **Manager's key**.
10. **Manager's office** (Manager's key). Computer: needs PF-02 from Office D. It reveals that main power is run by HR,
    in Plant room 2B.

    ```sql
    SELECT * FROM building_systems WHERE system = 'Main power' AND cost_centre = 'PF-02'
    ```

    Then search Linda's desk: **tape recorder**.
11. **Men's washroom**: use the end cubicle with the tape recorder. Linda and Ken leave and the meeting room unlocks.
12. **Meeting room terminal**: the hack gives **Human Resources clearance**, then five lessons:

    ```sql
    SELECT name FROM staff WHERE role LIKE '%Officer'
    SELECT name, department FROM staff WHERE department IN ('Security', 'People & Facilities')
    SELECT name FROM staff WHERE NOT department = 'Human Resources'
    SELECT name, start_year FROM staff WHERE department = 'Human Resources' ORDER BY start_year
    SELECT department, COUNT(*) FROM staff GROUP BY department
    ```

13. Walk back to the elevator: the kidnappers call. Choose **truth or lie** (W/S, then E). It's saved for the end.

**Shortest route:** East office (cabinet and computer), West office (cabinet), then A, B, C, D, E, F, the lounge
locker, the Manager's office, the washroom, the meeting room and the elevator. Only the West office computer is
optional.

Floor 1's truth or lie barely matters: lying only gets one extra line from the kidnappers at the very end.

---

## Floor 2: Human Resources

The **Head of HR** patrols the corridors. If he sees you **without the cart**, he chases you (he's slower than you).
If he catches you, you go back to the elevator. Keep the cart with you in the corridors.

0. Get the cart: Floor 2 janitor's closet, use the bin.
1. **Conference room**: watch the onboarding video on the projector. It says "Plant Room 2B" is really the
   **Northgate** plant, built in **2014** by the **Night** operations team. This is also a clue for the duty login.
2. **HR open office** (right side), four computers:

   Site register (Northgate's address):

   ```sql
   SELECT site, name, address FROM sites WHERE name LIKE '%Northgate%' AND type = 'Power generation'
   ```

   Change requests (the building is switched from this floor's breaker room):

   ```sql
   SELECT ref, requested, summary FROM change_requests WHERE system IN ('Main power', 'Northgate') ORDER BY requested
   ```

   Cleaning rota (the conference room plant needs a "special clean"):

   ```sql
   SELECT area, item, notes FROM cleaning_rota WHERE task LIKE 'Special%'
   ```

   Door locks (the interview room locks from outside with code **7341**, which goes into your pockets):

   ```sql
   SELECT room, code FROM door_locks WHERE room = 'Interview room' AND NOT status = 'expired'
   ```

3. **Breaker room**: the panel wants an authorised duty login. The reception computer has the list, but you need every
   clue first:
   - **Clue 1:** printer room, the jammed policy page. The switch only takes a login whose role ends in `Engineer`.
   - **Clue 2:** the onboarding video (step 1). Night shift, started 2014.
   - **Clue 3:** the conference room plant (after the cleaning rota). Sift the soil for a coin. Take the elevator down
     to Floor 0 and, with the coin in hand, use the vending machine to get crisps. Come back up and, with the crisps in
     hand, talk to the hungry candidate on the HR sofa. Half the logins are dead; only `active` ones work.
   - **Clue 4:** the interview room computer. Bram Kowal's login dies at noon, so it isn't him. The restructure file is
     also your leverage later:

     ```sql
     SELECT name, decision, notes FROM restructure WHERE NOT decision = 'keep'
     ```

4. **Reception computer**, with every clue at once:

   ```sql
   SELECT username, password FROM duty_logins WHERE role LIKE '%Engineer' AND shift = 'Night' AND start_year = 2014 AND status = 'active' AND NOT name = 'Bram Kowal'
   ```

   Answer: **i.vidal / Northgate-0914**.
5. **Employee terminal** (long hallway, centre table). Do this **before** the breaker panel: once the power is off, the
   terminal is too. It gives **Executive Offices clearance**, then five lessons:

   ```sql
   SELECT name FROM employees WHERE dept_id = 20
   SELECT employees.name, departments.dept_name FROM employees JOIN departments ON employees.dept_id = departments.id
   SELECT e.name FROM employees e JOIN departments d ON e.dept_id = d.id WHERE d.floor = 3
   SELECT d.dept_name, e.name FROM departments d LEFT JOIN employees e ON e.dept_id = d.id
   SELECT dept_name FROM departments ORDER BY id DESC LIMIT 2
   ```

6. Take the **torch** now, from the right-hand table in the long hallway. You'll need it in the dark.
7. **Breaker panel** with the login, then the call: **truth or lie. This one decides the ending.**

   **Truth:** Donnie trips the switch himself and the whole building goes dark. Then the kidnappers frame him: they
   send security an anonymous tip about a janitor next to the switch on Floor 2.
   - In the dark, security walks rounds. **F** flicks the torch at a spot and they go and look there.
   - Lockers hide you completely; plants hide you too (your eyes show).
   - Get to the elevator. The kidnappers call: Donnie threatens them with the restructure file, security is called
     off, and the new deal is to overheat the server room.

   **Lie:** they tell you **Ryker** is their spy. He clears the floor shouting "fire" and comes for you.
   1. Cut this floor's power at the breaker panel.
   2. Put the torch in your hand. Ryker is faster than you and keeps tracking you; hide in lockers or plants.
   3. When Ryker is nearby, flick the torch (**F**) into the interview room: he walks in to look.
   4. Use the interview room door while he's inside: it locks (code 7341).
   5. Through the door he lets slip **MagiCorp's database login**. Keep it, and don't tell the kidnappers.
   6. Turn the power back on at the breaker panel. Your leverage gets you the same deal: the server room.
8. Take the elevator to Floor 3.

---

## Floor 3: Executive Offices

Before the blackout, security and an assistant only question you if you're without the cart.

1. **Arrival call:** the server room code changes daily, belongs to a different person each day, and is encrypted.
   Liam's executive keycard decrypts it.
2. **Liam** paces by the windows, bottom left. Talk to him: he refuses and threatens Betty.
3. **Three coins** are in plant pots: by the stairs, in Liam's window corner, and in the lounge. The lounge vending
   machine sells **hot coffee**.
4. With the coffee in hand (its number key), talk to Liam: you throw it. He chases you, a bit slower than you. Run into
   the **small meeting room** and hide in the locker. He walks to the far end ("I know you're in here"). Slip out and
   use the door: it locks him in, and you get his **keycard**.
   If he catches you, you start again in the lounge with a fresh coffee.
5. **Employee terminal** (reception desk): **Server Room clearance**, then three lessons:

   ```sql
   INSERT INTO visitors (id, name, status) VALUES (5, 'Sam Ortiz', 'in')
   INSERT INTO archive (id, name) SELECT id, name FROM visitors WHERE status = 'left'
   DELETE FROM visitors WHERE status = 'left'
   ```

6. **Executive office A** (the orange room, safe): the desk drawer has a **pen drive**. Take the elevator to Floor 2
   and use HR's printer to print the **key map**. Now you know which column points where: `rota.holder`,
   `leave.on_leave`, `leave.cover` and `badges.owner` hold a `staff_id` (`staff.staff_id`); `code_parts.card` holds a
   `badge_no` (`badges.badge_no`).
7. The **link station** (office A) points one computer at another, so a JOIN can see both tables.

   | Computer | Table |
   |---|---|
   | Boardroom, long table | `staff` |
   | Boardroom, side table | `rota` |
   | Open office, left | `leave` |
   | Open office, right | `badges` |
   | Executive office B | `code_parts` |

   **Task 1** (boardroom side table). Link station: the side table reads from the boardroom long table.

   ```sql
   SELECT s.staff_id, s.name FROM rota r JOIN staff s ON r.holder = s.staff_id WHERE r.rota_day = 'Tuesday'
   ```

   Answer: **Priya Shah, 118**. She's on leave.

   **Task 2** (open office left). Link station: open office left reads from the boardroom long table.

   ```sql
   SELECT s.staff_id, s.name FROM leave l JOIN staff s ON l.cover = s.staff_id WHERE l.on_leave = 118 AND l.leave_day = 'Tuesday'
   ```

   Answer: **Marcus Hale, 205**. He covers for her, so it's his code.

   **Task 3** (executive office B). Link station: office B reads from open office right.

   ```sql
   SELECT c.position, c.cipher FROM code_parts c JOIN badges b ON c.card = b.badge_no WHERE b.owner = 205 AND b.status = 'active' ORDER BY c.position
   ```

   Answer: **V Q 3 L** (encrypted).
8. **Link station: decrypt with Liam's card.** It's revoked, the CEO is alerted, and the lights go out. Three security
   guards and the CEO hunt you. Only office A (safe) and the CEO's office can be entered.
9. **Torch:** the emergency cabinet in office A. **F** flicks it to send hunters off to look.
10. **CEO's office** (top left, open now):
    - His **keycard** is on the desk.
    - His **laptop** is on the desk too. **Read his mail** (about MagiCorp): the Capitalist ending needs it.
11. **Link station: decrypt with the CEO's card.** Your phone restarts and updates: Calls now works, and there's a bank
    app, **INFI Bank**. You can buy MagiCorp (MAGI) shares only if you have Ryker's MagiCorp login (you lied on Floor 2)
    **and** read the CEO's mail. Buy them now or later in the server room; Capitalist needs them.
12. **Link station: decrypt once more:** **7302**.
13. **Server room door** (right-hand side): keypad **7302**. Then the call, "did you get in?": **truth or lie. This
    decides the ending.**

**If you're caught in the blackout:** you start again in office A. Once you have the CEO's keycard, you start in the
CEO's office instead. Ducking into a room always shakes off whoever was chasing you.

---

## The server room: the four endings

The GyaraDB console is on the desk, top left. You can't leave until it's done.

| Floor 2 call | Floor 3 call | Ending |
|---|---|---|
| Truth | Truth | **Accomplice** |
| Truth | Lie | **The Last Query** |
| Lie | Truth | **Capitalist** (only if you read the CEO's mail; otherwise Donnie feels he missed something and you get Accomplice) |
| Lie | Lie | **Good Samaritan** |

### Accomplice

The call: upload the file from your phone. Use the console and press Enter to upload. The servers cook.
Betty is free; Donnie is arrested.

### The Last Query

Ryker was watching; they know you lied. The console only runs DELETE. One query ends it:

```sql
DELETE FROM gyaradb
```

A DELETE with a WHERE only removes part of it; the game wants all of it.

### Capitalist

The call: you talk them into a better deal: copy GyaraCorp's data to MagiCorp, wipe it, and sell out Liam.

1. If you haven't bought MagiCorp shares yet: phone (Tab), INFI Bank, buy MAGI.
2. Walk to the console and use it. Copy everything first (deleting first is refused):

   ```sql
   INSERT INTO magicorp_inbox SELECT * FROM gyaradb
   ```

3. Then wipe GyaraDB:

   ```sql
   DELETE FROM gyaradb
   ```

4. Cut to the CEO's office: Liam takes the blame.

### Good Samaritan

1. Use the console once: Donnie thinks it through. Not the computer: the CEO.
2. Leave the server room. Floor 3 is dark again; security still hunts you, and the CEO just walks around.
   If you're caught, you start again in the server room.
3. Find the CEO and talk to him (**E**). Tell him everything.
4. Cut to his office: he gets the location out of Liam and calls the police.

---

## The eight stars

Title screen → Extras → Achievements.

- **4 stars:** reach any ending on each difficulty (Vibe, Unlikely, Likely, Leet).
- **4 stars:** reach each ending (Accomplice, The Last Query, Capitalist, Good Samaritan).

One run can earn two stars. **Fastest route to every ending:** finish once, then use **Load Game → Start of Floor 2**
and answer the Floor 2 and Floor 3 calls differently. To replay just the finale, use **Load Game → The Server Room**.

## Difficulties

- **Vibe Programmer:** *The Theory of Everything* is in your pockets (press **B**). It has every answer.
- **Unlikely Programmer:** the standard game.
- **Likely Programmer:** a 60-second bar on each computer; stepping away drains it slowly. Read the task, plan the
  query, then sit down.
- **Leet Programmer:** the same timer, plus no KODEX lessons, Knowledge pages, clue lists or hints. Every query above
  still works.

## Extras

- **Artwork:** every illustration you've seen, unlocked as the story shows it.
- **Soundtrack:** each of the 14 tracks unlocks once it has played in the game (some only on certain paths). Play,
  pause, skip and scrub through them.
- **Achievements:** the eight stars.
- **Credits.**
