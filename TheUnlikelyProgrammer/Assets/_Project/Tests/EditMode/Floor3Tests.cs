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
    public class SqlWriteTests
    {
        private static Database Db()
        {
            var v = new Table("visitors", new[] { "id", "name", "status" });
            v.Rows.Add(new object[] { 1.0, "Ada", "in" });
            v.Rows.Add(new object[] { 2.0, "Rob", "left" });
            v.Rows.Add(new object[] { 3.0, "Mia", "left" });
            var a = new Table("archive", new[] { "id", "name" });
            var db = new Database();
            db.Add(v);
            db.Add(a);
            return db;
        }

        private static string[] Rows(QueryResult r) => r.Rows.Select(x => string.Join("|", x.Select(QueryResult.Format))).ToArray();

        [Test]
        public void Insert_Values_ShowsTheWholeTable()
        {
            var db = Db();
            var r = SqlEngine.Execute(db, "INSERT INTO visitors (id, name, status) VALUES (4, 'Sam', 'in')");
            CollectionAssert.AreEqual(new[] { "1|Ada|in", "2|Rob|left", "3|Mia|left", "4|Sam|in" }, Rows(r));
            Assert.AreEqual(4, db.Find("visitors").Rows.Count, "it really changed the table");
        }

        [Test]
        public void Insert_NoColumnList_SomeColumns_AndSeveralRows()
        {
            var db = Db();
            SqlEngine.Execute(db, "INSERT INTO visitors VALUES (4, 'Sam', 'in'), (5, 'Kit', 'in');");
            var r = SqlEngine.Execute(db, "INSERT INTO visitors (name, id) VALUES ('Lou', 6)");
            CollectionAssert.Contains(Rows(r), "6|Lou|NULL");
            Assert.AreEqual(6, r.Rows.Count);
        }

        [Test]
        public void Insert_Select_CopiesRows() =>
            CollectionAssert.AreEquivalent(new[] { "2|Rob", "3|Mia" },
                Rows(SqlEngine.Execute(Db(), "INSERT INTO archive (id, name) SELECT id, name FROM visitors WHERE status = 'left'")));

        [Test]
        public void Delete_Where_And_DeleteEverything()
        {
            var db = Db();
            CollectionAssert.AreEqual(new[] { "1|Ada|in" }, Rows(SqlEngine.Execute(db, "DELETE FROM visitors WHERE status = 'left'")));
            Assert.AreEqual(0, SqlEngine.Execute(db, "DELETE FROM visitors").Rows.Count, "no WHERE: every row goes");
        }

        [Test]
        public void Write_FriendlyErrors()
        {
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "INSERT visitors VALUES (4, 'Sam', 'in')"));        // no INTO
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "INSERT INTO visitors VALUES (4, 'Sam')"));          // too few
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "INSERT INTO nowhere VALUES (1)"));
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "INSERT INTO visitors (id, nope, status) VALUES (1, 'a', 'b')"));
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "INSERT INTO archive SELECT id FROM visitors"));     // column count
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "DELETE visitors WHERE id = 1"));                    // no FROM
            Assert.Throws<SqlException>(() => SqlEngine.Execute(Db(), "DELETE FROM visitors WHERE nope = 1"));
            Assert.IsTrue(SqlEngine.Changes(" insert into x values (1)"));
            Assert.IsTrue(SqlEngine.Changes("DELETE FROM x"));
            Assert.IsFalse(SqlEngine.Changes("SELECT * FROM x"));
        }
    }

    /// <summary>Floor 3 can be finished: every puzzle solvable through the right link, its text present, its objects in the scene.</summary>
    public class Floor3Tests
    {
        private const string Data = "Assets/_Project/Data";

        // puzzle -> (the computer's own table, the table the link station must point it at, the intended query)
        public static readonly Dictionary<string, (string own, string linked, string sql)> Solutions = new Dictionary<string, (string, string, string)>
        {
            { "f3_p1_rota", ("rota", "staff", "SELECT s.staff_id, s.name FROM rota r JOIN staff s ON r.holder = s.staff_id WHERE r.rota_day = 'Tuesday'") },
            { "f3_p2_cover", ("leave", "staff", "SELECT s.staff_id, s.name FROM leave l JOIN staff s ON l.cover = s.staff_id WHERE l.on_leave = 118 AND l.leave_day = 'Tuesday'") },
            { "f3_p3_code", ("code_parts", "badges", "SELECT c.position, c.cipher FROM code_parts c JOIN badges b ON c.card = b.badge_no WHERE b.owner = 205 AND b.status = 'active' ORDER BY c.position") },
        };

        private static readonly Dictionary<string, string> Tutorials = new Dictionary<string, string>
        {
            { "f3_tut_1", "INSERT INTO visitors (id, name, status) VALUES (5, 'Sam Ortiz', 'in')" },
            { "f3_tut_2", "INSERT INTO archive (id, name) SELECT id, name FROM visitors WHERE status = 'left'" },
            { "f3_tut_3", "DELETE FROM visitors WHERE status = 'left'" },
        };

        private static PuzzleData Load(string id) => PuzzleData.FromJson(File.ReadAllText($"{Data}/Puzzles/{id}.json"));

        /// <summary>What the computer sees: its own table plus the linked one (like Floor3Story.ViewFor).</summary>
        private static Database View(PuzzleData p, params string[] tables)
        {
            var copy = PuzzleData.FromJson(JsonUtility.ToJson(p));
            copy.tables = p.tables.Where(t => tables.Contains(t.name)).ToArray();
            return copy.BuildDatabase();
        }

        [Test]
        public void EveryFloor3Puzzle_IsSolvedThroughTheRightLink()
        {
            foreach (var kv in Solutions)
            {
                var p = Load(kv.Key);
                var r = SqlEngine.Execute(View(p, kv.Value.own, kv.Value.linked), kv.Value.sql);
                Assert.IsTrue(PuzzleChecker.Check(p, r, out string feedback), kv.Key + ": " + feedback);
                // without the link the other table simply isn't there
                Assert.Throws<SqlException>(() => SqlEngine.Execute(View(p, kv.Value.own), kv.Value.sql), kv.Key + " shouldn't work unlinked");
            }
        }

        [Test]
        public void CodeParts_SpellTheEncryptedCode_AndNeedEveryCondition()
        {
            var p = Load("f3_p3_code");
            Assert.AreEqual("VQ3L", string.Concat(p.expectedRows.Select(r => r.cells[1])));
            Assert.IsTrue(p.orderMatters);
            var db = View(p, "code_parts", "badges");
            string[] wrong =
            {
                "SELECT c.position, c.cipher FROM code_parts c JOIN badges b ON c.card = b.badge_no WHERE b.owner = 205 ORDER BY c.position",           // the revoked badge too
                "SELECT c.position, c.cipher FROM code_parts c JOIN badges b ON c.card = b.badge_no WHERE b.owner = 205 AND b.status = 'active'",       // out of order
                "SELECT c.position, c.cipher FROM code_parts c JOIN badges b ON c.card = b.badge_no WHERE b.owner = 118 AND b.status = 'active' ORDER BY c.position",   // Priya: she's off
            };
            foreach (var q in wrong) Assert.IsFalse(PuzzleChecker.Check(p, SqlEngine.Execute(db, q), out _), q);
            var p2 = Load("f3_p2_cover");
            Assert.IsFalse(PuzzleChecker.Check(p2, SqlEngine.Execute(View(p2, "leave", "staff"),
                "SELECT s.staff_id, s.name FROM leave l JOIN staff s ON l.cover = s.staff_id WHERE l.leave_day = 'Tuesday'"), out _), "everyone off on Tuesday");
        }

        [Test]
        public void JoinPuzzles_HiddenUntilTheKeyMapAndTheStepBefore()
        {
            Unlikely.Core.GameState.ResetAll();
            var p1 = Load("f3_p1_rota");
            var p3 = Load("f3_p3_code");
            StringAssert.DoesNotContain("Tuesday", p1.CurrentBrief(false));
            StringAssert.DoesNotContain("205", p3.CurrentBrief(false));
            Unlikely.Core.GameState.Set("f3_keymap");
            StringAssert.Contains("Tuesday", p1.CurrentBrief(false));
            StringAssert.Contains("Key map", p1.CurrentBrief(false));
            Unlikely.Core.GameState.Set("f3_ready_p3");
            StringAssert.Contains("205", p3.CurrentBrief(false));
            Unlikely.Core.GameState.ResetAll();
        }

        [Test]
        public void BankApp_OnlyIfRykerWasDiscovered()
        {
            Unlikely.Core.GameState.ResetAll();
            Assert.IsFalse(Unlikely.UI.PhoneUI.BankAllowed, "truth on Floor 2: no bank app, ever");
            Unlikely.Core.GameState.Set("d2_lie");
            Assert.IsTrue(Unlikely.UI.PhoneUI.BankAllowed);
            Unlikely.Core.GameState.ResetAll();
        }

        [Test]
        public void Floor3Terminal_TeachesInsertAndDelete()
        {
            var unlocked = new HashSet<string>();
            foreach (var kv in Tutorials)
            {
                var p = Load(kv.Key);
                var r = SqlEngine.Execute(p.BuildDatabase(), kv.Value);
                Assert.IsTrue(PuzzleChecker.Check(p, r, out string feedback), kv.Key + ": " + feedback);
                Assert.IsFalse(PuzzleChecker.Check(p, SqlEngine.Execute(p.BuildDatabase(), "SELECT * FROM visitors"), out _), kv.Key + " passes without writing anything");
                foreach (var l in p.unlocksLessons.Split(',')) unlocked.Add(l.Trim());
            }
            foreach (var lesson in ManualLessons.All.Where(l => l.floor == 3))
                Assert.IsTrue(unlocked.Contains(lesson.id), "No lesson step unlocks " + lesson.id);
        }

        [Test]
        public void Floor3Script_HasEveryDialogueEntry()
        {
            string story = File.ReadAllText("Assets/_Project/Scripts/Story/Floor3Story.cs") + File.ReadAllText("Assets/_Project/Scripts/UI/PhoneUI.cs");
            var known = new HashSet<string>(File.ReadAllText($"{Data}/Dialogue/floor3.json").Split('"'));
            var ids = System.Text.RegularExpressions.Regex.Matches(story, "(?:Say|Show|Call|One|Get|Plain|Barks\\([^,]+,|Shoo\\([^,]+,|SayWhenFree)\\(?\\s*\"(f3_[a-z0-9_]+|npc_f3_[a-z0-9_]+|guard_f3_[a-z0-9_]+)\"")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value)
                .Where(id => !id.EndsWith("_"))   // ids built in code ("f3_coin_" + n) are checked below
                .Distinct().ToList();
            Assert.Greater(ids.Count, 40);
            foreach (var id in ids)
                Assert.IsTrue(known.Contains(id), "floor3.json has no entry " + id);
            foreach (var id in new[] { "f3_coin_1", "f3_coin_2", "f3_coin_3", "f3_caught_ceo", "f3_caught_security", "f3_caught_liam" })
                Assert.IsTrue(known.Contains(id), "floor3.json has no entry " + id);
        }

        [Test]
        public void FloorsScene_HasAllFloor3StoryObjects()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Floors.unity");
            var hotspots = Object.FindObjectsByType<ScriptedInteractable>(FindObjectsInactive.Include).Select(s => s.Id).ToList();
            foreach (var id in new[] { "f3_terminal", "f3_link", "f3_drawer", "f3_torch", "f3_pc_board_long", "f3_pc_board_side", "f3_pc_open_l",
                         "f3_pc_open_r", "f3_pc_exec_b", "f3_plant_coin_1", "f3_plant_coin_2", "f3_plant_coin_3", "f3_vending", "f3_ceo_desk",
                         "f3_ceo_pc", "f3_bin" })
                Assert.Contains(id, hotspots);
            var people = Object.FindObjectsByType<Npc>(FindObjectsInactive.Include).ToDictionary(n => n.Id);
            Assert.AreEqual(Hunter.Mode.Liam, people["f3_liam"].GetComponent<Hunter>().Kind);
            Assert.AreEqual(Hunter.Mode.Ryker, people["f3_ceo_hunt"].GetComponent<Hunter>().Kind, "the CEO tracks like Ryker did");
            foreach (var id in new[] { "f3_sec_1", "f3_sec_2", "f3_sec_3" })
                Assert.AreEqual(Hunter.Mode.Security, people[id].GetComponent<Hunter>().Kind);
            foreach (var id in new[] { "f3_guard_a", "f3_guard_b", "f3_assistant" })
                Assert.IsNotNull(people[id].GetComponent<Guard>(), id + " should only mind a janitor without his cart");
            Assert.IsNotNull(people["f3_ceo"]);
            Assert.IsNotNull(people["f3_liam_in"]);
            var hides = Object.FindObjectsByType<HideSpot>(FindObjectsInactive.Include);
            Assert.IsTrue(hides.Any(h => h.Id == "f3_locker" && h.Type == HideSpot.Kind.Locker));
            Assert.GreaterOrEqual(hides.Count(h => h.Id.StartsWith("f3_plant")), 6);
            var doors = Object.FindObjectsByType<Door>(FindObjectsInactive.Include);
            Assert.IsTrue(doors.Any(d => d.TargetRoomId == "server"));
            Assert.IsTrue(doors.First(d => d.TargetRoomId == "f3_ceo" && d.GetComponentInParent<Room>(true).roomId == "floor3").IsLocked, "CEO's office starts locked");
            var rooms = Object.FindObjectsByType<Room>(FindObjectsInactive.Include).Select(r => r.roomId).ToList();
            foreach (var id in new[] { "f3_ceo", "f3_board", "f3_round", "f3_exec_a", "f3_exec_b", "f3_closet", "f3_lounge" })
                Assert.Contains(id, rooms);
            Assert.IsNotNull(Object.FindAnyObjectByType<Floor3Story>());
        }
    }
}
