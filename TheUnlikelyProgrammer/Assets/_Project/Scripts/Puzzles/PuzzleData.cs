using System;
using System.Globalization;
using Unlikely.SQL;
using UnityEngine;

namespace Unlikely.Puzzles
{
    [Serializable] public class RowData { public string[] cells; }

    /// <summary>A clue KODEX lists under the task once its GameState flag is set.</summary>
    [Serializable] public class ClueData { public string flag; public string text; }

    [Serializable]
    public class TableData
    {
        public string name;
        public string[] columns;
        public RowData[] rows;
    }

    /// <summary>
    /// One SQL puzzle, authored as a JSON file (see Assets/_Project/Data/Puzzles).
    /// The player passes by producing a result that matches expectedRows; the exact query text does not matter.
    /// </summary>
    [Serializable]
    public class PuzzleData
    {
        public string id;
        public string title;
        public string brief;            // what the player is asked to find
        public string successMessage;   // shown when solved (e.g. reveals a door code)
        public string rewardFlag;       // GameState flag set when solved
        public TableData[] tables;
        public RowData[] expectedRows;  // expected result, cells as text
        public bool orderMatters;       // true for ORDER BY puzzles
        public string unlocksLessons;   // terminal tutorials: Manual lesson ids unlocked when solved, comma separated
        public string note;             // optional sticky note on the monitor (e.g. Dan's notes for Ryker)
        public string user;             // who the computer is logged in as (shown in the app header)
        public string needsFlag;        // until this GameState flag is set (e.g. a clue found), KODEX shows briefMissing
        public string briefMissing;     // the task text while the clue is still missing
        public ClueData[] clues;        // optional: clues found so far are listed under the task

        /// <summary>The task text KODEX should show right now (Leet: the task alone, no clue list).</summary>
        public string CurrentBrief(bool solved, bool withClues = true)
        {
            string text = !solved && !string.IsNullOrEmpty(needsFlag) && !string.IsNullOrEmpty(briefMissing) && !Unlikely.Core.GameState.Has(needsFlag)
                ? briefMissing : brief;
            if (!withClues || clues == null || clues.Length == 0) return text;
            var sb = new System.Text.StringBuilder(text);
            int found = 0;
            foreach (var c in clues) if (Unlikely.Core.GameState.Has(c.flag)) found++;
            sb.Append($"\n\nCLUES ({found} of {clues.Length})");
            foreach (var c in clues) if (Unlikely.Core.GameState.Has(c.flag)) sb.Append("\n- ").Append(c.text);
            if (found < clues.Length) sb.Append("\n- ...still missing something. Look around the floor.");
            return sb.ToString();
        }

        public int CluesFound
        {
            get { int n = 0; if (clues != null) foreach (var c in clues) if (Unlikely.Core.GameState.Has(c.flag)) n++; return n; }
        }

        public static PuzzleData FromJson(string json) => JsonUtility.FromJson<PuzzleData>(json);

        public Database BuildDatabase()
        {
            var db = new Database();
            foreach (var td in tables)
            {
                var table = new Table(td.name, td.columns);
                foreach (var row in td.rows)
                {
                    var cells = new object[td.columns.Length];
                    for (int i = 0; i < cells.Length; i++)
                        cells[i] = ParseCell(i < row.cells.Length ? row.cells[i] : "NULL");
                    table.Rows.Add(cells);
                }
                db.Add(table);
            }
            return db;
        }

        public static object ParseCell(string s)
        {
            if (s == null || s == "NULL") return null;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            return s;
        }
    }
}
