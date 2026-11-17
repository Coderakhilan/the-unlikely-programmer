using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unlikely.Core;
using Unlikely.Puzzles;
using Unlikely.SQL;
using Unlikely.Story;
using Unlikely.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unlikely.Tests
{
    /// <summary>The home screen's pieces: save slots, the stars, the difficulty rules, the Vibe book's answers.</summary>
    public class MenuAndSaveTests
    {
        private const string Data = "Assets/_Project/Data";
        private string temp;

        [SetUp]
        public void SetUp()
        {
            temp = Path.Combine(Path.GetTempPath(), "unlikely_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            SaveSystem.Folder = temp;
            Profile.Folder = temp;
            Profile.Reload();
            SaveSystem.Stop();
            GameState.ResetAll();
            GameState.Difficulty = Difficulty.Unlikely;
            Inventory.Clear();
            PhoneData.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.Stop();
            SaveSystem.Folder = null;
            Profile.Folder = null;
            Profile.Reload();
            GameState.ResetAll();
            GameState.Difficulty = Difficulty.Unlikely;
            Inventory.Clear();
            PhoneData.Clear();
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }

        // ------------------------------------------------------------------ saves

        [Test]
        public void Soundtrack_EveryTrackHasItsMusicFile_AndHearingOneIsRemembered()
        {
            foreach (var t in Soundtrack.All)
                FileAssert.Exists("Assets/_Project/Resources/Music/" + t.id + ".mp3", t.title);
            Assert.AreEqual(14, Soundtrack.All.Length);
            Assert.IsFalse(Profile.Heard("the_intern"));
            Profile.HearTrack("the_intern");
            Profile.Reload();
            Assert.IsTrue(Profile.Heard("the_intern"), "saved with the profile");
            Profile.Erase();
            Assert.IsFalse(Profile.Heard("the_intern"), "hard reset forgets it");
        }

        [Test]
        public void ServerRoom_IsACheckpointOfItsOwn_AfterFloor3()
        {
            SaveSystem.NewGame(0, Difficulty.Unlikely);
            Assert.IsTrue(SaveSystem.Begin(0, 0));
            SaveSystem.ReachedFloor(3);
            SaveSystem.ReachedFloor(SaveSystem.ServerRoom);
            var s = SaveSystem.Load(0);
            Assert.IsNotNull(s.Get(SaveSystem.ServerRoom), "the server room is saved");
            Assert.AreEqual("The Server Room", SaveSystem.FloorLabel(SaveSystem.ServerRoom));
            Assert.IsTrue(SaveSystem.Begin(0, 3), "Floor 3 can still be loaded after it");
        }

        [Test]
        public void Checkpoint_RoundTripsFlagsPocketsAndPhone()
        {
            GameState.Set("f1_complete");
            GameState.Set("d1_lie");
            GameState.Clearance = 2;
            Inventory.Add("usb", "USB-C cable", false);
            Inventory.Add("flashlight", "Torch");
            PhoneData.AddMessage("Unknown number", "hello", "manual");
            PhoneData.AddMessage("Mum", "call me");
            var cp = SaveSystem.Capture(2);

            GameState.ResetAll();
            Inventory.Clear();
            PhoneData.Clear();
            SaveSystem.Restore(JsonUtility.FromJson<SaveSystem.Checkpoint>(JsonUtility.ToJson(cp)));

            Assert.IsTrue(GameState.Has("f1_complete") && GameState.Has("d1_lie"));
            Assert.AreEqual(2, GameState.Clearance);
            Assert.IsTrue(Inventory.Has("usb") && Inventory.Has("flashlight"));
            Assert.AreEqual("flashlight", Inventory.Equipped.id);
            Assert.AreEqual(2, PhoneData.Threads.Count);
            Assert.AreEqual("Mum", PhoneData.Threads[0].contact, "newest thread stays on top");
            Assert.AreEqual("manual", PhoneData.Threads[1].messages[0].attachment);
        }

        [Test]
        public void Slots_NewGameSavesFloor0_NewFloorsSave_ReplaysReplaceLaterOnes()
        {
            Assert.IsFalse(SaveSystem.AnySave);
            SaveSystem.NewGame(1, Difficulty.Likely);
            Assert.IsTrue(SaveSystem.AnySave);
            Assert.AreEqual(1, SaveSystem.LatestSlot);
            var s = SaveSystem.Load(1);
            Assert.AreEqual(Difficulty.Likely, s.Difficulty);
            Assert.IsTrue(s.Get(0).flags.Contains("intro_seen"), "loading Floor 0 skips the opening");

            GameState.Set("f0_complete");
            SaveSystem.ReachedFloor(1);
            GameState.Set("f1_complete");
            SaveSystem.ReachedFloor(2);
            SaveSystem.ReachedFloor(2);   // only the first time
            s = SaveSystem.Load(1);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, s.checkpoints.Select(c => c.floor).ToArray());
            Assert.AreEqual(2, s.current);

            // load Floor 1 again and reach Floor 2 again: the old Floor 2 checkpoint is replaced
            Assert.IsTrue(SaveSystem.Begin(1, 1));
            Assert.IsNotNull(SaveSystem.Pending);
            Assert.AreEqual(1, SaveSystem.Load(1).current);
            SaveSystem.TakePending();
            Assert.IsTrue(GameState.Has("f0_complete") && !GameState.Has("f1_complete"), "the Floor 1 checkpoint's state");
            GameState.Set("f1_complete");
            GameState.Set("d1_truth");
            SaveSystem.ReachedFloor(2);
            s = SaveSystem.Load(1);
            Assert.AreEqual(3, s.checkpoints.Count);
            Assert.IsTrue(s.Get(2).flags.Contains("d1_truth"));

            SaveSystem.Stop();
            SaveSystem.ReachedFloor(3);   // not saving
            Assert.IsNull(SaveSystem.Load(1).Get(3));
            Assert.IsFalse(SaveSystem.Begin(0, 0), "empty slot");
        }

        [Test]
        public void ResumeSave_KeepsTheSpotAndTheWorld_ContinuePrefersIt()
        {
            SaveSystem.NewGame(0, Difficulty.Leet);
            Assert.IsFalse(SaveSystem.Load(0).HasResume, "a new game has no resume save yet");
            Assert.IsTrue(SaveSystem.BeginResume(0), "Continue falls back to the floor checkpoint");
            Assert.IsFalse(SaveSystem.Pending.IsResume);

            // pretend the story filled in where Donnie is
            SaveSystem.WorldCapture = cp =>
            {
                cp.room = "f1_office_c";
                cp.x = 12.5f; cp.y = -3.25f;
                cp.objective = "Find out who controls the building's power";
                cp.world.cartOut = true;
                cp.world.cartRoom = "f1_closet";
                cp.world.cleanSpills.Add("dan_office_spill");
                cp.world.people.Add(new SaveSystem.Person { id = "omar", active = true, talkable = true, x = 1, y = 2, fx = 0, fy = -1 });
            };
            GameState.Set("f0_complete");
            SaveSystem.SaveResume(1);
            SaveSystem.WorldCapture = null;

            var s = SaveSystem.Load(0);
            Assert.IsTrue(s.HasResume);
            Assert.AreEqual(1, s.ContinueFloor);
            Assert.AreEqual("f1_office_c", s.resume.room);
            Assert.AreEqual(12.5f, s.resume.x);
            Assert.IsTrue(s.resume.world.cartOut);
            Assert.AreEqual("omar", s.resume.world.people[0].id);
            Assert.Contains("dan_office_spill", s.resume.world.cleanSpills);
            Assert.IsTrue(s.resume.flags.Contains("f0_complete"));

            Assert.IsTrue(SaveSystem.BeginResume(0));
            Assert.IsTrue(SaveSystem.Pending.IsResume, "Continue loads the exact spot");
            Assert.AreEqual(Difficulty.Leet, GameState.Difficulty);
        }

        [Test]
        public void Music_EveryCueAndTrackExists_AndTheLayersStack()
        {
            // every {music:...} cue in the dialogue, and every track the code names, is a file in Resources/Music
            var ids = new HashSet<string>();
            foreach (var f in Directory.GetFiles($"{Data}/Dialogue", "*.json"))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(f), @"\{music:(?:once:)?([a-z0-9_]+)\}"))
                    ids.Add(m.Groups[1].Value);
            // ...and every track the code asks for: Set*/PlayOnce calls, the phase tracks, the floors' tracks, the calls
            var calls = new System.Text.RegularExpressions.Regex(@"(?:Set(?:Base|Phase|Scene)|PlayOnce)\(""([a-z0-9_]+)""\)|phase = ""([a-z0-9_]+)""");
            foreach (var f in new[] { "Scripts/Story/StoryDirector.cs", "Scripts/Core/Music.cs", "Scripts/UI/TitleMenuUI.cs" })
                foreach (System.Text.RegularExpressions.Match m in calls.Matches(File.ReadAllText("Assets/_Project/" + f)))
                    ids.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
            foreach (var id in new[] { "another_day_at_work", "the_paper_trail", "corporate_intrigue", "the_people_upstairs", "the_usual_suspects", "threes_company" })
                ids.Add(id);
            ids.Remove("stop");
            ids.Remove("call");
            Assert.Greater(ids.Count, 12, "the soundtrack is wired in");
            foreach (var id in ids) Assert.IsTrue(File.Exists($"Assets/_Project/Resources/Music/{id}.mp3"), "no track file for " + id);

            // the layers: a scene track beats a conversation, which beats a phase, which beats the place
            Music.ClearStory();
            Music.SetBase("the_paper_trail");
            Assert.AreEqual("the_paper_trail", Music.Current);
            Music.SetPhase("framed");
            Assert.AreEqual("framed", Music.Current);
            Music.KidnapperCall();
            Assert.AreEqual("the_usual_suspects", Music.Current);
            Music.KidnapperCall();
            Assert.AreEqual("threes_company", Music.Current, "calls alternate");
            Assert.AreEqual("I'm done.", Music.ApplyCues("{music:the_final_query}I'm done."), "cues never show");
            Assert.AreEqual("the_final_query", Music.Current);
            Assert.AreEqual("Betty: Hi", Music.ApplyCues("{music:stop}Betty: Hi"));
            Assert.AreEqual("threes_company", Music.Current);
            Music.EndTalk();
            Assert.AreEqual("framed", Music.Current, "the call is over: back to what was playing before it");
            Music.ClearStory();
            Assert.AreEqual("the_paper_trail", Music.Current, "a new scene keeps only the place's track");
        }

        [Test]
        public void Stars_EndingAndDifficulty_SavedToDisk()
        {
            Assert.AreEqual(0, Profile.StarCount);
            Assert.AreEqual(8, Profile.Stars.Length);
            Profile.EndingReached(StoryScenes.EndingCapitalist, Difficulty.Leet);
            Profile.EndingReached(StoryScenes.EndingCapitalist, Difficulty.Leet);
            Assert.AreEqual(2, Profile.StarCount);
            Profile.SeeArt("capitalist_ending");
            Profile.Reload();
            Assert.IsTrue(Profile.HasStar("ending_capitalist") && Profile.HasStar("diff_leet"));
            Assert.IsTrue(Profile.Seen("capitalist_ending"));
            Assert.IsFalse(Profile.Seen("good_samaritan_ending"));
            foreach (var id in new[] { StoryScenes.EndingAccomplice, StoryScenes.EndingLastQuery, StoryScenes.EndingCapitalist, StoryScenes.EndingGoodSamaritan })
                Assert.IsTrue(Profile.Stars.Any(s => s.id == id), "a star for " + id);
            foreach (var d in DifficultyInfo.All)
                Assert.IsTrue(Profile.Stars.Any(s => s.id == Profile.DifficultyStar(d)), "a star for " + d);
        }

        // ------------------------------------------------------------------ difficulties

        [Test]
        public void Difficulties_DoWhatTheySay()
        {
            Assert.AreEqual(4, DifficultyInfo.All.Length);
            GameState.Difficulty = Difficulty.Unlikely;
            Assert.IsTrue(GameState.ManualPanelAllowed && DifficultyInfo.KodexHelps() && !DifficultyInfo.ComputersWatched() && !DifficultyInfo.HasTheory());
            GameState.Difficulty = Difficulty.Vibe;
            Assert.IsTrue(GameState.ManualPanelAllowed && DifficultyInfo.HasTheory() && !DifficultyInfo.ComputersWatched());
            GameState.Difficulty = Difficulty.Likely;
            Assert.IsTrue(GameState.ManualPanelAllowed && DifficultyInfo.ComputersWatched() && DifficultyInfo.KodexHelps());
            GameState.Difficulty = Difficulty.Leet;
            Assert.IsFalse(GameState.ManualPanelAllowed);
            Assert.IsFalse(DifficultyInfo.KodexHelps());
            Assert.IsTrue(DifficultyInfo.ComputersWatched());

            var logins = PuzzleData.FromJson(File.ReadAllText($"{Data}/Puzzles/f2_reception_logins.json"));
            GameState.Set(logins.clues[0].flag);
            StringAssert.Contains("CLUES", logins.CurrentBrief(false));
            StringAssert.DoesNotContain("CLUES", logins.CurrentBrief(false, withClues: false), "Leet: no clue list");
        }

        [Test]
        public void TheVibeBook_CannotBeTakenAway()
        {
            Inventory.Add(TheoryBookUI.ItemId, TheoryBookUI.ItemName, false);
            Inventory.Remove(TheoryBookUI.ItemId);
            Assert.IsTrue(Inventory.Has(TheoryBookUI.ItemId));
        }

        // ------------------------------------------------------------------ the guide

        private static List<string> GuideQueries(out TheoryBookUI.Book book)
        {
            book = TheoryBookUI.Parse(File.ReadAllText($"{Data}/Guide/theory_of_everything.json"));
            var queries = new List<string>();
            foreach (var c in book.chapters)
                foreach (var l in c.lines)
                {
                    string t = l.Trim();
                    if (t.StartsWith("SELECT ") || t.StartsWith("INSERT ") || t.StartsWith("DELETE ")) queries.Add(t);
                }
            return queries;
        }

        [Test]
        public void TheVibeBook_EveryQuerySolvesAPuzzle_AndEveryPuzzleIsCovered()
        {
            var queries = GuideQueries(out var book);
            Assert.Greater(book.chapters.Length, 10);
            Assert.LessOrEqual(book.chapters.Length, 15, "the contents column fits 15 chapters");
            var puzzles = Directory.GetFiles($"{Data}/Puzzles", "*.json")
                .Where(p => !p.EndsWith("f0_reception_roster.json"))   // the old test-room puzzle, not in the game
                .ToDictionary(p => Path.GetFileNameWithoutExtension(p), p => PuzzleData.FromJson(File.ReadAllText(p)));

            var solved = new HashSet<string>();
            var server = new List<string>();
            foreach (var q in queries)
            {
                if (q.Contains("gyaradb")) { server.Add(q); continue; }
                bool any = false;
                foreach (var kv in puzzles)
                {
                    QueryResult r;
                    try { r = SqlEngine.Execute(kv.Value.BuildDatabase(), q); }
                    catch (SqlException) { continue; }
                    if (PuzzleChecker.Check(kv.Value, r, out _)) { solved.Add(kv.Key); any = true; }
                }
                Assert.IsTrue(any, "The guide's query solves nothing: " + q);
            }
            foreach (var id in puzzles.Keys) Assert.IsTrue(solved.Contains(id), "The guide has no answer for " + id);

            // the server room: Last Query wipes it; Capitalist copies then wipes
            var lq = EndingStory.BuildGyaraDb(false);
            Assert.IsNull(EndingStory.DeleteOnlyRefusal("DELETE FROM gyaradb", 0));
            SqlEngine.Execute(lq, server.First(q => q.StartsWith("DELETE")));
            Assert.AreEqual(0, lq.Find("gyaradb").Rows.Count);
            var cap = EndingStory.BuildGyaraDb(true);
            foreach (var q in server.Where(q => q.Contains("magicorp_inbox")).Concat(new[] { "DELETE FROM gyaradb" }))
            {
                Assert.IsNull(EndingStory.DealRefusal(cap, q), q);
                SqlEngine.Execute(cap, q);
            }
            Assert.IsTrue(EndingStory.DealDone(cap));
        }

        // ------------------------------------------------------------------ the scenes

        [Test]
        public void Captions_ReadLikeTitles()
        {
            Assert.AreEqual("Betty's friends make fun of Betty", TitleMenuUI.Pretty("betty_s_friends_make_fun_of_betty"));
            Assert.AreEqual("Heated phone call between Liam and CEO", TitleMenuUI.Pretty("heated_phone_call_between_liam_and_ceo"));
        }

        [Test]
        public void Scenes_TitleFirst_ThenFloorsWithThePauseMenuBookAndWatch()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.AreEqual(2, scenes.Length);
            StringAssert.EndsWith("Title.unity", scenes[0].path);
            StringAssert.EndsWith("Floors.unity", scenes[1].path);

            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Title.unity");
            var title = Object.FindAnyObjectByType<TitleMenuUI>();
            Assert.IsNotNull(title);
            var so = new SerializedObject(title);
            Assert.IsNotNull(so.FindProperty("background").objectReferenceValue, "the title art");
            Assert.Greater(so.FindProperty("art").arraySize, 10, "the pictures for Extras");
            Assert.IsNotNull(so.FindProperty("cutscenes").objectReferenceValue);

            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Floors.unity");
            Assert.IsNotNull(Object.FindAnyObjectByType<PauseMenuUI>());
            Assert.IsNotNull(Object.FindAnyObjectByType<ComputerWatchUI>());
            var book = Object.FindAnyObjectByType<TheoryBookUI>();
            Assert.IsNotNull(book);
            Assert.IsNotNull(new SerializedObject(book).FindProperty("json").objectReferenceValue, "the guide's text");
        }
    }
}
