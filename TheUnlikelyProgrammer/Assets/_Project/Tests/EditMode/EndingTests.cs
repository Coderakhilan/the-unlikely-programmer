using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unlikely.Core;
using Unlikely.Interaction;
using Unlikely.NPC;
using Unlikely.SQL;
using Unlikely.Story;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unlikely.Tests
{
    /// <summary>The server room: which ending each truth-or-lie route gets, and what the GyaraDB console lets Donnie do.</summary>
    public class EndingTests
    {
        private const string Data = "Assets/_Project/Data";

        [SetUp]
        public void SetUp()
        {
            GameState.ResetAll();
            DialogueBank.Load(new TextAsset(File.ReadAllText($"{Data}/Dialogue/endings.json")));
        }

        [TearDown]
        public void TearDown() => GameState.ResetAll();

        private static string Route(bool lie1, bool lie2, bool lie3, bool mails = true)
        {
            GameState.ResetAll();
            GameState.Set(lie1 ? "d1_lie" : "d1_truth");
            GameState.Set(lie2 ? "d2_lie" : "d2_truth");
            GameState.Set(lie3 ? "d3_lie" : "d3_truth");
            if (lie2) GameState.Set("f2_magicorp");   // the lie on Floor 2 always gets Ryker's login
            if (mails) GameState.Set("f3_mails_read");
            return EndingStory.Pick();
        }

        [Test]
        public void EveryRoute_GetsItsEnding_AndFloor1DoesNotChangeIt()
        {
            foreach (bool lie1 in new[] { false, true })
            {
                Assert.AreEqual(EndingStory.Accomplice, Route(lie1, false, false));
                Assert.AreEqual(EndingStory.LastQuery, Route(lie1, false, true));
                Assert.AreEqual(EndingStory.Capitalist, Route(lie1, true, false));
                Assert.AreEqual(EndingStory.CapitalistMissed, Route(lie1, true, false, mails: false), "no CEO mail: the tease, then accomplice");
                Assert.AreEqual(EndingStory.Samaritan, Route(lie1, true, true));
            }
            Assert.AreEqual(EndingStory.Accomplice, Route(false, false, false, mails: true), "the truth on Floor 2 never reaches capitalist");
        }

        [Test]
        public void LastQuery_OnlyDeleteRuns_AndOneDeleteWipesEverything()
        {
            foreach (var q in new[] { "SELECT * FROM gyaradb", "INSERT INTO gyaradb VALUES (13, 'x', 'y')", "select record from gyaradb" })
                Assert.IsNotNull(EndingStory.DeleteOnlyRefusal(q, 0), q);
            Assert.IsNull(EndingStory.DeleteOnlyRefusal("DELETE FROM gyaradb", 0));
            Assert.IsNull(EndingStory.DeleteOnlyRefusal("delete from gyaradb where dept = 'hr'", 3));
            var db = EndingStory.BuildGyaraDb(false);
            Assert.AreEqual(12, db.Find("gyaradb").Rows.Count);
            Assert.IsNull(db.Find("magicorp_inbox"), "no MagiCorp link in the last query");
            SqlEngine.Execute(db, "DELETE FROM gyaradb WHERE dept = 'hr'");
            Assert.AreEqual(10, db.Find("gyaradb").Rows.Count);
            SqlEngine.Execute(db, "DELETE FROM gyaradb");
            Assert.AreEqual(0, db.Find("gyaradb").Rows.Count);
        }

        [Test]
        public void Capitalist_CopyAllToMagiCorp_ThenDelete()
        {
            var db = EndingStory.BuildGyaraDb(true);
            Assert.IsNotNull(EndingStory.DealRefusal(db, "DELETE FROM gyaradb"), "copy first");
            Assert.IsNotNull(EndingStory.DealRefusal(db, "INSERT INTO gyaradb SELECT * FROM gyaradb"), "into MagiCorp, not GyaraDB");
            Assert.IsNull(EndingStory.DealRefusal(db, "SELECT * FROM gyaradb"), "looking is fine");

            // a partial copy is not enough
            SqlEngine.Execute(db, "INSERT INTO magicorp_inbox SELECT * FROM gyaradb WHERE dept = 'clients'");
            Assert.AreEqual(3, EndingStory.Copied(db));
            Assert.IsNotNull(EndingStory.DealRefusal(db, "DELETE FROM gyaradb"));

            // the whole thing, either way of writing it
            const string all = "INSERT INTO magicorp_inbox (record_id, dept, record) SELECT record_id, dept, record FROM gyaradb";
            Assert.IsNull(EndingStory.DealRefusal(db, all));
            SqlEngine.Execute(db, all);
            Assert.AreEqual(12, EndingStory.Copied(db));
            Assert.IsNotNull(EndingStory.DealRefusal(db, "DELETE FROM magicorp_inbox"), "never delete the copy");
            Assert.IsNull(EndingStory.DealRefusal(db, "DELETE FROM gyaradb"));
            Assert.IsFalse(EndingStory.DealDone(db));
            SqlEngine.Execute(db, "DELETE FROM gyaradb");
            Assert.IsTrue(EndingStory.DealDone(db));
        }

        [Test]
        public void EndingScript_HasEveryDialogueEntry_AndEveryPicture()
        {
            string story = File.ReadAllText("Assets/_Project/Scripts/Story/EndingStory.cs");
            var known = new HashSet<string>(File.ReadAllText($"{Data}/Dialogue/endings.json").Split('"'));
            var ids = System.Text.RegularExpressions.Regex.Matches(story, "\"(end_[a-z0-9_]+)\"")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.Greater(ids.Count, 25);
            foreach (var id in ids) Assert.IsTrue(known.Contains(id), "endings.json has no entry " + id);

            var cutscenes = File.ReadAllText($"{Data}/Dialogue/cutscenes.json");
            foreach (var seq in new[] { "ending_accomplice", "ending_last_query", "ending_capitalist", "ending_good_samaritan" })
                StringAssert.Contains("\"" + seq + "\"", cutscenes);
            var images = System.Text.RegularExpressions.Regex.Matches(cutscenes, "\"image\":\\s*\"([a-z0-9_]+)\"")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct();
            foreach (var img in images)
                Assert.IsTrue(File.Exists($"Assets/_Project/Art/Processed/cutscenes/{img}.png"), "missing picture " + img);
        }

        [Test]
        public void FloorsScene_HasTheServerRoomConsoleAndTheEndingCast()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Floors.unity");
            Assert.IsTrue(Object.FindObjectsByType<ScriptedInteractable>(FindObjectsInactive.Include).Any(s => s.Id == "srv_gyaradb"));
            var people = Object.FindObjectsByType<Npc>(FindObjectsInactive.Include).Select(n => n.Id).ToList();
            foreach (var id in new[] { "srv_ceo", "srv_liam", "srv_guard_1", "srv_guard_2", "f3_liam_ceo", "f3_sec_ceo" })
                Assert.Contains(id, people);
            Assert.IsNotNull(Object.FindAnyObjectByType<EndingStory>());
            Assert.IsNotNull(Object.FindAnyObjectByType<Unlikely.UI.GyaraDbUI>());
        }
    }
}
