using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unlikely.Core;
using Unlikely.Puzzles;
using Unlikely.Story;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unlikely.Tests
{
    /// <summary>The title screen, loading a saved floor, the pause menu and what each difficulty changes in play.</summary>
    public class MenuPlayTests
    {
        private string temp;

        [SetUp]
        public void SetUp()
        {
            temp = Path.Combine(Path.GetTempPath(), "unlikely_play_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            SaveSystem.Folder = temp;
            SaveSystem.WorldCapture = null;   // the last test's scene is still loaded: its room and people mustn't end up in this test's saves
            TitleSequences.SkipForTests = true;   // straight to the menu (the credits have their own test)
            Profile.Folder = temp;
            Profile.Reload();
            SaveSystem.Stop();
            GameState.ResetAll();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Unlikely.Player.PlayerController.Frozen = false;
            HideSpot.Enabled = false;
            SaveSystem.Stop();
            SaveSystem.Folder = null;
            Profile.Folder = null;
            Profile.Reload();
            GameState.Difficulty = Difficulty.Unlikely;
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }

        private static IEnumerator Wait(Func<bool> condition, string what, float seconds = 30f)
        {
            float t = 0f;
            while (!condition())
            {
                if (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen && !DialogueUI.Instance.IsChoosing) { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); }
                if (PhoneUI.Instance != null && PhoneUI.Instance.IsRinging) PhoneUI.Instance.Answer();
                t += Time.unscaledDeltaTime;
                if (t > seconds) Assert.Fail("Timed out waiting for: " + what);
                yield return null;
            }
        }

        private static PuzzleData Puzzle(string id) => PuzzleData.FromJson(File.ReadAllText($"Assets/_Project/Data/Puzzles/{id}.json"));

        /// <summary>A run in slot 0 on this difficulty that has reached Floor 1, loaded from that checkpoint.</summary>
        private static IEnumerator LoadFloor1(Difficulty difficulty)
        {
            SaveSystem.NewGame(0, difficulty);
            GameState.Clearance = 1;
            foreach (var f in new[] { "intro_seen", "has_manual", "f0_complete", "f0_hacked" }) GameState.Set(f);
            Inventory.Add("usb", "USB-C cable", false);
            SaveSystem.ReachedFloor(1);
            GameState.ResetAll();
            Inventory.Clear();
            Assert.IsTrue(SaveSystem.Begin(0, 1));
            SceneManager.LoadScene("Floors");
            yield return Wait(() => RoomManager.Instance != null && !RoomManager.Instance.IsTransitioning
                                    && RoomManager.Instance.Current.roomId == "floor1", "Floor 1");
            Assert.AreEqual(difficulty, GameState.Difficulty);
            Assert.IsFalse(CutsceneUI.Instance.IsPlaying, "a loaded game skips the opening");
        }

        [UnityTest]
        public IEnumerator Title_ShowsTheMenu_ContinueOnlyOnceThereIsASave()
        {
            SceneManager.LoadScene("Title");
            yield return Wait(() => TitleMenuUI.Instance != null, "the title screen");
            var labels = TitleMenuUI.Instance.MainEntries.Select(e => e.label).ToArray();
            CollectionAssert.AreEqual(new[] { "New Game", "Options", "Extras", "Exit Game" }, labels);

            SaveSystem.NewGame(2, Difficulty.Vibe);
            SceneManager.LoadScene("Title");
            yield return null;
            yield return Wait(() => TitleMenuUI.Instance != null && TitleMenuUI.Instance.Page == "main", "the title screen again");
            labels = TitleMenuUI.Instance.MainEntries.Select(e => e.label).ToArray();
            CollectionAssert.AreEqual(new[] { "Continue", "New Game", "Load Game", "Options", "Extras", "Exit Game" }, labels);
            StringAssert.Contains("Slot 3", TitleMenuUI.Instance.MainEntries[0].note);
            StringAssert.Contains("Vibe", TitleMenuUI.Instance.MainEntries[0].note);
        }

        [UnityTest]
        public IEnumerator Soundtrack_OnlyHeardTracks_PlayHere_AndLeavingBringsBackTheTitleMusic()
        {
            Profile.HearTrack("framed");
            SceneManager.LoadScene("Title");
            yield return Wait(() => TitleMenuUI.Instance != null && TitleMenuUI.Instance.Page == "main", "the title screen");
            yield return Wait(() => Profile.Heard("the_unlikely_programmer"), "the title track counts as found once it plays");
            typeof(TitleMenuUI).GetMethod("ShowSoundtrack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(TitleMenuUI.Instance, null);
            var page = TitleMenuUI.Instance.SoundtrackPage;
            Assert.IsTrue(page.IsOpen);
            int intern = Array.FindIndex(Soundtrack.All, t => t.id == "the_intern");
            int framed = Array.FindIndex(Soundtrack.All, t => t.id == "framed");
            page.Play(intern);
            Assert.IsNull(page.Playing, "never heard The Intern: it stays ???");
            page.Play(framed);
            Assert.AreEqual("framed", Music.Current, "Framed plays over the title music");
            page.Play(framed);
            Assert.IsTrue(Music.PreviewPaused, "picking it again pauses");
            page.Close();
            Assert.AreEqual("the_unlikely_programmer", Music.Current, "back to the title music");
            Assert.IsNull(Music.Preview);
        }

        [UnityTest]
        public IEnumerator Continue_LoadsTheFloor_AndReachingTheNextFloorSaves()
        {
            yield return LoadFloor1(Difficulty.Unlikely);
            Assert.IsTrue(GameState.Has("f0_complete") && Inventory.Has("usb"), "the checkpoint's state");
            Assert.AreEqual(0, SaveSystem.ActiveSlot);
            GameState.Set("f1_complete");
            GameState.Clearance = 2;
            RoomManager.Instance.GoTo("floor2", "elevator");
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "floor2", "Floor 2");
            var s = SaveSystem.Load(0);
            Assert.IsNotNull(s.Get(2), "Floor 2 saved on arrival");
            Assert.AreEqual(2, s.current);
            Assert.IsTrue(s.Get(2).flags.Contains("f1_complete"));
        }

        [UnityTest]
        public IEnumerator Dialogue_FirstPressShowsTheWholeLine_SecondMovesOn()
        {
            yield return LoadFloor1(Difficulty.Unlikely);
            yield return Wait(() => !DialogueUI.Instance.IsOpen, "quiet");
            const string first = "This is a long line that takes the typewriter a good couple of seconds to finish typing out.";
            DialogueUI.Instance.Show("", new[] { first, "Second line." });
            yield return null;
            yield return null;
            Assert.Less(DialogueUI.Instance.VisibleText.Length, first.Length, "still typing");
            DialogueUI.Instance.Advance();
            Assert.AreEqual(first, DialogueUI.Instance.VisibleText, "one press: the whole line, straight away");
            yield return null;
            Assert.AreEqual(first, DialogueUI.Instance.VisibleText);
            DialogueUI.Instance.Advance();
            Assert.IsTrue(DialogueUI.Instance.IsOpen);
            Assert.AreNotEqual(first, DialogueUI.Instance.VisibleText, "second press: next line");
            DialogueUI.Instance.Advance();
            DialogueUI.Instance.Advance();
            Assert.IsFalse(DialogueUI.Instance.IsOpen);
        }

        [UnityTest]
        public IEnumerator Sounds_AllBuild_AndNoneClipOrIsSilent()
        {
            yield return null;
            var sounds = Sfx.Warm();
            Assert.AreEqual(Enum.GetValues(typeof(Sfx.Id)).Length + 1, sounds.Count, "every sound, plus the ring");
            foreach (var (name, samples, peak) in sounds)
            {
                Assert.Greater(samples, 400, name + " has some length");
                Assert.Greater(peak, 0.05f, name + " isn't silent");
                Assert.LessOrEqual(peak, 1.0f, name + " doesn't clip");
            }
        }

        [UnityTest]
        public IEnumerator AfterAnEnding_TheTitleRollsTheCredits_WithTheEndingsCount()
        {
            Profile.EndingReached(StoryScenes.EndingAccomplice, Difficulty.Unlikely);
            TitleSequences.SkipForTests = false;
            TitleSequences.BootDone = true;          // (not the start-up screens: they write the real terms setting)
            TitleSequences.PendingCredits = true;    // what StoryDirector.Restart sets after an ending's pictures
            SceneManager.LoadScene("Title");
            yield return Wait(() => TitleMenuUI.Instance != null && TitleMenuUI.Instance.Page == "sequence", "the credits");
            var seq = TitleMenuUI.Instance.GetComponent<TitleSequences>();
            yield return Wait(() => seq.Headline.Contains("INFI"), "A game by INFI");
            yield return Wait(() => seq.Headline.Contains("endings completed"), "the endings count", 15f);
            StringAssert.Contains("1 / 4 endings completed", seq.Headline);
            yield return Wait(() => TitleMenuUI.Instance.Page == "main", "back to the menu", 60f);
            Assert.IsFalse(TitleSequences.PendingCredits);
            var extras = TitleMenuUI.Instance.MainEntries;
            Assert.IsTrue(System.Linq.Enumerable.Any(extras, e => e.label == "Extras"));
        }

        [UnityTest]
        public IEnumerator Pause_StopsTheGame_AndResumes()
        {
            yield return LoadFloor1(Difficulty.Unlikely);
            PauseMenuUI.Instance.Open();
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsTrue(UiKit.AnyModalOpen);
            PauseMenuUI.Instance.Close();
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator Likely_TooLongOnAComputer_GetsYouCaught()
        {
            yield return LoadFloor1(Difficulty.Likely);
            var player = Unlikely.Player.PlayerController.Instance;
            player.transform.position += new Vector3(3f, 0f, 0f);   // step away from the elevator
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position = player.transform.position;
            yield return null;
            TerminalUI.Instance.Open(Puzzle("f1_a_doorcodes"), ComputerSkin.EmployeePc);
            yield return null;
            Assert.Greater(ComputerWatchUI.Instance.Heat, 0f, "the clock runs on a computer");
            ComputerWatchUI.Instance.Heat = ComputerWatchUI.Instance.Limit - 0.2f;
            yield return Wait(() => GameOverUI.Instance.IsOpen, "caught");
            Assert.IsFalse(TerminalUI.Instance.IsOpen);
            GameOverUI.Instance.Retry();
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "floor1", "back on the floor");
            var lift = RoomManager.Instance.MarkerPosition("floor1", "elevator");
            Assert.Less(Vector2.Distance(player.transform.position, lift), 1f, "at the elevator");

            // away from computers the bar drains; on Unlikely there's no clock at all
            ComputerWatchUI.Instance.Heat = 10f;
            yield return new WaitForSeconds(0.5f);
            Assert.Less(ComputerWatchUI.Instance.Heat, 10f);
            GameState.Difficulty = Difficulty.Unlikely;
            TerminalUI.Instance.Open(Puzzle("f1_a_doorcodes"), ComputerSkin.EmployeePc);
            yield return null;
            Assert.AreEqual(0f, ComputerWatchUI.Instance.Heat);
            TerminalUI.Instance.Close();
        }

        [UnityTest]
        public IEnumerator Leet_TerminalTeachesNothing_AndClearanceStillLands()
        {
            yield return LoadFloor1(Difficulty.Leet);
            var lessons = Enumerable.Range(1, 5).Select(i => Puzzle("f1_tut_" + i)).ToArray();
            bool hacked = false, done = false;
            TerminalUI.Instance.OpenTerminal(lessons, false, "Floor 2 (Human Resources)", () => hacked = true, () => done = true);
            yield return Wait(() => hacked, "the hack", 20f);
            foreach (var l in lessons) Assert.IsTrue(GameState.Has(l.rewardFlag), "lesson counted as done: " + l.id);
            foreach (var l in lessons)
                if (!string.IsNullOrEmpty(l.unlocksLessons))
                    foreach (var page in l.unlocksLessons.Split(',')) Assert.IsFalse(GameState.Has("lesson_" + page.Trim()), "no Knowledge page " + page);
            yield return new WaitForSecondsRealtime(1.5f);
            TerminalUI.Instance.Close();
            Assert.IsTrue(done, "the floor carries on");
            Assert.AreEqual(0f, ComputerWatchUI.Instance.Heat, 0.01f, "no clock on the terminal's lessons");
        }

        [UnityTest]
        public IEnumerator Vibe_TheBookIsInYourPockets_AndOpens()
        {
            yield return LoadFloor1(Difficulty.Vibe);
            Assert.IsTrue(Inventory.Has(TheoryBookUI.ItemId), "future Donnie's book");
            TheoryBookUI.Instance.Open();
            Assert.IsTrue(TheoryBookUI.Instance.IsOpen && UiKit.AnyModalOpen);
            Assert.Greater(TheoryBookUI.Instance.Content.chapters.Length, 10);
            TheoryBookUI.Instance.Show(3);
            Assert.AreEqual(3, TheoryBookUI.Instance.Current);
            TheoryBookUI.Instance.Close();

            // a dev jump empties the pockets: the book comes back
            DevJump.Go("f2");
            yield return Wait(() => RoomManager.Instance != null && !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "floor2", "Floor 2");
            Assert.IsTrue(Inventory.Has(TheoryBookUI.ItemId));
            Assert.AreEqual(-1, SaveSystem.ActiveSlot, "dev jumps don't save");
        }
    }
}
