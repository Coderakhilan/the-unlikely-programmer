using System.Linq;
using NUnit.Framework;
using Unlikely.Puzzles;
using Unlikely.SQL;

namespace Unlikely.Tests
{
    public class SqlEngineTests
    {
        private static Database MakeDb()
        {
            var t = new Table("employees", new[] { "id", "name", "department", "floor" });
            t.Rows.Add(new object[] { 1.0, "Dan", "Engineering", 0.0 });
            t.Rows.Add(new object[] { 2.0, "Maya", "Reception", 0.0 });
            t.Rows.Add(new object[] { 3.0, "Pete", "Reception", 0.0 });
            t.Rows.Add(new object[] { 4.0, "Linda", "Administration", 1.0 });
            t.Rows.Add(new object[] { 5.0, "Omar", "Administration", 2.0 });
            var db = new Database();
            db.Add(t);
            return db;
        }

        private static string[] Names(QueryResult r) => r.Rows.Select(x => QueryResult.Format(x[0])).ToArray();

        [Test]
        public void SelectStar_ReturnsEverything()
        {
            var r = SqlEngine.Execute(MakeDb(), "SELECT * FROM employees");
            Assert.AreEqual(4, r.Columns.Length);
            Assert.AreEqual(5, r.Rows.Count);
        }

        [Test]
        public void SelectColumns_ProjectsInOrder()
        {
            var r = SqlEngine.Execute(MakeDb(), "select name, floor from employees;");
            CollectionAssert.AreEqual(new[] { "name", "floor" }, r.Columns);
            Assert.AreEqual("Dan", QueryResult.Format(r.Rows[0][0]));
            Assert.AreEqual("0", QueryResult.Format(r.Rows[0][1]));
        }

        [Test]
        public void Where_TextEquals_IgnoresCase()
        {
            var r = SqlEngine.Execute(MakeDb(), "SELECT name FROM employees WHERE department = 'reception'");
            CollectionAssert.AreEquivalent(new[] { "Maya", "Pete" }, Names(r));
        }

        [Test]
        public void Where_NumberComparison()
        {
            var r = SqlEngine.Execute(MakeDb(), "SELECT name FROM employees WHERE floor >= 1");
            CollectionAssert.AreEquivalent(new[] { "Linda", "Omar" }, Names(r));
        }

        [Test]
        public void Where_And_Or_AndBindsTighterThanOr()
        {
            // Dan OR (Reception AND floor 1) -> only Dan
            var r = SqlEngine.Execute(MakeDb(),
                "SELECT name FROM employees WHERE name = 'Dan' OR department = 'Reception' AND floor = 1");
            CollectionAssert.AreEquivalent(new[] { "Dan" }, Names(r));
        }

        [Test]
        public void Where_Brackets_ChangeMeaning()
        {
            var r = SqlEngine.Execute(MakeDb(),
                "SELECT name FROM employees WHERE (name = 'Dan' OR department = 'Reception') AND floor = 0");
            CollectionAssert.AreEquivalent(new[] { "Dan", "Maya", "Pete" }, Names(r));
        }

        [Test]
        public void NotEqual_BothSpellings()
        {
            var a = SqlEngine.Execute(MakeDb(), "SELECT name FROM employees WHERE floor != 0");
            var b = SqlEngine.Execute(MakeDb(), "SELECT name FROM employees WHERE floor <> 0");
            Assert.AreEqual(2, a.Rows.Count);
            Assert.AreEqual(2, b.Rows.Count);
        }

        [Test]
        public void UnknownTable_ListsAvailableTables()
        {
            var e = Assert.Throws<SqlException>(() => SqlEngine.Execute(MakeDb(), "SELECT * FROM staff"));
            StringAssert.Contains("employees", e.Message);
        }

        [Test]
        public void UnknownColumn_ListsColumns()
        {
            var e = Assert.Throws<SqlException>(() => SqlEngine.Execute(MakeDb(), "SELECT salary FROM employees"));
            StringAssert.Contains("name", e.Message);
        }

        [Test]
        public void UnquotedText_GivesQuoteHint()
        {
            var e = Assert.Throws<SqlException>(() => SqlEngine.Execute(MakeDb(), "SELECT name FROM employees WHERE department = Reception"));
            StringAssert.Contains("quotes", e.Message);
        }

        [Test]
        public void MissingFrom_GivesHelpfulError()
        {
            var e = Assert.Throws<SqlException>(() => SqlEngine.Execute(MakeDb(), "SELECT name employees"));
            StringAssert.Contains("comma", e.Message);
            e = Assert.Throws<SqlException>(() => SqlEngine.Execute(MakeDb(), "SELECT *"));
            StringAssert.Contains("FROM", e.Message);
        }

        [Test]
        public void UnclosedQuote_IsReported()
        {
            var e = Assert.Throws<SqlException>(() => SqlEngine.Execute(MakeDb(), "SELECT name FROM employees WHERE name = 'Dan"));
            StringAssert.Contains("quote", e.Message);
        }

        [Test]
        public void DoubleEquals_IsExplained()
        {
            var e = Assert.Throws<SqlException>(() => SqlEngine.Execute(MakeDb(), "SELECT name FROM employees WHERE floor == 0"));
            StringAssert.Contains("one equals", e.Message);
        }

        [Test]
        public void EmptyQuery_AsksForInput()
        {
            Assert.Throws<SqlException>(() => SqlEngine.Execute(MakeDb(), "   "));
        }

        [Test]
        public void Puzzle_AcceptsAnyCorrectQuery_RejectsWrongOnes()
        {
            var puzzle = new PuzzleData
            {
                orderMatters = false,
                expectedRows = new[]
                {
                    new RowData { cells = new[] { "Maya" } },
                    new RowData { cells = new[] { "Pete" } },
                }
            };
            var db = MakeDb();

            var good1 = SqlEngine.Execute(db, "SELECT name FROM employees WHERE department = 'Reception'");
            var good2 = SqlEngine.Execute(db, "SELECT name FROM employees WHERE department = 'Reception' AND floor = 0");
            var tooMany = SqlEngine.Execute(db, "SELECT name FROM employees WHERE floor = 0");
            var wrongCols = SqlEngine.Execute(db, "SELECT name, floor FROM employees WHERE department = 'Reception'");

            Assert.IsTrue(PuzzleChecker.Check(puzzle, good1, out _));
            Assert.IsTrue(PuzzleChecker.Check(puzzle, good2, out _));
            Assert.IsFalse(PuzzleChecker.Check(puzzle, tooMany, out string f1));
            Assert.IsNotNull(f1);
            Assert.IsFalse(PuzzleChecker.Check(puzzle, wrongCols, out _));
        }
    }
}
