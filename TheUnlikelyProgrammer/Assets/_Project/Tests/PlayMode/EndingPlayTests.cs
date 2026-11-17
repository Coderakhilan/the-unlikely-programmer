using System;
using System.Collections;
using NUnit.Framework;
using Unlikely.Core;
using Unlikely.Interaction;
using Unlikely.NPC;
using Unlikely.Story;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unlikely.Tests
{
    /// <summary>Each route through the server room, from the dev jump to the ending's pictures.</summary>
    public class EndingPlayTests
    {
        [TearDown]
        public void TearDown()
        {
            // the tests stop on the ending's pictures, before the restart that would normally clear these
            Time.timeScale = 1f;
            Unlikely.Player.PlayerController.Frozen = false;
            HideSpot.Enabled = false;
        }

        private static IEnumerator Jump(string id)
        {
            GameState.ResetAll();
            SceneManager.LoadScene("Floors");
            yield return new WaitForSeconds(0.5f);
            DevJump.Go(id);
            yield return new WaitForSeconds(0.6f);
            Time.timeScale = 4f;
            yield return Wait(() => RoomManager.Instance != null && !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "server", "server room");
            yield return Wait(() => EndingStory.Instance.Stage == "ready", "console ready");
            Assert.AreEqual("Use the GyaraDB console (the desk, top left)", HudUI.Instance.Objective);
        }

        private static IEnumerator UseConsole()
        {
            yield return Wait(() => !DialogueUI.Instance.IsOpen, "quiet");
            ScriptedInteractable.Find("srv_gyaradb").OnUse();
        }

        [UnityTest]
        public IEnumerator Accomplice_UploadTheMalware()
        {
            yield return Jump("end_acc");
            Assert.AreEqual(EndingStory.Accomplice, EndingStory.Pick());
            yield return UseConsole();
            yield return Wait(() => GyaraDbUI.Instance.IsOpen, "upload screen");
            GyaraDbUI.Instance.StartUpload();
            yield return Wait(() => EndingStory.Instance.EndingShown != null, "the ending", 60f);
            Assert.AreEqual(StoryScenes.EndingAccomplice, EndingStory.Instance.EndingShown);
            Assert.IsTrue(Npc.Find("srv_ceo").gameObject.activeInHierarchy && Npc.Find("srv_liam").gameObject.activeInHierarchy, "they walked in");
            // they stand on the open floor, not on the racks
            var grid = NavGrid.For(RoomManager.Instance.Current);
            foreach (var id in new[] { "srv_guard_1", "srv_ceo", "srv_liam", "srv_guard_2" })
                Assert.IsTrue(grid.IsWalkable(Npc.Find(id).transform.position), id + " is standing on the floor, not on a rack");
        }

        [UnityTest]
        public IEnumerator LastQuery_OnlyDelete()
        {
            yield return Jump("end_lq");
            yield return UseConsole();
            yield return Wait(() => GyaraDbUI.Instance.IsOpen, "the console");
            var db = GyaraDbUI.Instance.Db;
            GyaraDbUI.Instance.Submit("SELECT * FROM gyaradb");
            GyaraDbUI.Instance.Submit("INSERT INTO gyaradb VALUES (13, 'x', 'y')");
            Assert.AreEqual(12, db.Find("gyaradb").Rows.Count, "nothing but DELETE runs");
            GyaraDbUI.Instance.Submit("DELETE FROM gyaradb WHERE dept = 'hr'");
            Assert.AreEqual(10, db.Find("gyaradb").Rows.Count);
            Assert.IsTrue(GyaraDbUI.Instance.IsOpen, "not finished until it's all gone");
            GyaraDbUI.Instance.Submit("DELETE FROM gyaradb");
            yield return Wait(() => EndingStory.Instance.EndingShown != null, "the ending", 60f);
            Assert.AreEqual(StoryScenes.EndingLastQuery, EndingStory.Instance.EndingShown);
        }

        [UnityTest]
        public IEnumerator Capitalist_SharesFirst_ThenInsertAndDelete()
        {
            yield return Jump("end_cap");
            Assert.AreEqual(EndingStory.Capitalist, EndingStory.Pick());
            yield return UseConsole();
            yield return Wait(() => HudUI.Instance.Objective.Contains("MagiCorp shares"), "the game asks for the shares first");
            Assert.IsFalse(GyaraDbUI.Instance.IsOpen);
            ScriptedInteractable.Find("srv_gyaradb").OnUse();   // still refuses
            yield return Wait(() => !DialogueUI.Instance.IsOpen, "quiet");
            Assert.IsFalse(GyaraDbUI.Instance.IsOpen);
            PhoneUI.Instance.Buy(1);
            Assert.IsTrue(GameState.Has("f3_stocks"));
            // "it'd better be worth it", then Donnie walks straight up and opens the console himself
            yield return Wait(() => DialogueUI.Instance.IsOpen, "the shares line");
            yield return Wait(() => !DialogueUI.Instance.IsOpen, "quiet");
            ScriptedInteractable.Find("srv_gyaradb").OnUse();
            yield return Wait(() => GyaraDbUI.Instance.IsOpen, "the console");
            var db = GyaraDbUI.Instance.Db;
            GyaraDbUI.Instance.Submit("DELETE FROM gyaradb");
            Assert.AreEqual(12, db.Find("gyaradb").Rows.Count, "copy first");
            GyaraDbUI.Instance.Submit("INSERT INTO magicorp_inbox SELECT * FROM gyaradb");
            GyaraDbUI.Instance.Submit("DELETE FROM gyaradb");
            yield return Wait(() => EndingStory.Instance.Stage == "busy", "the deal done");
            // straight on to the CEO's office: the console must never come back
            bool reopened = false;
            yield return Wait(() => (reopened |= GyaraDbUI.Instance.IsOpen) || EndingStory.Instance.EndingShown != null, "the ending", 90f);
            Assert.IsFalse(reopened, "the console reopened after the deal");
            Assert.AreEqual(StoryScenes.EndingCapitalist, EndingStory.Instance.EndingShown);
            Assert.IsFalse(Npc.Find("f3_liam_ceo").gameObject.activeSelf, "security took Liam away");
        }

        [UnityTest]
        public IEnumerator CapitalistMissed_TeaseThenAccomplice()
        {
            yield return Jump("end_cap_missed");
            Assert.AreEqual(EndingStory.CapitalistMissed, EndingStory.Pick());
            yield return UseConsole();
            yield return Wait(() => GyaraDbUI.Instance.IsOpen, "upload screen");
            GyaraDbUI.Instance.StartUpload();
            yield return Wait(() => EndingStory.Instance.EndingShown != null, "the ending", 60f);
            Assert.AreEqual(StoryScenes.EndingAccomplice, EndingStory.Instance.EndingShown);
        }

        [UnityTest]
        public IEnumerator GoodSamaritan_FindTheCeoInTheDark()
        {
            yield return Jump("end_sam");
            yield return UseConsole();
            yield return Wait(() => EndingStory.Instance.Stage == "samaritan", "Donnie thinks it through");
            Assert.IsFalse(GyaraDbUI.Instance.IsOpen);
            RoomManager.Instance.GoTo("floor3", "server_door");
            var ceo = Npc.Find("f3_ceo_hunt");
            yield return Wait(() => ceo.gameObject.activeInHierarchy && ceo.Talkable && ceo.OnTalk != null, "the CEO out on the floor");
            Assert.IsFalse(ceo.GetComponent<Hunter>().enabled, "the CEO isn't hunting");
            Assert.IsTrue(Npc.Find("f3_sec_1").GetComponent<Hunter>().enabled, "security still is");
            Assert.AreEqual("dark", Floor3Story.Instance.Phase);
            StringAssert.Contains("CEO", HudUI.Instance.Objective);

            // caught by security while looking for him: back in the server room, not the CEO's office
            var guard = Npc.Find("f3_sec_1");
            var p = Unlikely.Player.PlayerController.Instance;
            yield return Wait(() => { p.transform.position = guard.transform.position + (Vector3)(guard.Facing * 0.3f); return GameOverUI.Instance.IsOpen; },
                "security catches Donnie");
            GameOverUI.Instance.Retry();
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "server", "retry in the server room");
            Assert.AreEqual("samaritan", EndingStory.Instance.Stage, "still looking for the CEO");
            RoomManager.Instance.GoTo("floor3", "server_door");
            yield return Wait(() => !RoomManager.Instance.IsTransitioning && RoomManager.Instance.Current.roomId == "floor3", "back out on the floor");
            ceo.OnTalk();
            yield return Wait(() => EndingStory.Instance.EndingShown != null, "the ending", 90f);
            Assert.AreEqual(StoryScenes.EndingGoodSamaritan, EndingStory.Instance.EndingShown);
            Assert.IsFalse(Npc.Find("f3_sec_1").GetComponent<Hunter>().enabled, "the hunt is over");
        }

        private static IEnumerator Wait(Func<bool> condition, string what, float seconds = 40f)
        {
            float t = 0f;
            while (!condition())
            {
                if (DialogueUI.Instance != null && DialogueUI.Instance.IsOpen && !DialogueUI.Instance.IsChoosing) { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); }
                if (PhoneUI.Instance != null && PhoneUI.Instance.IsRinging) PhoneUI.Instance.Answer();
                t += Time.unscaledDeltaTime;
                if (t > seconds)
                    Assert.Fail("Timed out waiting for: " + what + "   (objective: " + HudUI.Instance?.Objective + ", stage: " + EndingStory.Instance?.Stage + ")");
                yield return null;
            }
        }
    }
}
