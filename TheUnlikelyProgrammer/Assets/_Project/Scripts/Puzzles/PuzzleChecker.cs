using System;
using System.Collections.Generic;
using Unlikely.SQL;

namespace Unlikely.Puzzles
{
    public static class PuzzleChecker
    {
        /// <summary>Compares the query result against the puzzle's expected rows. Feedback never reveals the answer.</summary>
        public static bool Check(PuzzleData puzzle, QueryResult result, out string feedback)
        {
            var expected = new List<string>();
            int expectedCols = 0;
            foreach (var row in puzzle.expectedRows)
            {
                var cells = new string[row.cells.Length];
                for (int i = 0; i < cells.Length; i++) cells[i] = QueryResult.Format(PuzzleData.ParseCell(row.cells[i]));
                expectedCols = cells.Length;
                expected.Add(string.Join("\u001f", cells).ToLowerInvariant());
            }

            if (expected.Count > 0 && result.Columns.Length != expectedCols)
            {
                feedback = $"Your result has {result.Columns.Length} column(s), but this puzzle needs a different number. Re-read what it asks for.";
                return false;
            }

            var actual = new List<string>();
            foreach (var row in result.Rows)
            {
                var cells = new string[row.Length];
                for (int i = 0; i < cells.Length; i++) cells[i] = QueryResult.Format(row[i]);
                actual.Add(string.Join("\u001f", cells).ToLowerInvariant());
            }

            if (actual.Count != expected.Count)
            {
                feedback = $"Your query returned {actual.Count} row(s), but the puzzle needs a different amount. Check your conditions.";
                return false;
            }

            if (!puzzle.orderMatters)
            {
                actual.Sort(StringComparer.Ordinal);
                expected.Sort(StringComparer.Ordinal);
            }

            for (int i = 0; i < actual.Count; i++)
            {
                if (actual[i] != expected[i])
                {
                    feedback = puzzle.orderMatters
                        ? "Right amount of data, but not in the order the puzzle wants."
                        : "Right number of rows, but the data isn't what the puzzle asks for.";
                    return false;
                }
            }

            feedback = null;
            return true;
        }
    }
}
