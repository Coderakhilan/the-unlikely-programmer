using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Unlikely.Core;
using Unlikely.Interaction;
using Unlikely.NPC;
using Unlikely.Player;
using Unlikely.Puzzles;
using Unlikely.Story;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unlikely.Tests
{
    /// <summary>
    /// Continue picks up exactly where the game was left: same room, same spot, same objective, the world as it was,
    /// and no scripted beat played twice. One test per floor's tricky part.
    /// </summary>
    public class ResumePlayTests
    {
        private string temp;

        [SetUp]
        public void SetUp()
        {
            temp = Path.Combine(Path.GetTempPath(), "unlikely_resume_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            SaveSystem.Folder = temp;
            SaveSystem.WorldCapture = null;   // the last test's scene is still loaded: its room and people mustn't end up in this test's saves
            Profile.Folder = temp;
            Profile.Reload();
            SaveSystem.Stop();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            PlayerController.Frozen = false;
            HideSpot.Enabled = false;
            SaveSystem.Stop();
            SaveSystem.Folder = null;
            Profile.Folder = null;
            Profile.Reload();
            GameState.Difficulty = Difficulty.Unlikely;
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }

        // ------------------------------------------------------------------ helpers

        private static IEnumerator Wait(Func<bool> condition, string what, float seconds = 30f)
        {
            float t = 0f;
            while (!condition())
            {
                if (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen && !DialogueUI.Instance.IsChoosing) { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); }
                if (PhoneUI.Instance != null && PhoneUI.Instance.IsRinging) PhoneUI.Instance.Answer();
                t += Time.unscaledDeltaTime;
                if (t > seconds) Assert.Fail("Timed out waiting for: " + what + "  (objective: " + HudUI.Instance?.Objective + ")");
                yield return null;
            }
        }

        /// <summary>Let any dialogue and calls play out, then a moment of calm.</summary>
        private static IEnumerator Settle()
        {
            for (int calm = 0; calm < 20;)
            {
                bool busy = DialogueUI.Instance.IsOpen || PhoneUI.Instance.IsRinging || PhoneUI.Instance.IsOpen || RoomManager.Instance.IsTransitioning
                            || StoryBeats.Playing > 0;
                if (DialogueUI.Instance.IsOpen && !DialogueUI.Instance.IsChoosing) { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); }
                if (PhoneUI.Instance.IsRinging) PhoneUI.Instance.Answer();
                else if (PhoneUI.Instance.IsOpen && !DialogueUI.Instance.IsOpen) PhoneUI.Instance.Close();
                calm = busy ? 0 : calm + 1;
                yield return null;
            }
        }

        private static IEnumerator Go(string room, string spawn)
        {
            yield return Wait(() => !RoomManager.Instance.IsTransitioning, "a calm moment");
            RoomManager.Instance.GoTo(room, spawn);
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == room, room);
        }

        /// <summary>A brand-new game in slot 1 (no opening), into the Floors scene.</summary>
        private static IEnumerator NewRun(Difficulty d = Difficulty.Unlikely)
        {
            SaveSystem.NewGame(0, d);
            Assert.IsTrue(SaveSystem.Begin(0, 0));
            SceneManager.LoadScene("Floors");
            yield return Wait(() => RoomManager.Instance != null && StoryDirector.Instance != null && !RoomManager.Instance.IsTransitioning, "the game");
            yield return null;
        }

        /// <summary>Continue: the slot's resume save, back into the Floors scene, fully put back.</summary>
        private static IEnumerator Continue()
        {
            Assert.IsTrue(SaveSystem.Load(0).HasResume, "there's a resume save");
            SaveSystem.Stop();
            GameState.ResetAll();
            Inventory.Clear();
            PhoneData.Clear();
            Assert.IsTrue(SaveSystem.BeginResume(0));
            SceneManager.LoadScene("Floors");
            yield return null;
            yield return Wait(() => StoryDirector.Instance != null && !StoryDirector.Instance.Resuming && RoomManager.Instance != null
                                    && !RoomManager.Instance.IsTransitioning, "the resumed game");
        }

        /// <summary>For a second, nothing scripted starts on its own (no line, no call): the beats aren't replayed.</summary>
        private static IEnumerator NothingReplays(string where)
        {
            for (float t = 0; t < 1.2f; t += Time.unscaledDeltaTime)
            {
                Assert.IsFalse(DialogueUI.Instance.IsOpen, where + ": a line replayed after Continue");
                Assert.IsFalse(PhoneUI.Instance.IsRinging, where + ": a call replayed after Continue");
                yield return null;
            }
        }

        private static void SetLessonsDone(string prefix, int count)
        {
            for (int i = 1; i <= count; i++)
                GameState.Set(PuzzleData.FromJson(File.ReadAllText($"Assets/_Project/Data/Puzzles/{prefix}{i}.json")).rewardFlag);
        }

        // ------------------------------------------------------------------ the tests

        [UnityTest]
        public IEnumerator Floor0_MidFloor_ComesBackExactly()
        {
            yield return NewRun();
            Time.timeScale = 4f;
            yield return Settle();
            var cart = JanitorCart.Find();
            yield return Go("f0_closet", "door0");
            ScriptedInteractable.Find("closet_bin").OnUse();
            yield return Settle();
            cart.PickUp();
            yield return Go("f0_office", "door0");
            yield return Wait(() => HudUI.Instance.Objective.Contains("Mop"), "mop objective");
            UnityEngine.Object.FindAnyObjectByType<Spill>(FindObjectsInactive.Include).DevClean();
            yield return Wait(() => HudUI.Instance.Objective.Contains("stairs"), "Dan and Ryker gone, stairs objective", 60f);
            yield return Settle();
            cart.Drop();
            yield return null;
            Assert.IsTrue(StoryDirector.Instance.Autosave(), "a calm moment: it saves");
            Vector3 spot = PlayerController.Instance.transform.position;
            Vector3 cartAt = cart.transform.position;

            yield return Continue();
            Assert.AreEqual("f0_office", RoomManager.Instance.Current.roomId, "same room");
            Assert.Less(Vector2.Distance(PlayerController.Instance.transform.position, spot), 0.05f, "same spot");
            yield return NothingReplays("Floor 0");
            StringAssert.Contains("stairs", HudUI.Instance.Objective);
            Assert.IsFalse(Npc.Find("dan").gameObject.activeSelf, "Dan already left");
            Assert.IsFalse(Npc.Find("ryker").gameObject.activeSelf, "Ryker already left");
            Assert.IsTrue(UnityEngine.Object.FindAnyObjectByType<Spill>(FindObjectsInactive.Include).Clean, "still mopped");
            Assert.IsTrue(JanitorCart.Find().Spawned, "the cart is out");
            Assert.Less(Vector2.Distance(JanitorCart.Find().transform.position, cartAt), 0.05f, "where he left it");
            Assert.AreEqual(1, PhoneData.Threads.Count, "Betty's message, once");

            // and the floor carries on from there
            yield return Go("floor0", "stairs_zone");
            yield return Wait(() => PhoneUI.Instance.IsRinging || DialogueUI.Instance.IsOpen, "the stairs call");
            yield return Settle();
            StringAssert.Contains("computer", HudUI.Instance.Objective);
        }

        [UnityTest]
        public IEnumerator Floor1_InAnOffice_KeepsWhatWasFound_AndTheCallIsNotReplayed()
        {
            yield return NewRun();
            GameState.Clearance = 1;
            foreach (var f in new[] { "has_manual", "f0_complete", "f0_hacked" }) GameState.Set(f);
            Inventory.Add("usb", "USB-C cable", false);
            Time.timeScale = 4f;
            yield return Go("floor1", "elevator");
            yield return Settle();
            StringAssert.Contains("power", HudUI.Instance.Objective);
            yield return Go("f1_office_sw", "door0");
            ScriptedInteractable.Find("f1_sw_drawer").OnUse();
            yield return Settle();
            Assert.IsTrue(Inventory.Has("key_f1_a"));
            Assert.IsTrue(StoryDirector.Instance.Autosave());
            Assert.AreEqual(1, SaveSystem.Load(0).ContinueFloor);

            yield return Continue();
            Assert.AreEqual("f1_office_sw", RoomManager.Instance.Current.roomId);
            yield return NothingReplays("Floor 1");
            Assert.IsTrue(Inventory.Has("key_f1_a") && Inventory.Has("usb"), "the pockets");
            Assert.IsTrue(GameState.Has("searched_f1_sw_drawer"), "the drawer stays searched");
            StringAssert.Contains("power", HudUI.Instance.Objective);

            // never mid-dialogue
            DialogueUI.Instance.Show("", new[] { "A line." });
            Assert.IsFalse(StoryDirector.Instance.CanAutosave(), "no saving while a text box is up");
            DialogueUI.Instance.Advance();
            DialogueUI.Instance.Advance();
        }

        [UnityTest]
        public IEnumerator Floor2_RykerLockedIn_PowerStillOff_ResumesThatStep()
        {
            yield return NewRun();
            GameState.Clearance = 3;
            foreach (var f in new[] { "has_manual", "f0_complete", "f0_hacked", "f1_complete", "f1_hacked", "d1_truth",
                         "f2_plant_found", "f2_need_login", "f2_hacked", "f2_clue_printer", "f2_clue_video", "f2_clue_candidate", "f2_clue_interview",
                         "f2_leverage", "f2_reception_logins_solved", "f2_iv_restructure_solved", "f2_t1_northgate_solved", "f2_t2_remote_solved",
                         "f2_t3_plants_solved", "f2_t4_locks_solved", "beat_f2_arrive", "beat_f2_plant",
                         "d2_lie", "ryker_discovered", "f2_ryker_trapped", "f2_magicorp", "f2_deal", "power_out_floor2" })
                GameState.Set(f);
            SetLessonsDone("f2_tut_", 5);
            Inventory.Add("magicorp", "MagiCorp login (written on hand)", false);
            yield return Go("f2_breaker", "door0");
            yield return null;
            SaveSystem.SaveResume(2);   // (the scene here was set up by hand: write the save directly)

            yield return Continue();
            Assert.AreEqual("f2_breaker", RoomManager.Instance.Current.roomId);
            yield return NothingReplays("Floor 2");
            Assert.AreEqual("restore", Floor2Story.Instance.Phase, "Ryker's locked in, the power's still off");
            StringAssert.Contains("power back on", HudUI.Instance.Objective);
            Assert.IsFalse(Npc.Find("f2_hr").GetComponent<Hunter>().enabled, "Head of HR isn't hunting in an empty building");
            Assert.IsNotNull(Elevator.Blocked(), "no lift until the lights are on");
            Assert.IsTrue(StoryDirector.Instance.CanAutosave(), "this step is a safe save point");

            ScriptedInteractable.Find("f2_breaker_panel").OnUse();
            yield return Wait(() => GameState.Has("f2_complete"), "the floor finishes");
            yield return Settle();
            StringAssert.Contains("Floor 3", HudUI.Instance.Objective);
        }

        [UnityTest]
        public IEnumerator Floor3_Blackout_InOfficeA_ComesBackDark_WithTheLinksAsSet()
        {
            yield return NewRun();
            Floor3Story.Instance.DevJump("ceo_card", true);   // (straight from the story, so the slot keeps saving)
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "f3_exec_a", "office A");
            yield return Settle();
            Assert.AreEqual("board_long", Floor3Story.Instance.LinkOf("open_l"));
            Floor3Story.Instance.SetLink("board_long", "open_r");
            Assert.IsTrue(StoryDirector.Instance.Autosave(), "office A is safe in the dark: it saves");

            yield return Continue();
            Assert.AreEqual("f3_exec_a", RoomManager.Instance.Current.roomId);
            yield return NothingReplays("Floor 3");
            Assert.AreEqual("dark", Floor3Story.Instance.Phase, "still the blackout");
            Assert.IsTrue(HideSpot.Enabled);
            Assert.IsTrue(Npc.Find("f3_sec_1").GetComponent<Hunter>().enabled, "security hunting again");
            Assert.AreEqual("board_long", Floor3Story.Instance.LinkOf("open_l"), "the link station kept its wiring");
            Assert.AreEqual("open_r", Floor3Story.Instance.LinkOf("board_long"));
            Assert.IsTrue(Inventory.Has("keycard_ceo") && Inventory.Has("flashlight"));

            // out in the dark corridor it doesn't save (the hunters are about)
            yield return Go("floor3", "f3_exec_a_door0");
            yield return null;
            Assert.IsFalse(Floor3Story.Instance.CanSave, "no saves in the dark corridor");
        }
    }
}
