using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unlikely.Interaction;
using Unlikely.NPC;
using Unlikely.Puzzles;
using Unlikely.SQL;
using Unlikely.Story;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unlikely.Tests
{
    public class SqlFloor1FeatureTests
    {
        private static Database Db()
        {
            var t = new Table("staff", new[] { "name", "department", "floor", "year" });
            t.Rows.Add(new object[] { "Sofia", "HR", 2.0, 2017.0 });
            t.Rows.Add(new object[] { "Sam", "HR", 2.0, 2021.0 });
            t.Rows.Add(new object[] { "Bea", "Facilities", 2.0, 2015.0 });
            t.Rows.Add(new object[] { "Hal", "Security", 0.0, 2018.0 });
            var db = new Database();
            db.Add(t);
            return db;
        }

        private static string[] Col(QueryResult r, int c = 0) => r.Rows.Select(x => QueryResult.Format(x[c])).ToArray();

        [Test] public void Like_Percent() => CollectionAssert.AreEquivalent(new[] { "Sofia", "Sam" }, Col(SqlEngine.Execute(Db(), "SELECT name FROM staff WHERE name LIKE 's%'")));
        [Test] public void Like_Underscore() => CollectionAssert.AreEquivalent(new[] { "Sam", "Bea", "Hal" }, Col(SqlEngine.Execute(Db(), "SELECT name FROM staff WHERE name LIKE '___'")));
        [Test] public void In_List() => CollectionAssert.AreEquivalent(new[] { "Bea", "Hal" }, Col(SqlEngine.Execute(Db(), "SELECT name FROM staff WHERE department IN ('Facilities', 'security')")));
        [Test] public void Not_Prefix() => CollectionAssert.AreEquivalent(new[] { "Bea", "Hal" }, Col(SqlEngine.Execute(Db(), "SELECT name FROM staff WHERE NOT department = 'HR'")));
        [Test] public void Not_In() => CollectionAssert.AreEquivalent(new[] { "Sofia", "Sam" }, Col(SqlEngine.Execute(Db(), "SELECT name FROM staff WHERE department NOT IN ('Facilities', 'Security')")));
        [Test] public void Not_Like() => CollectionAssert.AreEquivalent(new[] { "Bea", "Hal" }, Col(SqlEngine.Execute(Db(), "SELECT name FROM staff WHERE name NOT LIKE 'S%'")));
        [Test] public void OrderBy_Desc() => CollectionAssert.AreEqual(new[] { "Sam", "Hal", "Sofia", "Bea" }, Col(SqlEngine.Execute(Db(), "SELECT name FROM staff ORDER BY year DESC")));
        [Test] public void OrderBy_UnselectedColumn() => CollectionAssert.AreEqual(new[] { "Bea", "Sofia", "Hal", "Sam" }, Col(SqlEngine.Execute(Db(), "SELECT name FROM staff ORDER BY year")));
        [Test] public void Limit() => Assert.AreEqual(2, SqlEngine.Execute(Db(), "SELECT name FROM staff ORDER BY year LIMIT 2").Rows.Count);

        [Test]
        public void GroupBy_Count()
        {
            var r = SqlEngine.Execute(Db(), "SELECT department, COUNT(*) FROM staff GROUP BY department ORDER BY COUNT(*) DESC");
            Assert.AreEqual("COUNT(*)", r.Columns[1]);
            Assert.AreEqual("HR", Col(r)[0]);
            Assert.AreEqual("2", Col(r, 1)[0]);
            Assert.AreEqual(3, r.Rows.Count);
        }

        [Test]
        public void Aggregates_And_Alias()
        {
            var r = SqlEngine.Execute(Db(), "SELECT COUNT(*) AS people, MIN(year), MAX(year), SUM(floor) FROM staff WHERE floor = 2");
            Assert.AreEqual("people", r.Columns[0]);
            CollectionAssert.AreEqual(new[] { "3", "2015", "2021", "6" }, r.Rows[0].Select(QueryResult.Format).ToArray());
        }

        [Test]
        public void FriendlyErrors()
        {
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "SELECT name, COUNT(*) FROM staff"));
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "SELECT name FROM staff ORDER BY year WHERE floor = 2"));
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "SELECT name FROM staff WHERE name NOT = 'Sam'"));
        }
    }

    /// <summary>Floor 1 can be finished: every puzzle solvable by its intended query, its text present, its objects in the scene.</summary>
    public class Floor1Tests
    {
        private const string Data = "Assets/_Project/Data";

        private static readonly Dictionary<string, string> Solutions = new Dictionary<string, string>
        {
            { "f1_sw_bookings", "SELECT * FROM room_bookings WHERE room = 'Meeting room' AND day = 'Tuesday'" },
            { "f1_se_tickets", "SELECT * FROM helpdesk WHERE floor = 1 AND category = 'Electrical' AND status = 'Open'" },
            { "f1_a_doorcodes", "SELECT * FROM door_codes WHERE office = 'Office B' AND status = 'active'" },
            { "f1_b_inbox", "SELECT subject, body FROM inbox WHERE sender = 'Facilities' OR sender = 'Linda Park'" },
            { "f1_c_invoices", "SELECT * FROM invoices WHERE vendor_id = 'V-207' AND status = 'unpaid'" },
            { "f1_d_budget", "SELECT department, cost_centre, floor FROM budget WHERE item = 'Building power'" },
            { "f1_e_archive", "SELECT contents, location FROM archive WHERE contents = 'Electrical plans' OR contents = 'Generator manual'" },
            { "f1_f_keys", "SELECT holder FROM key_register WHERE door = 'Manager office' AND status = 'spare'" },
            { "f1_m_systems", "SELECT * FROM building_systems WHERE system = 'Main power' AND cost_centre = 'PF-02'" },
            { "f1_tut_1", "SELECT name FROM staff WHERE role LIKE '%Officer'" },
            { "f1_tut_2", "SELECT name, department FROM staff WHERE department IN ('Security', 'People & Facilities')" },
            { "f1_tut_3", "SELECT name FROM staff WHERE NOT department = 'Human Resources'" },
            { "f1_tut_4", "SELECT name, start_year FROM staff WHERE department = 'Human Resources' ORDER BY start_year" },
            { "f1_tut_5", "SELECT department, COUNT(*) FROM staff GROUP BY department" },
        };

        [Test]
        public void EveryFloor1Puzzle_IsSolvedByItsIntendedQuery()
        {
            foreach (var kv in Solutions)
            {
                var puzzle = PuzzleData.FromJson(File.ReadAllText($"{Data}/Puzzles/{kv.Key}.json"));
                var result = SqlEngine.Execute(puzzle.BuildDatabase(), kv.Value);
                Assert.IsTrue(PuzzleChecker.Check(puzzle, result, out string feedback), kv.Key + ": " + feedback);
            }
        }

        [Test]
        public void OfficeC_Kodex_HidesTheVendorId_UntilTheSlipIsFound()
        {
            var p = PuzzleData.FromJson(File.ReadAllText($"{Data}/Puzzles/f1_c_invoices.json"));
            Unlikely.Core.GameState.ResetAll();
            StringAssert.DoesNotContain("V-207", p.CurrentBrief(false));
            Unlikely.Core.GameState.Set("searched_f1_se_drawer");
            StringAssert.Contains("V-207", p.CurrentBrief(false));
            Unlikely.Core.GameState.ResetAll();
        }

        [Test]
        public void Floor1Puzzles_OnlyNeedFloor0Sql()
        {
            // the floor's own computers must be solvable with SELECT/FROM/WHERE/AND/OR (what Donnie knows by then)
            foreach (var kv in Solutions.Where(k => !k.Key.StartsWith("f1_tut")))
                foreach (var banned in new[] { " LIKE ", " IN (", " NOT ", "ORDER BY", "GROUP BY", "COUNT(" })
                    Assert.IsFalse(kv.Value.ToUpperInvariant().Contains(banned), kv.Key + " needs " + banned);
        }

        [Test]
        public void Floor1Terminal_UnlocksEveryFloor1Lesson()
        {
            var unlocked = new HashSet<string>();
            for (int i = 1; i <= 5; i++)
            {
                var p = PuzzleData.FromJson(File.ReadAllText($"{Data}/Puzzles/f1_tut_{i}.json"));
                foreach (var l in p.unlocksLessons.Split(',')) unlocked.Add(l.Trim());
            }
            foreach (var lesson in ManualLessons.All.Where(l => l.floor == 1))
                Assert.IsTrue(unlocked.Contains(lesson.id), "No lesson step unlocks " + lesson.id);
        }

        [Test]
        public void Floor1Script_HasEveryDialogueEntry()
        {
            DialogueBank.Load(new TextAsset(File.ReadAllText($"{Data}/Dialogue/floor1.json")));
            string story = File.ReadAllText("Assets/_Project/Scripts/Story/Floor1Story.cs");
            var ids = System.Text.RegularExpressions.Regex.Matches(story, "\"(f1_[a-z0-9_]+|npc_[a-z0-9_]+|guard_[a-z0-9_]+)\"")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct()
                .Where(id => !id.StartsWith("f1_pc_") || id.EndsWith("_done")).ToList();
            var known = new HashSet<string>(File.ReadAllText($"{Data}/Dialogue/floor1.json").Split('"'));
            int checkedIds = 0;
            foreach (var id in ids)
            {
                // ids that aren't dialogue (hotspots, flags, puzzles, NPCs) are skipped
                if (!known.Contains(id)) continue;
                checkedIds++;
                Assert.IsFalse(DialogueBank.Get(id)[0].StartsWith("[missing"), "floor1.json has no entry " + id);
            }
            foreach (var must in new[] { "f1_arrival_call", "f1_gag", "f1_truth", "f1_lie", "f1_decision_call", "f1_tape_found", "f1_pc_m_done", "f1_m_shelf" })
                Assert.IsFalse(DialogueBank.Get(must)[0].StartsWith("[missing"), "floor1.json has no entry " + must);
            Assert.Greater(checkedIds, 20);
        }

        [Test]
        public void FloorsScene_HasAllFloor1StoryObjects()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Floors.unity");
            var hotspots = Object.FindObjectsByType<ScriptedInteractable>(FindObjectsInactive.Include).Select(s => s.Id).ToList();
            foreach (var id in new[] { "f1_pc_sw", "f1_pc_se", "f1_pc_a", "f1_pc_b", "f1_pc_c", "f1_pc_d", "f1_pc_e", "f1_pc_f", "f1_pc_m",
                         "f1_sw_drawer", "f1_se_drawer", "f1_b_drawer", "f1_e_shelf", "f1_locker", "f1_m_shelf", "f1_bin", "f1_toilet", "f1_terminal" })
                Assert.Contains(id, hotspots);
            var people = Object.FindObjectsByType<Npc>(FindObjectsInactive.Include).ToDictionary(n => n.Id);
            foreach (var id in new[] { "linda", "ken", "linda_hall", "ken_hall", "joey", "omar", "tomas", "f1_guard_sw", "f1_guard_se" })
                Assert.IsTrue(people.ContainsKey(id), "missing person " + id);
            foreach (var id in new[] { "omar", "tomas", "f1_guard_sw", "f1_guard_se" })
                Assert.IsNotNull(people[id].GetComponent<Guard>(), id + " should patrol");
            Assert.IsNotNull(Object.FindAnyObjectByType<Floor1Story>());
        }
    }
}
