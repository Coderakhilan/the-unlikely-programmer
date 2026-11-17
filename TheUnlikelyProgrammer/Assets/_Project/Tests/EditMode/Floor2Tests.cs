using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unlikely.Interaction;
using Unlikely.NPC;
using Unlikely.Puzzles;
using Unlikely.SQL;
using Unlikely.Story;
using Unlikely.World;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unlikely.Tests
{
    public class SqlJoinTests
    {
        private static Database Db()
        {
            var e = new Table("employees", new[] { "id", "name", "dept_id" });
            e.Rows.Add(new object[] { 1.0, "Sofia", 10.0 });
            e.Rows.Add(new object[] { 2.0, "Ines", 20.0 });
            e.Rows.Add(new object[] { 3.0, "Owen", 40.0 });
            e.Rows.Add(new object[] { 4.0, "Newbie", null });
            var d = new Table("departments", new[] { "id", "dept_name", "floor" });
            d.Rows.Add(new object[] { 10.0, "HR", 2.0 });
            d.Rows.Add(new object[] { 20.0, "Facilities", 2.0 });
            d.Rows.Add(new object[] { 40.0, "Exec", 3.0 });
            d.Rows.Add(new object[] { 50.0, "Servers", 3.0 });
            var db = new Database();
            db.Add(e);
            db.Add(d);
            return db;
        }

        private static string[] Rows(QueryResult r) => r.Rows.Select(x => string.Join("|", x.Select(QueryResult.Format))).ToArray();

        [Test]
        public void Join_On_ForeignKey()
        {
            var r = SqlEngine.Execute(Db(), "SELECT employees.name, departments.dept_name FROM employees JOIN departments ON employees.dept_id = departments.id");
            CollectionAssert.AreEquivalent(new[] { "Sofia|HR", "Ines|Facilities", "Owen|Exec" }, Rows(r));
            CollectionAssert.AreEqual(new[] { "name", "dept_name" }, r.Columns);
        }

        [Test]
        public void Join_BareNames_WhenUnambiguous() =>
            CollectionAssert.AreEquivalent(new[] { "Sofia|HR", "Ines|Facilities", "Owen|Exec" },
                Rows(SqlEngine.Execute(Db(), "SELECT name, dept_name FROM employees INNER JOIN departments ON dept_id = departments.id")));

        [Test]
        public void Join_Aliases_And_Where() =>
            CollectionAssert.AreEquivalent(new[] { "Owen" },
                Rows(SqlEngine.Execute(Db(), "SELECT e.name FROM employees e JOIN departments AS d ON e.dept_id = d.id WHERE d.floor = 3")));

        [Test]
        public void LeftJoin_KeepsUnmatched() =>
            CollectionAssert.AreEquivalent(new[] { "HR|Sofia", "Facilities|Ines", "Exec|Owen", "Servers|NULL" },
                Rows(SqlEngine.Execute(Db(), "SELECT d.dept_name, e.name FROM departments d LEFT JOIN employees e ON e.dept_id = d.id")));

        [Test]
        public void Join_GroupBy_OrderBy_Limit()
        {
            var r = SqlEngine.Execute(Db(), "SELECT d.floor, COUNT(*) FROM employees e JOIN departments d ON e.dept_id = d.id GROUP BY d.floor ORDER BY COUNT(*) DESC LIMIT 1");
            CollectionAssert.AreEqual(new[] { "2|2" }, Rows(r));
        }

        [Test]
        public void Alias_OnSingleTable() =>
            Assert.AreEqual(2, SqlEngine.Execute(Db(), "SELECT s.name FROM employees s WHERE s.id <= 2").Rows.Count);

        [Test]
        public void Join_FriendlyErrors()
        {
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "SELECT id FROM employees JOIN departments ON dept_id = departments.id"));   // ambiguous
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "SELECT name FROM employees JOIN departments"));                         // no ON
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "SELECT name FROM employees JOIN nowhere ON a = b"));
        }
    }

    /// <summary>Floor 2 can be finished: every puzzle solvable by its intended query, its text present, its objects in the scene.</summary>
    public class Floor2Tests
    {
        private const string Data = "Assets/_Project/Data";

        public static readonly Dictionary<string, string> Solutions = new Dictionary<string, string>
        {
            { "f2_t1_northgate", "SELECT site, name, address FROM sites WHERE name LIKE '%Northgate%' AND type = 'Power generation'" },
            { "f2_t2_remote", "SELECT ref, requested, summary FROM change_requests WHERE system IN ('Main power', 'Northgate') ORDER BY requested" },
            { "f2_t3_plants", "SELECT area, item, notes FROM cleaning_rota WHERE task LIKE 'Special%'" },
            { "f2_t4_locks", "SELECT room, code FROM door_locks WHERE room = 'Interview room' AND NOT status = 'expired'" },
            { "f2_iv_restructure", "SELECT name, decision, notes FROM restructure WHERE NOT decision = 'keep'" },
            { "f2_reception_logins", "SELECT username, password FROM duty_logins WHERE role LIKE '%Engineer' AND start_year = 2014 AND shift = 'Night' AND status = 'active' AND NOT name = 'Bram Kowal'" },
            { "f2_tut_1", "SELECT name FROM employees WHERE dept_id = 20" },
            { "f2_tut_2", "SELECT employees.name, departments.dept_name FROM employees JOIN departments ON employees.dept_id = departments.id" },
            { "f2_tut_3", "SELECT e.name FROM employees e JOIN departments d ON e.dept_id = d.id WHERE d.floor = 3" },
            { "f2_tut_4", "SELECT d.dept_name, e.name FROM departments d LEFT JOIN employees e ON e.dept_id = d.id" },
            { "f2_tut_5", "SELECT dept_name FROM departments ORDER BY id DESC LIMIT 2" },
        };

        [Test]
        public void EveryFloor2Puzzle_IsSolvedByItsIntendedQuery()
        {
            foreach (var kv in Solutions)
            {
                var puzzle = PuzzleData.FromJson(File.ReadAllText($"{Data}/Puzzles/{kv.Key}.json"));
                var result = SqlEngine.Execute(puzzle.BuildDatabase(), kv.Value);
                Assert.IsTrue(PuzzleChecker.Check(puzzle, result, out string feedback), kv.Key + ": " + feedback);
            }
        }

        [Test]
        public void Floor2Computers_OnlyNeedSqlTaughtBelow()
        {
            foreach (var kv in Solutions.Where(k => !k.Key.StartsWith("f2_tut")))
                Assert.IsFalse(kv.Value.ToUpperInvariant().Contains(" JOIN "), kv.Key + " needs JOIN, which this floor's terminal teaches");
        }

        [Test]
        public void ReceptionPuzzle_NeedsEveryClue()
        {
            var p = PuzzleData.FromJson(File.ReadAllText($"{Data}/Puzzles/f2_reception_logins.json"));
            var db = p.BuildDatabase();
            string[] conds = { "role LIKE '%Engineer'", "start_year = 2014", "shift = 'Night'", "status = 'active'", "NOT name = 'Bram Kowal'" };
            for (int skip = 0; skip < conds.Length; skip++)
            {
                string where = string.Join(" AND ", conds.Where((c, i) => i != skip));
                var r = SqlEngine.Execute(db, "SELECT username, password FROM duty_logins WHERE " + where);
                Assert.IsFalse(PuzzleChecker.Check(p, r, out _), "solvable without clue: " + conds[skip]);
            }
            Assert.AreEqual(4, p.clues.Length);
            Unlikely.Core.GameState.ResetAll();
            StringAssert.Contains("CLUES (0 of 4)", p.CurrentBrief(false));
            Unlikely.Core.GameState.Set("f2_clue_printer");
            StringAssert.Contains("Engineer", p.CurrentBrief(false));
            Unlikely.Core.GameState.ResetAll();
        }

        [Test]
        public void Northgate_HiddenUntilTheVideo()
        {
            var p = PuzzleData.FromJson(File.ReadAllText($"{Data}/Puzzles/f2_t1_northgate.json"));
            Unlikely.Core.GameState.ResetAll();
            StringAssert.DoesNotContain("Northgate", p.CurrentBrief(false));
            Unlikely.Core.GameState.Set("f2_clue_video");
            StringAssert.Contains("Northgate", p.CurrentBrief(false));
            Unlikely.Core.GameState.ResetAll();
        }

        [Test]
        public void Floor2Terminal_UnlocksEveryFloor2Lesson()
        {
            var unlocked = new HashSet<string>();
            for (int i = 1; i <= 5; i++)
                foreach (var l in PuzzleData.FromJson(File.ReadAllText($"{Data}/Puzzles/f2_tut_{i}.json")).unlocksLessons.Split(','))
                    unlocked.Add(l.Trim());
            foreach (var lesson in ManualLessons.All.Where(l => l.floor == 2))
                Assert.IsTrue(unlocked.Contains(lesson.id), "No lesson step unlocks " + lesson.id);
        }

        [Test]
        public void Floor2Script_HasEveryDialogueEntry()
        {
            DialogueBank.Load(new TextAsset(File.ReadAllText($"{Data}/Dialogue/floor2.json")));
            string story = File.ReadAllText("Assets/_Project/Scripts/Story/Floor2Story.cs");
            var known = new HashSet<string>(File.ReadAllText($"{Data}/Dialogue/floor2.json").Split('"'));
            var ids = System.Text.RegularExpressions.Regex.Matches(story, "(?:Say|Show|Call|One|Get|Barks\\([^,]+,|Shoo\\([^,]+,|SetSpot\\([^,]+,|Clue\\([^,]+,|SayWhenFree)\\(?\\s*\"([a-z0-9_]+)\"")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.Greater(ids.Count, 30);
            foreach (var id in ids)
                Assert.IsTrue(known.Contains(id), "floor2.json has no entry " + id);
        }

        [Test]
        public void FloorsScene_HasAllFloor2StoryObjects()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Floors.unity");
            var hotspots = Object.FindObjectsByType<ScriptedInteractable>(FindObjectsInactive.Include).Select(s => s.Id).ToList();
            foreach (var id in new[] { "f2_pc_t1", "f2_pc_t2", "f2_pc_t3", "f2_pc_t4", "f2_pc_iv", "f2_pc_recep", "f2_printer", "f2_plant",
                         "f2_projector", "f2_bin", "f2_breaker_panel", "f2_terminal", "f2_flashlight" })
                Assert.Contains(id, hotspots);
            var people = Object.FindObjectsByType<Npc>(FindObjectsInactive.Include).ToDictionary(n => n.Id);
            foreach (var id in new[] { "f2_hr", "ryker_f2", "f2_sec_1", "f2_sec_2", "f2_sec_3" })
                Assert.IsNotNull(people[id].GetComponent<Hunter>(), id + " should hunt");
            foreach (var id in new[] { "f2_recep", "f2_interviewer", "f2_office_staff" })
                Assert.IsNotNull(people[id].GetComponent<Guard>(), id + " should watch");
            Assert.GreaterOrEqual(people.Keys.Count(k => k.StartsWith("f2_")), 16, "lots of people on this floor");
            var hides = Object.FindObjectsByType<HideSpot>(FindObjectsInactive.Include);
            Assert.GreaterOrEqual(hides.Count(h => h.Type == HideSpot.Kind.Locker), 4);
            Assert.GreaterOrEqual(hides.Count(h => h.Type == HideSpot.Kind.Plant), 8);
            Assert.IsNotNull(Object.FindAnyObjectByType<Floor2Story>());
            Assert.IsNotNull(Object.FindAnyObjectByType<Darkness>());
        }
    }
}
