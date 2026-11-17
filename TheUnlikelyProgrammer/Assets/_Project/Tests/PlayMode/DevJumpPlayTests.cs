using System;
using System.Collections;
using NUnit.Framework;
using Unlikely.Core;
using Unlikely.Story;
using Unlikely.UI;
using Unlikely.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Unlikely.Tests
{
    /// <summary>The F10 dev menu's jump points each land in the right place with the right state.</summary>
    public class DevJumpPlayTests
    {
        private static IEnumerator Jump(string id, string room)
        {
            DevJump.Go(id);
            yield return new WaitForSeconds(0.6f);
            float t = 0f;
            while (RoomManager.Instance == null || RoomManager.Instance.IsTransitioning || RoomManager.Instance.Current.roomId != room)
            {
                t += Time.unscaledDeltaTime;
                if (t > 20f) Assert.Fail($"{id}: never reached {room}");
                yield return null;
            }
            yield return null;
        }

        private static IEnumerator Ring(string what)
        {
            float t = 0f;
            while (!PhoneUI.Instance.IsRinging)
            {
                if (DialogueUI.Instance.IsOpen && !DialogueUI.Instance.IsChoosing) { DialogueUI.Instance.Advance(); DialogueUI.Instance.Advance(); }
                t += Time.unscaledDeltaTime;
                if (t > 30f) Assert.Fail("no call: " + what);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator JumpPoints_SetUpEachStage()
        {
            GameState.ResetAll();
            SceneManager.LoadScene("Floors");
            yield return new WaitForSeconds(0.5f);

            yield return Jump("f1_call", "floor1");
            yield return Ring("D1");
            Assert.AreEqual(2, GameState.Clearance);

            yield return Jump("f3_truth", "floor3");
            Assert.IsTrue(GameState.Has("d2_truth"));
            Assert.IsFalse(PhoneUI.BankAllowed, "truth on Floor 2: no bank app");
            yield return Ring("Floor 3 arrival");

            yield return Jump("f3_ceo_card", "f3_exec_a");
            Assert.AreEqual("dark", Floor3Story.Instance.Phase);
            Assert.IsTrue(Inventory.Has("keycard_ceo") && Inventory.Has("flashlight"));
            Assert.IsFalse(PhoneUI.Instance.IsRinging, "no arrival call when jumping past it");

            yield return Jump("f3_server", "floor3");
            Assert.IsTrue(GameState.Has("f3_code") && GameState.Has("bank_app"));
            Assert.AreEqual("Use keypad", Array.Find(UnityEngine.Object.FindObjectsByType<Door>(FindObjectsInactive.Exclude), d => d.TargetRoomId == "server").Prompt);
        }
    }
}
