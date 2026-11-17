# The Unlikely Programmer

**The Unlikely Programmer is a game made in Unity 6, available for Windows and Linux (64-bit).**
Download it from [Releases](https://github.com/Coderakhilan/the-unlikely-programmer/releases).

## Premise

Donald "Donnie" Raymond is a janitor at GyaraCorp. One Tuesday morning his daughter Betty is kidnapped. The kidnappers
want GyaraCorp's data, and they want Donnie to get it for them: they install a hacking app, KODEX, on his phone and
teach him SQL.

Donnie works his way up the building floor by floor: sneaking past staff, getting into offices and querying company
computers for door codes, keys, logins and secrets. Each time the kidnappers call, he chooses whether to tell them the
truth or lie, and those choices decide the ending.

## Genre

A top-down 2D narrative stealth-puzzle game where the puzzles are real SQL queries, from a first `SELECT` up to `JOIN`,
`INSERT` and `DELETE`.

Inspired by The Escapists, Yuppie Psycho and Five Nights at Freddy's.

## Features

- Four floors of GyaraCorp (General, Administration, Human Resources, Executive Offices) and the server room.
- 38 SQL puzzles on in-game computers and 18 terminal lessons, taught through KODEX on Donnie's phone.
- Stealth: patrols, a janitor's cart as a disguise, hiding spots, a torch to lure guards in the dark, and chases.
- Truth or lie at every kidnapper call, with four endings: Accomplice, The Last Query, Capitalist and Good Samaritan.
- Four difficulties:
  - **Vibe Programmer**: comes with *The Theory of Everything*, a guide to every answer.
  - **Unlikely Programmer**: the standard game.
  - **Likely Programmer**: a 60-second limit on each computer.
  - **Leet Programmer**: no lessons, hints or clue lists.
- Three save slots, autosave with resume-anywhere, and Load Game from the start of any floor reached (or the server room).
- Extras: artwork gallery, a 14-track soundtrack player that unlocks as you hear the music, and 8 achievements.

## Technical overview

| | |
|---|---|
| Engine | Unity 6 (6000.6.8f1), Universal Render Pipeline (2D) |
| Platforms | Windows and Linux (64-bit) |
| Language | C# (about 16,000 lines of game code, about 3,400 lines of tests) |
| SQL | A SQL engine written for the game (`Scripts/SQL`): tokenizer, parser and executor. Supports `SELECT`, `WHERE`, `AND / OR / NOT`, `LIKE`, `IN`, `ORDER BY ... ASC/DESC`, `LIMIT`, `COUNT / SUM / AVG / MIN / MAX` with `GROUP BY`, `INNER JOIN` / `LEFT JOIN` with aliases, `INSERT INTO ... VALUES`, `INSERT INTO ... SELECT` and `DELETE`. Every puzzle's tables live in JSON (`Data/Puzzles`), and answers are checked by comparing result rows, so any correct query works. |
| UI | uGUI, built entirely in code (`Scripts/UI`): HUD, phone, computers, dialogue, menus. It scales to any screen shape. |
| Story | Data-driven: all dialogue and cutscenes are JSON (`Data/Dialogue`), with music cues embedded in the lines. One script per floor runs its beats. |
| Levels | Floors are painted maps. A Python pipeline (`tools/`) turns each map and a hand-placed layout into collision, doors and room images (`build_level.py`, `process_art.py`). |
| NPCs | Patrolling, chasing and investigating staff with sight cones, A* pathfinding on a nav grid, and torch lures. |
| Audio | 14-track soundtrack with layered music (scene / conversation / phase / floor) and cross-fades. Sound effects are synthesised in code. |
| Saves | JSON save slots with per-floor checkpoints and a resume save that restores the exact room, position and world state. |
| Fonts | Barlow Semi Condensed and JetBrains Mono (SIL Open Font License, see `licenses/`). |
| Tests | Unity Test Framework: EditMode tests (SQL engine, puzzles, saves) and PlayMode tests that play through every floor and every ending. |

### Building from source

1. Install Unity **6000.6.8f1**, with Windows and/or Linux build support.
2. Open the `TheUnlikelyProgrammer` folder in Unity.
3. Run **Unlikely → Build Floors** once. It builds the Title and Floors scenes and sets the app icon.
4. Press Play from the Title scene, or build the game with **File → Build Profiles**.

## Walkthrough

The full solution, with every floor, every query, every ending and every achievement, is in
[docs/WALKTHROUGH.md](docs/WALKTHROUGH.md).

## AI use

AI-generated assets and artwork, and AI-assisted development, were used in this project.

## License

All rights reserved. The game is free to play; please don't resell, re-upload or redistribute it.
